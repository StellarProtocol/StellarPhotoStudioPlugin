using System;
using System.Text;
using Stellar.Abstractions.Domain;
using Stellar.Abstractions.Services;

namespace Stellar.PhotoStudio;

// Hide section (NEW in 2.0.0, approved prototype "Hide"): HIDE NOW (count + the hide-all key + Show all), then two-column
// PEOPLE / WORLD / EFFECTS panels of 72 px Shown / Hidden chips — the same layers as the 1.x Capture-tab hide toggles
// (HideLayers, framework 2.20.0) — and WHEN THE FREE CAMERA STARTS, the on-entry hides moved out of Camera › Movement
// (FreeCamSettings.EntryHides, effects included). A layer this client can't hide shows its chip RECESSED (rule 22).
// One "?" per panel: its body lists each row's own 1.x help text (no per-row "?" fits two columns), cached per language
// and per which layers are available.
public sealed partial class Plugin
{
    private const string SecHide = "ps.sec.hide";
    private const float HideLabelW = 150f;   // (475 inner − 10 gap) / 2 = 232 per column → 150 label + 6 + 72 chip
    private const float HideColGap = 10f;
    private static readonly ChipStyle HiddenChip = new("ps.kit.hidden", "ps.kit.shown", 72f, ChipTone.Accent);

    private readonly record struct HideItem(string LabelKey, VisibilityLayers Layer, string HelpKey);

    private static readonly HideItem[] PeopleItems =
    {
        new("ps.hide.me", VisibilityLayers.SelfCharacter, "ps.help.hide.me"),
        new("ps.hide.adventurers", VisibilityLayers.Strangers, "ps.help.hide.adventurers"),
        new("ps.hide.spiritEcho", VisibilityLayers.OwnSpiritEcho, "ps.help.hide.spiritEcho"),
        new("ps.hide.friends", VisibilityLayers.Friends, "ps.help.hide.friends"),
        new("ps.hide.npcs", VisibilityLayers.NonPlayers, "ps.help.hide.npcs"),
        new("ps.hide.party", VisibilityLayers.Party, "ps.help.hide.party"),
        new("ps.hide.enemies", VisibilityLayers.Enemies, "ps.help.hide.enemies"),
        new("ps.hide.guild", VisibilityLayers.Guild, "ps.help.hide.guild"),
        new("ps.hide.otherSpiritEchoes", VisibilityLayers.OtherSpiritEchoes, "ps.help.hide.otherSpiritEchoes"),
    };

    private static readonly HideItem[] WorldItems =
    {
        new("ps.hide.hud", VisibilityLayers.GameHud, "ps.help.hide.hud"),
        new("ps.hide.names", VisibilityLayers.Nameplates, "ps.help.hide.names"),
        new("ps.hide.weapons", VisibilityLayers.Weapons, "ps.help.hide.weapons"),
        new("ps.hide.collectibles", VisibilityLayers.Collectibles, "ps.help.hide.collectibles"),
    };

    private static readonly HideItem[] EffectItems =
    {
        new("ps.hide.fx.mine", VisibilityLayers.EffectsMine, "ps.help.hide.fx.mine"),
        new("ps.hide.fx.party", VisibilityLayers.EffectsParty, "ps.help.hide.fx.party"),
        new("ps.hide.fx.others", VisibilityLayers.EffectsOthers, "ps.help.hide.fx.others"),
        new("ps.hide.fx.monsters", VisibilityLayers.EffectsMonsters, "ps.help.hide.fx.monsters"),
    };

    // The free camera's entry hides (FreeCamSettings.EntryHideChoices); Effects stands for the Effects switches above.
    private static readonly HideItem[] EntryItems =
    {
        new("ps.hide.hud", VisibilityLayers.GameHud, ""),
        new("ps.hide.names", VisibilityLayers.Nameplates, ""),
        new("ps.hide.others", VisibilityLayers.OtherPlayers, ""),
        new("ps.hide.me", VisibilityLayers.Self, ""),
        new("ps.hide.fx", VisibilityLayerSets.Effects, ""),
    };

    private HudElement BuildHideSection() => SectionColumn(
        LivePanel(UpperLoc("ps.hide.now"), HiddenCountText, null, new HudElement[]
        {
            new RowElement(new HudElement[]
            {
                new CellElement(NoteLine(() => _hideAllNote), Weight: 1f),
                Pill(PillKind.Neutral, "ps.cap.showAll", ShowAllLayers, 68f, enabled: () => _settings.Hides != VisibilityLayers.None || _overlayHidden),
            }, Gap: 8f),
        }),
        HidePanel("ps.hide.people", PeopleItems, LayerChip),
        HidePanel("ps.hide.world", WorldItems, LayerChip),
        HidePanel("ps.hide.effects", EffectItems, LayerChip),
        HidePanel("ps.hide.entry", EntryItems, EntryChip));

    private HudElement HidePanel(string captionKey, HideItem[] items, Func<HideItem, HudElement> chip)
    {
        var rows = new HudElement[(items.Length + 1) / 2];
        for (var r = 0; r < rows.Length; r++)
        {
            var left = HideCell(items[2 * r], chip);
            var right = 2 * r + 1 < items.Length ? HideCell(items[2 * r + 1], chip) : new SpacerElement(HideLabelW + 6f + HiddenChip.Width, 1f);
            rows[r] = new RowElement(new HudElement[] { left, right }, Gap: HideColGap);
        }
        var help = new HelpKeys(captionKey, captionKey, SecHide) { Body = HideHelpBody(items) };
        return SectionPanelWithTrailing(captionKey, null, new HudElement[] { HelpButton(help) }, rows);
    }

    private HudElement HideCell(HideItem item, Func<HideItem, HudElement> chip)
    {
        Func<bool>? enabled = item.HelpKey.Length == 0 ? null : () => LayerAvailable(item.Layer);
        return new RowElement(new HudElement[]
        {
            new SpacerElement(0f, PillH),
            new CellElement(new TextElement(Loc(item.LabelKey), Color: () => RowLabelColor(enabled), NoWrap: true) { Ellipsize = true },
                Width: HideLabelW),
            chip(item),
        }, Gap: 6f);
    }

    private HudElement LayerChip(HideItem item) => Chip(
        () => LayerAvailable(item.Layer) && IsHidden(item.Layer),
        on => SetHidden(item.Layer, on),
        HiddenChip,
        () => LayerAvailable(item.Layer));

    private HudElement EntryChip(HideItem item) => Chip(
        () => _fcSettings.EntryHidesLayer(item.Layer),
        on => _fcSettings.SetEntryHide(item.Layer, on),
        HiddenChip);

    /// <summary>"Label: help" for every row of a panel, rebuilt only on a language switch or an availability change.</summary>
    private Func<string> HideHelpBody(HideItem[] items)
    {
        var cache = new Cached<(string, int)>(_ => ComposeHideHelp(items));
        if (items == EntryItems)
            return () => cache.Get((T("fc.help.entryHides"), 0));
        VisibilityLayers mask = 0;
        foreach (var i in items) mask |= i.Layer;
        return () => cache.Get((T(items[0].HelpKey), (int)(_availableThisTick & mask)));
    }

    private string ComposeHideHelp(HideItem[] items)
    {
        if (items == EntryItems) return T("fc.help.entryHides") + "\n\n" + T("ps.hide.fx") + ": " + T("fc.help.entryHide.fx");
        var sb = new StringBuilder(512);
        foreach (var i in items)
        {
            if (sb.Length > 0) sb.Append("\n\n");
            sb.Append(T(i.LabelKey)).Append(": ").Append(LayerAvailable(i.Layer) ? T(i.HelpKey) : T("ps.help.layerUnavailable"));
        }
        return sb.ToString();
    }

    // "3 hidden" / "Nothing hidden" — every remembered hide plus the Stellar-overlay hide (never persisted).
    private Cached<(int, string)>? _hiddenCount;

    private string HiddenCountText()
    {
        var n = System.Numerics.BitOperations.PopCount((uint)(int)(_settings.Hides & HideLayers.Persistable)) + (_overlayHidden ? 1 : 0);
        _hiddenCount ??= new Cached<(int, string)>(k => k.Item1 == 0 ? T("ps.hide.none") : _loc.TFormat("ps.hide.count", k.Item1));
        return _hiddenCount.Get((n, T("ps.hide.none")));
    }

    private void ShowAllLayers()
    {
        _overlayHidden = false;
        _settings.SetHides(VisibilityLayers.None);
        ApplyLiveHides();
    }
}
