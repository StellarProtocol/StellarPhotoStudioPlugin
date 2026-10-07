using System.Linq;
using Stellar.Abstractions.Domain;
using Xunit;

namespace Stellar.PhotoStudio.Tests.FreeCam;

// Spec § 3: Alt+F10 = free camera. 1.0.0 had hide-all there; it moves to Ctrl+F10. No two defaults may collide.
public sealed class StudioHotkeysTests
{
    private static KeyBinding? Default(string id) => StudioHotkeys.Defaults.Single(d => d.Id == id).Key;

    [Fact]
    public void Free_camera_is_Alt_F10() =>
        Assert.Equal(new KeyBinding(StellarKeyCode.F10, ModifierKeys.Alt), Default(StudioHotkeys.FreeCam));

    [Fact]
    public void Hide_all_moved_to_Ctrl_F10() =>
        Assert.Equal(new KeyBinding(StellarKeyCode.F10, ModifierKeys.Ctrl), Default(StudioHotkeys.HideAll));

    [Fact]
    public void Capture_and_panel_keep_their_1_0_defaults()
    {
        Assert.Equal(new KeyBinding(StellarKeyCode.F10), Default(StudioHotkeys.Capture));
        Assert.Equal(new KeyBinding(StellarKeyCode.F10, ModifierKeys.Shift), Default(StudioHotkeys.Panel));
    }

    [Fact]
    public void Defaults_never_collide()
    {
        var bound = StudioHotkeys.Defaults.Where(d => d.Key is not null).Select(d => d.Key!.Value).ToList();
        Assert.Equal(bound.Count, bound.Distinct().Count());
    }

    // Owner 2026-10-08 ("Close Photo Studio" hotkey with a default; players can rebind it).
    [Fact]
    public void Close_is_Ctrl_Shift_F10() =>
        Assert.Equal(new KeyBinding(StellarKeyCode.F10, ModifierKeys.Ctrl | ModifierKeys.Shift), Default(StudioHotkeys.Close));
}
