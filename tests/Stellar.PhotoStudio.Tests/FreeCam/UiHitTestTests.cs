using Stellar.Abstractions.Domain;
using Stellar.PhotoStudio.FreeCam;
using Xunit;

namespace Stellar.PhotoStudio.Tests.FreeCam;

// Click-to-orbit must never pick through Photo Studio's own windows (panel, HUD, tip).
public sealed class UiHitTestTests
{
    [Fact]
    public void Inside_and_outside_at_scale_one()
    {
        var r = new WindowRect(100, 50, 200, 100);
        Assert.True(UiHitTest.Contains(r, 150, 60, 1f));
        Assert.False(UiHitTest.Contains(r, 99, 60, 1f));
        Assert.False(UiHitTest.Contains(r, 150, 151, 1f));
    }

    [Fact]
    public void Screen_pixels_are_converted_to_canvas_units()
    {
        var r = new WindowRect(100, 50, 200, 100);       // canvas units
        Assert.True(UiHitTest.Contains(r, 160, 80, 1.5f)); // 106.7, 53.3 in canvas units
        Assert.False(UiHitTest.Contains(r, 140, 60, 1.5f));
    }

    [Fact]
    public void An_unmounted_or_zero_sized_window_never_contains() =>
        Assert.False(UiHitTest.Contains(default, 0, 0, 1f));
}
