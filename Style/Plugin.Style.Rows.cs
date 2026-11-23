using System;
using Stellar.Abstractions.Domain;
using Stellar.Abstractions.Services;

namespace Stellar.PhotoStudio;

// House settings rows (the approved prototype's .row — every row ends in a "?" so the "?" column lines up, R17):
//   slider row   [label 110] [slider fills] [‹ value ›] [↺] [?]
//   toggle row   [label fills] [On/Off chip] [?]
//   labelled row [label 110] [control] [spacer] [?]
// `help` null = no "?" (a 24 px blank keeps the column aligned). A disabled row greys its label (Muted) and recesses its
// controls; the slider draws its own recessed look when Enabled is false (fw ≥ 2.31, automatic).
public sealed partial class Plugin
{
    private const float RowLabelW  = 110f;
    private const float RowValueW  = 60f;
    private const float RowGap     = 8f;
    private const float StepGap    = 2f;
    private const float SliderKnob = 14f;

    // The house slider: a knob exactly 14 px square (the default handle stretches to the row height = a capsule); Width 0
    // = the track flexes to fill its cell (the prototype's flex:1 track).
    private static SliderElement KitSlider(Func<float> get, Action<float> set, float min, float max, Func<bool>? enabled = null)
        => new(get, set, min, max, enabled) { SquareHandle = true, HandleSize = SliderKnob };

    private ColorRgba RowLabelColor(Func<bool>? enabled) => enabled == null || enabled() ? TextColor() : MutedColor();

    private HudElement RowLabel(string labelKey, Func<bool>? enabled, float width)
        => new CellElement(new TextElement(Loc(labelKey), Color: () => RowLabelColor(enabled), NoWrap: true) { Ellipsize = true },
            Width: width);

    private HudElement HelpOrBlank(HelpKeys? help) => help == null ? new SpacerElement(HelpDotSize, HelpDotSize) : HelpButton(help);

    /// <summary>One slider row's options beyond the label / slider / value / reset (a record keeps the call sites short).
    /// <see cref="Step"/> = the ‹ › fine step (0 = 1/100 of the range); <see cref="Parse"/> lets the value be clicked
    /// and typed (Enter applies, clamped) — player feedback 2026-10-05: "can't do precise changes on slider alone".</summary>
    private sealed record SliderOpts(HelpKeys? Help = null, float Step = 0f, Func<string, float?>? Parse = null);

    /// <summary>[label] [slider] [‹ value ›] [↺] [?]. The slider's own Enabled gates the label colour, the steps, the
    /// typing and the ↺ (a disabled slider must not be resettable — sandbox D1).</summary>
    private HudElement SettingSlider(string labelKey, SliderElement slider, Func<string> value, Action reset,
        SliderOpts? opts = null)
    {
        var enabled = slider.Enabled;
        var o = opts ?? NoSliderOpts;
        var fine = new FineStep(slider, o.Step > 0f ? o.Step : (slider.Max - slider.Min) / 100f, enabled);
        var valueColor = new Func<ColorRgba?>(() => RowLabelColor(enabled));
        HudElement valueCell = o.Parse is not { } parse
            ? new TextElement(value, Color: valueColor, Align: TextAlign.Center, NoWrap: true) { Ellipsize = true }
            : new ConditionalElement(() => fine.Editing,
                new InputElement(value, text => { fine.Editing = false; if (parse(text) is { } v) fine.SetTo(v); }, Width: RowValueW),
                new SelectableElement(new TextElement(value, Color: valueColor, Align: TextAlign.Center, NoWrap: true) { Ellipsize = true },
                    OnClick: () => fine.Editing = fine.Can()) { Bare = true });
        return new RowElement(new HudElement[]
        {
            RowLabel(labelKey, enabled, RowLabelW),
            new CellElement(slider with { SquareHandle = true, HandleSize = SliderKnob }, Weight: 1f),
            new RowElement(new HudElement[]
            {
                StepButton("‹", () => fine.Nudge(-1), fine.Can),
                new CellElement(valueCell, Width: RowValueW),
                StepButton("›", () => fine.Nudge(1), fine.Can),
            }, Gap: StepGap),
            IconImageButton("reset", reset, IconButtonKind.Raised, enabled),
            HelpOrBlank(o.Help),
        }, Gap: RowGap);
    }

    private static readonly SliderOpts NoSliderOpts = new();

    /// <summary>[label fills] [On/Off chip] [?]. `style` null = the 64 px On/Off chip.</summary>
    private HudElement SettingToggle(string labelKey, Func<bool> get, Action<bool> set, HelpKeys? help = null,
        Func<bool>? enabled = null, ChipStyle? style = null)
        => new RowElement(new HudElement[]
        {
            new SpacerElement(0f, PillH),   // the row is a control row (28) even though the chip is 24 (prototype min-height)
            new CellElement(new TextElement(Loc(labelKey), Color: () => RowLabelColor(enabled), NoWrap: true) { Ellipsize = true },
                Weight: 1f),
            Chip(get, set, style ?? ChipStyle.OnOff, enabled),
            HelpOrBlank(help),
        }, Gap: RowGap);

    /// <summary>[label 110] [control] [spacer] [?] — e.g. a Segmented, a pill dropdown or a colour swatch row.</summary>
    private HudElement SettingRow(string labelKey, HudElement control, HelpKeys? help = null, Func<bool>? enabled = null)
        => new RowElement(new HudElement[]
        {
            new SpacerElement(0f, PillH),
            RowLabel(labelKey, enabled, RowLabelW),
            control,
            new CellElement(new SpacerElement(0f, 1f), Weight: 1f),   // a weighted filler pins the "?" to the right edge
            HelpOrBlank(help),
        }, Gap: RowGap);

    /// <summary>A muted note line inside a panel (prototype .mut: "Greyed settings only apply to JPG."). RichEmoji = the
    /// regular TMP face, which honours the 12 px size (plain text is a fixed 14 on Menu windows). Wraps.</summary>
    private HudElement NoteLine(Func<string> text)
        => new TextElement(text, Color: () => MutedColor(), FontSize: 12) { RichEmoji = true };

    /// <summary>A note in a live colour (a warning in theme Caution, an "on" status in the accent).</summary>
    private static HudElement NoteLine(Func<string> text, Func<ColorRgba> color)
        => new TextElement(text, Color: () => color(), FontSize: 12) { RichEmoji = true };

    /// <summary>Plain body text (13–14 px menu face) that wraps — the not-installed ReShade line, the folder hint.</summary>
    private HudElement BodyText(Func<string> text) => new TextElement(text, Color: () => TextColor());
}
