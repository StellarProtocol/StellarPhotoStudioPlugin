using System;
using System.Threading;
using System.Threading.Tasks;
using Stellar.Abstractions.Domain;
using Stellar.PhotoStudio.ReShade;
using Xunit;

namespace Stellar.PhotoStudio.Tests.ReShade;

// PluginDownloadService answers an overlapping call with "busy" (_gate.Wait(0)); packs and presets share this queue.
public sealed class SerialDownloadsTests
{
    private static DownloadRequest Req(string target) =>
        new(new Uri("https://example.invalid/" + target), new string('0', 64), 1, target, false);

    [Fact]
    public async Task A_second_download_starts_only_when_the_first_is_done()
    {
        SynchronizationContext.SetSynchronizationContext(null);
        var inner = new FakeDownloads();
        var serial = new SerialDownloads(inner);
        var a = serial.DownloadAsync(Req("a"), null, CancellationToken.None);
        var b = serial.DownloadAsync(Req("b"), null, CancellationToken.None);
        Assert.Single(inner.Calls);
        inner.Calls[0].Done.SetResult(new DownloadResult(true, "/data/a", null));
        Assert.True(a.IsCompletedSuccessfully);
        Assert.Equal(2, inner.Calls.Count);
        Assert.Equal("b", inner.Calls[1].Request.TargetPath);
        inner.Calls[1].Done.SetResult(new DownloadResult(false, null, "checksum mismatch"));
        Assert.False((await b).Ok);
    }

    [Fact]
    public void A_cancelled_download_does_not_block_the_next()
    {
        SynchronizationContext.SetSynchronizationContext(null);
        var inner = new FakeDownloads();
        var serial = new SerialDownloads(inner);
        using var cts = new CancellationTokenSource();
        var a = serial.DownloadAsync(Req("a"), null, cts.Token);
        var b = serial.DownloadAsync(Req("b"), null, CancellationToken.None);
        cts.Cancel();
        Assert.True(a.IsCanceled);
        Assert.Equal(2, inner.Calls.Count);
        Assert.False(b.IsCompleted);
    }

    [Fact]
    public void Data_folder_is_the_inner_one()
    {
        Assert.Equal("/data", new SerialDownloads(new FakeDownloads()).DataFolder);
    }

    // Review carry-over (e): a call still waiting for its turn must honour its own ct — completing as Canceled the
    // moment the token fires, without ever starting the inner download for it.
    [Fact]
    public void A_waiting_call_is_cancelled_without_starting_its_inner_download()
    {
        SynchronizationContext.SetSynchronizationContext(null);
        var inner = new FakeDownloads();
        var serial = new SerialDownloads(inner);
        var a = serial.DownloadAsync(Req("a"), null, CancellationToken.None);
        using var cts = new CancellationTokenSource();
        var b = serial.DownloadAsync(Req("b"), null, cts.Token);
        Assert.Single(inner.Calls);   // b is still queued behind a
        cts.Cancel();
        Assert.True(b.IsCanceled);   // cancelled while queued, before a ever finished
        Assert.Single(inner.Calls);   // b's inner download never started
        inner.Calls[0].Done.SetResult(new DownloadResult(true, "/data/a", null));
        Assert.True(a.IsCompletedSuccessfully);
        Assert.Single(inner.Calls);   // still just a — a cancelled waiter never runs, even once it would be its turn
    }

    // Review carry-over (e): a call queued behind one that FAILS (a legitimate Ok=false result, not a thrown exception
    // or a cancellation) must still start once that failure resolves.
    [Fact]
    public async Task A_call_queued_behind_one_that_fails_still_starts_once_it_finishes()
    {
        SynchronizationContext.SetSynchronizationContext(null);
        var inner = new FakeDownloads();
        var serial = new SerialDownloads(inner);
        var a = serial.DownloadAsync(Req("a"), null, CancellationToken.None);
        var b = serial.DownloadAsync(Req("b"), null, CancellationToken.None);
        Assert.Single(inner.Calls);   // b is still queued behind a
        inner.Calls[0].Done.SetResult(new DownloadResult(false, null, "offline"));
        Assert.False((await a).Ok);
        Assert.Equal(2, inner.Calls.Count);   // b started right after a's failure resolved
        Assert.Equal("b", inner.Calls[1].Request.TargetPath);
    }

    // Fix round 1 (Important): cancelling a MIDDLE waiter must not release the one behind it while the one AHEAD of it
    // is still running. Reproduced by the reviewer with a running, b and c waiting: cancelling b used to free c
    // immediately (c jumped ahead of a — calls=[a,c], maxInFlight=2), because b's finally unconditionally released
    // its own "done" signal regardless of whether b ever reached its turn.
    [Fact]
    public void Cancelling_a_middle_waiter_does_not_let_the_one_behind_it_jump_the_still_running_call()
    {
        SynchronizationContext.SetSynchronizationContext(null);
        var inner = new FakeDownloads();
        var serial = new SerialDownloads(inner);
        var a = serial.DownloadAsync(Req("a"), null, CancellationToken.None);
        using var ctsB = new CancellationTokenSource();
        var b = serial.DownloadAsync(Req("b"), null, ctsB.Token);
        var c = serial.DownloadAsync(Req("c"), null, CancellationToken.None);
        Assert.Single(inner.Calls);   // only a has started
        ctsB.Cancel();
        Assert.True(b.IsCanceled);
        Assert.Single(inner.Calls);   // b's cancellation must not let c start while a is still running
        inner.Calls[0].Done.SetResult(new DownloadResult(true, "/data/a", null));
        Assert.True(a.IsCompletedSuccessfully);
        Assert.Equal(2, inner.Calls.Count);   // only now, once a finishes, does c start
        Assert.Equal("c", inner.Calls[1].Request.TargetPath);
    }
}
