using System;
using Stellar.Abstractions.Domain;
using Stellar.Abstractions.Services;

namespace Stellar.PhotoStudio;

// House settings rows (the approved prototype's .row — every row ends in a "?" so the "?" column lines up, R17):
//   slider row   [label 110] [slider fills] [value 64, right] [↺] [?]
//   toggle row   [label fills] [On/Off chip] [?]
//   labelled row [label 110] [control] [spacer] [?]
// `help` null = no "?" (a 24 px blank keeps the column aligned). A disabled row greys its label (Muted) and recesses its
// controls; the slider draws its own recessed look when Enabled is false (fw ≥ 2.31, automatic).
// These replace Plugin.Help.cs SliderRow / HelpToggle / LabeledRow in phase 2 (different names until then).
public sealed partial class Plugin
{
    private const float RowLabelW  = 110f;
    private const float RowValueW  = 64f;
    private const float RowGap     = 8f;
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

    /// <summary>[label] [slider] [value] [↺] [?]. With <paramref name="parse"/> the value can be clicked and typed (Enter
    /// applies, clamped to the slider's range) — today's SliderRow precision path (player feedback 2026-10-05: "can't do
    /// precise changes on slider alone"). The slider's own Enabled gates the label colour, the typing and the ↺.
    /// ⚠️ The approved prototype has NO ‹ › fine-step buttons, so this row has none; typing is the precise path.</summary>
    private HudElement SettingSlider(string labelKey, SliderElement slider, Func<string> value, Action reset,
        HelpKeys? help = null, Func<string, float?>? parse = null)
    {
        var enabled = slider.Enabled;
        var fine = new FineStep(slider, 0f, enabled);
        var valueColor = new Func<ColorRgba?>(() => RowLabelColor(enabled));
        HudElement valueCell = parse == null
            ? new TextElement(value, Color: valueColor, Align: TextAlign.Right, NoWrap: true)
            : new ConditionalElement(() => fine.Editing,
                new InputElement(value, text => { fine.Editing = false; if (parse(text) is { } v) fine.SetTo(v); }, Width: RowValueW),
                new SelectableElement(new TextElement(value, Color: valueColor, Align: TextAlign.Right, NoWrap: true),
                    OnClick: () => fine.Editing = fine.Can()) { Bare = true });
        return new RowElement(new HudElement[]
        {
            RowLabel(labelKey, enabled, RowLabelW),
            new CellElement(slider with { SquareHandle = true, HandleSize = SliderKnob }, Weight: 1f),
            new CellElement(valueCell, Width: RowValueW),
            IconButton("↺", reset, IconButtonKind.Raised, enabled),
            HelpOrBlank(help),
        }, Gap: RowGap);
    }

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
    /// regular TMP face, which honours the 12 px size (plain text is a fixed 14 on Menu windows).</summary>
    private HudElement NoteLine(Func<string> text)
        => new TextElement(text, Color: () => MutedColor(), FontSize: 12) { RichEmoji = true };
}
