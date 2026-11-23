using System;
using Stellar.Abstractions.Domain;
using Stellar.Abstractions.Services;
using static Stellar.PhotoStudio.StyleMath;

namespace Stellar.PhotoStudio;

// Segmented pill control (NEW in this kit — the approved prototype's .seg: Resolution 1×/2×/4×, Shape, Format, the
// Off / Composing / Always quality levers, Orbit / Fly, Head / Eyes Default / Lens / Free, the preset source picker):
//   recessed track (sunken fill, r13, 28 px tall, 2 px inset) holding 24 px option pills (r11), 2 px apart.
//   selected = accent fill + bold label (white, or near-black when white < 3:1 on the accent — the solid-fill rule)
//   others   = bold MUTED label (pushed to ≥ 3:1 on the track), hover = a soft Text@0.08 wash in the pill's shape
//   disabled = the track stays sunken, every label recessed, no hover, clicks swallowed; the selected pill becomes a
//              faint muted pill so the current value stays readable (prototype .seg.dis span.on).
// Every option's label box is FIXED (labelWidths, StyleMath.FitBoldLabelWidth helps size them across locales), so
// changing the selection or the language never moves anything (R10). All labels are bold 12, so each option is ONE
// bubble with colour Funcs — no Conditional swap.
public sealed partial class Plugin
{
    private const float SegH = 28f;
    private const float SegOptionH = SegH - 2f * SegTrackInset;   // 24
    private const int SegLabelFont = 12;
    private const float SegDisabledSelectedMix = 0.15f;

    /// <param name="optionKeys">Catalog keys, one per option, in display order.</param>
    /// <param name="labelWidths">The fixed label box of each option (same length as <paramref name="optionKeys"/>).
    /// Size them for the LONGEST translation (house rule 10); the track is <see cref="StyleMath.SegmentTrackWidth"/> wide.</param>
    /// <param name="get">The selected index (anything out of range = nothing selected).</param>
    /// <param name="set">Called with the clicked index (not called for the already-selected option).</param>
    private HudElement Segmented(string[] optionKeys, float[] labelWidths, Func<int> get, Action<int> set,
        Func<bool>? enabled = null)
        => SegmentedText(Array.ConvertAll(optionKeys, Loc), labelWidths, get, set, enabled);

    /// <summary><see cref="Segmented"/> with label Funcs — for labels that are not catalog keys ("1×", "PNG", "9:16").
    /// Each Func must hand back a cached string.</summary>
    private HudElement SegmentedText(Func<string>[] labels, float[] labelWidths, Func<int> get, Action<int> set,
        Func<bool>? enabled = null)
    {
        if (labelWidths.Length != labels.Length)
            throw new ArgumentException("one label width per option", nameof(labelWidths));
        bool En() => enabled == null || enabled();
        var options = new HudElement[labels.Length];
        for (int i = 0; i < options.Length; i++) options[i] = SegmentOption(labels[i], labelWidths[i], i, get, set, En);
        return new BubbleElement(new RowElement(options, Gap: SegGap))
        {
            // Sunken track: translucent black over whatever it sits on (fades with the window opacity by itself).
            FillColorFn = () => DisabledShade,
            CornerRadius = RoundRadius(SegH), PaddingX = SegTrackInset, PaddingY = SegTrackInset,
        };
    }

    private HudElement SegmentOption(Func<string> label, float labelW, int index, Func<int> get, Action<int> set, Func<bool> enabled)
    {
        var hover = new HoverFlag();
        var solid = new SolidLabelCache();
        var muted = new ContrastCache();
        var disabled = new DisabledLabelCache();
        bool Selected() => get() == index;
        // The label contrast is measured on the track as drawn (sunken over the panel), the selected one on the accent.
        ColorRgba Track() => Over(DisabledShade, PanelColor());
        ColorRgba Label() => !enabled() ? DisabledLabel(PanelColor(), disabled)
                           : Selected() ? solid.Get(AsDrawn(AccentColor(), Track()))
                           : muted.Get(MutedColor(), Track(), ChipMinContrast);
        ColorRgba Fill() => !enabled() ? (Selected() ? MutedColor() with { A = SegDisabledSelectedMix * WindowOpacity() } : Transparent)
                          : Selected() ? Fade(AccentColor())
                          : hover.On ? TextColor() with { A = SoftHoverMix * WindowOpacity() }
                          : Transparent;
        return new BubbleElement(new SelectableElement(
            new RowElement(new HudElement[]
            {
                new SpacerElement(SegPadX, SegOptionH),
                DropBold(new TextElement(label, Color: () => Label(), Width: labelW, Align: TextAlign.Center,
                    NoWrap: true, FontSize: SegLabelFont) { Bold = true, Ellipsize = true }),
                new SpacerElement(SegPadX, SegOptionH),
            }, Gap: 0.01f, Justify: RowJustify.Center),
            OnClick: () => { if (enabled() && !Selected()) set(index); })
            { Bare = true, OnHover = over => hover.On = over && enabled() })
        {
            FillColorFn = () => Fill(),
            CornerRadius = RoundRadius(SegOptionH), PaddingX = 0f, PaddingY = 0f, MaxWidth = SegmentPillWidth(labelW),
        };
    }
}
