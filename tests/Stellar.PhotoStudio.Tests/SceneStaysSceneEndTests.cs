using System.Numerics;
using Stellar.Abstractions.Domain;
using Stellar.PhotoStudio.FreeCam;
using Stellar.PhotoStudio.Tests.Posing;
using Xunit;

namespace Stellar.PhotoStudio.Tests;

// Review 2026-10-02 (scene-stays-plugin-review.md): I-3 (the remembered camera pose belongs to ONE scene), the posed
// count (only people really posed), the stale off-camera selection and the off-camera freeze-centre move.
public sealed class SceneStaysSceneEndTests
{
    private static readonly Vector3 GameCamera = new(0, 2, -6);

    private static float Dist(Position3D p, Vector3 c) => Vector3.Distance(new Vector3(p.X, p.Y, p.Z), c);

    /// <summary>Moves the camera well away from the game pose, freezes and leaves — the pose is remembered.</summary>
    private static SceneRig MovedFrozenThenExit()
    {
        var r = new SceneRig();
        r.Cam.Session.Enter();
        r.Cam.Session.ScriptedIntent = new CamIntent { Looking = true, LookX = 40f, Wheel = -2f };
        for (var i = 0; i < 20; i++) r.Cam.Frame();
        r.Cam.Session.ScriptedIntent = null;
        r.Cam.Session.ToggleFreeze();
        r.Cam.Frame();
        Assert.True(Dist(r.Cam.Camera.Control.LastPosition, GameCamera) > 0.5f, "setup: the camera moved away from the game pose");
        r.Cam.Session.Exit();
        Assert.True(r.Cam.Session.HasLastPose);
        return r;
    }

    private static void AssertEntryFromGameCamera(SceneRig r)
    {
        Assert.False(r.Cam.Session.HasLastPose);
        r.Cam.Session.Enter();
        r.Cam.Frame();
        Assert.Equal(FreeCamMode.Orbit, r.Cam.Session.Mode);
        Assert.True(Dist(r.Cam.Camera.Control.LastPosition, GameCamera) < 1e-3f);
    }

    [Fact]
    public void scene_stays_reset_then_new_freeze_entry_starts_from_the_game_camera()
    {
        var r = MovedFrozenThenExit();
        r.ResetScene();
        r.Cam.Scene.ToggleFreeze(Vector3.Zero);           // a NEW scene, frozen off the camera
        AssertEntryFromGameCamera(r);
    }

    [Fact]
    public void scene_stays_zone_change_unfreeze_then_new_freeze_entry_starts_from_the_game_camera()
    {
        var r = MovedFrozenThenExit();
        r.Cam.Freeze.FrameworkReleaseAll();               // zone change: the freeze's Changed(false)
        Assert.False(r.Cam.Scene.IsSet);
        r.Cam.Scene.ToggleFreeze(Vector3.Zero);
        AssertEntryFromGameCamera(r);
    }

    [Fact]
    public void scene_stays_unfreeze_off_camera_then_new_freeze_entry_starts_from_the_game_camera()
    {
        var r = MovedFrozenThenExit();
        r.Cam.Scene.ToggleFreeze(null);                   // the Scene group's Unfreeze
        r.Cam.Scene.ToggleFreeze(Vector3.Zero);           // and Freeze again
        AssertEntryFromGameCamera(r);
    }

    [Fact]
    public void scene_stays_posed_set_emptying_then_new_pose_entry_starts_from_the_game_camera()
    {
        var r = new SceneRig();
        r.Cam.Session.Enter();
        r.Cam.Session.ScriptedIntent = new CamIntent { Looking = true, LookX = 40f };
        for (var i = 0; i < 20; i++) r.Cam.Frame();
        r.Cam.Session.ScriptedIntent = null;
        r.Ctl.Select(new EntityId(2));
        r.Ctl.Play(PosingRig.Dance);                      // set by a pose only
        r.Cam.Session.Exit();
        Assert.True(r.Cam.Session.HasLastPose);
        r.Posing.ResetAll();                              // the framework released everyone: the scene ended
        r.Ctl.Play(PosingRig.Dance);                      // a new pose = a new scene
        Assert.True(r.Cam.Scene.IsSet);
        AssertEntryFromGameCamera(r);
    }

    [Fact]
    public void scene_stays_the_same_scene_still_restores_after_a_freeze_end_while_posed()
    {
        var r = new SceneRig();
        r.Cam.Session.Enter();
        r.Ctl.Select(new EntityId(2));
        r.Ctl.Play(PosingRig.Dance);
        r.Cam.Session.ToggleFreeze();
        r.Cam.Session.Exit();
        r.Cam.Freeze.FrameworkReleaseAll();               // the freeze ends, the pose stays: the same scene
        Assert.True(r.Cam.Session.HasLastPose);
    }

    [Fact]
    public void scene_stays_posed_count_counts_only_people_really_posed()
    {
        var r = new SceneRig();
        var raised = 0;
        r.Ctl.PosedChanged += () => raised++;
        r.Ctl.EnsureSubject();

        r.Posing.Targets[2] = new FakePoseTarget { PlayResult = PoseResult.Full };
        r.Ctl.Select(new EntityId(2));
        r.Ctl.Play(PosingRig.Dance);                      // the photo-member limit: nothing was made
        Assert.Equal(PoseTargetState.Full, r.Posing.Targets[2].State);
        Assert.Equal(0, r.Ctl.PosedCount);
        Assert.False(r.Cam.Scene.IsSet);

        r.Ctl.Select(new EntityId(3));
        r.Ctl.SetYaw(45f);                                // facing only: posed
        Assert.Equal(1, r.Ctl.PosedCount);
        r.Ctl.Select(new EntityId(1));
        r.Ctl.SetLook(LookPart.Head, LookMode.Lens);      // look only: posed
        Assert.Equal(2, r.Ctl.PosedCount);

        r.Posing.Targets[3].State = PoseTargetState.Loading;   // an NPC model loading: still counted …
        r.Posing.RaiseChanged();
        Assert.Equal(2, r.Ctl.PosedCount);
        var before = raised;
        r.Posing.Targets[3].State = PoseTargetState.Failed;    // … until it fails
        r.Posing.RaiseChanged();
        Assert.Equal(1, r.Ctl.PosedCount);
        Assert.Equal(1, r.Cam.Scene.PosedCount);
        Assert.Equal(before + 1, raised);                 // the scene hears about it (SCENE pill / status refresh)
    }

    [Fact]
    public void scene_stays_off_camera_selection_moves_the_freeze_centre()
    {
        var r = new SceneRig();
        r.Ctl.EnsureSubject();
        r.Cam.Scene.ToggleFreeze(Vector3.Zero);           // frozen off the camera, centred on you
        r.Ctl.Select(new EntityId(2));                    // ‹ › off the camera
        Assert.Equal(new Vector3(3, 0, 3), r.Cam.Scene.FreezeCentre);

        r.Posing.Visible[3] = new Position3D(10, 0, 0);   // a posed stand-in is where the person is seen
        r.Ctl.Select(new EntityId(3));
        Assert.Equal(new Vector3(10, 0, 0), r.Cam.Scene.FreezeCentre);

        r.Ctl.Select(new EntityId(99));                   // someone the game no longer has: the centre stays
        Assert.Equal(new Vector3(10, 0, 0), r.Cam.Scene.FreezeCentre);

        r.Cam.Settings.SetLeash(5f, save: false);         // and the leash centres there when the camera comes back
        r.Cam.Session.Enter();
        for (var i = 0; i < 60; i++) r.Cam.Frame();
        Assert.True(Dist(r.Cam.Camera.Control.LastPosition, new Vector3(10, 0, 0)) <= 5.001f);
    }

    [Fact]
    public void scene_stays_off_camera_selection_without_freeze_leaves_no_centre()
    {
        var r = new SceneRig();
        r.Ctl.EnsureSubject();
        r.Ctl.Select(new EntityId(2));
        Assert.Null(r.Cam.Scene.FreezeCentre);
        Assert.False(r.Cam.Scene.Frozen);
    }

    [Fact]
    public void scene_stays_stale_selection_off_camera_falls_back_to_yourself()
    {
        var r = new SceneRig();
        r.Ctl.EnsureSubject();
        r.Cam.Scene.ToggleFreeze(Vector3.Zero);
        r.Ctl.Select(new EntityId(2));
        var centre = r.Cam.Scene.FreezeCentre;
        Assert.False(r.Ctl.FallBackIfGone(r.Selection.IsSeen));   // still here

        r.Cam.Transforms.Positions.Remove(2);             // the person left: no transform, no copy, no live target
        Assert.True(r.Ctl.FallBackIfGone(r.Selection.IsSeen));
        Assert.Equal(new EntityId(1), r.Ctl.Subject);
        Assert.Equal(centre, r.Cam.Scene.FreezeCentre);   // falling back never moves the freeze centre
        Assert.False(r.Ctl.FallBackIfGone(r.Selection.IsSeen));   // yourself never falls back
    }

    [Fact]
    public void scene_stays_stale_selection_keeps_a_person_still_posed_or_seen()
    {
        var r = new SceneRig();
        r.Ctl.EnsureSubject();
        r.Ctl.Select(new EntityId(2));
        r.Ctl.Play(PosingRig.Dance);                      // a live pose target
        r.Cam.Transforms.Positions.Remove(2);
        Assert.False(r.Ctl.FallBackIfGone(r.Selection.IsSeen));

        r.Ctl.Select(new EntityId(3));
        r.Cam.Transforms.Positions.Remove(3);
        r.Posing.Visible[3] = new Position3D(6, 0, 0);    // seen through a visible stand-in
        Assert.False(r.Ctl.FallBackIfGone(r.Selection.IsSeen));
        Assert.Equal(new EntityId(3), r.Ctl.Subject);
    }
}
