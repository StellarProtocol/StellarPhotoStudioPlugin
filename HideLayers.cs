using Stellar.Abstractions.Domain;

namespace Stellar.PhotoStudio;

/// <summary>
/// The Capture tab's hide toggles (1.7.0, player request "FOV Slider and Toggling Collectibles/Spirit Echo"): the game
/// photo screen's own list — Me, My own Spirit Echo, Other adventurers, Non-players, Enemy, Weapon, Friends, Party,
/// Guild, Collectible, Other Spirit Echo — one framework 2.20.0 layer each, plus the HUD / nameplate / effect layers.
/// 1.6.0 stored "Me" as <see cref="VisibilityLayers.Self"/>, "Other players" as <see cref="VisibilityLayers.OtherPlayers"/>
/// and "Keep my party visible" as <see cref="VisibilityLayers.KeepParty"/>; <see cref="Migrate"/> turns those into the
/// equivalent new toggles and <see cref="LegacyProjection"/> writes them back for a rollback. Pure — unit-tested.
/// </summary>
internal static class HideLayers
{
    /// <summary>The world toggles, in the game photo screen's order.</summary>
    public const VisibilityLayers World =
        VisibilityLayers.SelfCharacter | VisibilityLayers.OwnSpiritEcho | VisibilityLayerSets.PlayerGroups |
        VisibilityLayers.NonPlayers | VisibilityLayers.Enemies | VisibilityLayers.Weapons | VisibilityLayers.Collectibles |
        VisibilityLayers.OtherSpiritEchoes;

    /// <summary>What the Capture tab remembers (never the Stellar overlay, never a legacy bit).</summary>
    public const VisibilityLayers Persistable =
        VisibilityLayers.GameHud | VisibilityLayers.Nameplates | World | VisibilityLayerSets.Effects;

    private const VisibilityLayers PassThrough =
        VisibilityLayers.GameHud | VisibilityLayers.Nameplates | VisibilityLayerSets.Effects | World;

    private const VisibilityLayers KeepPartyGroups =
        VisibilityLayers.Strangers | VisibilityLayers.Friends | VisibilityLayers.Guild;

    /// <summary>1.6.0 toggles → 1.7.0 toggles: Me → Me + My own Spirit Echo (1.6.0's Me hid both); Other players →
    /// all four player groups; Other players + Keep my party visible → Other adventurers + Friends + Guild (exactly the
    /// switches 1.6.0 used). A lone KeepParty meant nothing and is dropped.</summary>
    public static VisibilityLayers Migrate(VisibilityLayers legacy)
    {
        var next = legacy & PassThrough;
        if ((legacy & VisibilityLayers.Self) != 0) next |= VisibilityLayers.SelfCharacter | VisibilityLayers.OwnSpiritEcho;
        if ((legacy & VisibilityLayers.OtherPlayers) != 0)
            next |= (legacy & VisibilityLayers.KeepParty) != 0 ? KeepPartyGroups : VisibilityLayerSets.PlayerGroups;
        return next;
    }

    /// <summary>The 1.6.0 reading of the toggles, stored under the old key so a rolled-back build keeps what it can
    /// express: Me (only together with My own Spirit Echo — 1.6.0's Me hid both), Other players (when Other
    /// adventurers, Friends and Guild are all on) with Keep my party visible when Party is off.</summary>
    public static VisibilityLayers LegacyProjection(VisibilityLayers hides)
    {
        var legacy = hides & (VisibilityLayers.GameHud | VisibilityLayers.Nameplates | VisibilityLayerSets.Effects);
        if ((hides & (VisibilityLayers.SelfCharacter | VisibilityLayers.OwnSpiritEcho)) ==
            (VisibilityLayers.SelfCharacter | VisibilityLayers.OwnSpiritEcho))
            legacy |= VisibilityLayers.Self;
        if ((hides & KeepPartyGroups) == KeepPartyGroups)
            legacy |= VisibilityLayers.OtherPlayers | ((hides & VisibilityLayers.Party) == 0 ? VisibilityLayers.KeepParty : 0);
        return legacy;
    }

    /// <summary>What to ask the framework for. All four player groups on = "no other player at all": the game's master
    /// switch (<see cref="VisibilityLayers.OtherPlayers"/>), which also hides a player who is in none of the groups —
    /// exactly what 1.6.0's "Other players" did. Fewer groups go through as they are: the game shows a player while any
    /// group they belong to is shown.</summary>
    public static VisibilityLayers ToRequest(VisibilityLayers hides) =>
        (hides & VisibilityLayerSets.PlayerGroups) == VisibilityLayerSets.PlayerGroups
            ? (hides & ~VisibilityLayerSets.PlayerGroups) | VisibilityLayers.OtherPlayers
            : hides;

    /// <summary>The layers a camera render needs hidden (it never contains UI or nameplates).</summary>
    public static VisibilityLayers ForCapture(VisibilityLayers hides) =>
        ToRequest(hides) & (VisibilityLayerSets.World | VisibilityLayerSets.Effects);
}
