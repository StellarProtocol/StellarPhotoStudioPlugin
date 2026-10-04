using System;
using System.Threading;
using System.Threading.Tasks;
using Stellar.Abstractions.Domain;
using Stellar.Abstractions.Services;

namespace Stellar.PhotoStudio.ReShade;

/// <summary>Runs downloads strictly one at a time, in call order. The framework allows ONE download per plugin and answers
/// an overlapping call with "busy" (PluginDownloadService); shader packs and presets share this queue so a preset never
/// collides with a pack the player started. A failed or cancelled download never blocks the next. Main thread only
/// (continuations run inline: there is no synchronization context — never ConfigureAwait(false)).</summary>
internal sealed class SerialDownloads : IPluginDownloads
{
    private readonly IPluginDownloads _inner;
    private Task _tail = Task.CompletedTask;

    public SerialDownloads(IPluginDownloads inner) => _inner = inner;

    public string DataFolder => _inner.DataFolder;

    public async Task<DownloadResult> DownloadAsync(DownloadRequest request, IProgress<double>? progress, CancellationToken ct)
    {
        var previous = _tail;
        var done = new TaskCompletionSource<bool>();
        _tail = done.Task;
        try
        {
            await previous;   // never faults: every tail is a TaskCompletionSource set in a finally
            ct.ThrowIfCancellationRequested();
            return await _inner.DownloadAsync(request, progress, ct);
        }
        finally
        {
            done.TrySetResult(true);
        }
    }
}
