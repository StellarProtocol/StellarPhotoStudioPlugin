using Stellar.Abstractions.Domain;
using Stellar.PhotoStudio.ReShade;
using Xunit;

namespace Stellar.PhotoStudio.Tests.ReShade;

public sealed class ReShadeControlTests
{
    private const float Frame = 1f / 60f;
    private static readonly ReShadeTechnique Mxao = new("MXAO", "MXAO.fx", Enabled: false, UsesDepth: true);
    private readonly FakeReShade _fake = new();

    private ReShadeControl Make()
    {
        _fake.List.Add(Mxao);
        return new ReShadeControl(_fake, "/data/reshade/presets");
    }

    // R1 regression pin (review of the 2026-10-04 framework plan: "PS must await Changed after Look before capture"): a
    // depth technique switched on just before the shutter must be APPLIED before the capture starts, or the framework's
    // D8 skip list (read at capture time) misses it and depth lands in a shaped photo.
    [Fact]
    public void D8_a_depth_technique_switch_stays_pending_until_reshade_reports_it()
    {
        var c = Make();
        c.SetTechnique(Mxao, true);
        Assert.True(c.Pending);
        c.Tick(Frame);
        Assert.True(c.Pending);                                  // requested, not applied yet
        _fake.List[0] = Mxao with { Enabled = true };            // ReShade applied it at its present
        c.Tick(Frame);
        Assert.False(c.Pending);
        Assert.Equal(("MXAO.fx", "MXAO", true), _fake.TechniqueCalls[0]);
    }

    [Fact]
    public void Even_an_already_satisfied_request_waits_one_framework_tick()
    {
        var c = Make();
        c.SetTechnique(Mxao, false);                             // already off
        Assert.True(c.Pending);
        c.Tick(Frame);
        Assert.False(c.Pending);
    }

    [Fact]
    public void Loading_holds_the_gate_even_when_the_switch_reads_applied()
    {
        var c = Make();
        _fake.State = ReShadeState.Loading;
        c.SetEnabled(true);                                      // EnabledNow is already true
        c.Tick(Frame);
        Assert.True(c.Pending);
        _fake.State = ReShadeState.Ready;
        c.Tick(Frame);
        Assert.False(c.Pending);
    }

    [Fact]
    public void A_switch_that_never_lands_releases_after_three_seconds_and_says_so()
    {
        var c = Make();
        c.SetTechnique(Mxao, true);
        for (var i = 0; i < 25; i++) c.Tick(0.1f);               // 2.5 s
        Assert.True(c.Pending);
        c.Tick(0.6f);                                            // 3.1 s ≥ TimeoutSeconds
        Assert.False(c.Pending);
        Assert.True(c.TimedOut);
        Assert.False(c.IsOn(Mxao));                              // the wish is dropped with the timeout
    }

    // R1 cap pin (Task 3 review): the 3 s cap runs from the OLDEST pending request — a later request must not restart
    // the clock and push a stuck switch out to ~6 s.
    [Fact]
    public void A_later_request_does_not_restart_the_three_second_cap()
    {
        var c = Make();
        c.SetTechnique(Mxao, true);                              // A: never lands
        for (var i = 0; i < 29; i++) c.Tick(0.1f);               // ~2.9 s
        Assert.True(c.Pending);
        c.SetEnabled(true);                                      // B: already satisfied (EnabledNow is true)
        c.Tick(0.2f);                                            // ~3.1 s since A
        Assert.False(c.Pending);
        Assert.True(c.TimedOut);
    }

    [Fact]
    public void Two_requests_that_both_land_release_one_tick_after_the_later_one()
    {
        var c = Make();
        c.SetTechnique(Mxao, true);                              // A
        c.Tick(1f);
        Assert.True(c.Pending);
        c.SetEnabled(false);                                     // B
        _fake.List[0] = Mxao with { Enabled = true };            // both applied by ReShade
        _fake.EnabledNow = false;
        c.OnChanged();                                           // ReShade reports both (the settle reads Changed's value)
        Assert.True(c.Pending);                                  // still waits for a tick after B
        c.Tick(Frame);
        Assert.False(c.Pending);
        Assert.False(c.TimedOut);
    }

    [Fact]
    public void Preset_switch_matches_reshade_paths_with_other_separators_and_case()
    {
        var c = Make();
        c.SetPresetPath("/data/reshade/presets/Golden hour.ini");
        Assert.Equal("/data/reshade/presets/Golden hour.ini", _fake.PresetCalls[0]);
        Assert.Equal("Golden hour.ini", c.PresetFile);           // the wish shows before ReShade applies it
        _fake.CurrentPreset = @"Z:\data\reshade\presets\GOLDEN HOUR.ini";
        c.Tick(Frame);
        Assert.False(c.Pending);
    }

    [Fact]
    public void Not_installed_sends_nothing_and_never_holds_the_shutter()
    {
        _fake.State = ReShadeState.NotInstalled;
        var c = Make();
        c.SetTechnique(Mxao, true);
        c.SetEnabled(false);
        c.SetPresetPath("/data/reshade/presets/a.ini");
        Assert.False(c.Pending);
        Assert.Empty(_fake.TechniqueCalls);
        Assert.Empty(_fake.EnabledRequests);
        Assert.Empty(_fake.PresetCalls);
        Assert.Null(c.Current());
    }

    [Fact]
    public void Wishes_show_in_the_panel_until_applied()
    {
        var c = Make();
        c.SetTechnique(Mxao, true);
        Assert.True(c.IsOn(Mxao));
        c.SetEnabled(false);
        Assert.False(c.Enabled);
        Assert.Equal(new[] { false }, _fake.EnabledRequests);
    }

    [Fact]
    public void Current_and_apply_round_trip_a_look_preset_choice()
    {
        var c = Make();
        _fake.CurrentPreset = "/data/reshade/presets/Noir.ini";
        c.OnChanged();
        Assert.Equal(new ReShadeChoice("/data/reshade/presets/Noir.ini", true), c.Current());   // full path in memory
        c.Apply(new ReShadeChoice("Golden hour.ini", false));
        Assert.Equal("/data/reshade/presets/Golden hour.ini", _fake.PresetCalls[0]);
        Assert.Equal(new[] { false }, _fake.EnabledRequests);
        Assert.Equal(new ReShadeChoice("/data/reshade/presets/Golden hour.ini", false), c.Current());
    }

    [Fact]
    public void Apply_without_a_preset_only_switches_on_or_off()
    {
        var c = Make();
        c.Apply(new ReShadeChoice(null, false));
        Assert.Empty(_fake.PresetCalls);
        Assert.Equal(new[] { false }, _fake.EnabledRequests);
    }

    [Theory]
    [InlineData(@"C:\g\stellar\a.ini", "a.ini")]
    [InlineData("/g/stellar/b.ini", "b.ini")]
    [InlineData("c.ini", "c.ini")]
    public void File_name_splits_on_both_separators(string path, string name) => Assert.Equal(name, ReShadePaths.FileName(path));

    [Fact]
    public void Same_preset_accepts_a_differently_rooted_report_of_our_presets_folder_only()
    {
        Assert.True(ReShadePaths.Same(@".\stellar\plugindata\x.data\reshade\presets\A.ini", "/g/stellar/plugindata/x.data/reshade/presets/a.ini"));
        Assert.False(ReShadePaths.Same(@"C:\g\ReShadePreset.ini", "/g/reshade/presets/ReShadePreset.ini"));
        Assert.False(ReShadePaths.Same(null, "/g/reshade/presets/a.ini"));
    }
}
