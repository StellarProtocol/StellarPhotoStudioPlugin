using System;
using System.Numerics;

namespace Stellar.PhotoStudio.FreeCam;

/// <summary>Orbit state: the pivot is the subject's chest height plus <see cref="PivotOffset"/> (WASD/Q/E slide it).</summary>
internal readonly record struct OrbitState(Vector3 PivotOffset, float Distance, float Yaw, float Pitch);

/// <summary>A rig's target camera pose for this frame (before damping).</summary>
internal readonly record struct RigOutput(Vector3 Position, float Yaw, float Pitch);

/// <summary>Orbit mode (spec § 3): RMB-drag rotates round the subject, wheel = distance, WASD slides the pivot (inside
/// the leash), Q/E lower/raise it.</summary>
internal static class OrbitRig
{
    internal const float PivotHeight = 1.3f;
    internal const float MinDistance = 0.5f;
    internal const float DegPerPixel = 0.15f;
    internal const float WheelZoom = 0.1f;

    /// <summary>Orbit that reproduces <paramref name="camera"/> and its angles exactly (entry, mode switch, reset).</summary>
    internal static OrbitState FromCamera(Vector3 camera, float yaw, float pitch, Vector3 subject)
    {
        var forward = CameraMath.Forward(yaw, pitch);
        var basePivot = subject + Vector3.UnitY * PivotHeight;
        var distance = MathF.Max(Vector3.Dot(basePivot - camera, forward), MinDistance);
        var pivot = camera + forward * distance;
        return new OrbitState(pivot - basePivot, distance, yaw, pitch);
    }

    /// <summary>Orbit centred on <paramref name="subject"/>, seen from <paramref name="camera"/> (a new subject).</summary>
    internal static OrbitState FromPose(Vector3 camera, Vector3 subject)
    {
        var toPivot = subject + Vector3.UnitY * PivotHeight - camera;
        var (yaw, pitch) = CameraMath.LookAngles(toPivot);
        return new OrbitState(Vector3.Zero, MathF.Max(toPivot.Length(), MinDistance), yaw, CameraMath.ClampPitch(pitch));
    }

    internal static OrbitState Step(OrbitState s, in CamIntent i, RigTuning t, float dt)
    {
        var (yaw, pitch) = Look(s.Yaw, s.Pitch, i, t);
        var distance = s.Distance;
        if (i.Wheel != 0f && !i.Shift) distance *= MathF.Pow(1f - WheelZoom, i.Wheel);
        distance = Math.Clamp(distance, MinDistance, t.Leash);
        var step = t.MoveSpeed * CameraMath.SpeedFactor(i.Shift, i.Ctrl) * dt;
        var forward = CameraMath.Flat(CameraMath.Forward(yaw, 0f));
        var move = CameraMath.Right(yaw) * i.Move.X + Vector3.UnitY * i.Move.Y + forward * i.Move.Z;
        var offset = CameraMath.ProjectIntoSphere(s.PivotOffset + move * step, Vector3.Zero, t.Leash);
        return new OrbitState(offset, distance, yaw, pitch);
    }

    internal static RigOutput Place(OrbitState s, Vector3 centre)
    {
        var pivot = centre + Vector3.UnitY * PivotHeight + s.PivotOffset;
        return new RigOutput(pivot - CameraMath.Forward(s.Yaw, s.Pitch) * s.Distance, s.Yaw, s.Pitch);
    }

    /// <summary>Mouse-look while RMB is held; mouse down = look down unless Y is inverted.</summary>
    internal static (float Yaw, float Pitch) Look(float yaw, float pitch, in CamIntent i, RigTuning t)
    {
        if (!i.Looking) return (yaw, pitch);
        var k = DegPerPixel * t.Sensitivity;
        return (CameraMath.WrapYaw(yaw + i.LookX * k), CameraMath.ClampPitch(pitch + (t.InvertY ? -1f : 1f) * i.LookY * k));
    }
}
