using Stellar.Abstractions.Domain;
using Stellar.PhotoStudio.FreeCam;
using Xunit;

namespace Stellar.PhotoStudio.Tests.FreeCam;

// Whole-feature review fixes (2026-10-01), pinned per rules § 3:
//   P1 — a focused Stellar text field owns the keys and the wheel: nothing moves, no key edge fires, Esc stays put;
//        mouse-look carries on.
//   P2 — a wheel turn over Photo Studio's own windows, or a right-button drag that STARTS over them, moves nothing.
//   P3 — Esc with the "?" popover open closes the popover only.
//   P8 — entry applies exactly the hide layers the player chose (spec §§ 3/5).
public sealed class FreeCamSessionInputGateTests
{
    private static readonly StellarKeyCode[] TypedKeys =
    {
        StellarKeyCode.W, StellarKeyCode.A, StellarKeyCode.S, StellarKeyCode.D, StellarKeyCode.Q, StellarKeyCode.E,
        StellarKeyCode.Z, StellarKeyCode.C, StellarKeyCode.Space, StellarKeyCode.Tab, StellarKeyCode.R, StellarKeyCode.H,
        StellarKeyCode.Backspace, StellarKeyCode.Escape,
    };

    private static SessionRig EnteredAndSettled()
    {
        var r = new SessionRig();
        r.Transforms.Positions[7] = new Position3D(3, 0, 3);
        r.Session.Enter();
        r.Frame();
        return r;
    }

    [Fact]
    public void Typing_into_a_focused_field_changes_nothing()
    {
        var r = EnteredAndSettled();
        r.Session.SetSubject(new EntityId(7));
        r.Frame();
        var pose = r.Camera.Control.LastPosition;
        var (yaw, fov, distance) = (r.Camera.Control.LastYaw, r.Session.Fov, r.Session.Distance);
        r.Shield.Handle.Focused = true;
        foreach (var k in TypedKeys) r.Shield.Handle.Held.Add(k);
        r.Shield.Handle.WheelValue = 3f;
        for (var i = 0; i < 30; i++) r.Frame(0.05f);

        Assert.True(r.Session.Active);                                     // Esc belongs to the field
        Assert.Equal(pose, r.Camera.Control.LastPosition);
        Assert.Equal(yaw, r.Camera.Control.LastYaw);
        Assert.Equal(fov, r.Session.Fov);
        Assert.Equal(distance, r.Session.Distance, 4);
        Assert.Equal(0f, r.Session.Roll);
        Assert.Equal(FreeCamMode.Orbit, r.Session.Mode);                   // Tab
        Assert.False(r.Session.Frozen);                                    // Space
        Assert.False(r.Settings.HintHidden);                               // H
        Assert.Equal(new EntityId(7), r.Session.Subject);                  // Backspace
    }

    [Fact]
    public void Esc_with_a_focused_field_does_not_exit_and_does_not_fire_once_focus_leaves()
    {
        var r = EnteredAndSettled();
        r.Shield.Handle.Focused = true;
        r.Shield.Handle.Held.Add(StellarKeyCode.Escape);
        r.Shield.Handle.Held.Add(StellarKeyCode.Space);
        r.Frame();
        Assert.True(r.Session.Active);
        r.Shield.Handle.Focused = false;                                   // keys still held as the field lets go
        r.Frame();
        Assert.True(r.Session.Active);
        Assert.False(r.Session.Frozen);
    }

    [Fact]
    public void Mouse_look_carries_on_while_a_field_has_focus()
    {
        var r = EnteredAndSettled();
        var yaw = r.Camera.Control.LastYaw;
        r.Shield.Handle.Focused = true;
        r.Shield.Handle.Rmb = true;
        r.Shield.Handle.Delta = (40f, 0f);
        r.Frame();
        Assert.NotEqual(yaw, r.Camera.Control.LastYaw);
    }

    [Fact]
    public void Without_focus_the_same_keys_still_work()
    {
        var r = EnteredAndSettled();
        var pose = r.Camera.Control.LastPosition;
        r.Shield.Handle.Held.Add(StellarKeyCode.W);
        for (var i = 0; i < 10; i++) r.Frame(0.05f);
        Assert.NotEqual(pose, r.Camera.Control.LastPosition);
        r.Shield.Handle.Held.Clear();
        r.Press(StellarKeyCode.Space);
        Assert.True(r.Session.Frozen);
        r.Press(StellarKeyCode.Escape);
        Assert.False(r.Session.Active);
    }

    [Fact]
    public void The_wheel_over_an_own_window_does_not_zoom()
    {
        var r = EnteredAndSettled();
        var distance = r.Session.Distance;
        r.OverUi = true;
        r.Shield.Handle.WheelValue = 2f;
        for (var i = 0; i < 5; i++) r.Frame();
        Assert.Equal(distance, r.Session.Distance, 4);
        r.OverUi = false;
        r.Frame();
        Assert.True(r.Session.Distance < distance - 0.1f);
    }

    [Fact]
    public void A_right_drag_that_starts_over_an_own_window_never_rotates_even_after_leaving_it()
    {
        var r = EnteredAndSettled();
        var yaw = r.Camera.Control.LastYaw;
        r.OverUi = true;
        r.Shield.Handle.Rmb = true;
        r.Shield.Handle.Delta = (40f, 10f);
        r.Frame();
        r.OverUi = false;                                                  // dragged off the panel, button still held
        for (var i = 0; i < 5; i++) r.Frame();
        Assert.Equal(yaw, r.Camera.Control.LastYaw);
        r.Shield.Handle.Rmb = false;
        r.Frame();
        r.Shield.Handle.Rmb = true;                                        // a fresh press outside the windows rotates
        r.Frame();
        Assert.NotEqual(yaw, r.Camera.Control.LastYaw);
    }

    [Fact]
    public void A_right_drag_that_starts_outside_keeps_rotating_over_a_window()
    {
        var r = EnteredAndSettled();
        r.Shield.Handle.Rmb = true;
        r.Shield.Handle.Delta = (40f, 0f);
        r.Frame();
        var yaw = r.Camera.Control.LastYaw;
        r.OverUi = true;
        r.Frame();
        Assert.NotEqual(yaw, r.Camera.Control.LastYaw);
    }

    [Fact]
    public void The_own_window_hit_test_runs_only_on_a_wheel_turn_or_a_right_press()
    {
        var r = EnteredAndSettled();
        r.Shield.Handle.Held.Add(StellarKeyCode.W);
        for (var i = 0; i < 20; i++) r.Frame();
        Assert.Equal(0, r.HitTests);
        r.Shield.Handle.Rmb = true;
        for (var i = 0; i < 20; i++) r.Frame();
        Assert.Equal(1, r.HitTests);
    }

    [Fact]
    public void Esc_with_the_help_popover_open_closes_the_popover_only()
    {
        var r = EnteredAndSettled();
        r.ModalOpen = true;
        r.Press(StellarKeyCode.Escape);
        Assert.True(r.Session.Active);
        Assert.Equal(1, r.Dismissed);
        Assert.False(r.ModalOpen);
        r.Press(StellarKeyCode.Escape);                                    // nothing left to close: now Esc exits
        Assert.False(r.Session.Active);
        Assert.Equal(1, r.Dismissed);
    }

    [Theory]
    [InlineData(VisibilityLayers.OtherPlayers)]
    [InlineData(VisibilityLayers.GameHud | VisibilityLayers.OtherPlayers)]
    [InlineData(VisibilityLayers.GameHud | VisibilityLayers.Nameplates | VisibilityLayers.OtherPlayers)]
    public void Entry_hides_exactly_the_chosen_layers_and_releases_them_on_exit(VisibilityLayers chosen)
    {
        var r = new SessionRig();
        foreach (var layer in new[] { VisibilityLayers.GameHud, VisibilityLayers.Nameplates, VisibilityLayers.OtherPlayers })
            r.Settings.SetEntryHide(layer, (chosen & layer) != 0);
        r.Session.Enter();
        Assert.Single(r.Visibility.Hides);
        Assert.Equal(chosen, r.Visibility.Hides[0].Layers);
        r.Session.Exit();
        Assert.Equal(1, r.Visibility.Hides[0].Handle.Disposed);
    }
}
