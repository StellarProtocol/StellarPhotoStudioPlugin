using System;
using Stellar.Abstractions.Domain;
using Stellar.Abstractions.Services;

namespace Stellar.PhotoStudio;

// House-kit containers (Stellar-Design-Language §2/§4 + the approved prototype): the ToolHeader, SectionPanel (plain
// and with a trailing caption slot), the foldable FoldPanel with a persisted open flag, and the 26 px list tile.
// ⚠️ Every variant has its OWN NAME and takes an explicit HudElement[] — never a pair of `params` overloads: with two
// params overloads C# binds the first child as the trailing control (Maestro Link bug, KB §4).
public sealed partial class Plugin
{
    // ── tool header ───────────────────────────────────────────────────────────────────────────────────────────────
    // [40 px accent icon tile] [bold title over a live status line] … [trailing: e.g. "Keep look" + chip + "?"].
    // The text column takes the leftover width and ellipsizes, so a long status never pushes the trailing slot out.
    private HudElement ToolHeader(string iconName, string titleKey, HudElement status, HudElement[]? trailing = null)
    {
        var items = new HudElement[2 + (trailing?.Length ?? 0)];
        items[0] = IconTile(iconName, HeaderTileSize, HeaderGlyph);
        items[1] = new CellElement(new ColumnElement(new HudElement[]
        {
            new TextElement(Loc(titleKey), Color: () => TextColor(), Emphasis: true, NoWrap: true) { Ellipsize = true },
            status,
        }, Gap: 2f), Weight: 1f);
        if (trailing != null) Array.Copy(trailing, 0, items, 2, trailing.Length);
        return new RowElement(items, Gap: 10f);
    }

    // A header / panel status line: plain text in a live colour, one line, ellipsized.
    private static HudElement StatusText(Func<string> text, Func<ColorRgba> color)
        => new TextElement(text, Color: () => color(), NoWrap: true) { Ellipsize = true };

    // ── section panels ────────────────────────────────────────────────────────────────────────────────────────────
    // A rounded panel (background darkened 20 %, live): [bold 12 UPPERCASE caption] [muted 12 hint, right-aligned, takes
    // the rest] [trailing…], then the children. Directly in a Column it FILLS the width (a Bubble only hugs inside a Row).
    // The caption row is pinned to 24 px (the "?" / chip height) on EVERY panel, so panels with and without a trailing
    // control line up and side-by-side panels' bodies start level (KB §6). Caption upper-cased once per language.
    private const float CaptionRowH = 24f;
    private const int CaptionFont = 12;

    private HudElement SectionPanel(string captionKey, string? hintKey, HudElement[] children)
        => SectionPanelWithTrailing(captionKey, hintKey, null, children);

    private HudElement SectionPanelWithTrailing(string captionKey, string? hintKey, HudElement[]? trailing,
        HudElement[] children)
    {
        var caption = new RowElement(new HudElement[] { CaptionText(captionKey), HintText(hintKey) }, Gap: 6f);
        return PanelShell(CaptionRow(caption, trailing), children);
    }

    // ── foldable panel ────────────────────────────────────────────────────────────────────────────────────────────
    // [chevron image] [CAPTION] [hint] … [trailing (e.g. an On/Off chip + "?")]; clicking the chevron / caption / hint
    // folds the body, the trailing controls keep their own clicks. The body is a Conditional with no Else, so a folded
    // panel is just its caption row (no gap left behind). The chevron is an IMAGE: "▾" / "▸" don't draw in bold and fall
    // back to a taller OS face in plain text (KB §6) — icons "fold_open" / "fold_closed" (Resources\icons, 12 units,
    // tinted Muted; until that art exists the 12 px slot stays empty).
    private const int ChevronUnits = 12;

    /// <summary>A fold's open flag. <see cref="Config"/> persists it in the plugin config section; for a key
    /// StudioSettings already owns (e.g. "ui.qualityOpen"), wrap ITS getter / setter instead so there is one writer.</summary>
    private sealed class FoldFlag
    {
        public readonly Func<bool> Get;
        public readonly Action<bool> Set;

        public FoldFlag(Func<bool> get, Action<bool> set) { Get = get; Set = set; }

        public static FoldFlag Config(IConfigSection cfg, string key, bool defaultOpen)
        {
            bool open = cfg.Get(key, defaultOpen);
            return new FoldFlag(() => open, v =>
            {
                if (v == open) return;
                open = v;
                cfg.Set(key, v);
                cfg.SaveQuiet();
            });
        }
    }

    private HudElement FoldPanel(string captionKey, string? hintKey, FoldFlag open, HudElement[]? trailing,
        HudElement[] children)
    {
        var closedPick = new IconPick(this, "fold_closed", ChevronUnits);
        var openPick = new IconPick(this, "fold_open", ChevronUnits);
        var chevron = new CellElement(new ImageElement(() => open.Get() ? openPick.Get() : closedPick.Get(), ChevronUnits, ChevronUnits)
        {
            Live = true, Crisp = true, Tint = () => MutedColor(),
        }, Width: ChevronUnits);
        var header = new SelectableElement(
            new RowElement(new HudElement[] { chevron, CaptionText(captionKey), HintText(hintKey) }, Gap: 6f),
            OnClick: () => open.Set(!open.Get())) { Bare = true };
        var body = new ConditionalElement(open.Get, new ColumnElement(children, Gap: 6f));
        return PanelShell(CaptionRow(header, trailing), new HudElement[] { body });
    }

    private HudElement CaptionText(string key)
        => new TextElement(UpperLoc(key), Color: () => TextColor(), NoWrap: true, FontSize: CaptionFont) { Bold = true };

    // The hint takes the leftover width (weighted cell), right-aligned and ellipsized; null key = an empty filler.
    private HudElement HintText(string? key)
        => new CellElement(new TextElement(key == null ? static () => string.Empty : Loc(key), Color: () => MutedColor(),
            Align: TextAlign.Right, NoWrap: true, FontSize: CaptionFont) { RichEmoji = true, Ellipsize = true }, Weight: 1f);

    private static HudElement CaptionRow(HudElement captionAndHint, HudElement[]? trailing)
    {
        var items = new HudElement[2 + (trailing?.Length ?? 0)];
        items[0] = new SpacerElement(0f, CaptionRowH);
        items[1] = new CellElement(captionAndHint, Weight: 1f);
        if (trailing != null) Array.Copy(trailing, 0, items, 2, trailing.Length);
        return new RowElement(items, Gap: 6f);
    }

    private HudElement PanelShell(HudElement captionRow, HudElement[] children)
    {
        var items = new HudElement[children.Length + 1];
        items[0] = captionRow;
        Array.Copy(children, 0, items, 1, children.Length);
        return new BubbleElement(new ColumnElement(items, Gap: 6f))
        {
            FillColorFn = () => Fade(PanelColor()), CornerRadius = TileRadius, PaddingX = 10f, PaddingY = 8f,
        };
    }

    // ── list tiles (virtual lists) ────────────────────────────────────────────────────────────────────────────────
    // A raised 26 px row: Tile fill, accent wash while selected (prototype .tile.sel), a light Text wash while hovered.
    // ⚠️ A VirtualList slot STRETCHES its child to the full RowHeight, so a 26 px tile in a 28 px slot filled all 28 and
    // the rows touched (R15): each pooled row is ListSlot(tile) = Column[26 px tile, 2 px spacer], RowHeight = ListRowPitch.
    // The row content should hold a 0-width ListTileH strut. Pooled hover flags must be keyed per SLOT and reset when the
    // slot rebinds to another item (KB §5).
    private const float ListTileH   = 26f;
    private const float ListRowGap  = 2f;
    private const float ListRowPitch = ListTileH + ListRowGap;

    private static HudElement ListSlot(HudElement tile) => new ColumnElement(new HudElement[]
    {
        tile,
        new SpacerElement(0f, ListRowGap),
    }, Gap: 0.01f);

    // Viewport height that shows exactly `rows` rows, ending flush with a tile (the last gap falls outside).
    private static float ListHeight(int rows) => rows * ListRowPitch - ListRowGap;

    private HudElement ListTile(HudElement row, Func<bool>? selected = null, Func<bool>? hovered = null)
        => new BubbleElement(row)
        {
            FillColorFn = () => Fade(selected != null && selected() ? StyleMath.Over(AccentColor() with { A = AccentWashAlpha }, PanelColor())
                                   : hovered != null && hovered() ? StyleMath.Over(TextColor() with { A = 0.05f }, TileColor())
                                   : TileColor()),
            CornerRadius = TileRadius, PaddingX = 0f, PaddingY = 0f,
        };
}
