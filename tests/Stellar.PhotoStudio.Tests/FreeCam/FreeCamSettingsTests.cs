using Stellar.Abstractions.Domain;
using Stellar.PhotoStudio.FreeCam;
using Xunit;

namespace Stellar.PhotoStudio.Tests.FreeCam;

// Spec § 3 settings (mockup defaults: 4.5 m/s, sensitivity 1.00, smoothing 0.30, leash 30 m in 5–50 m) + favourites.
public sealed class FreeCamSettingsTests
{
    [Fact]
    public void Defaults_match_the_spec_and_mockup()
    {
        var s = new FreeCamSettings(new MemConfigSection());
        Assert.Equal(4.5f, s.MoveSpeed);
        Assert.Equal(1f, s.Sensitivity);
        Assert.Equal(0.3f, s.Smoothing);
        Assert.Equal(30f, s.Leash);
        Assert.False(s.InvertY);
        Assert.Equal(VisibilityLayers.GameHud | VisibilityLayers.Nameplates, s.EntryHides);
        Assert.False(s.LookAt);
        Assert.Empty(s.Favourites);
        Assert.Equal(new RigTuning(4.5f, 1f, 0.3f, false, 30f), s.Tuning);
    }

    [Fact]
    public void Leash_is_clamped_to_5_50_and_speed_to_its_range()
    {
        var cfg = new MemConfigSection();
        cfg.Values["freecam.leash"] = 80f;
        cfg.Values["freecam.moveSpeed"] = -1f;
        var s = new FreeCamSettings(cfg);
        Assert.Equal(50f, s.Leash);
        Assert.Equal(FreeCamSettings.MinSpeed, s.MoveSpeed);
        s.SetLeash(2f, save: true);
        Assert.Equal(5f, s.Leash);
    }

    [Fact]
    public void Slider_drags_stay_in_memory_until_SaveSliders()
    {
        var cfg = new MemConfigSection();
        var s = new FreeCamSettings(cfg);
        var saves = cfg.Saves;
        s.SetMoveSpeed(9f, save: false);
        Assert.Equal(saves, cfg.Saves);
        s.SaveSliders();
        Assert.Equal(9f, new FreeCamSettings(cfg).MoveSpeed);
    }

    [Fact]
    public void Favourites_toggle_and_survive_a_reload()
    {
        var cfg = new MemConfigSection();
        var s = new FreeCamSettings(cfg);
        s.ToggleFavourite(9011);
        s.ToggleFavourite(9001);
        s.ToggleFavourite(9011);
        Assert.Equal(new[] { 9001 }, new FreeCamSettings(cfg).Favourites);
        Assert.True(new FreeCamSettings(cfg).IsFavourite(9001));
    }

    [Fact]
    public void Corrupt_favourites_are_skipped()
    {
        var cfg = new MemConfigSection();
        cfg.Values["freecam.favourites"] = "12,x,,40";
        Assert.Equal(new[] { 12, 40 }, new FreeCamSettings(cfg).Favourites);
    }

    [Fact]
    public void Toggles_round_trip()
    {
        var cfg = new MemConfigSection();
        var s = new FreeCamSettings(cfg);
        s.SetInvertY(true); s.SetEntryHide(VisibilityLayers.GameHud, false); s.SetLookAt(true); s.SetHintHidden(true); s.SetPoseOpen(false);
        var again = new FreeCamSettings(cfg);
        Assert.True(again.InvertY);
        Assert.Equal(VisibilityLayers.Nameplates, again.EntryHides);
        Assert.True(again.LookAt);
        Assert.True(again.HintHidden);
        Assert.False(again.PoseOpen);
    }

    // Review P8 (spec §§ 3/5): entry applies a configurable set of hide layers, persisted as flags.

    [Fact]
    public void Entry_hide_layers_round_trip_each_layer()
    {
        var cfg = new MemConfigSection();
        var s = new FreeCamSettings(cfg);
        s.SetEntryHide(VisibilityLayers.OtherPlayers, true);
        s.SetEntryHide(VisibilityLayers.GameHud, false);
        var again = new FreeCamSettings(cfg);
        Assert.Equal(VisibilityLayers.Nameplates | VisibilityLayers.OtherPlayers, again.EntryHides);
        Assert.True(again.EntryHidesLayer(VisibilityLayers.OtherPlayers));
        Assert.False(again.EntryHidesLayer(VisibilityLayers.GameHud));
        again.SetEntryHide(VisibilityLayers.Nameplates, false);
        again.SetEntryHide(VisibilityLayers.OtherPlayers, false);
        Assert.Equal(VisibilityLayers.None, new FreeCamSettings(cfg).EntryHides);   // "none" persists, not the default
    }

    [Fact]
    public void Entry_hide_layers_never_hold_a_layer_outside_the_three_choices()
    {
        var cfg = new MemConfigSection();
        cfg.Values["freecam.entryHideLayers"] = (int)(VisibilityLayers.StellarOverlay | VisibilityLayers.KeepParty | VisibilityLayers.GameHud);
        var s = new FreeCamSettings(cfg);
        Assert.Equal(VisibilityLayers.GameHud, s.EntryHides);
        s.SetEntryHide(VisibilityLayers.StellarOverlay, true);
        Assert.Equal(VisibilityLayers.GameHud, s.EntryHides);
    }

    [Theory]
    [InlineData(false, VisibilityLayers.None)]
    [InlineData(true, VisibilityLayers.GameHud | VisibilityLayers.Nameplates)]
    public void Old_entry_hides_bool_migrates_without_touching_the_old_key(bool old, VisibilityLayers expected)
    {
        var cfg = new MemConfigSection();
        cfg.Values["freecam.entryHides"] = old;
        var s = new FreeCamSettings(cfg);
        Assert.Equal(expected, s.EntryHides);
        s.SetEntryHide(VisibilityLayers.OtherPlayers, true);
        Assert.Equal(old, cfg.Values["freecam.entryHides"]);                       // rollback-safe (rules § 6)
        Assert.Equal(expected | VisibilityLayers.OtherPlayers, new FreeCamSettings(cfg).EntryHides);   // the flags key wins
    }

    [Fact]
    public void Old_presets_tab_migrates_to_the_new_index_without_touching_the_old_key()   // rollback-safe (rules § 6)
    {
        var cfg = new MemConfigSection();
        cfg.Values["ui.tab"] = 2;                       // 1.0.0: 2 = Presets
        var s = new StudioSettings(cfg);
        Assert.Equal(StudioTabs.Presets, s.Tab);
        s.SetTab(StudioTabs.Camera);
        Assert.Equal(2, cfg.Values["ui.tab"]);          // a 1.0.0 rollback still reads its own key
        Assert.Equal(StudioTabs.Camera, new StudioSettings(cfg).Tab);
    }

    [Fact]
    public void Old_look_tab_keeps_its_index()
    {
        var cfg = new MemConfigSection();
        cfg.Values["ui.tab"] = 1;
        Assert.Equal(StudioTabs.Look, new StudioSettings(cfg).Tab);
    }

    [Fact]
    public void Entry_hides_accept_self_and_the_effects_group()
    {
        var s = new FreeCamSettings(new MemConfigSection());
        s.SetEntryHide(VisibilityLayers.Self, true);
        s.SetEntryHide(VisibilityLayerSets.Effects, true);
        Assert.True(s.EntryHidesLayer(VisibilityLayers.Self));
        Assert.True(s.EntryHidesLayer(VisibilityLayerSets.Effects));
        s.SetEntryHide(VisibilityLayerSets.Effects, false);
        Assert.False(s.EntryHidesLayer(VisibilityLayers.EffectsMine));
    }
}
