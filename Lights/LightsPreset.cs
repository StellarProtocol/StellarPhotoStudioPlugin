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
    public bool Equivalent(LightsPreset? other) =>
        other is not null && PeopleLevel == other.PeopleLevel && Key == other.Key && Rim == other.Rim &&
        Lamps.SequenceEqual(other.Lamps);
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

    /// <summary>Back to a preset; malformed lamp rows are skipped and at most <see cref="LightsController.MaxLamps"/> kept.</summary>
    public LightsPreset ToPreset() => new(
        (Lamps ?? System.Array.Empty<float[]>())
            .Where(a => a is { Length: 9 })
            .Take(LightsController.MaxLamps)
            .Select(a => new PresetLamp(LightsMath.Clamp(new LampPlacement(a[0], a[1], a[2])), new RgbColor(a[3], a[4], a[5]), a[6], a[7],
                a[8] > 0.5f))
            .ToList(),
        People,
        Key is { Length: 2 } k ? new KeyLight(k[0], k[1]) : null,
        Rim is { Length: 4 } r ? new RimLight(new RgbColor(r[0], r[1], r[2]), r[3]) : null);
}
