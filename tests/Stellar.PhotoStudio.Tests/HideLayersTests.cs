using System.Collections.Generic;
using Stellar.Abstractions.Domain;
using Stellar.Abstractions.Services;
using Xunit;

namespace Stellar.PhotoStudio.Tests;

// 1.7.0 (player request "Photo Studio - FOV Slider and Toggling Collectibles/Spirit Echo"): the Capture tab mirrors the
// game photo screen's hide list. Pins the 1.6.0 → 1.7.0 settings migration (owner-tested 1.6.0 meanings: Me = character +
// pet + Battle Imagine; Other players = everyone; Keep my party visible = Stranger + Chum + Union), the rollback copy under
// the old key (process rules § 6: an old build must never lose user data a new build wrote), and the "all four player
// groups = the game's master switch" request.
public sealed class HideLayersTests
{
    private sealed class MemSection : IConfigSection
    {
        public readonly Dictionary<string, object?> Values = new();
        public T? Get<T>(string key, T? defaultValue) => Values.TryGetValue(key, out var v) && v is T t ? t : defaultValue;
        public void Set<T>(string key, T value) => Values[key] = value;
        public void Save() { }
        public void SaveQuiet() { }
        public void RemoveByPrefix(string prefix) { }
    }

    private const VisibilityLayers Me = VisibilityLayers.SelfCharacter | VisibilityLayers.OwnSpiritEcho;
    private const VisibilityLayers KeepPartyGroups = VisibilityLayers.Strangers | VisibilityLayers.Friends | VisibilityLayers.Guild;

    [Fact]
    public void Old_me_becomes_me_and_my_own_spirit_echo() =>
        Assert.Equal(Me, HideLayers.Migrate(VisibilityLayers.Self));

    [Fact]
    public void Old_other_players_becomes_all_four_player_groups() =>
        Assert.Equal(VisibilityLayerSets.PlayerGroups, HideLayers.Migrate(VisibilityLayers.OtherPlayers));

    [Fact]
    public void Old_keep_party_becomes_other_adventurers_friends_and_guild_but_not_party() =>
        Assert.Equal(KeepPartyGroups, HideLayers.Migrate(VisibilityLayers.OtherPlayers | VisibilityLayers.KeepParty));

    [Fact]
    public void A_lone_keep_party_meant_nothing_and_hud_names_effects_carry_over()
    {
        var keep = VisibilityLayers.GameHud | VisibilityLayers.Nameplates | VisibilityLayerSets.Effects;
        Assert.Equal(keep, HideLayers.Migrate(keep | VisibilityLayers.KeepParty));
    }

    [Fact]
    public void Migrated_toggles_ask_the_framework_for_exactly_what_1_6_0_asked_for()
    {
        // Other players → all four groups → the master switch again (no player in no group is left showing).
        Assert.Equal(VisibilityLayers.OtherPlayers, HideLayers.ToRequest(HideLayers.Migrate(VisibilityLayers.OtherPlayers)));
        // Keep my party → Strangers + Friends + Guild = the same camera types 1.6.0 hid (6, 2, 4).
        Assert.Equal(KeepPartyGroups, HideLayers.ToRequest(HideLayers.Migrate(VisibilityLayers.OtherPlayers | VisibilityLayers.KeepParty)));
    }

    [Fact]
    public void Fewer_than_four_player_groups_go_through_as_they_are()
    {
        var some = VisibilityLayers.Party | VisibilityLayers.Guild | VisibilityLayers.Collectibles;
        Assert.Equal(some, HideLayers.ToRequest(some));
        var all = VisibilityLayerSets.PlayerGroups | VisibilityLayers.Weapons;
        Assert.Equal(VisibilityLayers.OtherPlayers | VisibilityLayers.Weapons, HideLayers.ToRequest(all));
    }

    [Fact]
    public void Capture_hides_only_world_and_effect_layers()
    {
        var hides = VisibilityLayers.GameHud | VisibilityLayers.Nameplates | VisibilityLayers.Collectibles | VisibilityLayers.EffectsMine;
        Assert.Equal(VisibilityLayers.Collectibles | VisibilityLayers.EffectsMine, HideLayers.ForCapture(hides));
    }

    [Theory]
    [InlineData(VisibilityLayers.Self)]
    [InlineData(VisibilityLayers.OtherPlayers)]
    [InlineData(VisibilityLayers.OtherPlayers | VisibilityLayers.KeepParty)]
    [InlineData(VisibilityLayers.Self | VisibilityLayers.OtherPlayers | VisibilityLayers.GameHud | VisibilityLayers.EffectsMonsters)]
    public void The_legacy_projection_of_a_migration_is_the_original(VisibilityLayers legacy) =>
        Assert.Equal(legacy, HideLayers.LegacyProjection(HideLayers.Migrate(legacy)));

    [Fact]
    public void A_1_6_0_config_loads_migrated_and_saves_both_keys()
    {
        var cfg = new MemSection();
        cfg.Values["hide.layers"] = (int)(VisibilityLayers.Self | VisibilityLayers.OtherPlayers | VisibilityLayers.KeepParty);
        var s = new StudioSettings(cfg);
        Assert.Equal(Me | KeepPartyGroups, s.Hides);
        s.SetHides(s.Hides | VisibilityLayers.Collectibles);
        Assert.Equal((int)(Me | KeepPartyGroups | VisibilityLayers.Collectibles), cfg.Values["hide.layers2"]);
        // A rolled-back 1.6.0 reads the old key: Me, Other players, Keep my party visible — all still there.
        Assert.Equal((int)(VisibilityLayers.Self | VisibilityLayers.OtherPlayers | VisibilityLayers.KeepParty), cfg.Values["hide.layers"]);
        Assert.Equal(Me | KeepPartyGroups | VisibilityLayers.Collectibles, new StudioSettings(cfg).Hides);
    }

    [Fact]
    public void Toggles_only_1_7_0_can_express_round_trip_through_the_new_key()
    {
        var cfg = new MemSection();
        new StudioSettings(cfg).SetHides(VisibilityLayers.OwnSpiritEcho | VisibilityLayers.Party | VisibilityLayers.Weapons);
        Assert.Equal(0, cfg.Values["hide.layers"]);   // nothing 1.6.0 could show for these
        Assert.Equal(VisibilityLayers.OwnSpiritEcho | VisibilityLayers.Party | VisibilityLayers.Weapons, new StudioSettings(cfg).Hides);
    }

    [Fact]
    public void Changes_made_by_a_rolled_back_build_win_over_the_stale_new_key()
    {
        var cfg = new MemSection();
        new StudioSettings(cfg).SetHides(Me | VisibilityLayers.Weapons);
        cfg.Values["hide.layers"] = (int)VisibilityLayers.OtherPlayers;   // 1.6.0 turned Me off and Other players on
        Assert.Equal(VisibilityLayerSets.PlayerGroups, new StudioSettings(cfg).Hides);
    }

    [Fact]
    public void Legacy_bits_are_never_persisted_under_the_new_key()
    {
        var cfg = new MemSection();
        new StudioSettings(cfg).SetHides(VisibilityLayers.Self | VisibilityLayers.OtherPlayers | VisibilityLayers.KeepParty | VisibilityLayers.Guild);
        Assert.Equal((int)VisibilityLayers.Guild, cfg.Values["hide.layers2"]);
    }

    [Fact]
    public void Key_labels_show_the_bracket_characters() =>
        Assert.Equal(("]", "Shift+[", "\\"), (
            Plugin.KeyLabel(new KeyBinding(StellarKeyCode.RightBracket)),
            Plugin.KeyLabel(new KeyBinding(StellarKeyCode.LeftBracket, ModifierKeys.Shift)),
            Plugin.KeyLabel(new KeyBinding(StellarKeyCode.Backslash))));
}
