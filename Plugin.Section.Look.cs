using System;
using System.Collections.Generic;
using System.IO;
using Stellar.Abstractions.Domain;
using Stellar.Abstractions.Services;

namespace Stellar.PhotoStudio;

// Look section (approved prototype "Look"): the PRESET bar on top (Plugin.Section.LookPresets.cs — it replaces the 1.x
// Presets tab), then one foldable panel per effect — COLOR, DEPTH OF FIELD ("Photo only"), WHITE BALANCE, LUT, BLOOM,
// VIGNETTE, FILM GRAIN ("Photo only") — each with an On/Off chip and a "?" in its caption row. An effect this client
// can't render shows "n/a", stays folded and has a recessed chip. Render quality moved to Capture, the field of view to
// Camera › Lens, time of day to Camera › Scene, ReShade to its own section.
public sealed partial class Plugin
{
    private const string SecLook = "ps.tab.look";
    private const string Pct = "0'%'";
    private const string Signed = "+0;-0;0";
    private bool _editingFilter;
    private List<string> _lutFiles = new();

    private string LutFolder => Path.Combine(_studioFolder, "luts");

    private bool Supported(LookGroups g) => (_services.RenderLook.Capabilities.Supported & g) != 0;
    private bool LooksAvailable() => _services.RenderLook.Capabilities.Supported != LookGroups.None;
    private bool IsGroupOpen(LookGroups g) => (_settings.OpenGroups & g) != 0;

    private HudElement BuildLookSection() => SectionColumn(
        PresetBarPanel(),
        new ConditionalElement(() => !LooksAvailable(), NoteLine(Loc("ps.look.unavailable"), CautionColor)),
        ColorGroup(), DofGroup(), WhiteBalanceGroup(), LutGroup(), BloomGroup(), VignetteGroup(), FilmGrainGroup());

    private void SetPinned(bool on)
    {
        _settings.SetPinned(on);
        _look.SetPinned(on);
    }

    private void ResetAllToPreset() => _presetSession.ResetToSaved();   // on purpose: nothing is stashed

    private HudElement LookFold(LookGroups g, string key, bool photoOnly, params HudElement[] rows)
    {
        var helpKey = "ps.help.look." + key;
        var help = new HelpKeys("ps.look." + key, helpKey, SecLook) { Body = () => T(Supported(g) ? helpKey : "ps.help.lookUnavailable") };
        var chip = OnOffChip(() => Supported(g) && _editor.IsOn(g), on => _editor.SetOn(g, on), () => Supported(g));
        var trailing = photoOnly
            ? new HudElement[] { TagChip("ps.pill.photoOnly", CautionColor), chip, HelpButton(help) }
            : new HudElement[] { chip, HelpButton(help) };
        return FoldPanelLive(UpperLoc("ps.look." + key), () => Supported(g) ? string.Empty : T("ps.look.na"),
            new FoldFlag(() => Supported(g) && IsGroupOpen(g), open => _settings.SetGroupOpen(g, open)), trailing, rows);
    }

    private HudElement LookSlider(string key, Func<float> get, Action<float> set, float min, float max, Func<string> value,
        Action reset, float step = 0f, Func<bool>? enabled = null, bool typed = false)
        => SettingSlider(key, KitSlider(get, set, min, max, enabled), value, reset, new SliderOpts(Step: step, Parse: typed ? ParseNumber : null));

    private HudElement DofGroup()
    {
        var def = new DofLook();
        return LookFold(LookGroups.Dof, "dof", photoOnly: true,
            SettingToggle("ps.look.focusOnMe", () => _editor.Dof.FocusOnLocalPlayer, on => _editor.EditDof(d => d with { FocusOnLocalPlayer = on }),
                new HelpKeys("ps.look.focusOnMe", "ps.help.look.focusOnMe", SecLook, "ps.look.dof")),
            LookSlider("ps.look.focusDistance", () => _editor.Dof.FocusDistance, v => _editor.EditDof(d => d with { FocusDistance = v }), 0.1f, 100f,
                FocusDistanceText(), () => _editor.EditDof(d => d with { FocusDistance = def.FocusDistance }), 0.1f,
                () => !_editor.Dof.FocusOnLocalPlayer, typed: true),
            LookSlider("ps.look.aperture", () => _editor.Dof.Aperture, v => _editor.EditDof(d => d with { Aperture = v }), 1f, 22f,
                ValueText(() => _editor.Dof.Aperture, v => "f/" + F(v, "0.0")), () => _editor.EditDof(d => d with { Aperture = def.Aperture }), 0.1f, typed: true),
            LookSlider("ps.look.focalLength", () => _editor.Dof.FocalLength, v => _editor.EditDof(d => d with { FocalLength = v }), 10f, 300f,
                ValueText(() => _editor.Dof.FocalLength, v => F(v, "0") + " mm"), () => _editor.EditDof(d => d with { FocalLength = def.FocalLength }), 1f, typed: true));
    }

    private Func<string> FocusDistanceText()
    {
        var cache = new Cached<(bool, float, string)>(k => k.Item1 ? T("ps.look.auto") : F(k.Item2, "0.0") + " m");
        return () => cache.Get((_editor.Dof.FocusOnLocalPlayer, _editor.Dof.FocusDistance, T("ps.look.auto")));
    }

    private HudElement ColorGroup() => LookFold(LookGroups.Color, "color", photoOnly: false,
        LookSlider("ps.look.exposure", () => _editor.Color.PostExposure, v => _editor.EditColor(c => c with { PostExposure = v }), -3f, 3f,
            ValueText(() => _editor.Color.PostExposure, v => F(v, "+0.0;-0.0;0.0") + " EV"), () => _editor.EditColor(c => c with { PostExposure = 0f }), 0.1f),
        LookSlider("ps.look.contrast", () => _editor.Color.Contrast, v => _editor.EditColor(c => c with { Contrast = v }), -100f, 100f,
            ValueText(() => _editor.Color.Contrast, v => F(v, Signed)), () => _editor.EditColor(c => c with { Contrast = 0f }), 1f),
        LookSlider("ps.look.saturation", () => _editor.Color.Saturation, v => _editor.EditColor(c => c with { Saturation = v }), -100f, 100f,
            ValueText(() => _editor.Color.Saturation, v => F(v, Signed)), () => _editor.EditColor(c => c with { Saturation = 0f }), 1f),
        new RowElement(new HudElement[]
        {
            new SpacerElement(0f, PillH),
            RowLabel("ps.look.colorFilter", null, RowLabelW),
            new SwatchElement(FilterColor, Size: 20f),
            new CellElement(new SpacerElement(0f, 1f), Weight: 1f),
            Pill(PillKind.Neutral, () => T(_editingFilter ? "ps.done" : "ps.edit"), () => _editingFilter = !_editingFilter, 52f),
            IconImageButton("reset", () => _editor.EditColor(c => c with { Filter = RgbColor.White })),
            new SpacerElement(HelpDotSize, HelpDotSize),
        }, Gap: RowGap),
        new ConditionalElement(() => _editingFilter, new ColorPickerElement(FilterColor,
            c => _editor.EditColor(x => x with { Filter = new RgbColor(c.R, c.G, c.B) }))));

    private ColorRgba FilterColor()
    {
        var f = _editor.Color.Filter;
        return new ColorRgba(f.R, f.G, f.B, 1f);
    }

    private HudElement WhiteBalanceGroup() => LookFold(LookGroups.WhiteBalance, "whiteBalance", photoOnly: false,
        LookSlider("ps.look.temperature", () => _editor.WhiteBalance.Temperature, v => _editor.EditWhiteBalance(w => w with { Temperature = v }),
            -100f, 100f, ValueText(() => _editor.WhiteBalance.Temperature, v => F(v, Signed)),
            () => _editor.EditWhiteBalance(w => w with { Temperature = 0f }), 1f),
        LookSlider("ps.look.tint", () => _editor.WhiteBalance.Tint, v => _editor.EditWhiteBalance(w => w with { Tint = v }), -100f, 100f,
            ValueText(() => _editor.WhiteBalance.Tint, v => F(v, Signed)), () => _editor.EditWhiteBalance(w => w with { Tint = 0f }), 1f));

    private HudElement BloomGroup()
    {
        var def = new BloomLook();
        return LookFold(LookGroups.Bloom, "bloom", photoOnly: false,
            LookSlider("ps.look.intensity", () => _editor.Bloom.Intensity, v => _editor.EditBloom(b => b with { Intensity = v }), 0f, 8f,
                ValueText(() => _editor.Bloom.Intensity, v => F(v, "0.00")), () => _editor.EditBloom(b => b with { Intensity = def.Intensity }), 0.05f),
            LookSlider("ps.look.threshold", () => _editor.Bloom.Threshold, v => _editor.EditBloom(b => b with { Threshold = v }), 0f, 5f,
                ValueText(() => _editor.Bloom.Threshold, v => F(v, "0.00")), () => _editor.EditBloom(b => b with { Threshold = def.Threshold }), 0.05f));
    }

    private HudElement VignetteGroup()
    {
        var def = new VignetteLook();
        return LookFold(LookGroups.Vignette, "vignette", photoOnly: false,
            LookSlider("ps.look.intensity", () => _editor.Vignette.Intensity, v => _editor.EditVignette(x => x with { Intensity = v }), 0f, 1f,
                ValueText(() => _editor.Vignette.Intensity, v => F(v * 100f, Pct)), () => _editor.EditVignette(x => x with { Intensity = def.Intensity }), 0.01f),
            LookSlider("ps.look.smoothness", () => _editor.Vignette.Smoothness, v => _editor.EditVignette(x => x with { Smoothness = v }), 0.01f, 1f,
                ValueText(() => _editor.Vignette.Smoothness, v => F(v * 100f, Pct)), () => _editor.EditVignette(x => x with { Smoothness = def.Smoothness }), 0.01f));
    }

    private HudElement FilmGrainGroup()
    {
        var def = new FilmGrainLook();
        return LookFold(LookGroups.FilmGrain, "filmGrain", photoOnly: true,
            LookSlider("ps.look.intensity", () => _editor.FilmGrain.Intensity, v => _editor.EditFilmGrain(x => x with { Intensity = v }), 0f, 1f,
                ValueText(() => _editor.FilmGrain.Intensity, v => F(v * 100f, Pct)), () => _editor.EditFilmGrain(x => x with { Intensity = def.Intensity }), 0.01f),
            LookSlider("ps.look.response", () => _editor.FilmGrain.Response, v => _editor.EditFilmGrain(x => x with { Response = v }), 0f, 1f,
                ValueText(() => _editor.FilmGrain.Response, v => F(v * 100f, Pct)), () => _editor.EditFilmGrain(x => x with { Response = def.Response }), 0.01f));
    }

    private HudElement LutGroup() => LookFold(LookGroups.Lut, "lut", photoOnly: false,
        new RowElement(new HudElement[]
        {
            new SpacerElement(0f, PillH),
            RowLabel("ps.look.lutFile", null, RowLabelW),
            new CellElement(PillDropdown(LutSelectedIndex, LutOptions, SelectLut), Weight: 1f),
            new SpacerElement(HelpDotSize, HelpDotSize),
        }, Gap: RowGap),
        LookSlider("ps.look.contribution", () => _editor.Lut.Contribution, v => _editor.EditLut(l => l with { Contribution = v }), 0f, 1f,
            ValueText(() => _editor.Lut.Contribution, v => F(v * 100f, Pct)), () => _editor.EditLut(l => l with { Contribution = 1f }), 0.01f),
        new RowElement(new HudElement[]
        {
            Pill(PillKind.Neutral, "ps.look.openLuts", () => OpenFolderSafe(LutFolder)),
            Pill(PillKind.Neutral, "ps.look.rescan", RescanLuts),
        }, Gap: 8f));

    // Built once per rescan: the dropdown polls Options() every refresh.
    private List<string>? _lutOptionsCache;

    private IReadOnlyList<string> LutOptions()
    {
        if (_lutOptionsCache is not null) return _lutOptionsCache;
        var names = new List<string>(_lutFiles.Count + 1) { T("ps.look.lutNone") };
        foreach (var f in _lutFiles) names.Add(Path.GetFileName(f));
        return _lutOptionsCache = names;
    }

    private int LutSelectedIndex()
    {
        if (!_editor.IsOn(LookGroups.Lut)) return 0;
        var current = Path.GetFileName(_editor.Lut.FilePath);
        for (var i = 0; i < _lutFiles.Count; i++)
            if (string.Equals(Path.GetFileName(_lutFiles[i]), current, StringComparison.OrdinalIgnoreCase)) return i + 1;
        return 0;
    }

    private void SelectLut(int index)
    {
        if (index <= 0 || index > _lutFiles.Count) { _editor.SetOn(LookGroups.Lut, false); return; }
        var path = _lutFiles[index - 1];
        if (!IsValidLut(path))
        {
            _view.ShowError(_loc.TFormat("lut.bad", Path.GetFileName(path)));
            return;   // the previous LUT stays
        }
        _editor.EditLut(l => l with { FilePath = Path.GetFileName(path) });   // resolved against LutFolder on apply
    }

    private static bool IsValidLut(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            var head = new byte[33];
            var n = fs.Read(head, 0, head.Length);
            return n >= 24 && LutLibrary.Validate(head);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private void RescanLuts()
    {
        try { Directory.CreateDirectory(LutFolder); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        _lutFiles = new List<string>(LutLibrary.List(LutFolder));
        _lutOptionsCache = null;
    }
}
