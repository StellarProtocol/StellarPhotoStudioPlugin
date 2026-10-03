using System;
using System.Collections.Generic;
using System.Linq;
using Stellar.Abstractions.Domain;

namespace Stellar.PhotoStudio.Lights;

/// <summary>One lamp of a preset, relative to the person selected when it was saved (lights spec § 5).</summary>
internal sealed record PresetLamp(LampPlacement Placement, RgbColor Color, float Strength, float Range, bool On);

/// <summary>The lights a preset carries: lamps relative to the selected person, the Light-people level, and the key light
/// and rim of the selected person (null = none).</summary>
internal sealed record LightsPreset(IReadOnlyList<PresetLamp> Lamps, float PeopleLevel, KeyLight? Key, RimLight? Rim)
{
    /// <summary>Slack for a value that went through placement maths or JSON (a lamp re-captured around the same person
    /// reads back a hair off — lights review I-5: an exact compare kept Save lit for an unchanged scene).</summary>
    internal const float Tolerance = 1e-3f;

    /// <summary>The same lights, within <see cref="Tolerance"/> (angles compared around the circle).</summary>
    public bool Equivalent(LightsPreset? other)
    {
        if (other is null || !Near(PeopleLevel, other.PeopleLevel) || !Same(Key, other.Key) || !Same(Rim, other.Rim) ||
            Lamps.Count != other.Lamps.Count) return false;
        for (var i = 0; i < Lamps.Count; i++)
            if (!Same(Lamps[i], other.Lamps[i])) return false;
        return true;
    }

    private static bool Same(PresetLamp a, PresetLamp b) =>
        a.On == b.On && NearAngle(a.Placement.Around, b.Placement.Around) && Near(a.Placement.Height, b.Placement.Height) &&
        Near(a.Placement.Distance, b.Placement.Distance) && Same(a.Color, b.Color) && Near(a.Strength, b.Strength) &&
        Near(a.Range, b.Range);

    private static bool Same(KeyLight? a, KeyLight? b) =>
        a is null ? b is null : b is not null && NearAngle(a.Value.Direction, b.Value.Direction) && Near(a.Value.Height, b.Value.Height);

    private static bool Same(RimLight? a, RimLight? b) =>
        a is null ? b is null : b is not null && Same(a.Value.Color, b.Value.Color) && Near(a.Value.Strength, b.Value.Strength);

    private static bool Same(RgbColor a, RgbColor b) => Near(a.R, b.R) && Near(a.G, b.G) && Near(a.B, b.B);

    private static bool Near(float a, float b) => MathF.Abs(a - b) <= Tolerance;

    private static bool NearAngle(float a, float b) => MathF.Abs(LightsMath.Wrap(a - b)) <= Tolerance;
}

/// <summary>JSON shape of <see cref="LightsPreset"/> (own DTO, like PresetDto, so the file format stays stable).</summary>
internal sealed class LightsPresetDto
{
    public float People { get; set; }
    public float[][]? Lamps { get; set; }   // around, height, distance, r, g, b, strength, range, on(0/1)
    public float[]? Key { get; set; }       // direction, height
    public float[]? Rim { get; set; }       // r, g, b, strength

    public static LightsPresetDto From(LightsPreset p) => new()
    {
        People = p.PeopleLevel,
        Lamps = p.Lamps.Select(l => new[]
        {
            l.Placement.Around, l.Placement.Height, l.Placement.Distance, l.Color.R, l.Color.G, l.Color.B, l.Strength, l.Range,
            l.On ? 1f : 0f,
        }).ToArray(),
        Key = p.Key is { } k ? new[] { k.Direction, k.Height } : null,
        Rim = p.Rim is { } r ? new[] { r.Color.R, r.Color.G, r.Color.B, r.Strength } : null,
    };

    /// <summary>Back to a preset; malformed lamp rows are skipped, at most <see cref="LightsController.MaxLamps"/> kept, and
    /// every value clamped to what the controls and the framework accept (a hand-edited or foreign file — lights review).</summary>
    public LightsPreset ToPreset() => new(
        (Lamps ?? Array.Empty<float[]>())
            .Where(a => a is { Length: 9 })
            .Take(LightsController.MaxLamps)
            .Select(a => new PresetLamp(LightsMath.Clamp(new LampPlacement(Finite(a[0]), Finite(a[1]), Finite(a[2]))),
                Color(a[3], a[4], a[5]), Clamp(a[6], 0f, LightLimits.MaxStrength, LightsController.DefaultStrength),
                Clamp(a[7], LightLimits.MinRange, LightLimits.MaxRange, LightsController.DefaultRange), a[8] > 0.5f))
            .ToList(),
        Clamp(People, 0f, LightLimits.MaxPeopleLevel, LightsController.DefaultPeopleLevel),
        Key is { Length: 2 } k
            ? new KeyLight(LightsMath.Wrap(Finite(k[0])), Clamp(k[1], LightsMath.MinKeyHeight, LightsMath.MaxKeyHeight, 0f))
            : null,
        Rim is { Length: 4 } r
            ? new RimLight(Color(r[0], r[1], r[2]), Clamp(r[3], 0f, LightLimits.MaxRimStrength, 0f))
            : null);

    private static RgbColor Color(float r, float g, float b) => new(Clamp(r, 0f, 1f, 1f), Clamp(g, 0f, 1f, 1f), Clamp(b, 0f, 1f, 1f));

    private static float Clamp(float v, float min, float max, float fallback) => float.IsFinite(v) ? Math.Clamp(v, min, max) : fallback;

    private static float Finite(float v) => float.IsFinite(v) ? v : 0f;
}
