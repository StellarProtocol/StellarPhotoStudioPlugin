using Xunit;

namespace Stellar.PhotoStudio.Tests;

public sealed class CaptureGateTests
{
    [Fact]
    public void Boost_settle_ticks_then_fires_once()
    {
        var g = new CaptureGate();
        g.Arm(2);
        Assert.False(g.Tick(false));
        Assert.False(g.Tick(false));
        Assert.True(g.Tick(false));
        Assert.False(g.Armed);
        Assert.False(g.Tick(false));
    }

    // R1 pin: the shutter waits while a ReShade switch is still being applied (D8).
    [Fact]
    public void Holds_while_a_reshade_switch_is_pending_then_fires()
    {
        var g = new CaptureGate();
        g.Arm(0);
        Assert.False(g.Tick(true));
        Assert.False(g.Tick(true));
        Assert.True(g.Armed);
        Assert.True(g.Tick(false));
    }

    [Fact]
    public void Unarmed_never_fires()
    {
        var g = new CaptureGate();
        Assert.False(g.Tick(false));
    }
}
