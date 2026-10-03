using System;
using System.Collections.Generic;
using System.Linq;
using Stellar.Abstractions.Domain;
using Stellar.PhotoStudio.ReShade;
using Xunit;

namespace Stellar.PhotoStudio.Tests.ReShade;

public sealed class ReShadeViewTests
{
    private static readonly ReShadeTechnique Lift = new("Lift", "Lift.fx", true, false);
    private static readonly ReShadeTechnique Dof = new("DOF", "PD80_06_Depth_Slicer.fx", true, true);
    private static readonly ReShadeTechnique DofOff = Dof with { Enabled = false };
    private static bool Live(ReShadeTechnique t) => t.Enabled;

    // R3: three states from IReShade.State; NotInstalled splits on whether a dxgi.dll sits in the game folder (the
    // player's own ReShade, or ours without the bridge) — both point to Photo Studio's launcher page.
    // NOTE (deviation from the brief, see task-4 report): the brief's InlineData used ReShadePanel/PhotoShape directly
    // as Theory parameter types. Both are `internal` (declared in the plugin assembly); a `public` xunit test method
    // exposing an internal parameter type is CS0051 even with InternalsVisibleTo (IVT is not transitive to a third
    // assembly referencing the test assembly, so the compiler correctly refuses). Indirected through a string, the same
    // way the existing PhotoShapeTests.cs already works around this for PhotoShape (PhotoShapes.Key/Parse).
    [Theory]
    [InlineData(ReShadeState.Ready, false, nameof(ReShadePanel.Ready))]
    [InlineData(ReShadeState.Loading, false, nameof(ReShadePanel.Loading))]
    [InlineData(ReShadeState.NotInstalled, false, nameof(ReShadePanel.NotInstalled))]
    [InlineData(ReShadeState.NotInstalled, true, nameof(ReShadePanel.Unreachable))]
    public void Panel_state_follows_reshade_state(ReShadeState s, bool dxgi, string expected) =>
        Assert.Equal(expected, ReShadeView.Panel(s, dxgi).ToString());

    // R4 pin (D8 as amended in spec § 11): the depth note is keyed on the photo SHAPE, never on the scale.
    [Theory]
    [InlineData("9:16", 1)]
    [InlineData("1:1", 4)]
    [InlineData("21:9", 2)]
    public void Depth_note_shows_for_every_non_screen_shape_at_any_scale(string shapeKey, int scale) =>
        Assert.Equal(DepthNote.SkippedInShape, ReShadeView.Depth(PhotoShapes.Parse(shapeKey), scale, new[] { Lift, Dof }, Live));

    [Fact]
    public void Screen_shape_never_shows_the_skipped_note_only_the_detail_hint_above_1x()
    {
        Assert.Equal(DepthNote.None, ReShadeView.Depth(PhotoShape.Screen, 1, new[] { Dof }, Live));
        Assert.Equal(DepthNote.ScreenDetail, ReShadeView.Depth(PhotoShape.Screen, 2, new[] { Dof }, Live));
        Assert.Equal(DepthNote.ScreenDetail, ReShadeView.Depth(PhotoShape.Screen, 4, new[] { Dof }, Live));
    }

    [Fact]
    public void No_note_without_an_enabled_depth_effect_but_a_wished_one_counts()
    {
        Assert.Equal(DepthNote.None, ReShadeView.Depth(PhotoShape.Square, 1, new[] { Lift, DofOff }, Live));
        Assert.Equal(DepthNote.SkippedInShape, ReShadeView.Depth(PhotoShape.Square, 1, new[] { Lift, DofOff }, t => t.Name == "DOF" || t.Enabled));
    }

    [Fact]
    public void Rows_put_enabled_first_by_name_and_label_repeated_names_with_their_effect()
    {
        var a = new ReShadeTechnique("Sharpen", "CAS.fx", false, false);
        var b = new ReShadeTechnique("Sharpen", "PD80_05_Sharpening.fx", true, false);
        var c = new ReShadeTechnique("Bloom", "PD80_02_Bloom.fx", false, false);
        var rows = ReShadeView.Rows(new[] { a, c, b, Lift });
        Assert.Equal(new[] { "Lift", "Sharpen (PD80_05_Sharpening)", "Bloom", "Sharpen (CAS)" }, rows.Select(r => r.Label).ToArray());
        Assert.Equal(2, ReShadeView.EnabledCount(rows));
    }

    [Fact]
    public void Preset_options_list_our_folder_and_select_the_current_one()
    {
        var o = ReShadePresets.Options("/d/reshade/presets", new[] { "Golden hour.ini", "Noir.ini" },
            @"Z:\d\reshade\presets\Noir.ini", n => n + " (ReShade's own)");
        Assert.Equal(new[] { "Golden hour", "Noir" }, o.Labels);
        Assert.Equal(1, o.Selected);
        Assert.Equal("/d/reshade/presets/Golden hour.ini", o.Paths[0]);
    }

    [Fact]
    public void Preset_options_show_reshade_s_own_preset_first_and_offer_a_default_when_the_folder_is_empty()
    {
        var o = ReShadePresets.Options("/d/reshade/presets", Array.Empty<string>(), @"C:\g\ReShadePreset.ini", n => n + " (ReShade's own)");
        Assert.Equal(new[] { "ReShadePreset (ReShade's own)", "Photo Studio" }, o.Labels);
        Assert.Equal(@"C:\g\ReShadePreset.ini", o.Paths[0]);
        Assert.Equal("/d/reshade/presets/Photo Studio.ini", o.Paths[1]);
        Assert.Equal(0, o.Selected);
    }

    [Fact]
    public void Preset_list_keeps_ini_files_sorted_case_insensitively()
    {
        Assert.Equal(new[] { "a.ini", "B.INI", "c.ini" }, ReShadePresets.List(new[] { "/x/c.ini", @"C:\x\B.INI", "/x/a.ini", "/x/readme.txt" }));
    }

    [Theory]
    [InlineData("6.8.0.2155", "6.8.0.2155")]
    [InlineData("6, 8, 0, 2155", "6.8.0.2155")]
    [InlineData("  ", null)]
    [InlineData(null, null)]
    public void Version_is_normalized(string? raw, string? expected) => Assert.Equal(expected, ReShadeInfo.NormalizeVersion(raw));

    [Fact]
    public void Missing_dll_has_no_version() => Assert.Null(ReShadeInfo.ReadVersion("/nonexistent/dxgi.dll"));

    // R2: the framework's English "not ready" note is shown in the player's language; unknown notes pass through.
    [Fact]
    public void Known_note_is_localized_and_notes_join_the_toast_warning()
    {
        string T(string k) => k == "ps.rs.note.notReady" ? "LOCALIZED" : k;
        Assert.Equal("LOCALIZED", ReShadeInfo.LocalizeNote(ReShadeInfo.NotReadyNote, T));
        Assert.Equal("other", ReShadeInfo.LocalizeNote("other", T));
        Assert.Equal("Saved at 2× · LOCALIZED", ReShadeInfo.JoinWarnings("Saved at 2×", new[] { ReShadeInfo.NotReadyNote }, T));
        Assert.Equal("LOCALIZED", ReShadeInfo.JoinWarnings("", new[] { ReShadeInfo.NotReadyNote }, T));
        Assert.Equal("w", ReShadeInfo.JoinWarnings("w", Array.Empty<string>(), T));
    }
}
