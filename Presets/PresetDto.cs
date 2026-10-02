using Stellar.Abstractions.Domain;
namespace Stellar.PhotoStudio.Presets;

// Own DTO (rather than serializing LookSettings directly) so preset JSON on disk stays stable if the
// framework's LookSettings record ever changes shape — only PresetDto.From/ToLook need to move.
// Shape (spec 2026-10-03 photo shapes): null = the preset sets no shape (built-ins, files saved before shapes) and
// applying it leaves the current shape alone.
internal sealed record Preset(string Name, bool BuiltIn, LookSettings Look, PhotoShape? Shape = null);

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

    public static PresetDto From(string name, LookSettings s, PhotoShape? shape = null) => new()
    {
        Name = name,
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
