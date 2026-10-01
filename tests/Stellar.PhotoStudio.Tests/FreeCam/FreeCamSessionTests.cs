using System;
using System.Numerics;
using Stellar.Abstractions.Domain;
using Stellar.PhotoStudio.FreeCam;
using Xunit;

namespace Stellar.PhotoStudio.Tests.FreeCam;

// Spec § 3 controls, § 4 freeze/look-at, § 7 one release path / exception → release + toast / death keeps camera.
public sealed class FreeCamSessionTests
{
    private static float Dist(Position3D p, Vector3 c) => Vector3.Distance(new Vector3(p.X, p.Y, p.Z), c);

    [Fact]
    public void Enter_takes_camera_shield_and_the_entry_hides()
    {
        var r = new SessionRig();
        Assert.True(r.Session.Enter());
        Assert.True(r.Session.Active);
        Assert.Equal(1, r.Shield.Raised);
        Assert.Single(r.Visibility.Hides);
        Assert.Equal(VisibilityLayers.GameHud | VisibilityLayers.Nameplates, r.Visibility.Hides[0].Layers);
        Assert.Equal(r.Snapshot.LocalEntityId, r.Session.Subject);
        Assert.Equal(FreeCamMode.Orbit, r.Session.Mode);
    }

    [Fact]
    public void Entry_hides_are_optional()
    {
        var r = new SessionRig();
        r.Settings.SetEntryHides(false);
        r.Session.Enter();
        Assert.Empty(r.Visibility.Hides);
    }

    [Fact]
    public void A_busy_camera_refuses_and_leaks_nothing()
    {
        var r = new SessionRig();
        r.Camera.Busy = true;
        Assert.False(r.Session.Enter());
        Assert.Equal(0, r.Shield.Raised);
        Assert.Empty(r.Visibility.Hides);
        Assert.Equal(FreeCamNotice.Busy, r.Notices[0].Notice);
    }

    [Fact]
    public void Exit_releases_everything_exactly_once_and_says_nothing()
    {
        var r = new SessionRig();
        r.Settings.SetLookAt(true);
        r.Session.Enter();
        r.Press(Stellar.Abstractions.Domain.StellarKeyCode.Space);
        r.Session.Exit();
        r.Session.Exit();
        Assert.Equal(1, r.Camera.Control.Disposed);
        Assert.Equal(1, r.Shield.Handle.Disposed);
        Assert.Equal(1, r.Visibility.Hides[0].Handle.Disposed);
        Assert.Equal(1, r.Freeze.Tokens[0].Disposed);
        Assert.Equal(1, r.Camera.LookAts[0].Disposed);
        Assert.False(r.Session.Active);
        Assert.Empty(r.Notices);
    }

    [Fact]
    public void A_framework_release_releases_the_rest_and_tells_the_player()
    {
        var r = new SessionRig();
        r.Session.Enter();
        r.Camera.FrameworkRelease(CameraReleaseReason.SceneChanged);
        Assert.False(r.Session.Active);
        Assert.Equal(0, r.Camera.Control.Disposed);       // the framework already ended it
        Assert.Equal(1, r.Shield.Handle.Disposed);
        Assert.Equal(1, r.Visibility.Hides[0].Handle.Disposed);
        Assert.Equal((FreeCamNotice.Released, CameraReleaseReason.SceneChanged), r.Notices[0]);
    }

    [Fact]
    public void An_exception_in_the_frame_releases_everything_and_reports_an_error()
    {
        var r = new SessionRig();
        r.Session.Enter();
        r.Transforms.Throw = true;
        r.Frame();
        Assert.False(r.Session.Active);
        Assert.Equal(1, r.Camera.Control.Disposed);
        Assert.Equal(1, r.Shield.Handle.Disposed);
        Assert.Equal(FreeCamNotice.Error, r.Notices[0].Notice);
    }

    [Fact]
    public void Death_ends_the_freeze_but_keeps_the_camera()
    {
        var r = new SessionRig();
        r.Session.Enter();
        r.Session.ToggleFreeze();
        r.Session.OnLocalDeath();
        Assert.False(r.Session.Frozen);
        Assert.Equal(1, r.Freeze.Tokens[0].Disposed);
        Assert.True(r.Session.Active);
    }

    [Fact]
    public void Space_freezes_and_the_leash_centre_stays_where_the_subject_was()
    {
        var r = new SessionRig();
        r.Settings.SetLeash(5f, save: false);
        r.Session.Enter();
        r.Press(Stellar.Abstractions.Domain.StellarKeyCode.Space);
        Assert.True(r.Session.Frozen);
        r.Transforms.Positions[1] = new Position3D(100, 0, 0);      // the logical position runs on underneath
        for (var i = 0; i < 60; i++) r.Frame();
        Assert.True(Dist(r.Camera.Control.LastPosition, Vector3.Zero) <= 5.001f);
        r.Press(Stellar.Abstractions.Domain.StellarKeyCode.Space);
        Assert.False(r.Session.Frozen);
    }

    [Fact]
    public void A_framework_unfreeze_clears_the_frozen_state()
    {
        var r = new SessionRig();
        r.Session.Enter();
        r.Session.ToggleFreeze();
        r.Freeze.FrameworkReleaseAll();
        Assert.False(r.Session.Frozen);
        Assert.True(r.Session.Active);
    }

    [Fact]
    public void Click_picks_a_subject_in_orbit_only_and_never_through_a_window()
    {
        var r = new SessionRig();
        r.Transforms.Positions[7] = new Position3D(3, 0, 3);
        r.Picker.Result = new EntityId(7);
        r.Session.Enter();
        r.OverUi = true;
        r.Shield.Handle.Lmb = true; r.Frame(); r.Shield.Handle.Lmb = false; r.Frame();
        Assert.Equal(r.Snapshot.LocalEntityId, r.Session.Subject);
        Assert.Equal(0, r.Picker.Calls);
        r.OverUi = false;
        r.Shield.Handle.Lmb = true; r.Frame(); r.Shield.Handle.Lmb = false; r.Frame();
        Assert.Equal(new EntityId(7), r.Session.Subject);
        r.Session.ToggleMode();
        r.Picker.Result = new EntityId(1);
        r.Shield.Handle.Lmb = true; r.Frame();
        Assert.Equal(new EntityId(7), r.Session.Subject);          // fly mode: clicks do not pick
    }

    [Fact]
    public void Backspace_returns_to_yourself()
    {
        var r = new SessionRig();
        r.Transforms.Positions[7] = new Position3D(3, 0, 3);
        r.Session.Enter();
        r.Session.SetSubject(new EntityId(7));
        r.Press(Stellar.Abstractions.Domain.StellarKeyCode.Backspace);
        Assert.Equal(r.Snapshot.LocalEntityId, r.Session.Subject);
    }

    [Fact]
    public void A_picked_character_who_leaves_hands_the_subject_back_to_you()
    {
        var r = new SessionRig();
        r.Transforms.Positions[7] = new Position3D(3, 0, 3);
        r.Session.Enter();
        r.Session.SetSubject(new EntityId(7));
        r.Transforms.Positions.Remove(7);
        r.Frame();
        Assert.Equal(r.Snapshot.LocalEntityId, r.Session.Subject);
    }

    [Fact]
    public void The_camera_never_leaves_the_leash()
    {
        var r = new SessionRig();
        r.Session.Enter();
        r.Session.ToggleMode();
        r.Session.ScriptedIntent = new CamIntent { Move = new Vector3(1, 1, 1), Shift = true };
        for (var i = 0; i < 600; i++) r.Frame(0.05f);
        Assert.True(Dist(r.Camera.Control.LastPosition, Vector3.Zero) <= r.Settings.Leash + 0.001f);
        Assert.True(r.Session.Distance <= r.Settings.Leash + 0.001f);
    }

    [Fact]
    public void Entering_starts_at_the_game_pose_with_no_jump()
    {
        var r = new SessionRig();
        r.Session.Enter();
        r.Frame();
        Assert.True(Dist(r.Camera.Control.LastPosition, new Vector3(0, 2, -6)) < 1e-3f);
        Assert.Equal(0f, r.Camera.Control.LastYaw, 3);
        Assert.Equal(10f, r.Camera.Control.LastPitch, 3);
    }

    [Fact]
    public void Tab_switches_mode_without_moving_the_camera()
    {
        var r = new SessionRig();
        r.Session.Enter();
        r.Frame();
        var before = r.Camera.Control.LastPosition;
        r.Press(Stellar.Abstractions.Domain.StellarKeyCode.Tab);
        Assert.Equal(FreeCamMode.Fly, r.Session.Mode);
        Assert.True(Dist(r.Camera.Control.LastPosition, new Vector3(before.X, before.Y, before.Z)) < 1e-3f);
        r.Press(Stellar.Abstractions.Domain.StellarKeyCode.Tab);
        Assert.Equal(FreeCamMode.Orbit, r.Session.Mode);
        Assert.True(Dist(r.Camera.Control.LastPosition, new Vector3(before.X, before.Y, before.Z)) < 1e-3f);
    }

    [Fact]
    public void R_resets_to_the_entry_pose()
    {
        var r = new SessionRig();
        r.Session.Enter();
        r.Session.ScriptedIntent = new CamIntent { Looking = true, LookX = 50f, Wheel = 2f };
        for (var i = 0; i < 10; i++) r.Frame();
        r.Session.ScriptedIntent = null;
        r.Press(Stellar.Abstractions.Domain.StellarKeyCode.R);
        Assert.True(Dist(r.Camera.Control.LastPosition, new Vector3(0, 2, -6)) < 1e-3f);
        Assert.Equal(45f, r.Session.Fov);
        Assert.Equal(0f, r.Session.Roll);
    }

    [Fact]
    public void Look_at_toggle_takes_and_returns_the_handle()
    {
        var r = new SessionRig();
        r.Session.Enter();
        r.Session.SetLookAt(true);
        Assert.Single(r.Camera.LookAts);
        r.Session.SetLookAt(false);
        Assert.Equal(1, r.Camera.LookAts[0].Disposed);
        Assert.False(r.Settings.LookAt);
    }

    [Fact]
    public void Roll_and_fov_follow_the_intent_within_limits()
    {
        var r = new SessionRig();
        r.Session.Enter();
        r.Session.ScriptedIntent = new CamIntent { RollAxis = 1f };
        r.Frame(1f);
        Assert.Equal(60f, r.Session.Roll, 3);
        r.Frame(1f);
        Assert.Equal(90f, r.Session.Roll, 3);
        r.Session.ScriptedIntent = new CamIntent { Shift = true, Wheel = 1f };
        r.Frame();
        Assert.Equal(43f, r.Session.Fov, 3);
        Assert.Equal(43f, r.Camera.Control.Fov, 3);
    }

    [Fact]
    public void H_toggles_the_hint_line_setting()
    {
        var r = new SessionRig();
        r.Session.Enter();
        r.Press(Stellar.Abstractions.Domain.StellarKeyCode.H);
        Assert.True(r.Settings.HintHidden);
    }

    [Fact]
    public void Escape_exits_and_releases_everything_without_a_toast()   // spec D8 (owner 2026-10-01)
    {
        var r = new SessionRig();
        r.Session.Enter();
        r.Session.ToggleFreeze();
        r.Shield.Handle.Held.Add(Stellar.Abstractions.Domain.StellarKeyCode.Escape);
        r.Frame();
        Assert.False(r.Session.Active);
        Assert.Equal(1, r.Camera.Control.Disposed);
        Assert.Equal(1, r.Shield.Handle.Disposed);
        Assert.Equal(1, r.Freeze.Tokens[0].Disposed);
        Assert.Equal(1, r.Visibility.Hides[0].Handle.Disposed);
        Assert.Empty(r.Notices);
    }
}
