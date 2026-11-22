using System;
using Stellar.Abstractions.Domain;
using Stellar.Abstractions.Services;

namespace Stellar.PhotoStudio;

// The house "?" + help popover (Maestro Plugin.Help.Tip.cs → AutoFishing Plugin.Help.cs, localized). The toolkit has no
// hover tooltip (R12), so every setting row ends in a round 24 px "?" (Neutral, lighter on hover, accent-tinted while
// its tip is open) that opens ONE shared, title-bar-less GlassMenu popover anchored under it:
//   [32 px accent tile with a white bold "?"] [bold setting name / muted "SECTION · HELP"] … [round ✕]
//   then the help text in one Tile box, 360 wide.
// A "?" carries CATALOG KEYS (HelpKeys); the popover's Funcs resolve whichever set is current through _loc.T, so an open
// tip follows a live language switch. Clicking another "?" swaps the text and re-anchors; re-clicking the open one closes.
//
// NOT WIRED YET (phase 1 = no visible change): the panel still uses the old "photostudio.tip" (Plugin.Help.cs). Phase 2
// calls RegisterHelpPopover() from RegisterWindows (+ RemoveWindows), HelpPopoverTick() from TickStudio and
// CloseHelpPopover() wherever DismissHelpTip() is called today. Until then a HelpButton click does nothing.
public sealed partial class Plugin
{
    /// <summary>A "?"'s catalog keys: the popover title (usually the row's own label key), the body, and the section the
    /// setting lives in (subtitle "SECTION · HELP", or "SECTION · SUBSECTION · HELP" with <see cref="SubsectionKey"/>).
    /// <see cref="Body"/> overrides the body key for a text composed at runtime (e.g. one naming a hotkey) — it must
    /// hand back a cached string.</summary>
    private sealed record HelpKeys(string TitleKey, string BodyKey, string SectionKey, string? SubsectionKey = null)
    {
        public Func<string>? Body { get; init; }
    }

    private const string KitHelpKey  = "ps.kit.help";
    private const float HelpDotSize  = 24f;   // exactly 24 × 24 (RoundRadius 11, R4)
    private const float HelpPopWidth = 360f;
    private const float HelpPopIcon  = 32f;

    private IWindowControl? _helpPop;
    private HelpKeys? _helpPopKeys;           // the open tip; null = closed
    private WindowRect _helpPopRect;
    private int _helpPopRepositionTicks;
    private readonly HoverFlag _helpPopCloseHover = new();
    private readonly UpperText _helpPopSection = new(), _helpPopSubsection = new(), _helpPopHelp = new();
    private string? _subSection, _subSub, _subHelp;
    private string _helpPopSubtitle = string.Empty;

    // Content = [24 px strut, the "?" in a 24 px box] side by side in ONE Row, so the circle is exactly its side (a
    // stacked spacer drew Maestro's "?" 24 × 26). CenterGlyphs centres the drawn "?" in its box.
    private HudElement HelpButton(HelpKeys keys)
    {
        var hover = new HoverFlag();
        var text = new ContrastCache();
        bool Open() => _helpPop is { IsShown: true } && ReferenceEquals(_helpPopKeys, keys);
        return new CellElement(new BubbleElement(new SelectableElement(
            new RowElement(new HudElement[]
            {
                new SpacerElement(0f, HelpDotSize),
                new TextElement(static () => "?", Color: () => text.Get(TextColor(), NeutralFill(), PillMinContrast),
                    Width: HelpDotSize, Align: TextAlign.Center, NoWrap: true, FontSize: 12) { Bold = true, CenterGlyphs = true },
            }, Gap: 0.01f, Justify: RowJustify.Center),
            OnClick: () => { })                       // the real work is in OnClickWithRect (needs the button's rect)
            {
                Bare = true,
                OnHover = over => hover.On = over,
                OnClickWithRect = r => ToggleHelpPopover(r, keys),
            })
        {
            FillColorFn = () => Fade(Open() ? StyleMath.Over(AccentColor() with { A = 0.35f }, NeutralFill())
                                     : hover.On ? HoverFill(NeutralFill()) : NeutralFill()),
            CornerRadius = RoundRadius(HelpDotSize), PaddingX = 0f, PaddingY = 0f, MaxWidth = HelpDotSize,
        }, Width: HelpDotSize);
    }

    private void RegisterHelpPopover()
    {
        _helpPop = _services.Windows.Register(new WindowRegistration(
            new WindowSpec(
                Id:          "photostudio.help",
                Title:       T("ps.win.help"),
                DefaultRect: new WindowRect(20f, 20f, HelpPopWidth, 0f),
                Category:    WindowCategory.Tools,
                Style:       WindowPanelStyle.GlassMenu)
            {
                Draggable = true, Closable = true, StartVisible = false,
                // No title bar: the header carries the title and a close; the whole frame is the drag handle.
                ShowTitleBar = false,
                TitleProvider = () => _loc.T("ps.win.help"),
                ShouldRender = InWorld,
            },
            BuildHelpPopoverRoot(),
            OnClose: CloseHelpPopover));
    }

    private HudElement BuildHelpPopoverRoot() => new ColumnElement(new HudElement[]
    {
        new RowElement(new HudElement[]
        {
            HelpPopoverIcon(),
            new CellElement(new ColumnElement(new HudElement[]
            {
                new TextElement(() => _helpPopKeys == null ? string.Empty : _loc.T(_helpPopKeys.TitleKey),
                    Color: () => TextColor(), NoWrap: true, FontSize: 14) { Bold = true, Ellipsize = true },
                new TextElement(HelpPopoverSubtitle, Color: () => MutedColor(), NoWrap: true, FontSize: 11) { Bold = true, Ellipsize = true },
            }, Gap: 1f), Weight: 1f),
            IconButton("✕", CloseHelpPopover, IconButtonKind.Bare, surface: WindowBg),
        }, Gap: 10f),
        // The help text: one raised tile (house Tile colour, theme-live), wrapping inside the 360 px popover.
        new BubbleElement(new TextElement(HelpPopoverBody, Color: () => TextColor()))
        {
            FillColorFn = () => Fade(TileColor()), CornerRadius = TileRadius, PaddingX = 10f, PaddingY = 8f,
        },
    }, Gap: 10f);

    private string HelpPopoverBody()
    {
        var k = _helpPopKeys;
        return k == null ? string.Empty : k.Body != null ? k.Body() : _loc.T(k.BodyKey);
    }

    // "SECTION · HELP" / "SECTION · SUB · HELP": rebuilt only when a source string instance changes (language switch or
    // another tip), so a poll allocates nothing.
    private string HelpPopoverSubtitle()
    {
        var k = _helpPopKeys;
        if (k == null) return string.Empty;
        string section = _helpPopSection.Get(_loc.T(k.SectionKey));
        string? sub = k.SubsectionKey == null ? null : _helpPopSubsection.Get(_loc.T(k.SubsectionKey));
        string help = _helpPopHelp.Get(_loc.T(KitHelpKey));
        if (!ReferenceEquals(section, _subSection) || !ReferenceEquals(sub, _subSub) || !ReferenceEquals(help, _subHelp))
        {
            _subSection = section; _subSub = sub; _subHelp = help;
            _helpPopSubtitle = sub == null ? section + " · " + help : section + " · " + sub + " · " + help;
        }
        return _helpPopSubtitle;
    }

    // 32 px accent tile with a white bold "?" (the icon-tile look, drawn with text instead of a PNG; radius 7 keeps the
    // sliced tile's min size under the side).
    private HudElement HelpPopoverIcon()
    {
        var text = new SolidLabelCache();
        return new CellElement(new BubbleElement(new RowElement(new HudElement[]
            {
                new SpacerElement(0f, HelpPopIcon),
                new TextElement(static () => "?", Color: () => text.Get(AsDrawn(AccentColor(), WindowBg())),
                    Width: HelpPopIcon, Align: TextAlign.Center, NoWrap: true, FontSize: 18) { Bold = true, CenterGlyphs = true },
            }, Gap: 0.01f, Justify: RowJustify.Center))
        {
            FillColorFn = () => Fade(AccentColor()), CornerRadius = MathF.Round(HelpPopIcon * 0.22f), PaddingX = 0f, PaddingY = 0f,
        }, Width: HelpPopIcon);
    }

    private void ToggleHelpPopover(WindowRect r, HelpKeys keys)
    {
        if (_helpPop == null) return;   // not registered yet (phase 1)
        if (_helpPop.IsShown && ReferenceEquals(_helpPopKeys, keys)) { CloseHelpPopover(); return; }
        _helpPopKeys = keys;
        // OnClickWithRect reports SCREEN PIXELS; SetRect takes CANVAS units — scale first, or the tip drifts off its
        // "?" whenever the UI scale isn't 1 (WindowBuilder-Patterns.md, click-to-open tooltip).
        var fw = _services.Framework;
        float k = fw.ScreenWidth > 0 && fw.CanvasWidth > 1f ? (float)fw.CanvasWidth / fw.ScreenWidth : 1f;
        r = new WindowRect(r.X * k, r.Y * k, r.Width * k, r.Height * k);
        float canvasH = fw.CanvasHeight > 1f ? fw.CanvasHeight : fw.ScreenHeight;
        // Below the "?", right-aligned to it, so the popover opens to the LEFT over the row instead of off the window.
        float x = MathF.Max(10f, r.X + r.Width - HelpPopWidth);
        float y = MathF.Min(r.Y + r.Height + 4f, canvasH - 40f);
        _helpPopRect = new WindowRect(x, y, HelpPopWidth, 0f);
        _helpPop.SetVisible(true);
        _helpPop.SetRect(_helpPopRect);
        // Front: switching between "?"s the tip is already visible (no remount → no fresh stacking position), and the
        // panel may have been raised after it — CLAUDE.md Rule 5 exception (re-open of an already-visible child).
        _helpPop.BringToFront();
        _helpPop.MarkDirty();   // re-pull title / body for the new tip this frame
        // The first-ever open mounts the window and applies its DefaultRect AFTER this SetRect → re-apply the anchored
        // rect for the next few ticks so it lands under the "?" on the first open too.
        _helpPopRepositionTicks = 4;
    }

    private void CloseHelpPopover()
    {
        _helpPopRepositionTicks = 0;
        _helpPopKeys = null;
        if (_helpPop is { IsShown: true }) _helpPop.SetVisible(false);
    }

    // Per frame from the plugin update: early-out unless a tip was just opened.
    private void HelpPopoverTick()
    {
        if (_helpPopRepositionTicks <= 0) return;
        _helpPopRepositionTicks--;
        if (_helpPop is { IsShown: true }) _helpPop.SetRect(_helpPopRect);
    }
}
