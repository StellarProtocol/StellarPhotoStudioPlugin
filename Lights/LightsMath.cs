using System;
using System.Numerics;

namespace Stellar.PhotoStudio.Lights;

/// <summary>Where a lamp sits relative to a person (lights spec § 1): <see cref="Around"/> degrees from the way the person
/// faces (positive = toward their right, ±180 = behind them), <see cref="Height"/> metres above their feet and
/// <see cref="Distance"/> metres out along the ground.</summary>
internal readonly record struct LampPlacement(float Around, float Height, float Distance);

/// <summary>A person a lamp is placed around: where they stand (feet) and which way they face (Unity yaw, degrees).</summary>
internal readonly record struct LightAnchor(Vector3 Position, float Yaw);

/// <summary>Relative placement ↔ world (Unity axes: +Y up, yaw 0 = +Z, 90 = +X). Pure.</summary>
internal static class LightsMath
{
    // The ONE source of the placement ranges (lights review minor): the Around / Height / Distance sliders bind these, so a
    // slider can never show or set a value the maths would clamp away.
    internal const float MinAround = -180f, MaxAround = 180f;
    internal const float MinHeight = -2f, MaxHeight = 10f;
    internal const float MinDistance = 0.3f, MaxDistance = 20f;

    /// <summary>Person key-light height, degrees (the controller and preset files clamp to it; the slider binds it).</summary>
    internal const float MinKeyHeight = -89f, MaxKeyHeight = 89f;

    /// <summary>The world position of <paramref name="p"/> around <paramref name="a"/> (clamped to the slider ranges).</summary>
    public static Vector3 ToWorld(LightAnchor a, LampPlacement p)
    {
        var c = Clamp(p);
        var rad = (a.Yaw + c.Around) * (MathF.PI / 180f);
        return a.Position + new Vector3(MathF.Sin(rad) * c.Distance, c.Height, MathF.Cos(rad) * c.Distance);
    }

    /// <summary>Where <paramref name="world"/> sits around <paramref name="a"/>. A point right above the person reads
    /// around 0 (no direction to measure).</summary>
    public static LampPlacement FromWorld(LightAnchor a, Vector3 world)
    {
        var d = world - a.Position;
        var flat = MathF.Sqrt(d.X * d.X + d.Z * d.Z);
        var around = flat < 1e-4f ? 0f : Wrap(MathF.Atan2(d.X, d.Z) * (180f / MathF.PI) - a.Yaw);
        return new LampPlacement(around, d.Y, flat);
    }

    /// <summary>The slider ranges: around −180…180, height −2…10 m, distance 0.3…20 m.</summary>
    public static LampPlacement Clamp(LampPlacement p) => new(
        Wrap(p.Around),
        Math.Clamp(p.Height, MinHeight, MaxHeight),
        Math.Clamp(p.Distance, MinDistance, MaxDistance));

    /// <summary>An angle in (−180, 180].</summary>
    public static float Wrap(float deg)
    {
        var a = deg % 360f;
        if (a <= -180f) a += 360f;
        else if (a > 180f) a -= 360f;
        return a;
    }
}
