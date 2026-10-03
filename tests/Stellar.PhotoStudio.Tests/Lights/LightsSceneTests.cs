using System.Numerics;
using Stellar.Abstractions.Domain;
using Stellar.PhotoStudio.Lights;
using Stellar.PhotoStudio.Tests.FreeCam;
using Xunit;

namespace Stellar.PhotoStudio.Tests.Lights;

// Lights spec § 4: lights belong to the scene (scene-stays spec) — they stay when the free camera exits, make the scene
// "set", and end on Reset scene and unload (and on the framework's own scene end — ILights.Released).
public sealed class LightsSceneTests
{
    private sealed class Rig
    {
        public readonly SessionRig Cam = new();
        public readonly FakeLights Lights = new();
        public readonly LightsController Ctl;

        public Rig()
        {
            Ctl = new LightsController(new LightsPorts(Lights, () => new EntityId(1),
                _ => new LightAnchor(Vector3.Zero, 0f), () => Cam.Session.ShownPose));
            Cam.Scene.TrackLights(() => Ctl.SceneCount, Ctl.Clear);
            Ctl.Changed += Cam.Scene.NotifyLightsChanged;
        }
    }

    [Fact]
    public void Lamps_drop_at_the_free_cameras_pose_and_stay_when_it_exits()
    {
        var r = new Rig();
        Assert.Equal(LightsResult.NoCamera, r.Ctl.AddAtCamera());
        Assert.True(r.Cam.Session.Enter());
        Assert.Equal(LightsResult.Ok, r.Ctl.AddAtCamera());
        Assert.True(r.Cam.Scene.IsSet);
        r.Cam.Session.Exit();
        Assert.Equal(1, r.Ctl.Count);
        Assert.Single(r.Lights.Lamps);
        Assert.True(r.Cam.Scene.IsSet);
    }

    [Fact]
    public void Reset_scene_clears_the_lights_and_ends_the_scene()
    {
        var r = new Rig();
        r.Cam.Session.Enter();
        r.Ctl.AddAtCamera();
        r.Ctl.SetKeyOn(true);
        var gen = r.Cam.Scene.Generation;
        r.Cam.Scene.Reset();
        Assert.Empty(r.Lights.Lamps);
        Assert.Empty(r.Lights.People);
        Assert.False(r.Cam.Scene.IsSet);
        Assert.True(r.Cam.Scene.Generation > gen);
    }

    [Fact]
    public void Unload_clears_the_lights()
    {
        var r = new Rig();
        r.Cam.Session.Enter();
        r.Ctl.AddAtCamera();
        r.Cam.Session.Dispose();
        r.Cam.Scene.Dispose();
        Assert.Empty(r.Lights.Lamps);
    }

    [Fact]
    public void A_framework_scene_end_ends_the_scene_when_only_lights_set_it()
    {
        var r = new Rig();
        r.Cam.Session.Enter();
        r.Ctl.AddAtCamera();
        r.Cam.Session.Exit();
        var gen = r.Cam.Scene.Generation;
        r.Lights.EndScene();
        Assert.False(r.Cam.Scene.IsSet);
        Assert.True(r.Cam.Scene.Generation > gen);
    }
}
