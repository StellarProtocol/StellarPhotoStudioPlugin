using System;
using System.Collections.Generic;
using Stellar.Abstractions.Domain;
using Stellar.Abstractions.Services;

namespace Stellar.PhotoStudio;

// The compact strip shown beside the game's own photo / selfie mode. Measured from the game's photo UI at
// 1920×1080, the only free band is y 944–1057, so the strip is 1100×96, bottom-anchored 30 px up (screen
// x 410–1510, y 954–1050) — clear of the pose panel, the key-hint strip and the [G] icon. Moves only in layout
// edit mode: with no title bar, free-drag made the whole strip a drag handle and swallowed slider drags (owner report).
// 2.0.0 house style (approved prototype § 4): row A = [capture tile] Photo Studio ‹ [preset ▾] › [Modified] … Others
// [Shown/Hidden] Keep look [On/Off] [Full panel] [✕]; row B = Exposure / Warmth / Blur + [Capture (F10)]. Just after a
// capture row A shows the saved line instead (Saved · file · size · Open folder), so the sliders never move. The close
// confirm takes row B in place (a modal card doesn't fit 96 px).
public sealed partial class Plugin
{
    private const float MaxAperture = 22f;
    private const float MinAperture = 1f;
    private const float StripTile = 28f;
    private const int StripGlyph = 16;
    private const float StripSliderW = 120f;
    private static readonly ChipStyle OthersChip = new("ps.kit.hidden", "ps.kit.shown", 64f, ChipTone.Accent);

    private IWindowControl RegisterDockedWindow() => _services.Windows.Register(new WindowRegistration(
        new WindowSpec(
            Id: "photostudio.docked",
            Title: T("ps.title"),
            DefaultRect: new WindowRect(0f, -30f, 1100f, 96f),
            Category: WindowCategory.Tools,
            Style: WindowPanelStyle.GlassMenu)
        { ShowTitleBar = false, Draggable = true, EditModeDragOnly = true, StartVisible = false, Anchor = WindowAnchor.Bottom, ShouldRender = InWorld },
        new ColumnElement(new HudElement[]
        {
            new ConditionalElement(() => _toastLeft > 0f && _dockedShown, DockedSavedRow(), DockedTopRow()),
            new ConditionalElement(() => _closeConfirm, DockedCloseConfirm(), DockedMinisRow()),   // Plugin.Close.cs
        }, Gap: 8f)));

    private HudElement StripTitle() => new RowElement(new HudElement[]
    {
        IconTile("header", StripTile, StripGlyph),
        new TextElement(Loc("ps.title"), Color: () => TextColor(), NoWrap: true, FontSize: 13) { Bold = true },
    }, Gap: 8f);

    private HudElement DockedTopRow() => new RowElement(new HudElement[]
    {
        StripTitle(),
        IconButton("‹", PrevPreset, surface: WindowBg),
        new CellElement(PillDropdown(() => IndexOfPreset(_activePresetName), PresetNameOptions, i => ApplyPreset(_presets.All[i])), Width: 170f),
        IconButton("›", NextPreset, surface: WindowBg),
        new ConditionalElement(() => _modified, TagChip("ps.pill.modified", AccentColor, WindowBg)),
        new CellElement(new SpacerElement(0f, 1f), Weight: 1f),
        // "Others" = all four player groups of the Hide section (HideLayers.ToRequest makes that the game's no-other-player
        // switch), so the strip and the full panel always agree.
        new ConditionalElement(() => LayerAvailable(VisibilityLayerSets.PlayerGroups), new RowElement(new HudElement[]
        {
            StripLabel("ps.docked.others"),
            Chip(() => IsHidden(VisibilityLayerSets.PlayerGroups), on => SetHidden(VisibilityLayerSets.PlayerGroups, on), OthersChip,
                surface: WindowBg),
        }, Gap: 6f)),
        StripLabel("ps.docked.keepLook"),
        Chip(() => _settings.Pinned, SetPinned, ChipStyle.OnOff, surface: WindowBg),
        Pill(PillKind.Neutral, "ps.docked.fullPanel", DockedToFullPanel, 72f, surface: WindowBg),
        IconButton("✕", RequestClose, IconButtonKind.Bare, surface: WindowBg),   // Plugin.Close.cs
    }, Gap: 8f);

    // Row A just after a capture from the strip: [tile] Saved file size … [Open folder] [Full panel] [✕ = dismiss].
    private HudElement DockedSavedRow() => new RowElement(new HudElement[]
    {
        IconTile("header", StripTile, StripGlyph),
        new TextElement(() => _toastTitle, Color: () => SuccessColor(), NoWrap: true, FontSize: 13) { Bold = true },
        new TextElement(() => _toastFile, Color: () => TextColor(), NoWrap: true),
        new CellElement(new TextElement(DockedSavedDetail, Color: () => MutedColor(), NoWrap: true) { Ellipsize = true }, Weight: 1f),
        Pill(PillKind.Neutral, "ps.cap.openFolder", () => OpenFolderSafe(_toastDir), 80f, surface: WindowBg),
        Pill(PillKind.Neutral, "ps.docked.fullPanel", DockedToFullPanel, 72f, surface: WindowBg),
        IconButton("✕", HideToast, IconButtonKind.Bare, surface: WindowBg),
    }, Gap: 8f);

    // The size line plus any warning (memory / folder / ReShade notes), one line.
    private Cached<(string, string)>? _savedDetail;
    private string DockedSavedDetail() => (_savedDetail ??= new Cached<(string, string)>(k => k.Item2.Length == 0 ? k.Item1 : k.Item1 + " · " + k.Item2))
        .Get((_toastDetail, _toastWarning));

    private HudElement DockedMinisRow() => new RowElement(new HudElement[]
    {
        Mini("ps.look.exposure", 64f,
            KitSlider(() => _editor.Color.PostExposure, v => _editor.EditColor(c => c with { PostExposure = v }), -3f, 3f,
                () => Supported(LookGroups.Color)),
            ValueText(() => _editor.Color.PostExposure, v => F(v, "+0.0;-0.0;0.0")), 48f),
        Mini("ps.docked.warmth", 56f,
            KitSlider(() => _editor.WhiteBalance.Temperature, v => _editor.EditWhiteBalance(w => w with { Temperature = v }), -100f, 100f,
                () => Supported(LookGroups.WhiteBalance)),
            ValueText(() => _editor.WhiteBalance.Temperature, v => F(v, Signed)), 40f),
        Mini("ps.docked.blur", 44f,
            KitSlider(BlurAmount, SetBlurAmount, 0f, 1f, () => Supported(LookGroups.Dof)), BlurText(), 40f),
        new CellElement(new SpacerElement(0f, 1f), Weight: 1f),
        Pill(PillKind.Primary, DockedCaptureLabel, CaptureNow, 106f, enabled: () => !Capturing, surface: WindowBg),
    }, Gap: 12f);

    private Func<string> BlurText()
    {
        var cache = new Cached<(bool, float, string)>(k => k.Item1 ? "f/" + F(k.Item2, "0.0") : T("ps.docked.blurOff"));
        return () => cache.Get((_editor.IsOn(LookGroups.Dof), _editor.Dof.Aperture, T("ps.docked.blurOff")));
    }

    // Keyed on the binding OBJECT (no string built per poll); the label is rebuilt only when the key is rebound.
    private Cached<(KeyBinding?, string)>? _dockedCapture;
    private string DockedCaptureLabel() => (_dockedCapture ??= new Cached<(KeyBinding?, string)>(_ => _loc.TFormat("ps.docked.capture", BindingText(StudioHotkeys.Capture))))
        .Get((BindingOf(StudioHotkeys.Capture), T("ps.docked.capture")));

    private HudElement StripLabel(string key)
        => new TextElement(Loc(key), Color: () => MutedColor(), NoWrap: true, FontSize: 12) { Bold = true };

    // [muted label] [120 px slider] [value]
    private HudElement Mini(string labelKey, float labelW, SliderElement slider, Func<string> value, float valueW) => new RowElement(new HudElement[]
    {
        new SpacerElement(0f, PillH),
        new CellElement(new TextElement(Loc(labelKey), Color: () => MutedColor(), NoWrap: true) { Ellipsize = true }, Width: labelW),
        new CellElement(slider with { Width = StripSliderW }, Width: StripSliderW),
        new CellElement(new TextElement(value, Color: () => TextColor(), Align: TextAlign.Right, NoWrap: true), Width: valueW),
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
