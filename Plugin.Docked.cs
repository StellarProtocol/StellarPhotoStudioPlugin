using System;
using System.Collections.Generic;
using Stellar.Abstractions.Domain;
using Stellar.Abstractions.Services;

namespace Stellar.PhotoStudio;

// The compact strip shown beside the game's own photo / selfie mode. Measured from the game's photo UI at
// 1920×1080, the only free band is y 944–1057, so the strip is 1100×96, bottom-anchored 30 px up (screen
// x 410–1510, y 954–1050) — clear of the pose panel, the key-hint strip and the [G] icon. Moves only in layout
// edit mode: with no title bar, free-drag made the whole strip a drag handle and swallowed slider drags (owner report).
public sealed partial class Plugin
{
    private const float MaxAperture = 22f;
    private const float MinAperture = 1f;

    private IWindowControl RegisterDockedWindow() => _services.Windows.Register(new WindowRegistration(
        new WindowSpec(
            Id: "photostudio.docked",
            Title: T("ps.title"),
            DefaultRect: new WindowRect(0f, -30f, 1100f, 96f),
            Category: WindowCategory.Tools,
            Style: WindowPanelStyle.GlassMenu)
        { ShowTitleBar = false, Draggable = true, EditModeDragOnly = true, StartVisible = false, Anchor = WindowAnchor.Bottom, ShouldRender = InWorld },
        new ColumnElement(new HudElement[] { DockedTopRow(), DockedSliderRow() }, Gap: 8f)));

    private HudElement DockedTopRow() => new RowElement(new HudElement[]
    {
        new TextElement(() => T("ps.title"), Emphasis: true, Width: 104f),
        new TextElement(() => T("ps.docked.preset"), Color: Muted, Width: 48f),
        new DropdownElement(() => IndexOfPreset(_activePresetName), PresetNameOptions, i => ApplyPreset(_presets.All[i]), Width: 220f),
        new ButtonElement(() => "◀", OnClick: PrevPreset, Width: 28f),
        new ButtonElement(() => "▶", OnClick: NextPreset, Width: 28f),
        new ConditionalElement(() => _modified, new PillElement(() => T("ps.pill.modified"), Color: () => _services.Theme.Colors.Accent)),
        new SpacerElement(),
        // "Other players" here = all four player groups on the Capture tab (1.7.0; HideLayers.ToRequest makes that the
        // game's no-other-player switch), so the strip and the full panel always agree.
        new ConditionalElement(() => LayerAvailable(VisibilityLayerSets.PlayerGroups), LabeledToggle(() => T("ps.hide.others"),
            () => IsHidden(VisibilityLayerSets.PlayerGroups), on => SetHidden(VisibilityLayerSets.PlayerGroups, on))),
        new SpacerElement(Width: 12f),   // separate the two [switch][label] pairs so each label reads as its own
        LabeledToggle(() => T("ps.docked.keepLook"), () => _settings.Pinned, SetPinned),
        new SpacerElement(Width: 12f),
        new ButtonElement(() => T("ps.docked.fullPanel"), OnClick: DockedToFullPanel, Width: 96f),
        new ButtonElement(() => "✕", OnClick: DismissDocked, Width: 28f),
    }, Gap: 6f);

    // Row B shows the sliders, or — for the toast's few seconds after a capture from the strip — the saved line in
    // their place: the row is ~1034 of 1100 px wide already, so the line can't sit beside them, and a toast window
    // above would cover row A.
    private HudElement DockedSliderRow() => new ConditionalElement(() => _toastLeft > 0f && _dockedShown,
        DockedSavedRow(), DockedMinisRow());

    private HudElement DockedMinisRow() => new RowElement(new HudElement[]
    {
        Mini(() => T("ps.look.exposure"),
            new SliderElement(() => _editor.Color.PostExposure, v => _editor.EditColor(c => c with { PostExposure = v }), -3f, 3f,
                Enabled: () => Supported(LookGroups.Color)),
            () => F(_editor.Color.PostExposure, "+0.0;-0.0;0.0")),
        Mini(() => T("ps.docked.warmth"),
            new SliderElement(() => _editor.WhiteBalance.Temperature, v => _editor.EditWhiteBalance(w => w with { Temperature = v }), -100f, 100f,
                Enabled: () => Supported(LookGroups.WhiteBalance)),
            () => F(_editor.WhiteBalance.Temperature, Signed)),
        Mini(() => T("ps.docked.blur"),
            new SliderElement(BlurAmount, SetBlurAmount, 0f, 1f, Enabled: () => Supported(LookGroups.Dof)),
            () => _editor.IsOn(LookGroups.Dof) ? "f/" + F(_editor.Dof.Aperture, "0.0") : T("ps.docked.blurOff"), labelWidth: 112f),
        new SpacerElement(),
        DockedCaptureButton(),
    }, Gap: 12f);

    private HudElement DockedSavedRow() => new RowElement(new HudElement[]
    {
        new TextElement(() => _loc.TFormat("ps.docked.saved", _toastFile), NoWrap: true),
        new TextElement(() => _toastDetail, Color: Muted, NoWrap: true),
        new ConditionalElement(() => _toastWarning.Length > 0,
            new TextElement(() => _toastWarning, Color: () => _services.Theme.Colors.Warning, NoWrap: true)),
        new SpacerElement(),
        new ButtonElement(() => T("ps.cap.openFolder"), OnClick: () => OpenFolderSafe(_toastDir)),
        DockedCaptureButton(),
    }, Gap: 12f);

    private HudElement DockedCaptureButton() => new ButtonElement(() => _loc.TFormat("ps.docked.capture", BindingText("photostudio.capture")),
        OnClick: CaptureNow, Enabled: () => !Capturing, Style: MenuButtonStyle.Filled, Width: 170f);

    private HudElement Mini(Func<string> label, SliderElement slider, Func<string> value, float labelWidth = 76f) => new RowElement(new HudElement[]
    {
        new TextElement(label, Color: Muted, Width: labelWidth, NoWrap: true),
        new CellElement(slider with { Width = 120f, SquareHandle = true }, Width: 120f),
        new TextElement(value, Width: 48f, Align: TextAlign.Right),
    }, Gap: 6f);

    private static HudElement LabeledToggle(Func<string> label, Func<bool> get, Action<bool> set) => new RowElement(new HudElement[]
    {
        new ToggleElement(() => "", get, set),
        new TextElement(label, NoWrap: true),
    }, Gap: 6f);

    // "Background blur" = depth of field focused on the player; more blur = a wider aperture (lower f-number).
    private float BlurAmount() => _editor.IsOn(LookGroups.Dof)
        ? (MaxAperture - _editor.Dof.Aperture) / (MaxAperture - MinAperture)
        : 0f;

    private void SetBlurAmount(float t)
    {
        var aperture = MaxAperture - Math.Clamp(t, 0f, 1f) * (MaxAperture - MinAperture);
        _editor.EditDof(d => d with { Aperture = aperture, FocusOnLocalPlayer = true });
    }

    // Built once per preset-list change: the dropdown polls Options() every refresh.
    private List<string>? _presetNamesCache;

    private IReadOnlyList<string> PresetNameOptions()
    {
        if (_presetNamesCache is not null) return _presetNamesCache;
        var all = _presets.All;
        var names = new List<string>(all.Count);
        foreach (var p in all) names.Add(p.Name);
        return _presetNamesCache = names;
    }

    private void PrevPreset()
    {
        var all = _presets.All;
        if (all.Count == 0) return;
        var i = IndexOfPreset(_activePresetName);
        ApplyPreset(all[(i - 1 + all.Count) % all.Count]);
    }
}
