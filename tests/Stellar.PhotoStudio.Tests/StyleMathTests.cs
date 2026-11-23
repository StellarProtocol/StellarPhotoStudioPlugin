using System.Linq;
using Stellar.Abstractions.Domain;
using Xunit;

namespace Stellar.PhotoStudio.Tests;

// The house-style kit's pure maths (Style\StyleMath.cs): colour compositing / contrast, the recessed label walk, icon-size
// picking + the embedded icon index, and the segmented-pill geometry.
public sealed class StyleMathTests
{
    private static ColorRgba Rgb(uint rgb) => new(((rgb >> 16) & 0xff) / 255f, ((rgb >> 8) & 0xff) / 255f, (rgb & 0xff) / 255f);

    [Fact]
    public void Over_composites_by_the_foreground_alpha()
    {
        var c = StyleMath.Over(new ColorRgba(1f, 1f, 1f, 0.25f), new ColorRgba(0f, 0f, 0f));
        Assert.Equal(0.25f, c.R, 4);
        Assert.Equal(1f, c.A);
    }

    [Fact]
    public void Contrast_of_white_on_black_is_21()
        => Assert.Equal(21f, StyleMath.Contrast(StyleMath.White, StyleMath.Black), 2);

    [Fact]
    public void EnsureContrast_lifts_a_dim_label_on_a_dark_fill()
    {
        var bg = Rgb(0x1c2126);
        var lifted = StyleMath.EnsureContrast(Rgb(0x3a4448), bg, 4.5f);
        Assert.True(StyleMath.Contrast(lifted, bg) >= 4.5f);
    }

    [Fact]
    public void EnsureContrast_darkens_toward_black_on_a_light_fill()
    {
        var bg = Rgb(0xf2f2f2);
        var fixedText = StyleMath.EnsureContrast(Rgb(0xffcc73), bg, 4.5f);   // amber label on a light theme
        Assert.True(fixedText.R < 1f && StyleMath.Contrast(fixedText, bg) >= 4.5f);
    }

    [Theory]
    [InlineData(0x5fb8c4u, false)]   // the slate-teal accent: white is under 3:1 → near-black label
    [InlineData(0xc8508fu, true)]    // the purple theme's accent keeps white (≈4.2:1, user-approved option B)
    [InlineData(0xd94446u, true)]    // the Danger red keeps white
    public void Solid_fill_label_is_white_or_near_black(uint fill, bool white)
        => Assert.Equal(white ? StyleMath.White : StyleMath.SolidDarkLabel, StyleMath.SolidLabel(Rgb(fill)));

    [Theory]
    [InlineData(0x7e8a8eu, 0x23292fu)]   // Default (dark) muted on the window background
    [InlineData(0x6b6b6bu, 0xf2f2f2u)]   // a Light theme
    public void Recessed_label_sits_between_1_3_and_2_2_to_1(uint muted, uint surface)
    {
        var sunken = StyleMath.Over(new ColorRgba(0f, 0f, 0f, 0.38f), Rgb(surface));
        float a = StyleMath.RecessedLabelAlpha(Rgb(muted), sunken, 0.45f, 1.3f, 2.2f);
        float ratio = StyleMath.Contrast(StyleMath.Over(Rgb(muted) with { A = a }, sunken), sunken);
        Assert.InRange(ratio, 1.3f, 2.2f);
    }

    [Theory]
    [InlineData(20f, 20)]    // 1440p, exact
    [InlineData(15f, 15)]    // 1080p (×0.75), exact
    [InlineData(22f, 24)]    // no exact drawing → the next LARGER one
    [InlineData(400f, 128)]  // beyond the largest → the largest
    [InlineData(19.97f, 20)] // ±0.05 counts as exact
    public void Icon_size_is_exact_else_next_larger(float want, int expected)
        => Assert.Equal(expected, StyleMath.PickIconSize(new[] { 12, 15, 16, 20, 24, 30, 32, 40, 64, 128 }, want));

    [Fact]
    public void No_drawings_picks_nothing() => Assert.Equal(-1, StyleMath.PickIconSize(System.Array.Empty<int>(), 20f));

    [Fact]
    public void Icon_index_groups_sizes_by_name_and_skips_other_resources()
    {
        const string p = "Stellar.PhotoStudio.icons.";
        var index = StyleMath.IndexIcons(new[]
        {
            p + "20.rail_capture.png", p + "15.rail_capture.png", p + "20.rail_capture.png",
            p + "24.header.png", p + "big.header.png", p + "24.png", "Stellar.PhotoStudio.photostudio-icon.png",
        }, p);
        Assert.Equal(new[] { 15, 20 }, index["rail_capture"]);
        Assert.Equal(new[] { 24 }, index["header"]);
        Assert.Equal(2, index.Count);
    }

    // The csproj glob must embed Resources\icons\<size>\<name>.png under the dotted name the loader indexes, on any OS.
    [Fact]
    public void Embedded_icons_use_the_dotted_logical_name()
    {
        const string p = "Stellar.PhotoStudio.icons.";
        var names = typeof(Plugin).Assembly.GetManifestResourceNames().Where(n => n.StartsWith(p)).ToArray();
        var index = StyleMath.IndexIcons(names, p);
        Assert.Equal(names.Length, index.Values.Sum(s => s.Length));
        Assert.All(names, n => Assert.DoesNotContain('\\', n));
        Assert.All(names, n => Assert.DoesNotContain('/', n));
    }

    [Fact]
    public void Segment_track_is_insets_plus_pills_plus_gaps()
    {
        // 3 options with 20 px label boxes: 2 + (38 + 2 + 38 + 2 + 38) + 2 = 122.
        Assert.Equal(38f, StyleMath.SegmentPillWidth(20f));
        Assert.Equal(122f, StyleMath.SegmentTrackWidth(new[] { 20f, 20f, 20f }));
        Assert.Equal(4f, StyleMath.SegmentTrackWidth(System.Array.Empty<float>()));
    }

    [Fact]
    public void Label_box_fits_the_longest_string_in_any_script()
    {
        // "Composing" = 9 Latin chars ≈ 67.5 + 10 spare → 78 (CJK 5 × 12.5 = 62.5 → 73); the widest wins.
        Assert.Equal(78f, StyleMath.FitBoldLabelWidth(new[] { "Off", "Composing", "構図中のみ" }));
        // Thai: the two above-vowels (U+0E31) stack on their consonant; the other 6 code points advance 8 each.
        Assert.Equal(48f, StyleMath.EstimateBold12("กำลังจัด"));
    }
}
