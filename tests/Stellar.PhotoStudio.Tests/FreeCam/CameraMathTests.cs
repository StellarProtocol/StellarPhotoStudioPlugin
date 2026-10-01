using System;
using System.Numerics;
using Stellar.PhotoStudio.FreeCam;
using Xunit;

namespace Stellar.PhotoStudio.Tests.FreeCam;

// Spec § 3: orbit/fly maths, leash = projection onto the sphere (slides, never stops dead), exponential damping,
// roll ±90°, FOV 10°–100°.
public sealed class CameraMathTests
{
    private static void Close(Vector3 expected, Vector3 actual, float eps = 1e-4f) =>
        Assert.True(Vector3.Distance(expected, actual) <= eps, $"expected {expected} got {actual}");

    [Fact]
    public void Forward_follows_unity_axes()
    {
        Close(Vector3.UnitZ, CameraMath.Forward(0, 0));
        Close(Vector3.UnitX, CameraMath.Forward(90, 0));
        Close(-Vector3.UnitY, CameraMath.Forward(0, 90));
    }

    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(45f, 20f)]
    [InlineData(-120f, -35f)]
    [InlineData(179f, 80f)]
    public void LookAngles_inverts_Forward(float yaw, float pitch)
    {
        var (y, p) = CameraMath.LookAngles(CameraMath.Forward(yaw, pitch));
        Assert.InRange(CameraMath.DeltaAngle(yaw, y), -1e-2f, 1e-2f);
        Assert.InRange(p - pitch, -1e-2f, 1e-2f);
    }

    [Fact]
    public void Right_is_flat_and_perpendicular()
    {
        Close(Vector3.UnitX, CameraMath.Right(0));
        Assert.InRange(Vector3.Dot(CameraMath.Right(30), CameraMath.Forward(30, 0)), -1e-5f, 1e-5f);
    }

    [Fact]
    public void Inside_the_sphere_is_unchanged_and_outside_lands_on_it()
    {
        Close(new Vector3(1, 2, 3), CameraMath.ProjectIntoSphere(new Vector3(1, 2, 3), Vector3.Zero, 30));
        var p = CameraMath.ProjectIntoSphere(new Vector3(0, 0, 100), new Vector3(0, 0, 10), 30);
        Close(new Vector3(0, 0, 40), p);
    }

    [Fact]
    public void Moving_tangentially_at_the_boundary_slides_and_never_stops_dead()
    {
        var p = new Vector3(30, 0, 0);
        var lastZ = 0f;
        for (var i = 0; i < 10; i++)
        {
            p = CameraMath.ProjectIntoSphere(p + new Vector3(0, 0, 1), Vector3.Zero, 30);
            Assert.InRange(p.Length(), 29.999f, 30.001f);
            Assert.True(p.Z > lastZ);
            lastZ = p.Z;
        }
    }

    [Fact]
    public void Damping_snaps_at_zero_tau_and_converges_exponentially()
    {
        Assert.Equal(10f, CameraMath.Damp(0f, 10f, 0f, 0.016f));
        var v = CameraMath.Damp(0f, 10f, 0.2f, 1.0f);   // 5 tau
        Assert.InRange(v, 9.9f, 10f);
        var half = CameraMath.Damp(0f, 10f, 0.2f, 0.2f); // 1 tau → 1 - e^-1
        Assert.InRange(half, 6.32f, 6.33f);
    }

    [Fact]
    public void Angle_damping_takes_the_short_arc()
    {
        var a = CameraMath.DampAngle(350f, 10f, 0.1f, 0.05f);
        Assert.True(a > 350f && a < 370f, $"went the long way: {a}");
    }

    [Fact]
    public void Clamps_follow_the_spec()
    {
        Assert.Equal(90f, CameraMath.ClampRoll(120f));
        Assert.Equal(-90f, CameraMath.ClampRoll(-500f));
        Assert.Equal(10f, CameraMath.ClampFov(5f));
        Assert.Equal(100f, CameraMath.ClampFov(120f));
        Assert.Equal(89f, CameraMath.ClampPitch(95f));
    }

    [Fact]
    public void Roll_steps_60_deg_per_second_or_15_fine_and_clamps()
    {
        Assert.Equal(60f, CameraMath.StepRoll(0f, 1f, false, 1f));
        Assert.Equal(15f, CameraMath.StepRoll(0f, 1f, true, 1f));
        Assert.Equal(90f, CameraMath.StepRoll(80f, 1f, false, 1f));
        Assert.Equal(-60f, CameraMath.StepRoll(0f, -1f, false, 1f));
    }

    [Fact]
    public void Fov_steps_2_deg_per_notch_wheel_up_zooms_in()
    {
        Assert.Equal(43f, CameraMath.StepFov(45f, 1f));
        Assert.Equal(47f, CameraMath.StepFov(45f, -1f));
        Assert.Equal(10f, CameraMath.StepFov(11f, 2f));
    }

    [Fact]
    public void Smoothing_and_speed_factors()
    {
        Assert.Equal(0f, CameraMath.SmoothingToTau(0f));
        Assert.Equal(0.35f, CameraMath.SmoothingToTau(1f), 5);
        Assert.Equal(0.105f, CameraMath.SmoothingToTau(0.3f), 5);
        Assert.Equal(3f, CameraMath.SpeedFactor(true, false));
        Assert.Equal(0.25f, CameraMath.SpeedFactor(false, true));
        Assert.Equal(1f, CameraMath.SpeedFactor(false, false));
    }
}
