using System;
using Stellar.Abstractions.Domain;
using Stellar.Abstractions.Services;

namespace Stellar.PhotoStudio;

// House-kit controls (Stellar-Design-Language §4 + the approved prototype _scratch\prototypes\
// photostudio-redesign-APPROVED.html): 28 px pill buttons (Primary / Neutral / Caution / Danger, recessed when disabled),
// the clickable On/Off chip and its custom-label variants, the non-clickable tag chip, and the 28 px icon buttons.
// Every label is a catalog KEY (Rule 10); every fill goes through Fade (window opacity) and every hover follows the shape.
public sealed partial class Plugin
{
    // ── pill buttons ──────────────────────────────────────────────────────────────────────────────────────────────
    // ONE build for every labelled button: a fill-tinted Bubble around a Bare SelectableElement, 28 px tall (the struts
    // either side of a bold 13 label, 12 px side padding). `labelW` > 0 = a FIXED label box (the pill never resizes with
    // the language or a label change, R10; Ellipsize keeps a long translation inside); 0 = hug the label. A disabled pill
    // keeps its geometry, swallows the click and is RECESSED (rule 22). `surface` = what the pill sits on (recessed
    // label contrast). Pill = label box + 24 px, so a fixed cell for it must be ≥ that (KB "fused pills").
    private enum PillKind { Primary, Neutral, Caution, Danger }

    private const float PillH    = 28f;
    private const float PillPadX = 12f;
    private const float PillPairLabelW = 96f;   // both halves of a PillPair get the same label box (equal split)

    private HudElement Pill(PillKind kind, string labelKey, Action onClick, float labelW = 0f, Func<bool>? enabled = null,
        Func<ColorRgba>? surface = null)
        => Pill(kind, Loc(labelKey), onClick, labelW, enabled, surface);

    // Func-label form for the few labels composed at runtime ("Capture (F10)"); the Func must hand back a cached string.
    private HudElement Pill(PillKind kind, Func<string> label, Action onClick, float labelW = 0f, Func<bool>? enabled = null,
        Func<ColorRgba>? surface = null)
    {
        var hover = new HoverFlag();
        var disabled = new DisabledLabelCache();
        var solid = new SolidLabelCache();
        var tinted = new ContrastCache();
        Func<ColorRgba> under = surface ?? PanelColor;
        Func<ColorRgba> fill = kind switch
        {
            PillKind.Primary => PrimaryFill,
            PillKind.Caution => CautionFill,
            PillKind.Danger  => DangerFill,
            _                => NeutralFill,
        };
        ColorRgba Label() => kind switch
        {
            PillKind.Primary or PillKind.Danger => solid.Get(AsDrawn(fill(), under())),
            PillKind.Caution => tinted.Get(CautionColor(), fill(), PillMinContrast),
            _                => tinted.Get(TextColor(), fill(), PillMinContrast),
        };
        bool En() => enabled == null || enabled();
        return new BubbleElement(new SelectableElement(
            new RowElement(new HudElement[]
            {
                new SpacerElement(PillPadX, PillH),
                DropBold(new TextElement(label, Color: () => En() ? Label() : DisabledLabel(under(), disabled),
                    Width: labelW, Align: TextAlign.Center, NoWrap: true, FontSize: 13) { Bold = true, Ellipsize = labelW > 0f }),
                new SpacerElement(PillPadX, PillH),
            }, Gap: 0.01f, Justify: RowJustify.Center),
            OnClick: () => { if (En()) onClick(); })
            { Bare = true, OnHover = over => hover.On = over })
        {
            FillColorFn = () => !En() ? DisabledShade : Fade(hover.On ? HoverFill(fill()) : fill()),
            CornerRadius = RoundRadius(PillH), PaddingX = 0f, PaddingY = 0f,
        };
    }

    // Stretch variant: the pill fills its share of the row (prototype `style="flex:1"` — Unfreeze / Reset scene, Move here
    // / Duplicate). A Cell stretches the Bubble; the centred Row keeps the label in the middle. Equal halves need equal
    // label boxes (weighted cells split only the LEFTOVER equally — WindowBuilder-Patterns CellElement note).
    private static HudElement Stretch(HudElement pill, float weight = 1f) => new CellElement(pill, Weight: weight);

    private static HudElement PillPair(HudElement left, HudElement right) => new RowElement(new HudElement[]
    {
        new CellElement(left,  Weight: 1f),
        new CellElement(right, Weight: 1f),
    }, Gap: 8f);

    // ── chips (24 px) ─────────────────────────────────────────────────────────────────────────────────────────────
    // A clickable chip in a fixed-width cell: Cell → round Bubble (fill + hover) → Bare Selectable → Row[24 px strut,
    // label]. BOTH states are bold 12 (the prototype .chip), so on/off is ONE bubble whose fill / label colour / text
    // are Funcs — no Conditional swap, so the two states can't drift apart (WindowBuilder-Patterns "ImageElement icon
    // gotchas": same-weight states = one bubble). MaxWidth pins the width (MinimalNameplate, measured 64×24 in-game).
    //   On  = bold coloured label on a wash of the tone (Success 22 % / Accent 28 % with a white label — "Hidden").
    //   Off = muted label (≥ 3:1) on the Neutral fill (prototype .chip.off).
    //   Disabled = recessed: sunken fill + faded label, same size, no hover, click swallowed.
    private enum ChipTone { Success, Accent }

    /// <summary>A chip's labels (catalog keys), width and "on" colour. <see cref="OnOff"/> = the 64 px On/Off chip.</summary>
    private readonly record struct ChipStyle(string OnKey, string OffKey, float Width, ChipTone Tone = ChipTone.Success)
    {
        public static readonly ChipStyle OnOff = new(KitOnKey, KitOffKey, OnOffChipW);
    }

    private const string KitOnKey  = "ps.kit.on";
    private const string KitOffKey = "ps.kit.off";
    private const float ChipH      = 24f;
    private const float OnOffChipW = 64f;
    private const int ChipLabelFont = 12;

    private HudElement OnOffChip(Func<bool> get, Action<bool> set, Func<bool>? enabled = null)
        => Chip(get, set, ChipStyle.OnOff, enabled);

    private HudElement Chip(Func<bool> get, Action<bool> set, ChipStyle style, Func<bool>? enabled = null,
        Func<ColorRgba>? surface = null)
    {
        var hover = new HoverFlag();
        var onText = new ContrastCache();
        var offText = new ContrastCache();
        var disabled = new DisabledLabelCache();
        Func<ColorRgba> under = surface ?? PanelColor;
        bool En() => enabled == null || enabled();
        ColorRgba Tone() => style.Tone == ChipTone.Accent ? AccentColor() : SuccessColor();
        ColorRgba OnFill() => StyleMath.Over(Tone() with { A = style.Tone == ChipTone.Accent ? AccentWashAlpha : ChipWashAlpha }, under());
        ColorRgba OnLabel() => style.Tone == ChipTone.Accent
            ? onText.Get(StyleMath.White, OnFill(), PillMinContrast)
            : onText.Get(SuccessColor(), OnFill(), ChipMinContrast);
        string Text() => _loc.T(get() ? style.OnKey : style.OffKey);
        return new CellElement(new BubbleElement(new SelectableElement(
            new RowElement(new HudElement[]
            {
                new SpacerElement(0f, ChipH),   // height strut = the side (R4: no stacked spacers)
                DropBold(new TextElement(Text, Width: style.Width, Align: TextAlign.Center, NoWrap: true, FontSize: ChipLabelFont,
                    Color: () => !En() ? DisabledLabel(under(), disabled) : get() ? OnLabel()
                                 : offText.Get(MutedColor(), NeutralFill(), ChipMinContrast)) { Bold = true, Ellipsize = true }),
            }, Gap: 0.01f, Justify: RowJustify.Center),
            OnClick: () => { if (En()) set(!get()); })
            { Bare = true, OnHover = over => hover.On = over })
        {
            FillColorFn = () => !En() ? DisabledShade
                              : Fade(hover.On ? HoverFill(get() ? OnFill() : NeutralFill()) : get() ? OnFill() : NeutralFill()),
            CornerRadius = RoundRadius(ChipH), PaddingX = 0f, PaddingY = 0f, MaxWidth = style.Width,
        }, Width: style.Width);
    }

    // A NON-clickable tag on a caption row or a list tile ("Modified", "Photo only", "Depth" in the approved prototype):
    // bold 12 coloured text on a wash of its colour, hugging its label (padding 8). `color` is live (accent / Caution).
    private HudElement TagChip(string labelKey, Func<ColorRgba> color, Func<ColorRgba>? surface = null)
    {
        var text = new ContrastCache();
        Func<ColorRgba> under = surface ?? PanelColor;
        ColorRgba Fill() => StyleMath.Over(color() with { A = ChipWashAlpha }, under());
        return new BubbleElement(new RowElement(new HudElement[]
            {
                new SpacerElement(8f, ChipH),
                DropBold(new TextElement(Loc(labelKey), Color: () => text.Get(color(), Fill(), ChipMinContrast),
                    NoWrap: true, FontSize: ChipLabelFont) { Bold = true }),
                new SpacerElement(8f, ChipH),
            }, Gap: 0.01f, Justify: RowJustify.Center))
        {
            FillColorFn = () => Fade(Fill()), CornerRadius = RoundRadius(ChipH), PaddingX = 0f, PaddingY = 0f,
        };
    }

    // ── icon buttons (28 × 28, r13) ───────────────────────────────────────────────────────────────────────────────
    // For ‹ › ↺ ✕ ❚❚. The glyph is PLAIN text (fixed 14 px menu face): the bold TMP face lacks several symbols ("✕" bold
    // draws blank, "■" / "▾" too — KB §6). ⚠️ "❚❚" (U+275A) is not yet proven to draw in-game; if it is blank, pass a PNG
    // via IconImageButton instead. Raised = Neutral fill (prototype .ib.r); Bare = transparent at rest, soft round wash
    // on hover (.ib); Remove = the house remove-hover (red wash + red glyph, Stellar-Design-Language §3). Disabled =
    // recessed (raised) or a faded glyph (bare), click swallowed.
    private enum IconButtonKind { Raised, Bare, Remove }

    private const float IconButtonSize = 28f;

    private HudElement IconButton(string glyph, Action onClick, IconButtonKind kind = IconButtonKind.Raised,
        Func<bool>? enabled = null, Func<ColorRgba>? surface = null)
    {
        var glyphCache = new ContrastCache();
        var disabled = new DisabledLabelCache();
        var hover = new HoverFlag();
        Func<ColorRgba> under = surface ?? PanelColor;
        bool En() => enabled == null || enabled();
        ColorRgba GlyphColor()
        {
            if (!En()) return DisabledLabel(under(), disabled);
            return kind switch
            {
                IconButtonKind.Raised => glyphCache.Get(TextColor(), NeutralFill(), PillMinContrast),
                IconButtonKind.Remove when hover.On => glyphCache.Get(RemoveGlyph(), StyleMath.Over(RemoveWash(), under()), ChipMinContrast),
                _ => hover.On ? TextColor() : MutedColor(),
            };
        }
        var content = new TextElement(() => glyph, Color: () => GlyphColor(), Width: IconButtonSize,
            Align: TextAlign.Center, NoWrap: true) { CenterGlyphs = true };
        return IconButtonShell(content, onClick, kind, hover, En, under);
    }

    // Same shell around a white glyph PNG (Resources\icons, tinted live) — for a symbol the text faces can't draw.
    private HudElement IconImageButton(string iconName, Action onClick, IconButtonKind kind = IconButtonKind.Raised,
        Func<bool>? enabled = null, Func<ColorRgba>? surface = null)
    {
        var hover = new HoverFlag();
        var disabled = new DisabledLabelCache();
        Func<ColorRgba> under = surface ?? PanelColor;
        bool En() => enabled == null || enabled();
        var image = IconImage(iconName, IconButtonGlyph,
            () => !En() ? DisabledLabel(under(), disabled) : kind == IconButtonKind.Raised || hover.On ? TextColor() : MutedColor());
        return IconButtonShell(new RowElement(new HudElement[] { image }, Gap: 0.01f, Justify: RowJustify.Center),
            onClick, kind, hover, En, under);
    }

    private const int IconButtonGlyph = 16;

    private HudElement IconButtonShell(HudElement content, Action onClick, IconButtonKind kind, HoverFlag hover,
        Func<bool> enabled, Func<ColorRgba> surface)
        => new CellElement(new BubbleElement(new SelectableElement(
            new RowElement(new HudElement[]
            {
                new SpacerElement(0f, IconButtonSize),
                new CellElement(content, Width: IconButtonSize),
            }, Gap: 0.01f, Justify: RowJustify.Center),
            OnClick: () => { if (enabled()) onClick(); })
            { Bare = true, OnHover = over => hover.On = over && enabled() })
        {
            FillColorFn = () => kind switch
            {
                IconButtonKind.Raised => !enabled() ? DisabledShade : Fade(hover.On ? HoverFill(NeutralFill()) : NeutralFill()),
                IconButtonKind.Remove => hover.On ? Fade(RemoveWash()) : Transparent,
                _ => hover.On ? Fade(StyleMath.Over(TextColor() with { A = HoverMix }, surface())) : Transparent,
            },
            CornerRadius = RoundRadius(IconButtonSize), PaddingX = 0f, PaddingY = 0f, MaxWidth = IconButtonSize,
        }, Width: IconButtonSize);

    // The house "Remove hover": the theme Danger red as a 22 % wash, glyph a lighter red (#ff8a8b on the dark presets).
    private ColorRgba RemoveWash()  => DangerColor() with { A = ChipWashAlpha };
    private ColorRgba RemoveGlyph() => StyleMath.Mix(DangerColor(), StyleMath.White, 0.25f);
}
