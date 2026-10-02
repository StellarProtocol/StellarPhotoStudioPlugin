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
        r.Settings.SetEntryHide(VisibilityLayers.GameHud, false);
        r.Settings.SetEntryHide(VisibilityLayers.Nameplates, false);
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

    // Scene-stays spec § 1 (2026-10-02): exit returns the camera, shield and look-at — the freeze is the scene's, and so
    // are the entry hides while the scene is set (review I-1, 2026-10-02: they used to be released here; the spec says
    // they stay). They go, exactly once, when the scene ends.
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
        Assert.Equal(0, r.Visibility.Hides[0].Handle.Disposed);   // frozen → the scene keeps the entry hides
        Assert.True(r.Scene.HoldsEntryHide);
        Assert.Equal(0, r.Freeze.Tokens[0].Disposed);   // the freeze stays with the scene
        Assert.True(r.Scene.Frozen);
        Assert.Equal(1, r.Camera.LookAts[0].Disposed);
        Assert.False(r.Session.Active);
        Assert.Empty(r.Notices);
        r.Scene.Reset();                                          // the scene ends → the hides go, once
        r.Scene.Reset();
        Assert.Equal(1, r.Visibility.Hides[0].Handle.Disposed);
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
    public void A_frame_exception_reaches_the_log_because_the_toast_points_there()   // review P4
    {
        var r = new SessionRig();
        r.Session.Enter();
        r.Transforms.Throw = true;
        r.Frame();
        Assert.Single(r.Warnings);
        Assert.Contains("game read failed", r.Warnings[0]);
        Assert.Contains(nameof(InvalidOperationException), r.Warnings[0]);
    }

    [Fact]
    public void Death_ends_the_freeze_but_keeps_the_camera()
    {
        var r = new SessionRig();
        r.Session.Enter();
        r.Session.ToggleFreeze();
        r.Scene.OnLocalDeath();
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

    // Owner-run smoke 2026-10-02: selecting a far NPC kept the camera 20 m away (pivot only), so the posed person was tiny.
    [Fact]
    public void Selecting_a_far_person_frames_them_at_most_FrameDistance_away()
    {
        var r = new SessionRig();
        r.Transforms.Positions[7] = new Position3D(20, 0, 20);
        r.Session.Enter();
        r.Session.SetSubject(new EntityId(7));
        for (var i = 0; i < 600; i++) r.Frame();   // let the smoothing settle
        // Distance is to the subject's feet; the orbit distance is to the pivot ~1.4 m above them.
        Assert.True(r.Session.Distance <= FreeCamSession.FrameDistance + 1.5f, $"distance {r.Session.Distance}");
    }

    [Fact]
    public void Selecting_a_close_person_keeps_the_current_distance()
    {
        var r = new SessionRig();
        r.Session.Enter();
        for (var i = 0; i < 60; i++) r.Frame();
        // Zoom all the way in first, so "before" is a camera genuinely already inside FrameDistance (the old
        // assertion's `Max(before, FrameDistance) + 1.5` passed even when a 2 m camera got pushed out to ~5.5 m —
        // it never actually proved the camera stayed close).
        r.Session.ScriptedIntent = new CamIntent { Wheel = 1f };
        for (var i = 0; i < 300; i++) r.Frame();
        r.Session.ScriptedIntent = null;
        for (var i = 0; i < 60; i++) r.Frame();
        var before = r.Session.Distance;
        Assert.True(before < FreeCamSession.FrameDistance, $"setup: before ({before}) should already be inside FrameDistance");
        r.Transforms.Positions[7] = new Position3D(0.05f, 0, 0.05f);   // right where you're already standing
        r.Session.SetSubject(new EntityId(7));
        for (var i = 0; i < 600; i++) r.Frame();
        // Like-for-like: both readings are the same orbit Distance. A camera already closer than FrameDistance must
        // not be pushed further away by picking a nearby person.
        Assert.True(r.Session.Distance <= before + 0.25f, $"before {before} after {r.Session.Distance}");
    }

    // P3 (review 2026-10-02): re-picking the subject you're already orbiting (clicking them again, or Backspace
    // while already on yourself) must not reframe the camera — SetSubject now early-returns on id == Subject.
    [Fact]
    public void Resubmitting_the_current_subject_does_not_reframe_a_distant_camera()
    {
        var r = new SessionRig();
        r.Transforms.Positions[7] = new Position3D(3, 0, 3);
        r.Session.Enter();
        r.Session.SetSubject(new EntityId(7));
        for (var i = 0; i < 60; i++) r.Frame();
        r.Session.ScriptedIntent = new CamIntent { Wheel = -1f };   // zoom back out past FrameDistance
        for (var i = 0; i < 300; i++) r.Frame();
        r.Session.ScriptedIntent = null;
        for (var i = 0; i < 60; i++) r.Frame();
        var before = r.Session.Distance;
        Assert.True(before > FreeCamSession.FrameDistance, $"setup: before ({before}) should be zoomed out");
        r.Session.SetSubject(new EntityId(7));   // re-pick the same subject, e.g. clicking them again
        for (var i = 0; i < 60; i++) r.Frame();
        Assert.True(MathF.Abs(r.Session.Distance - before) < 0.1f, $"before {before} after {r.Session.Distance}");
    }

    [Fact]
    public void Backspace_on_yourself_does_not_reframe_the_camera()
    {
        var r = new SessionRig();
        r.Session.Enter();
        for (var i = 0; i < 60; i++) r.Frame();
        r.Session.ScriptedIntent = new CamIntent { Wheel = -1f };   // zoom out past FrameDistance
        for (var i = 0; i < 300; i++) r.Frame();
        r.Session.ScriptedIntent = null;
        for (var i = 0; i < 60; i++) r.Frame();
        var before = r.Session.Distance;
        Assert.True(before > FreeCamSession.FrameDistance, $"setup: before ({before}) should be zoomed out");
        r.Press(Stellar.Abstractions.Domain.StellarKeyCode.Backspace);   // BackToSelf while already the subject
        for (var i = 0; i < 60; i++) r.Frame();
        Assert.True(MathF.Abs(r.Session.Distance - before) < 0.1f, $"before {before} after {r.Session.Distance}");
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
        Assert.Equal(0, r.Freeze.Tokens[0].Disposed);   // scene-stays § 1: the freeze outlives the free camera
        Assert.True(r.Scene.Frozen);
        Assert.Equal(0, r.Visibility.Hides[0].Handle.Disposed);   // … and so do the entry hides (review I-1)
        Assert.Empty(r.Notices);
        r.Freeze.FrameworkReleaseAll();                           // the scene ends (zone change) → they go
        Assert.Equal(1, r.Visibility.Hides[0].Handle.Disposed);
    }

    // Hardening round 1 (2026-10-01): Release isolates every disposal step, and EndFreeze survives the real
    // SceneFreezeService's synchronous reentrant Changed(false) on last-token dispose.

    [Fact]
    public void A_throwing_control_dispose_does_not_block_the_rest_of_release()
    {
        var r = new SessionRig();
        r.Settings.SetLookAt(true);
        r.Session.Enter();
        r.Press(Stellar.Abstractions.Domain.StellarKeyCode.Space);   // freeze on, so Release also has a freeze token to end
        r.Camera.Control.ThrowOnDispose = true;
        r.Session.Exit();
        Assert.False(r.Session.Active);
        Assert.Equal(1, r.Camera.Control.Disposed);
        Assert.Equal(1, r.Shield.Handle.Disposed);
        Assert.Equal(0, r.Freeze.Tokens[0].Disposed);   // scene-stays § 1: not the camera's to end
        Assert.Equal(1, r.Camera.LookAts[0].Disposed);
        Assert.True(r.Scene.HoldsEntryHide);             // the hide step still ran after the throw: handed to the scene (I-1)
        Assert.Equal(0, r.Visibility.Hides[0].Handle.Disposed);
        Assert.Empty(r.Notices);   // Exit keeps the existing toast semantics: none, throw or not
    }

    [Fact]
    public void EndFreeze_disposes_its_token_once_despite_a_reentrant_Changed()
    {
        var r = new SessionRig();
        r.Session.Enter();
        r.Session.ToggleFreeze();                  // freeze on
        var raises = 0;
        var sceneRaises = 0;
        r.Session.StateChanged += () => raises++;
        r.Scene.Changed += () => sceneRaises++;
        r.Scene.EndFreeze();                        // the fake's token raises Changed(false) synchronously from
                                                       // inside its own Dispose(), re-entering OnFreezeChanged while
                                                       // the scene's own token field still references it — the
                                                       // real, ref-counted SceneFreezeService does the same.
        Assert.False(r.Session.Frozen);
        Assert.Equal(1, r.Freeze.Tokens[0].Disposed);
        Assert.Equal(1, raises);
        Assert.Equal(1, sceneRaises);
    }

    [Fact]
    public void Dispose_releases_everything_and_shows_no_toast()
    {
        var r = new SessionRig();
        r.Settings.SetLookAt(true);
        r.Session.Enter();
        r.Press(Stellar.Abstractions.Domain.StellarKeyCode.Space);   // freeze on
        r.Session.Dispose();
        Assert.False(r.Session.Active);
        Assert.Equal(1, r.Camera.Control.Disposed);
        Assert.Equal(1, r.Shield.Handle.Disposed);
        Assert.Equal(0, r.Freeze.Tokens[0].Disposed);   // the camera's dispose leaves the freeze to the scene …
        Assert.Equal(1, r.Camera.LookAts[0].Disposed);
        Assert.Equal(1, r.Visibility.Hides[0].Handle.Disposed);
        Assert.Empty(r.Notices);
        r.Scene.Dispose();                               // … whose dispose (Photo Studio unloading) ends it, once
        r.Scene.Dispose();
        Assert.Equal(1, r.Freeze.Tokens[0].Disposed);
    }
}
