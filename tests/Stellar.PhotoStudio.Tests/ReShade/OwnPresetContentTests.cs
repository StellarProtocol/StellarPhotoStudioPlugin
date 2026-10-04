using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Stellar.PhotoStudio.ReShade;
using Xunit;

namespace Stellar.PhotoStudio.Tests.ReShade;

// Photo Studio's own presets use ONLY techniques of the pinned packs (plan § Re-measure: `technique` lines of every
// .fx at standard fd00221 / SweetFX 93ddf39 / prod80 1c2ed5b, + the two macro-named prod80 LUT techniques; and the five
// FXShaders 76365e3 / OtisFX 193aa0b techniques the 2026-10-05 looks use, compiled with ReShade 6.8.0's own front end),
// set only real uniforms within their ui_min..ui_max, and enable a depth-reading effect (measured with the framework's
// scanner rule) only where pinned in DepthPresets.
public sealed class OwnPresetContentTests
{
    // (effect file, technique) -> pack id. 72 rows, measured from clones at the pinned commits.
    private static readonly Dictionary<(string File, string Technique), string> Inventory = Build(@"
standard Daltonize.fx Daltonize
standard Deband.fx Deband
standard DisplayDepth.fx DisplayDepth
standard LUT.fx LUT
standard UIMask.fx UIMask_Top
standard UIMask.fx UIMask_Bottom
sweetfx ASCII.fx ASCII
sweetfx Border.fx Border
sweetfx Cartoon.fx Cartoon
sweetfx CAS.fx ContrastAdaptiveSharpen
sweetfx ChromaticAberration.fx CA
sweetfx ColorMatrix.fx ColorMatrix
sweetfx Compare.fx Capture
sweetfx Compare.fx Restore
sweetfx Compare.fx Compare
sweetfx CRT.fx AdvancedCRT
sweetfx Curves.fx Curves
sweetfx DPX.fx DPX
sweetfx FakeHDR.fx HDR
sweetfx FilmGrain.fx FilmGrain
sweetfx FXAA.fx FXAA
sweetfx Layer.fx Layer
sweetfx Levels.fx Levels
sweetfx LiftGammaGain.fx LiftGammaGain
sweetfx LumaSharpen.fx LumaSharpen
sweetfx Monochrome.fx Monochrome
sweetfx Nostalgia.fx Nostalgia
sweetfx Sepia.fx Tint
sweetfx SMAA.fx SMAA
sweetfx Splitscreen.fx Before
sweetfx Splitscreen.fx After
sweetfx Technicolor2.fx Technicolor2
sweetfx Technicolor.fx Technicolor
sweetfx Template.fx Template
sweetfx Tonemap.fx Tonemap
sweetfx Vibrance.fx Vibrance
sweetfx Vignette.fx Vignette
prod80 PD80_01A_RT_Correct_Contrast.fx prod80_01A_RT_Correct_Contrast
prod80 PD80_01B_RT_Correct_Color.fx prod80_01B_RT_Correct_Color
prod80 PD80_01_Color_Gamut.fx prod80_01_Color_Gamut
prod80 PD80_02_Bloom.fx prod80_02_Bloom
prod80 PD80_02_Bonus_LUT_pack.fx prod80_02_Bonus_LUT_pack
prod80 PD80_02_Cinetools_LUT.fx prod80_02_Cinetools_LUT
prod80 PD80_02_LUT_Creator.fx prod80_02_LUT_Creator
prod80 PD80_03_Color_Space_Curves.fx prod80_03_Color_Space_Curves
prod80 PD80_03_Curved_Levels.fx prod80_03_CurvedLevels
prod80 PD80_03_Filmic_Adaptation.fx prod80_03_FilmicTonemap
prod80 PD80_03_Levels.fx prod80_03_Levels
prod80 PD80_03_Shadows_Midtones_Highlights.fx prod80_03_Shadows_Midtones_Highlights
prod80 PD80_04_BlacknWhite.fx prod80_04_Black_and_White
prod80 PD80_04_Color_Balance.fx prod80_04_ColorBalance
prod80 PD80_04_Color_Gradients.fx prod80_04_ColorGradient
prod80 PD80_04_Color_Isolation.fx prod80_04_ColorIsolation
prod80 PD80_04_Color_Temperature.fx prod80_04_ColorTemperature
prod80 PD80_04_Contrast_Brightness_Saturation.fx prod80_04_ContrastBrightnessSaturation
prod80 PD80_04_Magical_Rectangle.fx prod80_04_Magical_Rectangle
prod80 PD80_04_Saturation_Limit.fx prod80_04_Saturation_Limiter
prod80 PD80_04_Selective_Color.fx prod80_04_SelectiveColor
prod80 PD80_04_Selective_Color_v2.fx prod80_04_SelectiveColor_v2
prod80 PD80_04_Technicolor.fx prod80_04_Technicolor
prod80 PD80_05_Sharpening.fx prod80_05_LumaSharpen
prod80 PD80_06_Chromatic_Aberration.fx prod80_06_ChromaticAberration
prod80 PD80_06_Depth_Slicer.fx prod80_06_Depth_Slicer
prod80 PD80_06_Film_Grain.fx prod80_06_FilmGrain
prod80 PD80_06_Luma_Fade.fx prod80_06_LumaFade_Start
prod80 PD80_06_Luma_Fade.fx prod80_06_LumaFade_End
prod80 PD80_06_Posterize_Pixelate.fx prod80_06_Posterize_Pixelate
fxshaders NeoBloom.fx NeoBloom
fxshaders MagicHDR.fx MagicHDR
fxshaders HexLensFlare.fx HexLensFlare
fxshaders ArtisticVignette.fx ArtisticVignette
otisfx Emphasize.fx Emphasize");

    // Effect files the framework's EffectDepthScanner finds depth-FREE (includes flattened; standard headers skipped).
    private static readonly HashSet<string> DepthFree = new(StringComparer.OrdinalIgnoreCase)
    {
        "Curves.fx", "Vibrance.fx", "Vignette.fx", "CAS.fx", "LiftGammaGain.fx",
        "PD80_04_Color_Temperature.fx", "PD80_02_Bloom.fx", "PD80_04_Color_Balance.fx",
        "MagicHDR.fx", "HexLensFlare.fx", "ArtisticVignette.fx",
    };

    // Own presets that enable a depth-FLAGGED effect (owner-approved looks, 2026-10-05) -> those effect files. Emphasize
    // reads depth by design. NeoBloom reads depth only under NEO_BLOOM_DEPTH (0 by default; the preset sets no
    // definitions), but the scanner ORs every #if branch, so it is flagged too: in photos where depth effects are left
    // out, Dreamy glow keeps only its MagicHDR glow.
    private static readonly Dictionary<string, string[]> DepthPresets = new(StringComparer.Ordinal)
    {
        ["dreamy-glow"] = new[] { "NeoBloom.fx" },
        ["subject-focus"] = new[] { "Emphasize.fx" },
    };

    // Uniforms of the effects our presets set: name -> (ui_min, ui_max, components). From the pinned sources, comments stripped.
    private static readonly Dictionary<string, Dictionary<string, (double Min, double Max, int N)>> Uniforms =
        new(StringComparer.OrdinalIgnoreCase)
    {
        ["Curves.fx"] = new() { ["Mode"] = (0, 2, 1), ["Formula"] = (0, 10, 1), ["Contrast"] = (-1, 1, 1) },
        ["Vibrance.fx"] = new() { ["Vibrance"] = (-1, 1, 1), ["VibranceRGBBalance"] = (0, 10, 3) },
        ["Vignette.fx"] = new()
        {
            ["Type"] = (0, 6, 1), ["Ratio"] = (0.15, 6, 1), ["Radius"] = (-1, 3, 1), ["Amount"] = (-2, 1, 1), ["Slope"] = (2, 16, 1),
            ["Center"] = (0, 1, 2),
        },
        ["CAS.fx"] = new() { ["Contrast"] = (0, 1, 1), ["Sharpening"] = (0, 1, 1) },
        ["LiftGammaGain.fx"] = new() { ["RGB_Lift"] = (0, 2, 3), ["RGB_Gamma"] = (0, 2, 3), ["RGB_Gain"] = (0, 2, 3) },
        ["PD80_04_Color_Temperature.fx"] = new() { ["Kelvin"] = (1000, 40000, 1), ["LumPreservation"] = (0, 1, 1), ["kMix"] = (0, 1, 1) },
        ["PD80_02_Bloom.fx"] = new()
        {
            ["BloomMix"] = (0, 1, 1), ["BloomLimit"] = (0, 1, 1), ["GreyValue"] = (0, 1, 1), ["bExposure"] = (-1, 5, 1),
            ["BlurSigmaNarrow"] = (10, 40, 1), ["BlurSigma"] = (10, 300, 1), ["BloomSaturation"] = (0, 2, 1),
        },
        ["NeoBloom.fx"] = new()
        {
            ["Intensity"] = (0, 1, 1), ["Saturation"] = (0, 3, 1), ["ColorFilter"] = (0, 1, 3), ["BloomBlendMode"] = (0, 2, 1),
            ["Mean"] = (0, 5, 1), ["Variance"] = (1, 5, 1), ["MaxBrightness"] = (1, 1000, 1), ["NormalizeBrightness"] = (0, 1, 1),
            ["MagicMode"] = (0, 1, 1), ["Sigma"] = (1, 10, 1), ["Padding"] = (0, 10, 1),
        },
        ["MagicHDR.fx"] = new()
        {
            ["InputExposure"] = (-3, 3, 1), ["Exposure"] = (-3, 3, 1), ["InvTonemap"] = (0, 5, 1), ["Tonemap"] = (0, 5, 1),
            ["BloomAmount"] = (0, 1, 1), ["BloomBrightness"] = (1, 5, 1), ["BloomSaturation"] = (0, 2, 1),
            ["BlurSize"] = (0.01, 1, 1), ["BlendingAmount"] = (0.1, 1, 1), ["BlendingBase"] = (0, 1, 1),
        },
        ["HexLensFlare.fx"] = new()
        {
            ["uIntensity"] = (0, 3, 1), ["uThreshold"] = (0, 1, 1), ["uScale"] = (0, 10, 1),
            ["uColor0"] = (0, 1, 3), ["uColor1"] = (0, 1, 3), ["uColor2"] = (0, 1, 3), ["uColor3"] = (0, 1, 3),
        },
        ["ArtisticVignette.fx"] = new()
        {
            ["VignetteColor"] = (0, 1, 4), ["BlendMode"] = (0, 7, 1), ["VignetteStartEnd"] = (0, 3, 2), ["VignetteRatio"] = (0, 1, 1),
            ["VignetteShape"] = (0, 6, 1),
        },
        ["Emphasize.fx"] = new()
        {
            ["FocusDepth"] = (0, 1, 1), ["FocusRangeDepth"] = (0, 1, 1), ["FocusEdgeDepth"] = (0, 1, 1), ["Spherical"] = (0, 1, 1),
            ["Sphere_FieldOfView"] = (1, 180, 1), ["Sphere_FocusHorizontal"] = (0, 1, 1), ["Sphere_FocusVertical"] = (0, 1, 1),
            ["BlendColor"] = (0, 1, 3), ["BlendFactor"] = (0, 1, 1), ["EffectFactor"] = (0, 1, 1),
        },
        ["PD80_04_Color_Balance.fx"] = new()
        {
            ["preserve_luma"] = (0, 1, 1), ["separation_mode"] = (0, 1, 1),
            ["s_RedShift"] = (-1, 1, 1), ["s_GreenShift"] = (-1, 1, 1), ["s_BlueShift"] = (-1, 1, 1),
            ["m_RedShift"] = (-1, 1, 1), ["m_GreenShift"] = (-1, 1, 1), ["m_BlueShift"] = (-1, 1, 1),
            ["h_RedShift"] = (-1, 1, 1), ["h_GreenShift"] = (-1, 1, 1), ["h_BlueShift"] = (-1, 1, 1),
        },
    };

    public static IEnumerable<object[]> Own() =>
        PresetCatalog.All.Where(e => e.Kind == PresetKind.Own).Select(e => new object[] { e.Id });

    [Theory]
    [InlineData("cinematic-warm", "prod80_04_ColorTemperature@PD80_04_Color_Temperature.fx,Curves@Curves.fx,Vibrance@Vibrance.fx,Vignette@Vignette.fx")]
    [InlineData("soft-anime", "prod80_02_Bloom@PD80_02_Bloom.fx,LiftGammaGain@LiftGammaGain.fx,Vibrance@Vibrance.fx")]
    [InlineData("cool-night", "prod80_04_ColorTemperature@PD80_04_Color_Temperature.fx,prod80_04_ColorBalance@PD80_04_Color_Balance.fx,Curves@Curves.fx,Vignette@Vignette.fx")]
    [InlineData("clean-sharpen", "ContrastAdaptiveSharpen@CAS.fx")]
    [InlineData("dreamy-glow", "NeoBloom@NeoBloom.fx,MagicHDR@MagicHDR.fx")]
    [InlineData("lens-flare", "HexLensFlare@HexLensFlare.fx,ArtisticVignette@ArtisticVignette.fx")]
    [InlineData("subject-focus", "Emphasize@Emphasize.fx")]
    public void Each_preset_enables_exactly_its_designed_techniques_in_order(string id, string techniques)
    {
        var ini = Parse(OwnPresets.Text(id));
        Assert.Equal(techniques, ini.Top["Techniques"]);
        Assert.Equal(techniques, ini.Top["TechniqueSorting"]);
    }

    [Theory]
    [MemberData(nameof(Own))]
    public void Techniques_exist_in_the_pinned_packs_and_the_catalog_lists_exactly_their_packs(string id)
    {
        var packs = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var t in Parse(OwnPresets.Text(id)).Top["Techniques"].Split(','))
        {
            var at = t.IndexOf('@');
            Assert.True(at > 0, "technique without @file: " + t);
            Assert.True(Inventory.TryGetValue((t[(at + 1)..], t[..at]), out var pack), id + ": not in the pinned packs: " + t);
            packs.Add(pack!);
        }
        Assert.Equal(packs, new SortedSet<string>(PresetCatalog.Find(id)!.Packs, StringComparer.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Own))]
    public void Only_pinned_presets_enable_a_depth_effect_and_none_sets_preprocessor_definitions(string id)
    {
        var ini = Parse(OwnPresets.Text(id));
        var depth = new List<string>();
        foreach (var t in ini.Top["Techniques"].Split(','))
        {
            var file = t[(t.IndexOf('@') + 1)..];
            if (!DepthFree.Contains(file)) depth.Add(file);
        }
        Assert.Equal(DepthPresets.TryGetValue(id, out var pinned) ? pinned : Array.Empty<string>(), depth);
        Assert.False(ini.Top.ContainsKey("PreprocessorDefinitions"));
        Assert.All(ini.Sections.Values, s => Assert.False(s.ContainsKey("PreprocessorDefinitions")));
    }

    [Theory]
    [MemberData(nameof(Own))]
    public void Sections_set_only_real_uniforms_of_enabled_effects_within_range(string id)
    {
        var ini = Parse(OwnPresets.Text(id));
        var enabled = ini.Top["Techniques"].Split(',').Select(t => t[(t.IndexOf('@') + 1)..]).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (effect, keys) in ini.Sections)
        {
            Assert.Contains(effect, enabled);
            foreach (var (key, value) in keys)
            {
                Assert.True(Uniforms[effect].TryGetValue(key, out var u), $"{id}: [{effect}] {key} is not a uniform");
                var parts = value.Split(',');
                Assert.Equal(u.N, parts.Length);
                foreach (var p in parts)
                {
                    var v = double.Parse(p, NumberStyles.Float, CultureInfo.InvariantCulture);
                    Assert.InRange(v, u.Min, u.Max);
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(Own))]
    public void Files_are_ascii_with_lf_line_ends(string id)
    {
        var text = OwnPresets.Text(id);
        Assert.All(text, c => Assert.True(c < 128, $"{id}: non-ASCII char U+{(int)c:X4}"));
        Assert.DoesNotContain("\r", text);
    }

    [Fact]
    public void An_unknown_id_throws()
    {
        Assert.Throws<InvalidOperationException>(() => OwnPresets.Text("nope"));
    }

    // Review fix round 2, item 7: every own preset carries its own licence line as a plain ini comment (the ini
    // parser already skips ';' lines, so this adds no new section/key). No em dash (U+2014): that is not ASCII, and
    // Files_are_ascii_with_lf_line_ends must still pass, so " - " is used instead.
    [Theory]
    [MemberData(nameof(Own))]
    public void Carries_the_AGPL_licence_line(string id)
    {
        Assert.Contains("; License: AGPL-3.0-or-later - https://github.com/StellarProtocol/StellarPhotoStudioPlugin",
            OwnPresets.Text(id).Split('\n'));
    }

    private sealed record Ini(Dictionary<string, string> Top, Dictionary<string, Dictionary<string, string>> Sections);

    private static Ini Parse(string text)
    {
        var top = new Dictionary<string, string>(StringComparer.Ordinal);
        var sections = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string>? current = null;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0 || line[0] == ';') continue;   // ReShade's ini reader skips ';' comment lines
            if (line[0] == '[')
            {
                current = new Dictionary<string, string>(StringComparer.Ordinal);
                sections[line.Trim('[', ']')] = current;
                continue;
            }
            var eq = line.IndexOf('=');
            Assert.True(eq > 0, "not a key=value line: " + line);
            (current ?? top)[line[..eq]] = line[(eq + 1)..];
        }
        return new Ini(top, sections);
    }

    private static Dictionary<(string, string), string> Build(string rows)
    {
        var d = new Dictionary<(string, string), string>();
        foreach (var row in rows.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var f = row.Split(' ');
            d[(f[1], f[2])] = f[0];
        }
        Assert.Equal(72, d.Count);
        return d;
    }
}
