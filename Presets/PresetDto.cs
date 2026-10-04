using Stellar.Abstractions.Domain;
using Stellar.PhotoStudio.ReShade;
namespace Stellar.PhotoStudio.Presets;

// Own DTO (rather than serializing LookSettings directly) so preset JSON on disk stays stable if the
// framework's LookSettings record ever changes shape — only PresetDto.From/ToLook need to move.
// Shape (spec 2026-10-03 photo shapes): null = the preset sets no shape (built-ins, files saved before shapes) and
// applying it leaves the current shape alone.
// Lights (spec 2026-10-03 lights § 5): null = the preset carries no lights and applying it leaves the scene's lights alone.
internal sealed record Preset(string Name, bool BuiltIn, LookSettings Look, PhotoShape? Shape = null, Lights.LightsPreset? Lights = null)
{
    /// <summary>The ReShade preset + on/off this look uses (spec 2026-10-03 reshade § 6); null = it leaves ReShade alone.</summary>
    public ReShadeChoice? ReShade { get; init; }
}

/// <summary>On-disk form of <see cref="ReShadeChoice"/>: the preset FILE NAME only, so a preset travels between PCs and a
/// crafted "../x.ini" never reaches outside Photo Studio's ReShade presets folder. In memory a choice may hold a full
/// path (ReShade's own preset, outside our folder — review fix 2026-10-04): such a path is NOT stored (the look then keeps
/// on/off only and leaves ReShade's preset alone), because its bare name would re-open as an empty preset in our folder.
/// A name that is not a safe ".ini" file name on Windows and Linux drops the preset too.</summary>
internal sealed class ReShadeDto
{
    public string? Preset { get; set; }
    public bool Enabled { get; set; } = true;

    public static ReShadeDto From(ReShadeChoice c) => new() { Preset = FileOnly(c.Preset), Enabled = c.Enabled };

    public ReShadeChoice ToChoice() => new(FileOnly(Preset), Enabled);

    private static string? FileOnly(string? p)
    {
        if (p is not { Length: > 0 }) return null;
        if (ReShadePaths.HasFolder(p) && !ReShadePaths.InPresetsFolder(p)) return null;
        var name = ReShadePaths.FileName(p);
        if (!name.EndsWith(".ini", System.StringComparison.OrdinalIgnoreCase)) return null;
        return PresetNames.IsSafeFileStem(name.Substring(0, name.Length - 4)) ? name : null;
    }
}

internal sealed class PresetDto
{
    public int Version { get; set; } = 1;
    public string Name { get; set; } = "";
    public float[]? Dof { get; set; }          // focus, aperture, focal, focusOnPlayer(0/1)
    public float[]? Color { get; set; }        // exposure, contrast, saturation, r, g, b
    public float[]? WhiteBalance { get; set; } // temperature, tint
    public string? LutFile { get; set; }
    public float LutContribution { get; set; } = 1f;
    public float[]? Bloom { get; set; }        // intensity, threshold
    public float[]? Vignette { get; set; }     // intensity, smoothness
    public float[]? FilmGrain { get; set; }    // intensity, response
    // Photo shape key ("screen", "9:16", …; PhotoShapes.Key). Absent/null = no shape. Additive: an older build's
    // reader ignores the unknown property, so a rollback keeps reading these files.
    public string? Shape { get; set; }
    // Lights (lamps relative to the selected person, Light people, key + rim). Absent/null = no lights. Additive.
    public Lights.LightsPresetDto? Lights { get; set; }
    // ReShade preset file + on/off (spec 2026-10-03 reshade § 6). Absent/null = the preset leaves ReShade alone. Additive.
    public ReShadeDto? ReShade { get; set; }

    public static PresetDto From(string name, LookSettings s, PhotoShape? shape = null, Lights.LightsPreset? lights = null,
        ReShadeChoice? reshade = null) => new()
    {
        Name = name,
        ReShade = reshade is null ? null : ReShadeDto.From(reshade),
        Lights = lights is null ? null : Stellar.PhotoStudio.Lights.LightsPresetDto.From(lights),
        Shape = shape is { } sh ? PhotoShapes.Key(sh) : null,
        Dof = s.Dof is { } d ? new[] { d.FocusDistance, d.Aperture, d.FocalLength, d.FocusOnLocalPlayer ? 1f : 0f } : null,
        Color = s.Color is { } c ? new[] { c.PostExposure, c.Contrast, c.Saturation, c.Filter.R, c.Filter.G, c.Filter.B } : null,
        WhiteBalance = s.WhiteBalance is { } w ? new[] { w.Temperature, w.Tint } : null,
        // An empty FilePath means "no LUT" (same as Lut being absent entirely) — normalize it away
        // here so a round trip never has to distinguish "no LUT" from "a LUT with no file".
        // File name only: an exported preset must find the LUT in the importer's own LUT folder.
        LutFile = s.Lut is { FilePath.Length: > 0 } lut ? System.IO.Path.GetFileName(lut.FilePath) : null,
        LutContribution = s.Lut?.Contribution ?? 1f,
        Bloom = s.Bloom is { } b ? new[] { b.Intensity, b.Threshold } : null,
        Vignette = s.Vignette is { } v ? new[] { v.Intensity, v.Smoothness } : null,
        FilmGrain = s.FilmGrain is { } f ? new[] { f.Intensity, f.Response } : null,
    };

    /// <summary>The preset's shape; null when it sets none (or names a shape this build does not know).</summary>
    public PhotoShape? ToShape() => PhotoShapes.TryParse(Shape);

    /// <summary>The preset's lights; null when it carries none.</summary>
    public Lights.LightsPreset? ToLights() => Lights?.ToPreset();

    /// <summary>The preset's ReShade choice; null when it carries none.</summary>
    public ReShadeChoice? ToReShade() => ReShade?.ToChoice();

    public LookSettings ToLook() => new()
    {
        Dof = Dof is { Length: 4 } d ? new DofLook { FocusDistance = d[0], Aperture = d[1], FocalLength = d[2], FocusOnLocalPlayer = d[3] > 0.5f } : null,
        Color = Color is { Length: 6 } c ? new ColorLook { PostExposure = c[0], Contrast = c[1], Saturation = c[2], Filter = new RgbColor(c[3], c[4], c[5]) } : null,
        WhiteBalance = WhiteBalance is { Length: 2 } w ? new WhiteBalanceLook { Temperature = w[0], Tint = w[1] } : null,
        // File name only, also for imported files: a crafted "../x.png" must not reach outside the LUT folder.
        Lut = LutFile is { Length: > 0 } l && System.IO.Path.GetFileName(l).Length > 0
            ? new LutLook { FilePath = System.IO.Path.GetFileName(l), Contribution = LutContribution } : null,
        Bloom = Bloom is { Length: 2 } b ? new BloomLook { Intensity = b[0], Threshold = b[1] } : null,
        Vignette = Vignette is { Length: 2 } v ? new VignetteLook { Intensity = v[0], Smoothness = v[1] } : null,
        FilmGrain = FilmGrain is { Length: 2 } f ? new FilmGrainLook { Intensity = f[0], Response = f[1] } : null,
    };
}
