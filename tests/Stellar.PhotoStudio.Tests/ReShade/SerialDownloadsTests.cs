using System;
using System.Threading;
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
    public void A_second_download_starts_only_when_the_first_is_done()
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
        Assert.False(b.Result.Ok);
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
}
