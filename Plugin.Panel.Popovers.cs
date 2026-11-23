using System;
using Stellar.Abstractions.Domain;
using Stellar.Abstractions.Services;

namespace Stellar.PhotoStudio;

// In-window popovers over the full panel (approved prototype § 3 "Keys popover" and Look "More popover"), drawn as
// LayerStackElement overlays (fw 2.33/2.34), so they live inside the window, move and fade with it:
//   layer 1  a full-cover, input-blocking catcher while either popover is open — a click anywhere outside the card closes
//            it (the click is swallowed, as a click-away should be);
//   layer 2  Keys: every hotkey with its CURRENT binding (read from the hotkey service at 4 Hz while it is open, Plugin.Panel.Status.cs),
//            anchored above the footer's Keys pill;
//   layer 3  More: Save as / Rename / Import / Export / Delete (Danger text), anchored under the Look preset bar's More pill.
// ⚠️ A LayerRect is fixed at build time, so More always opens at the preset bar's place at the top of Look (the bar is the
// first panel there; a scrolled Look section still shows it there). Esc closes them through the layers' OnEscape (fw
// 2.35): the framework's ticker picks the shown popover (the modal confirm and a focused field take Esc first). The press
// is not hidden from the game; inside the free camera its input shield keeps it from the game anyway.
public sealed partial class Plugin
{
    private const float KeysCardW = 340f, KeysCardH = 452f, KeyColW = 112f, KeyRowH = 20f;
    private const float MoreCardW = 200f, MoreCardTop = 118f, MoreCardRight = 19f;
    private const float FooterBand = PillH + 8f;   // the footer row + the root gap: the Keys card sits just above it
    private const float CardRadius = 8f;
    private bool _keysOpen;

    private static readonly ColorRgba CardLift = new(1f, 1f, 1f, 0.06f);   // popover surface = bg lightened 6 % (fw PanelBgColor)

    private bool PopoverOpen() => _keysOpen || _moreOpen;

    private void ClosePopovers()
    {
        if (!PopoverOpen()) return;
        _keysOpen = false;
        _moreOpen = false;
        _panelWin.MarkDirty();
    }

    // The frame the framework's Esc closed a popover in (OnEscape); -10 = none (FrameCount starts at 0, no overflow).
    private long _popoverEscFrame = -10;

    /// <summary>StackLayer.OnEscape of the Keys / More layers (fw 2.35, per render frame from the framework's ticker).</summary>
    private void OnPopoverEscape()
    {
        if (!PopoverOpen()) return;
        ClosePopovers();
        _popoverEscFrame = _services.Framework.FrameCount;
    }

    /// <summary>Esc inside the free camera (FreeCamHost.DismissModalUi): closes the "?" tip or the modal confirm, and
    /// reports an open popover; true when any of them was open (then Esc does nothing else — it does not leave the free
    /// camera).</summary>
    private bool DismissPanelOverlays()
    {
        // Precedence: the free camera reads Esc in its own per-frame callback, the popover's OnEscape runs from the
        // framework ticker, and the two are not ordered within a frame. The popover is closed ONLY by OnEscape (the
        // framework also decides whether a focused field / pop-up / the confirm takes the press first); this hook just
        // keeps that same Esc from leaving the free camera. Hook first: the popover is still open → true, and the ticker
        // closes it later this frame. Ticker first: it is already closed, so a popover OnEscape closed this frame (or the
        // one before — FrameCount is bumped between the two passes in an unknown order) still counts. Consumed on use.
        var escClosedPopover = _services.Framework.FrameCount - _popoverEscFrame <= 1;
        _popoverEscFrame = -10;
        var any = DismissHelpPopover() | PopoverOpen() | escClosedPopover | _panelConfirm.IsOpen;
        _panelConfirm.Cancel();
        return any;
    }

    private void ToggleKeys()
    {
        var open = !_keysOpen;
        ClosePopovers();
        _keysOpen = open;
        if (open) { CloseHelpPopover(); RefreshPanelTexts(); }
        _panelWin.MarkDirty();
    }

    private void ToggleMore()
    {
        var open = !_moreOpen;
        ClosePopovers();
        _moreOpen = open;
        _nameMode = NameMode.None;
        _panelWin.MarkDirty();
    }

    /// <summary>The panel content with the popover layers over it.</summary>
    private HudElement WithPopovers(HudElement content) => new LayerStackElement(content, new[]
    {
        new StackLayer(new SelectableElement(new SpacerElement(0f, 0f), OnClick: ClosePopovers) { Bare = true })
            { Visible = PopoverOpen, BlocksInput = true },
        new StackLayer(KeysCard())
            { Visible = () => _keysOpen, BlocksInput = true, OnEscape = OnPopoverEscape, Rect = new LayerRect { Right = 0f, Bottom = FooterBand, Width = KeysCardW, Height = KeysCardH } },
        new StackLayer(MoreCard())
            { Visible = () => _moreOpen, BlocksInput = true, OnEscape = OnPopoverEscape, Rect = new LayerRect { Right = MoreCardRight, Top = MoreCardTop, Width = MoreCardW, Height = MoreCardH } },
    });

    private ColorRgba CardColor8() => StyleMath.Over(CardLift, WindowBg());

    // A popover card: lifted window background, a 1 px Neutral edge, radius 8 (the framework pill-dropdown list look).
    private HudElement PopoverCard(HudElement body, float pad) => new BubbleElement(body)
    {
        FillColorFn = () => Fade(CardColor8()), OutlineColorFn = () => Fade(NeutralFill()),
        CornerRadius = CardRadius, PaddingX = pad, PaddingY = pad,
    };

    // ── Keys ──────────────────────────────────────────────────────────────────────────────────────────────────────

    private readonly string[] _keyLabels = { "", "", "", "", "", "", "", "", "" };

    // The rebindable hotkeys (StudioHotkeys), in the prototype's order, then the free camera's fixed keys.
    private static readonly (string Id, string DescKey)[] KeyRows =
    {
        (StudioHotkeys.Capture, "ps.keys.capture"), (StudioHotkeys.Panel, "ps.keys.panel"), (StudioHotkeys.FreeCam, "ps.keys.freecam"),
        (StudioHotkeys.HideAll, "ps.keys.hideall"), (StudioHotkeys.NextPreset, "ps.keys.nextpreset"), (StudioHotkeys.Close, "ps.keys.close"),
        (StudioHotkeys.FovIn, "ps.keys.fovin"), (StudioHotkeys.FovOut, "ps.keys.fovout"), (StudioHotkeys.FovReset, "ps.keys.fovreset"),
    };

    private static readonly (string Keys, string DescKey)[] FixedKeyRows =
    {
        ("RMB · wheel", "ps.keys.rotate"), ("WASD · Q/E", "ps.keys.move"), ("Tab", "ps.keys.mode"),
        ("Space", "ps.keys.freeze"), ("L · Shift+L", "ps.keys.lamp"), ("U · H", "ps.keys.ui"),
    };

    /// <summary>From RefreshPanelTexts (4 Hz) while the Keys popover is open: the live bindings.</summary>
    private void RefreshKeyLabels()
    {
        if (!_keysOpen) return;
        for (var i = 0; i < KeyRows.Length; i++)
        {
            var label = BindingText(KeyRows[i].Id);
            if (label != _keyLabels[i]) _keyLabels[i] = label;   // keep the old instance when unchanged (no re-draw)
        }
    }

    private Cached<(string, string)>? _keysSubtitleText;

    private HudElement KeysCard()
    {
        var rows = new HudElement[KeyRows.Length + FixedKeyRows.Length + 1];
        for (var i = 0; i < KeyRows.Length; i++)
        {
            var slot = i;
            rows[i] = KeyRow(() => _keyLabels[slot], KeyRows[i].DescKey);
        }
        for (var i = 0; i < FixedKeyRows.Length; i++)
        {
            var keys = FixedKeyRows[i].Keys;
            rows[KeyRows.Length + i] = KeyRow(() => keys, FixedKeyRows[i].DescKey);
        }
        rows[^1] = NoteLine(Loc("ps.keys.note"));
        return PopoverCard(new ColumnElement(new HudElement[]
        {
            new RowElement(new HudElement[]
            {
                HelpPopoverIcon(),
                new CellElement(new ColumnElement(new HudElement[]
                {
                    new TextElement(Loc("ps.keys.title"), Color: () => TextColor(), NoWrap: true, FontSize: 14) { Bold = true },
                    new TextElement(KeysSubtitle, Color: () => MutedColor(), NoWrap: true, FontSize: 11) { Bold = true, Ellipsize = true },
                }, Gap: 1f), Weight: 1f),
                IconButton("✕", ClosePopovers, IconButtonKind.Bare, surface: CardColor8),
            }, Gap: 10f),
            new BubbleElement(new ColumnElement(rows, Gap: 2f))
            {
                FillColorFn = () => Fade(TileColor()), CornerRadius = TileRadius, PaddingX = 10f, PaddingY = 8f,
            },
        }, Gap: 10f), 12f);
    }

    // "PHOTO STUDIO · HELP", rebuilt only on a language switch.
    private string KeysSubtitle() => (_keysSubtitleText ??= new Cached<(string, string)>(k => k.Item1.ToUpperInvariant() + " · " + k.Item2.ToUpperInvariant()))
        .Get((T("ps.title"), T(KitHelpKey)));

    private HudElement KeyRow(Func<string> keys, string descKey) => new RowElement(new HudElement[]
    {
        new SpacerElement(0f, KeyRowH),
        new CellElement(new TextElement(keys, Color: () => TextColor(), NoWrap: true, FontSize: 13) { Bold = true, Ellipsize = true }, Width: KeyColW),
        new CellElement(new TextElement(Loc(descKey), Color: () => MutedColor(), NoWrap: true) { Ellipsize = true }, Weight: 1f),
    }, Gap: 8f);

    // ── More ──────────────────────────────────────────────────────────────────────────────────────────────────────

    private const float MoreCardH = 5f * ListTileH + 4f * 2f + 2f * 4f;   // five rows, 2 px apart, 4 px card padding

    private HudElement MoreCard() => PopoverCard(new ColumnElement(new HudElement[]
    {
        MenuRow("ps.pre.saveAs", () => { ClosePopovers(); BeginName(NameMode.SaveAs, _activePresetName + T("ps.pre.mineSuffix")); }),
        MenuRow("ps.pre.rename", () => { ClosePopovers(); BeginName(NameMode.Rename, _activePresetName); }, () => !ActiveIsBuiltIn),
        MenuRow("ps.pre.import", () => { ClosePopovers(); BeginImport(); }),
        MenuRow("ps.pre.export", () => { ClosePopovers(); ExportActive(); }),
        MenuRow("ps.pre.deleteMenu", () => { ClosePopovers(); ConfirmDeletePreset(); }, () => !ActiveIsBuiltIn, danger: true),
    }, Gap: 2f), 4f);

    /// <summary>One popover row: 26 px, transparent at rest, a soft wash on hover; a destructive one in the remove red.</summary>
    private HudElement MenuRow(string labelKey, Action onClick, Func<bool>? enabled = null, bool danger = false)
    {
        var hover = new HoverFlag();
        bool En() => enabled == null || enabled();
        return new BubbleElement(new SelectableElement(new RowElement(new HudElement[]
            {
                new SpacerElement(8f, ListTileH),
                new TextElement(Loc(labelKey), Color: () => !En() ? MutedColor() : danger ? RemoveGlyph() : TextColor(), NoWrap: true)
                    { Ellipsize = true },
            }, Gap: 0.01f), OnClick: () => { if (En()) onClick(); })
            { Bare = true, OnHover = over => hover.On = over && En() })
        {
            FillColorFn = () => hover.On ? Fade(StyleMath.Over(TextColor() with { A = SoftHoverMix }, CardColor8())) : Transparent,
            CornerRadius = TileRadius, PaddingX = 0f, PaddingY = 0f,
        };
    }
}
