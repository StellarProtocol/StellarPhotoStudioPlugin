using System;
using System.Security.Cryptography;
using System.Text;
using Stellar.PhotoStudio.ReShade;
using Xunit;

namespace Stellar.PhotoStudio.Tests.ReShade;

// Plan D-OVR: the sha256-checked download is never changed; the copy ReShade loads leaves out definitions made for
// another game (every RESHADE_DEPTH_* one, plus the entry's DropDefinitions). Every other byte stays identical.
public sealed class PresetOverridesTests
{
    // Line 1 of the pinned Stella Medium preset (plan § The catalog), verbatim.
    private const string StellaLine =
        "PreprocessorDefinitions=RESHADE_DEPTH_INPUT_IS_REVERSED=1,RESHADE_DEPTH_INPUT_IS_UPSIDE_DOWN=1,INFINITE_BOUNCES=1,SKYCOLOR_MODE=2,MATERIAL_TYPE=1,FADEOUT_MODE=0,IMAGEBASEDLIGHTING=1,BLOOM_ENABLE_CA=0,BLOOM_QUALITY_0_TO_2=2,BLOOM_USE_FOCUS_BLOOM=0,ENABLE_MISC_CONTROLS=1";

    [Fact]
    public void Drops_depth_definitions_set_for_another_game_and_keeps_the_rest_in_order()
    {
        var text = StellaLine + "\nTechniques=prod80_02_Bloom@PD80_02_Bloom.fx\n";
        Assert.Equal(
            "PreprocessorDefinitions=INFINITE_BOUNCES=1,SKYCOLOR_MODE=2,MATERIAL_TYPE=1,FADEOUT_MODE=0,IMAGEBASEDLIGHTING=1,BLOOM_ENABLE_CA=0,BLOOM_QUALITY_0_TO_2=2,BLOOM_USE_FOCUS_BLOOM=0,ENABLE_MISC_CONTROLS=1\nTechniques=prod80_02_Bloom@PD80_02_Bloom.fx\n",
            PresetOverrides.Apply(text, Array.Empty<string>()));
    }

    // Review fix round 2, item 5: line 2 of the real, pinned StarLuxe Luminescence download (sha256
    // 5e1b23f882fede42f1cf7d9cd0076574daae354710eca19e143cec00e4571d63, re-fetched from the author's repo at the pinned
    // commit and confirmed verbatim), not a hand-written stand-in. It carries BOTH kinds of drop at once: two
    // RESHADE_DEPTH_* definitions AND the entry's own DropDefinitions name (fLUT_TextureName="DarkNRich.png" — no
    // pinned pack ships that texture). The expected output was independently re-derived with the plan's own Python
    // reference script (PresetOverridesTests.cs step 5 of the brief), not just eyeballed against the input.
    private const string LuminescenceLine =
        "PreprocessorDefinitions=RESHADE_DEPTH_INPUT_IS_UPSIDE_DOWN=1,RESHADE_DEPTH_INPUT_IS_REVERSED=1,INFINITE_BOUNCES=1,MATERIAL_TYPE=1,SKYCOLOR_MODE=2,WINDOW_SIZE=15,SECOND_PASS=0,MXAO_MIPLEVEL_IL=0,MXAO_TWO_LAYER=1,MXAO_SMOOTHNORMALS=1,IMAGEBASEDLIGHTING=1,fLUT_TextureName=\"DarkNRich.png\",ENABLE_MISC_CONTROLS=1,RT_ENABLE_HIGH_PERF_MODE=1";

    [Fact]
    public void Drops_the_real_Luminescence_depth_and_missing_texture_definitions_together()
    {
        var text = LuminescenceLine + "\nTechniques=LumaSharpen@LumaSharpen.fx\n";
        Assert.Equal(
            "PreprocessorDefinitions=INFINITE_BOUNCES=1,MATERIAL_TYPE=1,SKYCOLOR_MODE=2,WINDOW_SIZE=15,SECOND_PASS=0,MXAO_MIPLEVEL_IL=0,MXAO_TWO_LAYER=1,MXAO_SMOOTHNORMALS=1,IMAGEBASEDLIGHTING=1,ENABLE_MISC_CONTROLS=1,RT_ENABLE_HIGH_PERF_MODE=1\nTechniques=LumaSharpen@LumaSharpen.fx\n",
            PresetOverrides.Apply(text, new[] { "fLUT_TextureName" }));
    }

    [Fact]
    public void Drops_a_listed_texture_definition_by_exact_name()
    {
        var text = "PreprocessorDefinitions=RESHADE_DEPTH_INPUT_IS_UPSIDE_DOWN=1,MXAO_TWO_LAYER=1,fLUT_TextureName=\"DarkNRich.png\",fLUT_TileAmount=64\n";
        Assert.Equal("PreprocessorDefinitions=MXAO_TWO_LAYER=1,fLUT_TileAmount=64\n",
            PresetOverrides.Apply(text, new[] { "fLUT_TextureName" }));
    }

    [Fact]
    public void Drops_depth_definitions_in_an_effect_section_too()
    {
        var text = "Techniques=X@X.fx\n\n[X.fx]\nPreprocessorDefinitions=RESHADE_DEPTH_LINEARIZATION_FAR_PLANE=1000,KEEP=2\nValue=1.000000\n";
        Assert.Equal("Techniques=X@X.fx\n\n[X.fx]\nPreprocessorDefinitions=KEEP=2\nValue=1.000000\n",
            PresetOverrides.Apply(text, Array.Empty<string>()));
    }

    [Fact]
    public void Removes_the_whole_line_when_nothing_is_left()
    {
        Assert.Equal("Techniques=X@X.fx\n",
            PresetOverrides.Apply("PreprocessorDefinitions=RESHADE_DEPTH_INPUT_IS_REVERSED=0\nTechniques=X@X.fx\n", Array.Empty<string>()));
    }

    [Fact]
    public void Keeps_crlf_and_every_untouched_byte()
    {
        var text = "KeyX=33,0,0,0\r\nPreprocessorDefinitions=A=1,RESHADE_DEPTH_INPUT_IS_MIRRORED=1\r\n[Y.fx]\r\nZ=0.5\r\n";
        Assert.Equal("KeyX=33,0,0,0\r\nPreprocessorDefinitions=A=1\r\n[Y.fx]\r\nZ=0.5\r\n", PresetOverrides.Apply(text, Array.Empty<string>()));
    }

    [Fact]
    public void Text_without_game_specific_definitions_comes_back_as_the_same_instance_so_its_checksum_is_unchanged()
    {
        // e.g. StarLuxe Legacy's line 2 (`UNSHARP_BLUR_SAMPLES=20`) and Okami's (`fLUT_TextureName="lut.png"` — a texture we ship).
        var text = "KeyMartyMcFlyDOF@DOF.fx=109,0,0,0\nPreprocessorDefinitions=UNSHARP_BLUR_SAMPLES=20\nPreprocessorDefinitions=\nTechniques=Curves@Curves.fx";
        var result = PresetOverrides.Apply(text, Array.Empty<string>());
        Assert.Same(text, result);
        Assert.Equal(Sha(text), Sha(result));
    }

    [Fact]
    public void Only_a_name_that_starts_with_the_prefix_is_dropped()
    {
        var text = "PreprocessorDefinitions=MY_RESHADE_DEPTH_X=1, RESHADE_DEPTH_INPUT_Y_SCALE=1\n";
        Assert.Equal("PreprocessorDefinitions=MY_RESHADE_DEPTH_X=1\n", PresetOverrides.Apply(text, Array.Empty<string>()));
    }

    [Fact]
    public void A_line_that_only_mentions_the_key_elsewhere_is_left_alone()
    {
        var text = "Description=PreprocessorDefinitions=RESHADE_DEPTH_INPUT_IS_REVERSED=1\n";
        Assert.Same(text, PresetOverrides.Apply(text, Array.Empty<string>()));
    }

    private static string Sha(string s) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)));
}
