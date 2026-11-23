using System;
using System.Collections.Generic;
using System.IO;
using Stellar.Abstractions.Domain;
using Stellar.Abstractions.Services;

namespace Stellar.PhotoStudio;

// Pre-drawn icon art (house icon rule: one pixel-hinted PNG PER SIZE, never a master scaled down — Stellar-Design-
// Language §3). Resources\icons\<size>\<name>.png is embedded by glob as "Stellar.PhotoStudio.icons.<size>.<name>.png"
// (csproj); the first lookup indexes every embedded size once. A call site asks for `units` canvas units and gets the
// drawing for units × the live canvas→screen scale (1.0 at 1440p, 0.75 at 1080p, 1.5 at 4K): exact else the next
// LARGER size else the largest (StyleMath.PickIconSize = the framework IconSizePicker). A missing icon (art not drawn
// yet) returns null and the image simply draws nothing — the layout keeps its slot.
public sealed partial class Plugin
{
    private const string IconResourcePrefix = "Stellar.PhotoStudio.icons.";

    private static Dictionary<string, int[]>? _iconIndex;
    private static readonly Dictionary<string, byte[]?> _iconPngCache = new(StringComparer.Ordinal);

    private static int[] IconSizesOf(string name)
    {
        _iconIndex ??= StyleMath.IndexIcons(typeof(Plugin).Assembly.GetManifestResourceNames(), IconResourcePrefix);
        return _iconIndex.TryGetValue(name, out var sizes) ? sizes : Array.Empty<int>();
    }

    // Read once per resource and kept (ImageElement Funcs are polled; ImageElement.Live compares by REFERENCE, so the
    // same array must come back every time). A missing / unreadable resource caches null instead of re-reading.
    private static byte[]? EmbeddedPng(string resource)
    {
        if (_iconPngCache.TryGetValue(resource, out var png)) return png;
        try
        {
            using var s = typeof(Plugin).Assembly.GetManifestResourceStream(resource);
            if (s != null) { using var ms = new MemoryStream(); s.CopyTo(ms); png = ms.ToArray(); }
        }
        catch { png = null; }
        _iconPngCache[resource] = png;
        return png;
    }

    private float CanvasToScreen()
    {
        var fw = _services.Framework;
        return fw.CanvasWidth > 1 && fw.ScreenWidth > 0 ? (float)fw.ScreenWidth / fw.CanvasWidth : 1f;
    }

    /// <summary>One call site's icon choice: re-picks only when the UI scale changes, so the per-refresh Func is two
    /// property reads + a float compare and hands back the same array.</summary>
    private sealed class IconPick
    {
        private readonly Plugin _owner;
        private readonly string _name;
        private readonly float _units;
        private float _scale = -1f;
        private byte[]? _png;

        public IconPick(Plugin owner, string name, float units) { _owner = owner; _name = name; _units = units; }

        public byte[]? Get()
        {
            float scale = _owner.CanvasToScreen();
            if (scale == _scale) return _png;
            _scale = scale;
            int px = StyleMath.PickIconSize(IconSizesOf(_name), _units * scale);
            _png = px > 0 ? EmbeddedPng(IconResourcePrefix + px + "." + _name + ".png") : null;
            return _png;
        }
    }

    // A white glyph PNG drawn `units` square in a live tint (ImageElement.Tint, fw 2.30). Live: a resolution / UI-scale
    // change swaps in the matching pre-drawn size. Crisp (fw 2.34): the picked size is normally EXACT (the art ships every
    // size the UI draws), so it draws 1:1 with no mip smoothing. A fixed Cell pins the slot (ImageElement only
    // sets a PREFERRED size, so a crowded row would squeeze it; the Cell is exactly the image wide, so its force-expand is harmless — WindowBuilder-Patterns "ImageElement icon gotchas").
    private HudElement IconImage(string name, int units, Func<ColorRgba>? tint = null)
    {
        var pick = new IconPick(this, name, units);
        var image = new ImageElement(pick.Get, units, units)
        {
            Live = true, Crisp = true,
            Tint = tint == null ? null : () => tint(),
        };
        return new CellElement(image, Width: units);
    }

    // ── accent icon tile ──────────────────────────────────────────────────────────────────────────────────────────
    // A rounded tile in the LIVE accent with the flat white glyph centred (art can't follow the theme, so the colour
    // lives in the tile; Maestro option A, 2026-11-06). 40 px tile → 24 px glyph (radius 9 ≈ 22 %); the help popover's
    // 32 px tile → radius 7. The fixed Cell pins the column (Cell / Selectable force-expand a child's width — the
    // icon-squish gotcha); the centred Row keeps the image square. Header art ships 16/18/24/32/36/48/64/128.
    private const float HeaderTileSize = 40f;
    private const int HeaderGlyph = 24;

    private HudElement IconTile(string iconName, float size, int glyph)
    {
        float pad = (size - glyph) / 2f;
        return new CellElement(new BubbleElement(
            new RowElement(new HudElement[] { IconImage(iconName, glyph) }, Gap: 0.01f, Justify: RowJustify.Center))
        {
            FillColorFn = () => Fade(AccentColor()), CornerRadius = MathF.Round(size * 0.22f), PaddingX = pad, PaddingY = pad,
        }, Width: size);
    }

    // ── rail item (vertical section navigation) ───────────────────────────────────────────────────────────────────
    // The approved prototype's .ri: a 56 × 50 cell with a 20-unit glyph (white PNG tinted Muted when inactive, Text when
    // active) over a bold 11 label; active = accent @24 % wash, r6; hover = a soft Text wash in the same shape. The label
    // box is the full 56 px and ellipsizes, so a long translation never widens the rail (R10). Rail art ships
    // 12/15/16/20/24/30/32/40/64/128 (20 u at 0.6 / 0.75 / 0.8 / 1 / 1.2 / 1.5 / 1.6 / 2 scale = exact).
    private const float RailItemW = 56f;
    private const float RailItemH = 50f;
    private const int RailGlyph = 20;
    private const int RailLabelFont = 11;

    private HudElement RailItem(string iconName, string labelKey, Func<bool> active, Action onClick)
    {
        var hover = new HoverFlag();
        var label = new ContrastCache();
        ColorRgba Fill() => active() ? StyleMath.Over(AccentColor() with { A = RailActiveAlpha }, PanelColor())
                          : StyleMath.Over(TextColor() with { A = SoftHoverMix }, PanelColor());
        ColorRgba Ink() => active() ? label.Get(TextColor(), Fill(), PillMinContrast) : MutedColor();
        return new CellElement(new BubbleElement(new SelectableElement(
            new RowElement(new HudElement[]
            {
                new SpacerElement(0f, RailItemH),
                new ColumnElement(new HudElement[]
                {
                    new RowElement(new HudElement[] { IconImage(iconName, RailGlyph, Ink) }, Gap: 0.01f, Justify: RowJustify.Center),
                    new TextElement(Loc(labelKey), Color: () => Ink(), Width: RailItemW, Align: TextAlign.Center,
                        NoWrap: true, FontSize: RailLabelFont) { Bold = true, Ellipsize = true },
                }, Gap: 3f),
            }, Gap: 0.01f, Justify: RowJustify.Center),
            OnClick: onClick)
            { Bare = true, OnHover = over => hover.On = over })
        {
            FillColorFn = () => active() || hover.On ? Fade(Fill()) : Transparent,
            CornerRadius = TileRadius, PaddingX = 0f, PaddingY = 0f, MaxWidth = RailItemW,
        }, Width: RailItemW);
    }
}
