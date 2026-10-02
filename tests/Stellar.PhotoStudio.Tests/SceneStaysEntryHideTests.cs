using System.Numerics;
using Stellar.Abstractions.Domain;
using Stellar.PhotoStudio.Tests.Posing;
using Xunit;

namespace Stellar.PhotoStudio.Tests;

// Review I-1 (2026-10-02, scene-stays-plugin-review.md) against spec 2026-10-02-photo-studio-scene-stays-design.md § 1:
// "the entry hides stay" when the free camera leaves while the scene is set; they end with the scene (Reset scene, the
// framework's scene end, the posed set emptying with nothing frozen, unload). Re-entry never stacks a second hide.
public sealed class SceneStaysEntryHideTests
{
    private const VisibilityLayers DefaultHides = VisibilityLayers.GameHud | VisibilityLayers.Nameplates;

    private static SceneRig PosedAndFrozenThenExit()
    {
        var r = new SceneRig();
        r.Cam.Session.Enter();
        r.Ctl.Select(new EntityId(2));
        r.Ctl.Play(PosingRig.Dance);
        r.Cam.Session.ToggleFreeze();
        r.Cam.Session.Exit();
        return r;
    }

    [Fact]
    public void scene_stays_exit_with_scene_keeps_entry_hides()
    {
        var r = PosedAndFrozenThenExit();
        Assert.Single(r.Cam.Visibility.Hides);
        Assert.Equal(DefaultHides, r.Cam.Visibility.Hides[0].Layers);
        Assert.Equal(0, r.Cam.Visibility.Hides[0].Handle.Disposed);
        Assert.True(r.Cam.Scene.HoldsEntryHide);
        Assert.Equal(DefaultHides, r.Cam.Scene.EntryHideLayers);
    }

    [Fact]
    public void scene_stays_exit_with_only_a_pose_keeps_entry_hides()
    {
        var r = new SceneRig();
        r.Cam.Session.Enter();
        r.Ctl.Select(new EntityId(2));
        r.Ctl.SetYaw(30f);                                // a facing-only touch is a pose
        r.Cam.Session.Exit();
        Assert.Equal(0, r.Cam.Visibility.Hides[0].Handle.Disposed);
        Assert.True(r.Cam.Scene.HoldsEntryHide);
    }

    [Fact]
    public void scene_stays_exit_without_scene_releases_entry_hides()
    {
        var r = new SceneRig();
        r.Cam.Session.Enter();
        r.Cam.Session.Exit();                             // nothing frozen, nobody posed
        Assert.Equal(1, r.Cam.Visibility.Hides[0].Handle.Disposed);
        Assert.False(r.Cam.Scene.HoldsEntryHide);
    }

    [Fact]
    public void scene_stays_exit_after_the_scene_ended_on_camera_releases_entry_hides()
    {
        var r = new SceneRig();
        r.Cam.Session.Enter();
        r.Cam.Session.ToggleFreeze();
        r.ResetScene();                                   // the scene ends while the camera is still on …
        Assert.Equal(0, r.Cam.Visibility.Hides[0].Handle.Disposed);   // … the camera still holds its hides
        r.Cam.Session.Exit();                             // … and releases them at exit (nothing set)
        Assert.Equal(1, r.Cam.Visibility.Hides[0].Handle.Disposed);
    }

    [Fact]
    public void scene_stays_reset_scene_releases_entry_hides()
    {
        var r = PosedAndFrozenThenExit();
        r.ResetScene();
        Assert.Equal(1, r.Cam.Visibility.Hides[0].Handle.Disposed);
        Assert.False(r.Cam.Scene.HoldsEntryHide);
        r.ResetScene();                                   // once only
        Assert.Equal(1, r.Cam.Visibility.Hides[0].Handle.Disposed);
    }

    [Fact]
    public void scene_stays_framework_unfreeze_with_nobody_posed_releases_entry_hides()
    {
        var r = new SceneRig();
        r.Cam.Session.Enter();
        r.Cam.Session.ToggleFreeze();
        r.Cam.Session.Exit();
        Assert.Equal(0, r.Cam.Visibility.Hides[0].Handle.Disposed);
        r.Cam.Freeze.FrameworkReleaseAll();               // zone change / cutscene: Changed(false), nobody posed
        Assert.Equal(1, r.Cam.Visibility.Hides[0].Handle.Disposed);
    }

    [Fact]
    public void scene_stays_framework_unfreeze_keeps_entry_hides_while_someone_is_still_posed()
    {
        var r = PosedAndFrozenThenExit();
        r.Cam.Freeze.FrameworkReleaseAll();               // the freeze ends but the pose stays → the scene is still set
        Assert.Equal(0, r.Cam.Visibility.Hides[0].Handle.Disposed);
        r.Posing.ResetAll();                              // the framework releases the poses too (leave scene)
        Assert.Equal(1, r.Cam.Visibility.Hides[0].Handle.Disposed);
    }

    [Fact]
    public void scene_stays_posed_set_emptying_with_nothing_frozen_releases_entry_hides()
    {
        var r = new SceneRig();
        r.Cam.Session.Enter();
        r.Ctl.Select(new EntityId(2));
        r.Ctl.Play(PosingRig.Dance);
        r.Cam.Session.Exit();
        Assert.Equal(0, r.Cam.Visibility.Hides[0].Handle.Disposed);
        r.Ctl.ResetPerson();                              // the last posed person is reset off the camera
        Assert.Equal(1, r.Cam.Visibility.Hides[0].Handle.Disposed);
    }

    [Fact]
    public void scene_stays_unfreeze_off_camera_with_nobody_posed_releases_entry_hides()
    {
        var r = new SceneRig();
        r.Cam.Session.Enter();
        r.Cam.Session.ToggleFreeze();
        r.Cam.Session.Exit();
        r.Cam.Scene.ToggleFreeze(null);                   // the Scene group's Unfreeze: nothing is left set
        Assert.Equal(1, r.Cam.Visibility.Hides[0].Handle.Disposed);
    }

    [Fact]
    public void scene_stays_unload_releases_kept_entry_hides()
    {
        var r = PosedAndFrozenThenExit();
        r.Cam.Session.Dispose();
        r.Cam.Scene.Dispose();
        Assert.Equal(1, r.Cam.Visibility.Hides[0].Handle.Disposed);
    }

    [Fact]
    public void scene_stays_framework_camera_release_with_scene_hands_hides_over_until_the_scene_ends()
    {
        var r = new SceneRig();
        r.Cam.Session.Enter();
        r.Cam.Session.ToggleFreeze();
        r.Cam.Camera.FrameworkRelease(CameraReleaseReason.SceneChanged);   // the camera ends first …
        Assert.Equal(0, r.Cam.Visibility.Hides[0].Handle.Disposed);
        r.Cam.Freeze.FrameworkReleaseAll();                                 // … then the freeze (same zone change)
        Assert.Equal(1, r.Cam.Visibility.Hides[0].Handle.Disposed);
    }

    [Fact]
    public void scene_stays_reentry_reuses_the_kept_entry_hide_and_never_double_hides()
    {
        var r = PosedAndFrozenThenExit();
        for (var i = 0; i < 3; i++)
        {
            r.Cam.Session.Enter();
            Assert.Single(r.Cam.Visibility.Hides);                         // reused, never a second hide
            Assert.Equal(0, r.Cam.Visibility.Hides[0].Handle.Disposed);
            Assert.False(r.Cam.Scene.HoldsEntryHide);                      // the camera holds it while on
            r.Cam.Session.Exit();
            Assert.True(r.Cam.Scene.HoldsEntryHide);
        }
        r.ResetScene();
        Assert.Single(r.Cam.Visibility.Hides);
        Assert.Equal(1, r.Cam.Visibility.Hides[0].Handle.Disposed);
    }

    [Fact]
    public void scene_stays_reentry_after_an_entry_hide_setting_change_swaps_the_hide()
    {
        var r = PosedAndFrozenThenExit();
        r.Cam.Settings.SetEntryHide(VisibilityLayers.Nameplates, false);   // changed while the camera was off
        r.Cam.Session.Enter();
        Assert.Equal(2, r.Cam.Visibility.Hides.Count);
        Assert.Equal(1, r.Cam.Visibility.Hides[0].Handle.Disposed);        // the old layers go …
        Assert.Equal(VisibilityLayers.GameHud, r.Cam.Visibility.Hides[1].Layers);   // … the new ones are hidden once
        Assert.Equal(0, r.Cam.Visibility.Hides[1].Handle.Disposed);

        r.Cam.Session.Exit();
        r.Cam.Settings.SetEntryHide(VisibilityLayers.GameHud, false);      // entry hides now off entirely
        r.Cam.Session.Enter();
        Assert.Equal(2, r.Cam.Visibility.Hides.Count);                     // nothing new hidden
        Assert.Equal(1, r.Cam.Visibility.Hides[1].Handle.Disposed);        // and the kept one is dropped
        r.Cam.Session.Exit();
        Assert.False(r.Cam.Scene.HoldsEntryHide);
    }

    [Fact]
    public void scene_stays_a_refused_reentry_leaves_the_kept_hide_with_the_scene()
    {
        var r = PosedAndFrozenThenExit();
        r.Cam.Camera.Busy = true;
        Assert.False(r.Cam.Session.Enter());
        Assert.True(r.Cam.Scene.HoldsEntryHide);
        Assert.Equal(0, r.Cam.Visibility.Hides[0].Handle.Disposed);
    }

    [Fact]
    public void scene_stays_reset_after_dispose_does_nothing()
    {
        var r = new SceneRig();
        r.Cam.Scene.ToggleFreeze(Vector3.Zero);
        r.Cam.Scene.Dispose();
        var resets = r.Posing.ResetAllCalls;
        var changes = 0;
        r.Cam.Scene.Changed += () => changes++;
        r.Cam.Scene.Reset();                              // a late button click after unload
        Assert.Equal(resets, r.Posing.ResetAllCalls);
        Assert.Equal(0, changes);
        Assert.Equal(1, r.Cam.Freeze.Tokens[0].Disposed);
    }
}
