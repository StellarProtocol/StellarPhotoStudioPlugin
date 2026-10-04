using Stellar.PhotoStudio.ReShade;
using System.Collections.Generic;
using Stellar.Abstractions.Domain;
using Stellar.Abstractions.Services;
using Xunit;

namespace Stellar.PhotoStudio.Tests;

public sealed class StudioSettingsTests
{
    private sealed class MemSection : IConfigSection
    {
        public readonly Dictionary<string, object?> Values = new();
        public int Saves;
        public T? Get<T>(string key, T? defaultValue) => Values.TryGetValue(key, out var v) && v is T t ? t : defaultValue;
        public void Set<T>(string key, T value) => Values[key] = value;
        public void Save() => Saves++;
        public void SaveQuiet() => Saves++;
        public void RemoveByPrefix(string prefix) { }
    }

    [Fact]
    public void Defaults_match_the_spec()
    {
        var s = new StudioSettings(new MemSection());
        Assert.Equal(2, s.Scale);
        Assert.Equal(CaptureFormat.Png, s.Format);
        Assert.Equal(92, s.JpgQuality);
        Assert.Equal("", s.Folder);
        Assert.True(s.DockedAuto);
        Assert.Equal(LookGroups.Color, s.OpenGroups);
    }

    // Lights spec (2026-10-03): the Lights tab is inserted at 3, so Presets moves to 4. A 1.1/1.2 user who left the panel on
    // Presets ("ui.tab2" = 3) must land on Presets, not Lights; the new value is stored under "ui.tab3".
    [Theory]
    [InlineData(0, StudioTabs.Capture)]
    [InlineData(2, StudioTabs.Camera)]
    [InlineData(3, StudioTabs.Presets)]
    public void A_v11_saved_tab_migrates_past_the_new_Lights_tab(int savedTab2, int expected)
    {
        var cfg = new MemSection();
        cfg.Values["ui.tab2"] = savedTab2;
        Assert.Equal(expected, new StudioSettings(cfg).Tab);
    }

    [Fact]
    public void The_Lights_tab_is_stored_under_the_new_key()
    {
        var cfg = new MemSection();
        new StudioSettings(cfg).SetTab(StudioTabs.Lights);
        Assert.Equal(StudioTabs.Lights, cfg.Values["ui.tab3"]);
        Assert.False(cfg.Values.ContainsKey("ui.tab2"));
        Assert.Equal(StudioTabs.Lights, new StudioSettings(cfg).Tab);
    }

    [Fact]
    public void Stellar_overlay_hide_is_never_persisted()
    {
        var cfg = new MemSection();
        new StudioSettings(cfg).SetHides(VisibilityLayers.StellarOverlay | VisibilityLayers.Nameplates);
        var again = new StudioSettings(cfg);
        Assert.Equal(VisibilityLayers.Nameplates, again.Hides);
    }

    [Fact]
    public void Invalid_scale_and_quality_are_normalized()
    {
        var cfg = new MemSection();
        cfg.Values["capture.scale"] = 3;
        cfg.Values["capture.jpgQuality"] = 400;
        var s = new StudioSettings(cfg);
        Assert.Equal(2, s.Scale);
        Assert.Equal(100, s.JpgQuality);
    }

    [Fact]
    public void Setters_round_trip_and_save()
    {
        var cfg = new MemSection();
        var s = new StudioSettings(cfg);
        s.SetScale(4); s.SetFormat(CaptureFormat.Jpg); s.SetFolder("  /pics  "); s.SetPinned(true);
        s.SetGroupOpen(LookGroups.Dof, true); s.SetGroupOpen(LookGroups.Color, false);
        var again = new StudioSettings(cfg);
        Assert.Equal(4, again.Scale);
        Assert.Equal(CaptureFormat.Jpg, again.Format);
        Assert.Equal("/pics", again.Folder);
        Assert.True(again.Pinned);
        Assert.Equal(LookGroups.Dof, again.OpenGroups);
        Assert.True(cfg.Saves >= 6);
    }

    [Fact]
    public void Render_quality_choices_round_trip_and_default_off_with_capture_boost_on()
    {
        var cfg = new MemSection();
        var s = new StudioSettings(cfg);
        Assert.Equal(QualityMode.Off, s.Supersample);
        Assert.True(s.BoostForCapture);
        Assert.Equal(12f, s.TimeHour);
        s.SetSupersample(QualityMode.WhileComposing); s.SetShadows(QualityMode.Always); s.SetTimeMode(QualityMode.Always);
        s.SetTimeHour(18.5f, save: true); s.SetBoostForCapture(false);
        var again = new StudioSettings(cfg);
        Assert.Equal(QualityMode.WhileComposing, again.Supersample);
        Assert.Equal(QualityMode.Always, again.Shadows);
        Assert.Equal(QualityMode.Always, again.TimeMode);
        Assert.Equal(18.5f, again.TimeHour);
        Assert.False(again.BoostForCapture);
    }

    [Fact]
    public void An_unsaved_hour_change_does_not_hit_the_config()
    {
        var cfg = new MemSection();
        var s = new StudioSettings(cfg);
        var saves = cfg.Saves;
        s.SetTimeHour(7f, save: false);
        Assert.Equal(saves, cfg.Saves);
        Assert.Equal(7f, s.TimeHour);
    }

    [Fact]
    public void Invalid_mode_values_fall_back_to_off()
    {
        var cfg = new MemSection();
        cfg.Values["quality.supersample"] = 9;
        Assert.Equal(QualityMode.Off, new StudioSettings(cfg).Supersample);
    }

    [Fact]
    public void ReShade_fold_states_default_and_persist()
    {
        var cfg = new MemSection();
        var s = new StudioSettings(cfg);
        Assert.True(s.ReShadeOpen);
        Assert.True(s.PacksOpen);
        Assert.False(s.AllFxOpen);
        Assert.True(s.PresetsOpen);
        s.SetAllFxOpen(true);
        s.SetPresetsOpen(false);
        s.SetPacksOpen(false);
        s.SetReShadeOpen(false);
        var again = new StudioSettings(cfg);
        Assert.True(again.AllFxOpen);
        Assert.False(again.PresetsOpen);
        Assert.False(again.PacksOpen);
        Assert.False(again.ReShadeOpen);
    }

    [Fact]
    public void Preset_groups_open_only_photo_studios_own_by_default_and_persist()
    {
        var cfg = new MemSection();
        var s = new StudioSettings(cfg);
        Assert.True(s.PresetGroupOpen(PresetKind.Own));
        Assert.False(s.PresetGroupOpen(PresetKind.Community));
        Assert.False(s.PresetGroupOpen(PresetKind.LinkOnly));
        s.SetPresetGroupOpen(PresetKind.Own, false);
        s.SetPresetGroupOpen(PresetKind.LinkOnly, true);
        var again = new StudioSettings(cfg);
        Assert.False(again.PresetGroupOpen(PresetKind.Own));
        Assert.False(again.PresetGroupOpen(PresetKind.Community));
        Assert.True(again.PresetGroupOpen(PresetKind.LinkOnly));
    }
}
