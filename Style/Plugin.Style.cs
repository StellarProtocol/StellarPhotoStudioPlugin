using System;
using Stellar.Abstractions.Domain;
using Stellar.Abstractions.Services;
using static Stellar.PhotoStudio.StyleMath;

namespace Stellar.PhotoStudio;

// House style (Stellar Design Language, Knowledge Base\Stellar-Design-Language.md): the palette, caches and shared shape
// rules every house-kit control in Style\ builds from. Ported from StellarMaestroPlugin Plugin.Style*.cs (the reference
// kit) via StellarAutoFishingPlugin (the localized port: catalog keys, uniform window-opacity fade) and
// StellarRaidManagerPlugin (the round-2 recessed label). Needs SDK ≥ 2.30 (theme status colours) — built on 2.34.
// The pure maths (Over / Mix / contrast / icon-size pick / segment widths) is StyleMath.cs, unit-tested.
//
// Toolkit limits behind some choices (WindowBuilder-Patterns.md):
//   • Plain TextElement ignores FontSize on Menu windows (fixed 14 px) — only the styled path (Bold/Emphasis) or
//     RichEmoji (the regular TMP face) honours it, so captions / pill labels are Bold+FontSize and 12 px hints RichEmoji.
//   • Row/Column Gap 0 means the DEFAULT gap (8/12), never zero — every container here passes an explicit Gap (0.01 = none).
//   • A Bubble hugs its child's height, so every round control pins its height with a 0-width SpacerElement strut.
//   • A FillColorFn returning null = the build-time accent blob, NOT transparent → "no fill" is an explicit Transparent.
//   • Bold can't toggle live. Controls whose states share one weight (chips, segments) are ONE bubble with colour Funcs.
public sealed partial class Plugin
{
    // ── palette: LIVE from the current framework theme ────────────────────────────────────────────────────────────
    // Derived from _services.Theme.Colors on every call and handed to elements as Funcs, so a theme switch repaints on
    // the next refresh. Status colours come from the theme too (IThemeStatusColors, fw 2.30), not hard-coded hex.
    private static readonly ColorRgba PanelShade = new(0f, 0f, 0f, 0.20f);   // panel = background darkened
    private const float TileTextMix      = 0.08f;   // raised tile = panel + 8 % MenuText …
    private const float TileAccentMix    = 0.06f;   // … + a 6 % accent tint (teal on teal, pink on pink; user-approved 11-06)
    private const float CardTextMix      = 0.06f;   // card on the WINDOW bg = bg + 6 % text …
    private const float CardAccentMix    = 0.08f;   // … + 8 % accent (the panel-based tile merged with the window, R6)
    private const float NeutralMix       = 0.22f;   // neutral pill = panel + 22 % MenuMuted …
    private const float NeutralAccentMix = 0.08f;   // … + an 8 % accent tint, like the tiles
    private const float PillMinContrast  = 4.5f;    // tinted fills (Neutral, Caution) keep 4.5:1 labels (R7)
    private const float ChipMinContrast  = 3.0f;    // chip / unselected-segment labels: pressable ≥ 3:1 (rule 22)
    private const float ChipWashAlpha    = 0.22f;   // coloured chip = bold coloured text on a 22 % wash of its colour
    private const float AccentWashAlpha  = 0.28f;   // accent chip ("Hidden") / selected list tile (prototype #5FB8C447)
    private const float RailActiveAlpha  = 0.24f;   // the active rail cell (prototype #5FB8C43d)
    private const float DangerFillAlpha  = 0.85f;   // solid red Danger pill
    private const float HoverMix         = 0.10f;   // hover = 10 % MenuText over the fill
    private const float SoftHoverMix     = 0.08f;   // hover over a transparent / recessed surface (segments, rail)
    private static readonly ColorRgba Transparent = new(0f, 0f, 0f, 0f);

    private IThemeColors Menu => _services.Theme.Colors;

    private ColorRgba TextColor()    => Menu.MenuText;
    private ColorRgba MutedColor()   => Menu.MenuMuted;
    private ColorRgba AccentColor()  => Menu.MenuAccent;
    private ColorRgba SuccessColor() => Menu.Success;
    private ColorRgba CautionColor() => Menu.Caution;
    private ColorRgba DangerColor()  => Menu.Danger;
    private ColorRgba WindowBg()     => Menu.MenuBackground;
    private ColorRgba PanelColor()   => Over(PanelShade, Menu.MenuBackground);
    private ColorRgba TileColor()    => Over(Menu.MenuAccent with { A = TileAccentMix },
                                             Over(Menu.MenuText with { A = TileTextMix }, PanelColor()));
    private ColorRgba CardColor()    => Over(Menu.MenuAccent with { A = CardAccentMix },
                                             Over(Menu.MenuText with { A = CardTextMix }, Menu.MenuBackground));
    private ColorRgba NeutralFill()  => Over(Menu.MenuAccent with { A = NeutralAccentMix },
                                             Over(Menu.MenuMuted with { A = NeutralMix }, PanelColor()));
    private ColorRgba PrimaryFill()  => Menu.MenuAccent;
    private ColorRgba DangerFill()   => Over(Menu.Danger with { A = DangerFillAlpha }, PanelColor());
    // Caution = a TINTED amber pill (the amber chip wash over the panel): it reads one step below the solid red Danger
    // (severity ladder R8 — e.g. "Exit free camera" stops only my own part).
    private ColorRgba CautionFill()  => Over(Menu.Caution with { A = ChipWashAlpha }, PanelColor());
    private ColorRgba HoverFill(ColorRgba fill) => Over(Menu.MenuText with { A = HoverMix }, fill);

    // ── window opacity: EVERY coloured fill follows the window opacity, uniformly (house rule 2026-11-07) ──────────
    // The framework fades ONLY the GlassMenu frame Image; every bubble fill here is an opaque pre-blended colour, so at a
    // low opacity the panels and pills would stay solid on a see-through frame. Every fill goes through Fade with the
    // frame's factor; only text and pictures stay solid. Composed OPAQUE first, alpha last. A cast + a read per poll.
    private float WindowOpacity()
    {
        float? v;
        try { v = (_services.NamedTheme as IChromeStyle)?.WindowOpacity; } catch { v = null; }
        return v is { } f ? Math.Clamp(f, 0.3f, 1f) : 1f;
    }

    private ColorRgba Fade(ColorRgba c) => c with { A = c.A * WindowOpacity() };

    // An opaque fill as it is actually DRAWN at the current window opacity (faded over its surface) — what a solid-fill
    // label's 3:1 check must be measured against (fw 8df7f29: "against the fill AS DRAWN, window opacity included").
    private ColorRgba AsDrawn(ColorRgba fill, ColorRgba surface)
    {
        float k = WindowOpacity();
        return k >= 1f ? fill : Over(fill with { A = k }, surface);
    }

    // ── recessed (disabled) look — house rule 22 ──────────────────────────────────────────────────────────────────
    // A disabled control is SUNKEN: black @38 % drawn TRANSLUCENT over whatever it sits on (the renderer blends it, so it
    // already fades with the window opacity), a strongly faded label (Muted @45 %, held between 1.3:1 and 2.2:1 on the
    // sunken fill — every pressable label is ≥ 3:1, so a recessed one always reads dimmer), no ring, same size and radius.
    // RaidManager round-2 recipe (in-game 2026-11-12); the older Text@32 % → 2:1 walk read like an unselected chip.
    // The segmented track is the same sunken fill (the prototype's --rec), so a disabled segment = no hover + faded labels.
    private static readonly ColorRgba DisabledShade = new(0f, 0f, 0f, 0.38f);
    private const float DisabledLabelAlpha = 0.45f;
    private const float DisabledLabelMinContrast = 1.3f;
    private const float DisabledLabelMaxContrast = 2.2f;

    // Per-call-site cache: the label only changes with the theme / surface, so the walk runs once per colour change.
    private sealed class DisabledLabelCache { public ColorRgba Muted, Surface, Label; public bool Valid; }

    private ColorRgba DisabledLabel(ColorRgba surface, DisabledLabelCache cache)
    {
        var muted = Menu.MenuMuted;
        if (cache.Valid && muted.Equals(cache.Muted) && surface.Equals(cache.Surface)) return cache.Label;
        float a = RecessedLabelAlpha(muted, Over(DisabledShade, surface), DisabledLabelAlpha,
            DisabledLabelMinContrast, DisabledLabelMaxContrast);
        cache.Muted = muted; cache.Surface = surface; cache.Label = muted with { A = a }; cache.Valid = true;
        return cache.Label;
    }

    // ── contrast caches ───────────────────────────────────────────────────────────────────────────────────────────
    // EnsureContrast walks up to 12 steps of pow() maths; labels poll it every refresh with inputs that only change with
    // the theme, so each call site keeps its last answer.
    private sealed class ContrastCache
    {
        private ColorRgba _fg, _bg, _out;
        private bool _valid;

        public ColorRgba Get(ColorRgba fg, ColorRgba bg, float ratio)
        {
            if (_valid && fg.Equals(_fg) && bg.Equals(_bg)) return _out;
            _fg = fg; _bg = bg; _out = EnsureContrast(fg, bg, ratio); _valid = true;
            return _out;
        }
    }

    private sealed class SolidLabelCache
    {
        private ColorRgba _fill, _out;
        private bool _valid;

        public ColorRgba Get(ColorRgba fill)
        {
            if (_valid && fill.Equals(_fill)) return _out;
            _fill = fill; _out = SolidLabel(fill); _valid = true;
            return _out;
        }
    }

    // ── localized text ────────────────────────────────────────────────────────────────────────────────────────────
    // Kit controls take catalog KEYS (Rule 10). A plain label is `() => _loc.T(key)`: T hands back the catalog's own
    // string instance (LocalizationEngine dictionary), so a poll allocates nothing and a language switch shows on the next
    // refresh. Text DERIVED from catalog text (UPPERCASE captions, "SECTION · HELP") is rebuilt only when a source string
    // changes INSTANCE (= a language switch), so no LanguageChanged plumbing is needed and a poll still allocates nothing.
    private Func<string> Loc(string key) => () => _loc.T(key);

    private sealed class UpperText
    {
        private string? _src;
        private string _out = string.Empty;

        public string Get(string src)
        {
            if (!ReferenceEquals(src, _src)) { _src = src; _out = src.ToUpperInvariant(); }
            return _out;
        }
    }

    private Func<string> UpperLoc(string key)
    {
        var cache = new UpperText();
        return () => cache.Get(_loc.T(key));
    }

    // ── shape ─────────────────────────────────────────────────────────────────────────────────────────────────────
    // The corner radius for a ROUND (capsule / circle) bubble of side `side`: side / 2 − 1, NOT side / 2 (R1). The
    // bubble's 9-slice background reports a MIN size of its summed sprite borders (radius + 1 each side), so side/2 drew
    // every round control 2 px taller than its content (Maestro, measured in-game 2026-11-06).
    private static float RoundRadius(float side) => side / 2f - 1f;
    private const float TileRadius = 6f;   // panels, tiles, rail cells (R3)

    // Bold (TMP) labels sit ~1.5 px high in a centred line box; a 2×drop spacer above moves the cap band to the middle
    // (Maestro BoldLabelDrop, measured in-game 2026-11-06, R9).
    private const float BoldLabelDrop = 1f;

    private static HudElement DropBold(HudElement label) => new ColumnElement(new HudElement[]
    {
        new SpacerElement(0f, 2f * BoldLabelDrop),
        label,
    }, Gap: 0.01f);

    // Hover that follows the shape (R2): a Bare selectable (no square wash of its own) flips this flag and the
    // surrounding round Bubble's FillColorFn shows the hover. No pressed tint: a Bare selectable has no pointer-down.
    private sealed class HoverFlag { public bool On; }
}
