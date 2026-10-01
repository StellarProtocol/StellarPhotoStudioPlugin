using System;
using System.Numerics;
using Stellar.Abstractions.Domain;

namespace Stellar.PhotoStudio.FreeCam;

/// <summary>Pure camera maths (Unity axes: +Y up, yaw 0 = +Z, positive pitch looks down). No game dependency.</summary>
internal static class CameraMath
{
    internal const float MinFov = 10f, MaxFov = 100f, MaxRoll = 90f, MaxPitch = 89f;
    internal const float MaxTau = 0.35f;            // smoothing 1.0 = 0.35 s time constant
    internal const float RollSpeed = 60f, FineRollSpeed = 15f, FovStep = 2f;

    internal static Vector3 Forward(float yaw, float pitch)
    {
        float y = Rad(yaw), p = Rad(pitch);
        return new Vector3(MathF.Sin(y) * MathF.Cos(p), -MathF.Sin(p), MathF.Cos(y) * MathF.Cos(p));
    }

    internal static Vector3 Right(float yaw)
    {
        var y = Rad(yaw);
        return new Vector3(MathF.Cos(y), 0f, -MathF.Sin(y));
    }

    internal static Vector3 Flat(Vector3 v)
    {
        var f = new Vector3(v.X, 0f, v.Z);
        var l = f.Length();
        return l < 1e-6f ? Vector3.UnitZ : f / l;
    }

    internal static (float Yaw, float Pitch) LookAngles(Vector3 dir)
    {
        var l = dir.Length();
        if (l < 1e-6f) return (0f, 0f);
        var d = dir / l;
        return (Deg(MathF.Atan2(d.X, d.Z)), Deg(-MathF.Asin(Math.Clamp(d.Y, -1f, 1f))));
    }

    /// <summary>The leash: a point outside the sphere is projected onto it, so motion along the edge slides on.</summary>
    internal static Vector3 ProjectIntoSphere(Vector3 p, Vector3 centre, float radius)
    {
        var d = p - centre;
        var l = d.Length();
        return l <= radius || l < 1e-6f ? p : centre + d * (radius / l);
    }

    internal static float Damp(float current, float target, float tau, float dt) =>
        tau <= 0f ? target : target + (current - target) * MathF.Exp(-dt / tau);

    internal static Vector3 Damp(Vector3 current, Vector3 target, float tau, float dt) =>
        tau <= 0f ? target : target + (current - target) * MathF.Exp(-dt / tau);

    internal static float DampAngle(float current, float target, float tau, float dt) =>
        tau <= 0f ? target : current + DeltaAngle(current, target) * (1f - MathF.Exp(-dt / tau));

    internal static float DeltaAngle(float from, float to)
    {
        var d = (to - from) % 360f;
        if (d > 180f) d -= 360f;
        if (d < -180f) d += 360f;
        return d;
    }

    internal static float WrapYaw(float yaw)
    {
        var y = yaw % 360f;
        if (y > 180f) y -= 360f;
        if (y <= -180f) y += 360f;
        return y;
    }

    internal static float SmoothingToTau(float smoothing) => Math.Clamp(smoothing, 0f, 1f) * MaxTau;
    internal static float ClampRoll(float roll) => Math.Clamp(roll, -MaxRoll, MaxRoll);
    internal static float ClampFov(float fov) => Math.Clamp(fov, MinFov, MaxFov);
    internal static float ClampPitch(float pitch) => Math.Clamp(pitch, -MaxPitch, MaxPitch);
    internal static float StepRoll(float roll, float axis, bool fine, float dt) => ClampRoll(roll + axis * (fine ? FineRollSpeed : RollSpeed) * dt);
    internal static float StepFov(float fov, float wheel) => ClampFov(fov - wheel * FovStep);
    internal static float SpeedFactor(bool shift, bool ctrl) => shift ? 3f : ctrl ? 0.25f : 1f;
    internal static Vector3 ToVec(Position3D p) => new(p.X, p.Y, p.Z);
    internal static Position3D ToPos(Vector3 v) => new(v.X, v.Y, v.Z);

    private static float Rad(float deg) => deg * MathF.PI / 180f;
    private static float Deg(float rad) => rad * 180f / MathF.PI;
}
