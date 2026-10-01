using System.Numerics;
using Stellar.PhotoStudio.FreeCam;
using Xunit;

namespace Stellar.PhotoStudio.Tests.FreeCam;

public sealed class OrbitRigTests
{
    private static readonly RigTuning Tune = new(4.5f, 1f, 0f, false, 30f);

    [Fact]
    public void FromCamera_then_Place_returns_the_camera_exactly()   // entry starts at the game pose: no jump (spec § 3)
    {
        var cam = new Vector3(3, 4, -6);
        var subject = new Vector3(0, 0, 0);
        var s = OrbitRig.FromCamera(cam, 20f, 15f, subject);
        var o = OrbitRig.Place(s, subject);
        Assert.True(Vector3.Distance(cam, o.Position) < 1e-3f);
        Assert.Equal(20f, o.Yaw);
        Assert.Equal(15f, o.Pitch);
    }

    [Fact]
    public void FromPose_aims_at_the_subject_pivot()
    {
        var subject = new Vector3(10, 0, 10);
        var s = OrbitRig.FromPose(new Vector3(10, OrbitRig.PivotHeight, 0), subject);
        Assert.Equal(Vector3.Zero, s.PivotOffset);
        Assert.InRange(s.Distance, 9.999f, 10.001f);
        Assert.InRange(s.Yaw, -1e-3f, 1e-3f);
    }

    [Fact]
    public void Wheel_up_zooms_in_and_distance_is_clamped()
    {
        var s = new OrbitState(Vector3.Zero, 10f, 0f, 0f);
        Assert.InRange(OrbitRig.Step(s, new CamIntent { Wheel = 1f }, Tune, 0.016f).Distance, 8.999f, 9.001f);
        Assert.Equal(OrbitRig.MinDistance, OrbitRig.Step(s, new CamIntent { Wheel = 100f }, Tune, 0.016f).Distance);
        Assert.Equal(30f, OrbitRig.Step(s, new CamIntent { Wheel = -100f }, Tune, 0.016f).Distance);
    }

    [Fact]
    public void Shift_wheel_is_fov_not_zoom()
    {
        var s = new OrbitState(Vector3.Zero, 10f, 0f, 0f);
        Assert.Equal(10f, OrbitRig.Step(s, new CamIntent { Wheel = 1f, Shift = true }, Tune, 0.016f).Distance);
    }

    [Fact]
    public void Right_mouse_rotates_by_sensitivity_and_invert_flips_pitch()
    {
        var s = new OrbitState(Vector3.Zero, 10f, 0f, 0f);
        var look = new CamIntent { Looking = true, LookX = 10f, LookY = 10f };
        var a = OrbitRig.Step(s, look, Tune, 0.016f);
        Assert.InRange(a.Yaw, 1.499f, 1.501f);
        Assert.InRange(a.Pitch, 1.499f, 1.501f);
        var b = OrbitRig.Step(s, look, Tune with { Sensitivity = 2f, InvertY = true }, 0.016f);
        Assert.InRange(b.Yaw, 2.999f, 3.001f);
        Assert.InRange(b.Pitch, -3.001f, -2.999f);
        Assert.Equal(0f, OrbitRig.Step(s, look with { Looking = false }, Tune, 0.016f).Yaw);
    }

    [Fact]
    public void Pitch_is_clamped()
    {
        var s = new OrbitState(Vector3.Zero, 10f, 0f, 0f);
        Assert.Equal(89f, OrbitRig.Step(s, new CamIntent { Looking = true, LookY = 100000f }, Tune, 0.016f).Pitch);
    }

    [Fact]
    public void Sliding_the_pivot_stays_inside_the_leash()
    {
        var s = new OrbitState(Vector3.Zero, 5f, 0f, 0f);
        for (var i = 0; i < 1000; i++) s = OrbitRig.Step(s, new CamIntent { Move = new Vector3(0, 0, 1) }, Tune, 0.1f);
        Assert.InRange(s.PivotOffset.Length(), 29.99f, 30.001f);
    }
}
