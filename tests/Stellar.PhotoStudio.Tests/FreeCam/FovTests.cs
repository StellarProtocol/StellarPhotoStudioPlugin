using System.Linq;
using Stellar.Abstractions.Domain;
using Stellar.PhotoStudio.FreeCam;
using Xunit;

namespace Stellar.PhotoStudio.Tests.FreeCam;

// 1.7.0 (player request "Photo Studio - FOV Slider"): the Look-tab slider and the FOV hotkeys set the free camera's
// field of view, clamped to 10–100°, reset to the game's FOV at entry, and never touch the game camera while off.
public sealed class FovTests
{
    [Fact]
    public void Slider_sets_and_clamps_the_fov_and_the_next_frame_applies_it()
    {
        var r = new SessionRig();
        r.Session.Enter();
        r.Session.SetFov(30f);
        r.Frame();
        Assert.Equal(30f, r.Camera.Control.Fov);
        r.Session.SetFov(500f);
        Assert.Equal(CameraMath.MaxFov, r.Session.Fov);
        r.Session.SetFov(-5f);
        Assert.Equal(CameraMath.MinFov, r.Session.Fov);
    }

    [Fact]
    public void Reset_returns_to_the_games_fov_at_entry()
    {
        var r = new SessionRig();
        r.Session.Enter();
        Assert.Equal(45f, r.Session.EntryFov);   // FakeCamera's GamePose FOV
        r.Session.NudgeFov(20f);
        Assert.Equal(65f, r.Session.Fov);
        r.Session.ResetFov();
        Assert.Equal(45f, r.Session.Fov);
    }

    [Fact]
    public void Nothing_changes_while_the_free_camera_is_off()
    {
        var r = new SessionRig();
        r.Session.SetFov(30f);
        r.Session.NudgeFov(10f);
        r.Session.ResetFov();
        Assert.Equal(0f, r.Session.Fov);
        Assert.Equal(45f, r.Camera.Control.Fov);
    }

    [Fact]
    public void Shift_wheel_still_steps_the_fov()
    {
        var r = new SessionRig();
        r.Session.Enter();
        var before = r.Session.Fov;
        r.Session.ScriptedIntent = new CamIntent { Shift = true, Wheel = 1f };
        r.Frame();
        Assert.Equal(before - CameraMath.FovStep, r.Session.Fov);
    }

    [Fact]
    public void A_press_steps_one_wheel_notch_in_or_out()
    {
        Assert.Equal(-CameraMath.FovStep, FovKeys.PressStep(zoomIn: true));
        Assert.Equal(CameraMath.FovStep, FovKeys.PressStep(zoomIn: false));
    }

    [Fact]
    public void A_held_key_repeats_only_after_the_delay_then_at_the_rate()
    {
        var k = new FovKeys();
        Assert.Equal(0f, k.Tick(inHeld: true, outHeld: false, dt: 0.1f));
        Assert.Equal(0f, k.Tick(true, false, 0.1f));
        Assert.Equal(0f, k.Tick(true, false, 0.1f));                                     // 0.30 s held
        Assert.Equal(-0.05f * FovKeys.RatePerSecond, k.Tick(true, false, 0.1f), 3);      // 0.40 s: 0.05 s past the delay
        Assert.Equal(-0.1f * FovKeys.RatePerSecond, k.Tick(true, false, 0.1f), 3);
    }

    [Fact]
    public void Releasing_restarts_the_delay_and_both_keys_cancel()
    {
        var k = new FovKeys();
        for (var i = 0; i < 5; i++) k.Tick(false, true, 0.1f);
        Assert.Equal(0f, k.Tick(false, false, 0.1f));
        Assert.Equal(0f, k.Tick(false, true, 0.1f));   // delay again
        var both = new FovKeys();
        for (var i = 0; i < 5; i++) both.Tick(true, true, 0.1f);
        Assert.Equal(0f, both.Tick(true, true, 0.1f));
    }

    [Fact]
    public void A_hitch_does_not_jump_the_fov()
    {
        var k = new FovKeys();
        for (var i = 0; i < 5; i++) k.Tick(false, true, 0.1f);                    // held 0.5 s: repeating
        Assert.Equal(0.1f * FovKeys.RatePerSecond, k.Tick(false, true, 3f), 3);   // a 3 s hitch counts as 0.1 s
    }

    [Fact]
    public void Fov_hotkeys_default_to_brackets_and_backslash_and_never_collide()
    {
        KeyBinding? D(string id) => StudioHotkeys.Defaults.Single(d => d.Id == id).Key;
        Assert.Equal(new KeyBinding(StellarKeyCode.RightBracket), D(StudioHotkeys.FovIn));
        Assert.Equal(new KeyBinding(StellarKeyCode.LeftBracket), D(StudioHotkeys.FovOut));
        Assert.Equal(new KeyBinding(StellarKeyCode.Backslash), D(StudioHotkeys.FovReset));
        var bound = StudioHotkeys.Defaults.Where(d => d.Key is not null).Select(d => d.Key!.Value).ToList();
        Assert.Equal(bound.Count, bound.Distinct().Count());
    }
}
