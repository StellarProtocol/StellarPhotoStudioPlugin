using System;
using System.Threading;
using System.Threading.Tasks;
using Stellar.Abstractions.Domain;
using Stellar.Abstractions.Services;

namespace Stellar.PhotoStudio.ReShade;

/// <summary>Runs downloads strictly one at a time, in call order. The framework allows ONE download per plugin and answers
/// an overlapping call with "busy" (PluginDownloadService); shader packs and presets share ONE instance of this queue so a
/// preset never collides with a pack the player started (<see cref="PresetInstaller"/> takes this type, not the bare
/// <c>IPluginDownloads</c>, precisely so the sharing is a compile-time fact and not just a wiring convention). A failed or
/// cancelled download never blocks the next. A call still WAITING for its turn honours its own cancellation token
/// immediately — it completes as Canceled without ever starting the inner download — and releases the next waiter's turn
/// exactly as a completed call would. Main thread only (continuations run inline: there is no synchronization context —
/// never ConfigureAwait(false)).</summary>
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
        var turn = false;
        try
        {
            await WaitTurn(previous, ct);
            turn = true;
            return await _inner.DownloadAsync(request, progress, ct);
        }
        finally
        {
            // Release the next waiter now ONLY if this call actually reached its turn (previous was already done by
            // then) — or if previous finished by coincidence while we were unwinding. Otherwise this call was cancelled
            // while still queued BEHIND previous: releasing "done" immediately would let the call behind US skip past
            // previous, which is still running (the bug: cancelling a middle waiter let the one behind it jump ahead of
            // one still in flight — calls=[a,c], maxInFlight=2). Chain the release onto previous instead, so the next
            // waiter still waits for the call actually ahead of it, not for us.
            if (turn || previous.IsCompleted) done.TrySetResult(true);
            else _ = previous.ContinueWith(_ => done.TrySetResult(true), CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }

    /// <summary>Waits for <paramref name="previous"/> (which never faults — every tail is a TaskCompletionSource set in a
    /// finally), but gives up early, as Canceled, the moment <paramref name="ct"/> fires — so a call queued behind another
    /// never starts the inner download once its own caller has stopped waiting for it.</summary>
    private static async Task WaitTurn(Task previous, CancellationToken ct)
    {
        if (previous.IsCompleted)
        {
            ct.ThrowIfCancellationRequested();
            return;
        }
        var cancelled = new TaskCompletionSource<bool>();
        using var reg = ct.Register(() => cancelled.TrySetResult(true));
        if (await Task.WhenAny(previous, cancelled.Task) == cancelled.Task) throw new OperationCanceledException(ct);
        ct.ThrowIfCancellationRequested();   // previous finishing and the token firing raced; a cancelled caller never proceeds
    }
}
