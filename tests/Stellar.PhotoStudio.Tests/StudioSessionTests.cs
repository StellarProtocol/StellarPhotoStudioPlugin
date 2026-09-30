using System.Threading.Tasks;
using Stellar.Abstractions.Domain;
using Stellar.Abstractions.Services;
using Xunit;
namespace Stellar.PhotoStudio.Tests;

public sealed class StudioSessionTests
{
    private sealed class FakeCapture : IScreenCapture
    {
        public TaskCompletionSource<CaptureResult> Tcs = new();
        public int Calls;
        public bool IsCapturing { get; private set; }
        public async Task<CaptureResult> CaptureAsync(CaptureRequest r) { Calls++; IsCapturing = true; var x = await Tcs.Task; IsCapturing = false; return x; }
    }

    [Fact]
    public async Task Second_press_while_capturing_is_ignored()
    {
        var cap = new FakeCapture();
        var s = new StudioSession(cap, () => new CaptureRequest(), _ => { });
        s.Open();
        var first = s.CaptureAsync();
        Assert.Equal(StudioState.Capturing, s.State);
        Assert.Null(await s.CaptureAsync());
        cap.Tcs.SetResult(CaptureResult.Ok("p", 1, 1));
        await first;
        Assert.Equal(1, cap.Calls);
        Assert.Equal(StudioState.Open, s.State);
    }

    [Fact]
    public async Task Capture_while_closed_returns_to_closed()
    {
        var cap = new FakeCapture();
        var s = new StudioSession(cap, () => new CaptureRequest(), _ => { });
        var t = s.CaptureAsync();
        cap.Tcs.SetResult(CaptureResult.Fail("x"));
        await t;
        Assert.Equal(StudioState.Closed, s.State);
    }

    [Fact]
    public async Task Close_during_capture_closes_after_it()
    {
        var cap = new FakeCapture();
        var s = new StudioSession(cap, () => new CaptureRequest(), _ => { });
        s.Open();
        var t = s.CaptureAsync();
        s.Close();
        cap.Tcs.SetResult(CaptureResult.Ok("p", 1, 1));
        await t;
        Assert.Equal(StudioState.Closed, s.State);
    }

    [Fact]
    public async Task Result_is_reported()
    {
        var cap = new FakeCapture();
        CaptureResult? seen = null;
        var s = new StudioSession(cap, () => new CaptureRequest(), r => seen = r);
        var t = s.CaptureAsync();
        cap.Tcs.SetResult(CaptureResult.Ok("p", 2, 2));
        await t;
        Assert.Equal("p", seen!.Path);
    }
}
