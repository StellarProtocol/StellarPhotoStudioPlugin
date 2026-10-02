using System.Numerics;
using Stellar.Abstractions.Domain;
using Stellar.PhotoStudio.FreeCam;
using Stellar.PhotoStudio.Posing;
using Stellar.PhotoStudio.Tests.FreeCam;
using Stellar.PhotoStudio.Tests.Posing;
using Xunit;

namespace Stellar.PhotoStudio.Tests;

/// <summary>The free camera, the scene and the Person group's controller wired as the plugin wires them
/// (Plugin.Scene.cs / Plugin.Posing.cs): the controller's selection orbits while the camera is on, the scene counts poses.</summary>
internal sealed class SceneRig
{
    public readonly FakePosing Posing = new();
    public readonly SessionRig Cam;
    public readonly PosingController Ctl;

    public SceneRig()
    {
        Cam = new SessionRig(posing: Posing);
        Cam.Transforms.Positions[2] = new Position3D(3, 0, 3);
        Cam.Transforms.Positions[3] = new Position3D(6, 0, 0);
        Ctl = new PosingController(Posing, new PosingHost(() => Cam.Session.Subject, id => Cam.Session.SetSubject(id), _ => { },
            () => Cam.Snapshot.LocalEntityId, id => new DescribedAction(new EmoteInfo(id, "Current pose", "", false), false)));
        Cam.Scene.TrackPoses(() => Ctl.PosedCount);
        Ctl.PosedChanged += Cam.Scene.NotifyPosesChanged;
        Cam.Session.StateChanged += () => { if (Cam.Session.Active) Ctl.SyncSubject(); };
        Posing.Changed += Ctl.OnPosingChanged;
    }

    /// <summary>The plugin's Reset scene button (Plugin.ResetScene).</summary>
    public void ResetScene()
    {
        Ctl.ForgetPoses();
        Cam.Scene.Reset();
    }
}

// Spec 2026-10-02-photo-studio-scene-stays-design.md: the freeze and the posed people belong to the scene, not the free
// camera (owner: "when i setup completely and i want to move myself, i have to leave the freecam mode … and everything
// reset").
public sealed class SceneStaysTests
{
    private static float Dist(Position3D p, Vector3 c) => Vector3.Distance(new Vector3(p.X, p.Y, p.Z), c);

    [Fact]
    public void scene_stays_exit_keeps_freeze_and_poses()
    {
        var r = new SceneRig();
        r.Cam.Session.Enter();
        r.Ctl.Select(new EntityId(2));
        r.Ctl.Play(PosingRig.Dance);
        r.Cam.Session.ToggleFreeze();
        r.Cam.Session.Exit();

        Assert.False(r.Cam.Session.Active);
        Assert.True(r.Cam.Scene.Frozen);
        Assert.Equal(0, r.Cam.Freeze.Tokens[0].Disposed);
        Assert.Equal(0, r.Posing.ResetAllCalls);
        Assert.Equal(PoseTargetState.Ready, r.Posing.Targets[2].State);
        Assert.DoesNotContain("reset", r.Posing.Targets[2].Calls);
        Assert.Equal(1, r.Ctl.PosedCount);
        Assert.Equal(1, r.Cam.Scene.PosedCount);
        Assert.True(r.Cam.Scene.IsSet);
        Assert.Equal(new EntityId(2), r.Ctl.Subject);           // the selection stays too
        Assert.Equal(PosingRig.Dance.Id, r.Ctl.State.Action?.Id);   // and the panel state
    }

    [Fact]
    public void scene_stays_reset_scene_clears_all()
    {
        var r = new SceneRig();
        r.Cam.Session.Enter();
        r.Ctl.Select(new EntityId(2));
        r.Ctl.Play(PosingRig.Dance);
        r.Cam.Session.ToggleFreeze();
        r.Cam.Session.Exit();
        var changes = 0;
        r.Cam.Scene.Changed += () => changes++;

        r.ResetScene();

        Assert.False(r.Cam.Scene.Frozen);
        Assert.Equal(1, r.Cam.Freeze.Tokens[0].Disposed);
        Assert.False(r.Cam.Freeze.IsFrozen);
        Assert.Equal(1, r.Posing.ResetAllCalls);
        Assert.Equal(PoseTargetState.Released, r.Posing.Targets[2].State);
        Assert.Equal(0, r.Ctl.PosedCount);
        Assert.False(r.Cam.Scene.IsSet);
        Assert.Null(r.Ctl.State.Action);
        Assert.True(changes > 0);
    }

    [Fact]
    public void scene_stays_reset_scene_keeps_the_free_camera_on()
    {
        var r = new SceneRig();
        r.Cam.Session.Enter();
        r.Cam.Session.ToggleFreeze();
        r.ResetScene();
        Assert.True(r.Cam.Session.Active);
        Assert.False(r.Cam.Session.Frozen);
        Assert.Equal(1, r.Cam.Freeze.Tokens[0].Disposed);
    }

    [Fact]
    public void scene_stays_reentry_restores_last_pose()
    {
        var r = new SceneRig();
        r.Cam.Session.Enter();
        r.Cam.Session.ScriptedIntent = new CamIntent { Looking = true, LookX = 40f, Wheel = -2f };
        for (var i = 0; i < 20; i++) r.Cam.Frame();
        r.Cam.Session.ScriptedIntent = null;
        r.Cam.Session.ToggleFreeze();                    // the scene is set
        r.Cam.Frame();
        var left = r.Cam.Camera.Control.LastPosition;
        var (yaw, pitch, fov) = (r.Cam.Camera.Control.LastYaw, r.Cam.Camera.Control.LastPitch, r.Cam.Session.Fov);
        Assert.True(Dist(left, new Vector3(0, 2, -6)) > 0.5f, "setup: the camera moved away from the game pose");
        r.Cam.Session.Exit();

        r.Cam.Session.Enter();
        r.Cam.Frame();
        Assert.True(Dist(r.Cam.Camera.Control.LastPosition, new Vector3(left.X, left.Y, left.Z)) < 1e-3f);
        Assert.Equal(yaw, r.Cam.Camera.Control.LastYaw, 3);
        Assert.Equal(pitch, r.Cam.Camera.Control.LastPitch, 3);
        Assert.Equal(fov, r.Cam.Session.Fov, 3);

        r.Cam.Press(StellarKeyCode.R);                   // R still snaps to the game camera
        Assert.True(Dist(r.Cam.Camera.Control.LastPosition, new Vector3(0, 2, -6)) < 1e-3f);
    }

    [Fact]
    public void scene_stays_reentry_restores_fly_mode_too()
    {
        var r = new SceneRig();
        r.Cam.Session.Enter();
        r.Cam.Session.ToggleMode();
        r.Cam.Session.ToggleFreeze();
        r.Cam.Session.Exit();
        r.Cam.Session.Enter();
        Assert.Equal(FreeCamMode.Fly, r.Cam.Session.Mode);
    }

    [Fact]
    public void scene_stays_reentry_with_no_scene_starts_from_the_game_camera()
    {
        var r = new SceneRig();
        r.Cam.Session.Enter();
        r.Cam.Session.ToggleMode();
        r.Cam.Session.ScriptedIntent = new CamIntent { Looking = true, LookX = 40f, Move = new Vector3(1, 0, 0) };
        for (var i = 0; i < 20; i++) r.Cam.Frame();
        r.Cam.Session.ScriptedIntent = null;
        Assert.True(Dist(r.Cam.Camera.Control.LastPosition, new Vector3(0, 2, -6)) > 0.5f, "setup: the camera moved");
        r.Cam.Session.Exit();                            // nothing frozen, nobody posed

        r.Cam.Session.Enter();
        r.Cam.Frame();
        Assert.Equal(FreeCamMode.Orbit, r.Cam.Session.Mode);
        Assert.True(Dist(r.Cam.Camera.Control.LastPosition, new Vector3(0, 2, -6)) < 1e-3f);
        Assert.Equal(0f, r.Cam.Camera.Control.LastYaw, 3);
        Assert.Equal(10f, r.Cam.Camera.Control.LastPitch, 3);
    }

    [Fact]
    public void scene_stays_reentry_after_reset_scene_starts_from_the_game_camera()
    {
        var r = new SceneRig();
        r.Cam.Session.Enter();
        r.Cam.Session.ScriptedIntent = new CamIntent { Looking = true, LookX = 40f };
        for (var i = 0; i < 20; i++) r.Cam.Frame();
        r.Cam.Session.ScriptedIntent = null;
        r.Cam.Session.ToggleFreeze();
        r.Cam.Session.Exit();
        r.ResetScene();                                   // the scene ended while the camera was off

        r.Cam.Session.Enter();
        r.Cam.Frame();
        Assert.True(Dist(r.Cam.Camera.Control.LastPosition, new Vector3(0, 2, -6)) < 1e-3f);
    }

    [Fact]
    public void scene_stays_selection_without_free_camera()
    {
        var r = new SceneRig();
        r.Ctl.EnsureSubject();                            // the panel opens off the camera: you are selected
        Assert.Equal(new EntityId(1), r.Ctl.Subject);
        r.Ctl.Cycle(1);                                   // › selects the next person, no camera needed
        Assert.Equal(new EntityId(2), r.Ctl.Subject);
        Assert.False(r.Cam.Session.Active);
        r.Ctl.Play(PosingRig.Dance);                      // and posing works off the camera
        Assert.Equal(PoseTargetState.Ready, r.Posing.Targets[2].State);
        Assert.True(r.Cam.Scene.IsSet);

        r.Cam.Session.Enter(r.Ctl.Subject);               // the camera comes on orbiting the selection
        Assert.Equal(new EntityId(2), r.Cam.Session.Subject);
        Assert.Equal(new EntityId(2), r.Ctl.Subject);
        r.Ctl.Cycle(1);                                   // selecting in the panel orbits that person
        Assert.Equal(new EntityId(3), r.Cam.Session.Subject);
        r.Cam.Session.SetSubject(new EntityId(1));        // picking in the camera selects
        Assert.Equal(new EntityId(1), r.Ctl.Subject);

        r.Cam.Session.Exit();
        r.Ctl.Cycle(1);                                   // off again: ‹ › still select, the camera does not move
        Assert.Equal(new EntityId(2), r.Ctl.Subject);
        Assert.Equal(new EntityId(1), r.Cam.Session.Subject);
    }

    [Fact]
    public void scene_stays_entry_on_a_person_who_left_orbits_yourself()
    {
        var r = new SceneRig();
        r.Cam.Session.Enter(new EntityId(99));            // the selection is no longer in the game
        Assert.Equal(r.Cam.Snapshot.LocalEntityId, r.Cam.Session.Subject);
    }

    [Fact]
    public void scene_stays_freeze_off_camera_and_the_leash_centres_on_it_at_reentry()
    {
        var r = new SceneRig();
        r.Cam.Settings.SetLeash(5f, save: false);
        r.Cam.Scene.ToggleFreeze(Vector3.Zero);           // the Scene group's Freeze, camera off
        Assert.True(r.Cam.Freeze.IsFrozen);
        r.Cam.Transforms.Positions[1] = new Position3D(30, 0, 0);   // you walk off (you are never frozen)
        r.Cam.Session.Enter();
        for (var i = 0; i < 60; i++) r.Cam.Frame();
        Assert.True(Dist(r.Cam.Camera.Control.LastPosition, Vector3.Zero) <= 5.001f);
    }

    [Fact]
    public void scene_stays_framework_unfreeze_with_the_camera_off_ends_the_freeze()
    {
        var r = new SceneRig();
        r.Cam.Scene.ToggleFreeze(Vector3.Zero);
        r.Cam.Freeze.FrameworkReleaseAll();               // zone change / cutscene
        Assert.False(r.Cam.Scene.Frozen);
        Assert.Null(r.Cam.Scene.FreezeCentre);
    }

    [Fact]
    public void scene_stays_framework_release_of_poses_with_the_camera_off_empties_the_scene()
    {
        var r = new SceneRig();
        r.Ctl.EnsureSubject();
        r.Ctl.Select(new EntityId(2));
        r.Ctl.Play(PosingRig.Dance);
        var changes = 0;
        r.Cam.Scene.Changed += () => changes++;
        r.Posing.ResetAll();                              // the framework ended posing (leave scene)
        Assert.Equal(0, r.Cam.Scene.PosedCount);
        Assert.False(r.Cam.Scene.IsSet);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void scene_stays_local_death_ends_the_freeze_with_the_camera_off()
    {
        var r = new SceneRig();
        r.Cam.Scene.ToggleFreeze(Vector3.Zero);
        r.Cam.Scene.OnLocalDeath();
        Assert.False(r.Cam.Scene.Frozen);
        Assert.Equal(1, r.Cam.Freeze.Tokens[0].Disposed);
    }
}
