using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Stellar.Abstractions.Domain;
using Stellar.Abstractions.Services;

namespace Stellar.PhotoStudio.ReShade;

internal enum PackStatus { NotInstalled, UpdateAvailable, Queued, Downloading, Installed, Failed }

/// <summary>
/// Downloads shader packs one at a time through <see cref="IPluginDownloads"/> (a pack's <c>Requires</c> first), and knows
/// which packs are on disk. Disk state is read at construction and after each download — never per UI refresh (the panel
/// polls <see cref="Status"/> every refresh). A pack folder from another commit reads as <see cref="PackStatus.UpdateAvailable"/>.
/// Main thread only: IPluginDownloads completes and reports progress on the main thread, so no locking.
/// </summary>
internal sealed class PackInstaller : IDisposable
{
    private readonly IPluginDownloads _downloads;
    private readonly IReadOnlyList<ShaderPack> _catalog;
    private readonly Func<string, bool> _dirExists;
    private readonly Action<string> _warn;
    private readonly Queue<ShaderPack> _queue = new();
    private readonly Dictionary<string, PackStatus> _disk = new(StringComparer.Ordinal);
    private readonly Dictionary<string, LiveState> _live = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _cts = new();
    private bool _pumping;

    private sealed class LiveState
    {
        public PackStatus Status = PackStatus.Queued;
        public double Progress;
        public string? Error;
        public string? FailedRequirement;   // the required pack's Name, when this one failed because that one did
    }

    /// <summary>Not <see cref="Progress{T}"/>: with no synchronization context it would call back on a pool thread.</summary>
    private sealed class ProgressSink : IProgress<double>
    {
        private readonly Action<double> _report;
        public ProgressSink(Action<double> report) => _report = report;
        public void Report(double value) => _report(value);
    }

    public PackInstaller(IPluginDownloads downloads, IReadOnlyList<ShaderPack> catalog, Func<string, bool> dirExists, Action<string> warn)
    {
        _downloads = downloads;
        _catalog = catalog;
        _dirExists = dirExists;
        _warn = warn;
        Rescan();
    }

    /// <summary>A pack finished installing (the search paths changed). Main thread.</summary>
    public event Action? PacksChanged;

    /// <summary>A pack's own download failed (raised after its status is Failed; dependents failed with it raise nothing).
    /// The preset installer fails the presets waiting for it.</summary>
    public event Action<ShaderPack>? PackFailed;

    public PackStatus Status(ShaderPack p) =>
        _live.TryGetValue(p.Id, out var s) ? s.Status : _disk.TryGetValue(p.Id, out var d) ? d : PackStatus.NotInstalled;

    public double Progress(ShaderPack p) => _live.TryGetValue(p.Id, out var s) ? s.Progress : 0d;

    public string? Error(ShaderPack p) => _live.TryGetValue(p.Id, out var s) ? s.Error : null;

    /// <summary>When <paramref name="p"/> failed WITHOUT being downloaded because a pack it requires failed: that pack's
    /// name (the panel shows "Needs {0}, which failed to download."); otherwise null (<see cref="Error"/> applies).</summary>
    public string? FailedRequirement(ShaderPack p) =>
        _live.TryGetValue(p.Id, out var s) && s.Status == PackStatus.Failed ? s.FailedRequirement : null;

    public void Rescan()
    {
        var root = _downloads.DataFolder;
        foreach (var p in _catalog)
            _disk[p.Id] = _dirExists(PackCatalog.EffectsFolder(root, p)) ? PackStatus.Installed
                : _dirExists(PackCatalog.PackFolder(root, p)) ? PackStatus.UpdateAvailable
                : PackStatus.NotInstalled;
    }

    /// <summary>Queues <paramref name="p"/> (and any required pack not installed yet, first) and starts downloading.</summary>
    public void Request(ShaderPack p)
    {
        foreach (var id in p.Requires)
            if (Find(id) is { } dep && Status(dep) != PackStatus.Installed) Enqueue(dep);
        Enqueue(p);
        _ = PumpAsync();
    }

    /// <summary>Installed packs' Shaders folders (and Textures folders that exist), in catalog order.</summary>
    public (IReadOnlyList<string> Effects, IReadOnlyList<string> Textures) SearchPaths()
    {
        var effects = new List<string>();
        var textures = new List<string>();
        var root = _downloads.DataFolder;
        foreach (var p in _catalog)
        {
            if (Status(p) != PackStatus.Installed) continue;   // Status falls back to the cached disk state
            effects.Add(PackCatalog.EffectsFolder(root, p));
            var tex = PackCatalog.TexturesFolder(root, p);
            if (_dirExists(tex)) textures.Add(tex);
        }
        return (effects, textures);
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }

    private ShaderPack? Find(string id)
    {
        foreach (var p in _catalog)
            if (p.Id == id) return p;
        return null;
    }

    private void Enqueue(ShaderPack p)
    {
        if (Status(p) is PackStatus.Queued or PackStatus.Downloading) return;
        _live[p.Id] = new LiveState();
        _queue.Enqueue(p);
    }

    private async Task PumpAsync()
    {
        if (_pumping) return;
        _pumping = true;
        try
        {
            while (_queue.Count > 0 && !_cts.IsCancellationRequested)
                await InstallOneAsync(_queue.Dequeue());
        }
        catch (OperationCanceledException)
        {
            // Plugin unloading: nothing to report.
        }
        catch (Exception ex)
        {
            _warn("[PhotoStudio] shader pack downloads stopped: " + ex.Message);
        }
        finally
        {
            _pumping = false;
        }
    }

    private async Task InstallOneAsync(ShaderPack p)
    {
        var state = _live[p.Id];
        state.Status = PackStatus.Downloading;
        var sink = new ProgressSink(v => { if (state.Status == PackStatus.Downloading) state.Progress = v; });
        DownloadResult result;
        try
        {
            result = await _downloads.DownloadAsync(PackCatalog.Request(p), sink, _cts.Token);
        }
        catch (OperationCanceledException)
        {
            _live.Remove(p.Id);
            throw;
        }
        if (result.Ok)
        {
            state.Status = PackStatus.Installed;   // a late progress value now writes into a detached state
            _live.Remove(p.Id);
            Rescan();
            PacksChanged?.Invoke();
            return;
        }
        state.Status = PackStatus.Failed;
        state.Progress = 0d;
        state.Error = result.Error ?? "";
        _warn($"[PhotoStudio] shader pack '{p.Id}' download failed: {result.Error}");
        FailDependents(p, state.Error);
        RaisePackFailed(p);
    }

    /// <summary>Invokes each <see cref="PackFailed"/> subscriber in its own try/catch, so one throwing subscriber can
    /// neither stop the others from seeing the event nor escape into <see cref="PumpAsync"/> and halt the pump.</summary>
    private void RaisePackFailed(ShaderPack p)
    {
        var handler = PackFailed;
        if (handler is null) return;
        foreach (var d in handler.GetInvocationList())
        {
            try
            {
                ((Action<ShaderPack>)d)(p);
            }
            catch (Exception ex)
            {
                _warn($"[PhotoStudio] a PackFailed subscriber threw: {ex.Message}");
            }
        }
    }

    /// <summary>Queued packs that require <paramref name="failed"/> fail too, never downloaded; they keep the requirement's
    /// error and record which pack it was (<see cref="FailedRequirement"/>).</summary>
    private void FailDependents(ShaderPack failed, string error)
    {
        var keep = new Queue<ShaderPack>();
        while (_queue.Count > 0)
        {
            var q = _queue.Dequeue();
            var needsFailed = false;
            foreach (var r in q.Requires) needsFailed |= r == failed.Id;
            if (needsFailed) _live[q.Id] = new LiveState { Status = PackStatus.Failed, Error = error, FailedRequirement = failed.Name };
            else keep.Enqueue(q);
        }
        foreach (var q in keep) _queue.Enqueue(q);
    }
}
