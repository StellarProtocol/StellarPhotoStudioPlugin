using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Stellar.PhotoStudio.ReShade;

internal enum PresetStatus { NotInstalled, Queued, Downloading, Installed, Failed }

/// <summary>
/// Installs Look → ReShade → Presets entries (spec 2026-10-03 reshade § 12 V4). A request queues the preset's packs
/// through <see cref="PackInstaller"/> (requires first), waits for them, then writes Photo Studio's own text or downloads
/// the pinned community file into reshade/preset-sources/ (sha256 + size checked by IPluginDownloads) and writes the
/// working copy ReShade loads (<see cref="PresetOverrides"/>) into reshade/presets/. Link-only entries are never
/// downloaded. An existing preset file is never overwritten. Installed state comes from the disk at start and on
/// <see cref="Rescan"/> only — never per status call. Main thread only.
/// The constructor takes a <see cref="SerialDownloads"/>, not the bare <c>IPluginDownloads</c>, on purpose: shader packs
/// and presets MUST share exactly one download queue (the framework answers an overlapping call with "busy"), and typing
/// the parameter this way makes that sharing a compile-time fact — whoever wires this class up (Task 6) cannot pass a
/// second, unshared queue by accident. The same <see cref="SerialDownloads"/> instance is also given to <see cref="PackInstaller"/>.
/// </summary>
internal sealed class PresetInstaller : IDisposable
{
    private readonly SerialDownloads _downloads;
    private readonly PackInstaller _packs;
    private readonly IReadOnlyList<PresetEntry> _catalog;
    private readonly IReShadePresetFiles _files;
    private readonly Action<string> _warn;
    private readonly Dictionary<string, bool> _disk = new(StringComparer.Ordinal);
    private readonly Dictionary<string, LiveState> _live = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _cts = new();
    private readonly Action _onPacksChanged;
    private readonly Action<ShaderPack> _onPackFailed;

    private sealed class LiveState
    {
        public PresetStatus Status = PresetStatus.Queued;
        public string? Error;
        public string? FailedRequirement;   // the required pack's Name, when the preset failed because that pack did
    }

    public PresetInstaller(SerialDownloads downloads, PackInstaller packs, IReadOnlyList<PresetEntry> catalog, IReShadePresetFiles files,
        Action<string> warn)
    {
        _downloads = downloads;
        _packs = packs;
        _catalog = catalog;
        _files = files;
        _warn = warn;
        _onPacksChanged = StartReady;
        _onPackFailed = FailWaiting;
        _packs.PacksChanged += _onPacksChanged;
        _packs.PackFailed += _onPackFailed;
        Rescan();
    }

    /// <summary>A preset was installed (the plugin rescans the Preset dropdown).</summary>
    public event Action? PresetsChanged;

    public string PresetsFolder => Path.Combine(_downloads.DataFolder, "reshade", "presets");

    public string PresetPath(PresetEntry e) => Path.Combine(PresetsFolder, e.FileName);

    public PresetStatus Status(PresetEntry e) =>
        _live.TryGetValue(e.Id, out var s) ? s.Status
        : _disk.TryGetValue(e.Id, out var on) && on ? PresetStatus.Installed : PresetStatus.NotInstalled;

    public string? Error(PresetEntry e) => _live.TryGetValue(e.Id, out var s) ? s.Error : null;

    public string? FailedRequirement(PresetEntry e) =>
        _live.TryGetValue(e.Id, out var s) && s.Status == PresetStatus.Failed ? s.FailedRequirement : null;

    public void Rescan()
    {
        foreach (var e in _catalog)
            if (e.Installable) _disk[e.Id] = _files.Exists(PresetPath(e));
    }

    public void Request(PresetEntry e)
    {
        if (!e.Installable) return;   // link-only: never downloaded (spec § 12 V4)
        if (Status(e) is PresetStatus.Installed or PresetStatus.Queued or PresetStatus.Downloading) return;
        _live[e.Id] = new LiveState();
        foreach (var p in PresetCatalog.PacksWithRequires(e))
            if (_packs.Status(p) != PackStatus.Installed) _packs.Request(p);
        StartReady();
    }

    public void Dispose()
    {
        _packs.PacksChanged -= _onPacksChanged;
        _packs.PackFailed -= _onPackFailed;
        _cts.Cancel();
        _cts.Dispose();
    }

    private bool PacksReady(PresetEntry e)
    {
        foreach (var p in PresetCatalog.PacksWithRequires(e))
            if (_packs.Status(p) != PackStatus.Installed) return false;
        return true;
    }

    /// <summary>After a request and on every PacksChanged: start each waiting preset whose packs are all installed.</summary>
    private void StartReady()
    {
        foreach (var e in _catalog)
            if (_live.TryGetValue(e.Id, out var s) && s.Status == PresetStatus.Queued && PacksReady(e))
                _ = InstallAsync(e, s);
    }

    /// <summary>A pack's own download failed: fail every preset waiting on it, via <see cref="PresetCatalog.PacksWithRequires"/>
    /// (so a Standard failure fails a SweetFX-only preset too — Standard is SweetFX's own requirement, not the preset's
    /// direct pack). Never throws: this runs as a <see cref="PackInstaller.PackFailed"/> subscriber, and a misbehaving
    /// handler here must not stop PackInstaller's pump or any sibling subscriber from running.</summary>
    private void FailWaiting(ShaderPack failed)
    {
        try
        {
            foreach (var e in _catalog)
            {
                if (!_live.TryGetValue(e.Id, out var s) || s.Status != PresetStatus.Queued) continue;
                foreach (var p in PresetCatalog.PacksWithRequires(e))
                {
                    if (p.Id != failed.Id) continue;
                    s.Status = PresetStatus.Failed;
                    s.Error = _packs.Error(failed) ?? "";
                    s.FailedRequirement = failed.Name;
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            _warn($"[PhotoStudio] ReShade preset handling of failed pack '{failed.Id}' threw: {ex.Message}");
        }
    }

    private async Task InstallAsync(PresetEntry e, LiveState s)
    {
        s.Status = PresetStatus.Downloading;
        try
        {
            var text = e.Kind == PresetKind.Own ? OwnPresets.Text(e.Id) : await FetchAsync(e, s);
            if (text is null) return;   // FetchAsync recorded the failure
            _files.WriteNew(PresetPath(e), text);   // false: a file of that name exists and is kept (edits are never lost)
            _live.Remove(e.Id);
            _disk[e.Id] = true;
            RaisePresetsChanged();
        }
        catch (OperationCanceledException)
        {
            _live.Remove(e.Id);
        }
        catch (Exception ex)
        {
            Fail(e, s, ex.Message);
        }
    }

    /// <summary>Invokes each <see cref="PresetsChanged"/> subscriber in its own try/catch (mirrors
    /// <see cref="PackInstaller"/>'s <c>RaisePackFailed</c>): a throwing subscriber must not propagate into the
    /// surrounding catch in <see cref="InstallAsync"/>, which would otherwise log "install failed" for a preset that
    /// actually installed successfully.</summary>
    private void RaisePresetsChanged()
    {
        var handler = PresetsChanged;
        if (handler is null) return;
        foreach (var d in handler.GetInvocationList())
        {
            try
            {
                ((Action)d)();
            }
            catch (Exception ex)
            {
                _warn($"[PhotoStudio] a PresetsChanged subscriber threw: {ex.Message}");
            }
        }
    }

    /// <summary>Downloads the pinned file (never written again afterwards) and returns the working-copy text, or null on failure.</summary>
    private async Task<string?> FetchAsync(PresetEntry e, LiveState s)
    {
        var result = await _downloads.DownloadAsync(PresetCatalog.Request(e)!, null, _cts.Token);
        if (!result.Ok || result.Folder is null)
        {
            Fail(e, s, result.Error ?? "");
            return null;
        }
        return PresetOverrides.Apply(_files.ReadAllText(result.Folder), e.DropDefinitions);   // Folder = the exact FILE path
    }

    private void Fail(PresetEntry e, LiveState s, string error)
    {
        s.Status = PresetStatus.Failed;
        s.Error = error;
        _warn($"[PhotoStudio] ReShade preset '{e.Id}' install failed: {error}");
    }
}
