using System.Collections.Generic;
using Stellar.Abstractions.Domain;
namespace Stellar.PhotoStudio.Presets;

/// <summary>The five stock looks shipped with the plugin; read-only (see <see cref="PresetStore"/>).</summary>
internal static class BuiltInPresets
{
    public static readonly IReadOnlyList<Preset> All = new[]
    {
        new Preset("Natural", true, new LookSettings()),
        new Preset("Cinematic", true, new LookSettings
        {
            Dof = new DofLook { Aperture = 2.8f, FocalLength = 50f, FocusOnLocalPlayer = true },
            Color = new ColorLook { Contrast = 15f, Saturation = -5f },
            Vignette = new VignetteLook { Intensity = 0.3f, Smoothness = 0.4f },
            Bloom = new BloomLook { Intensity = 0.8f },
        }),
        new Preset("Warm Dusk", true, new LookSettings
        {
            WhiteBalance = new WhiteBalanceLook { Temperature = 25f, Tint = 5f },
            Color = new ColorLook { PostExposure = 0.1f, Saturation = 10f },
        }),
        new Preset("Noir", true, new LookSettings
        {
            Color = new ColorLook { Saturation = -100f, Contrast = 35f },
            Vignette = new VignetteLook { Intensity = 0.45f, Smoothness = 0.5f },
            FilmGrain = new FilmGrainLook { Intensity = 0.35f },
        }),
        new Preset("Film", true, new LookSettings
        {
            FilmGrain = new FilmGrainLook { Intensity = 0.25f, Response = 0.8f },
            Color = new ColorLook { Contrast = 10f, Saturation = -10f },
            WhiteBalance = new WhiteBalanceLook { Temperature = 8f },
        }),
    };
}
