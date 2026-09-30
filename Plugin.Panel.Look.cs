using System;
using System.Collections.Generic;
using System.IO;
using Stellar.Abstractions.Domain;
using Stellar.Abstractions.Services;

namespace Stellar.PhotoStudio;

// Look tab: the pin, then one foldable group per effect (ModuleOptimizer "▾" + ConditionalElement recipe),
// each with an enable toggle, slider rows (Maestro recipe) and a "?" — greyed out when this client can't render it.
public sealed partial class Plugin
{
    private const string Pct = "0'%'";
    private const string Signed = "+0;-0;0";
    private bool _editingFilter;
    private List<string> _lutFiles = new();

    private string LutFolder => Path.Combine(_studioFolder, "luts");

    private bool Supported(LookGroups g) => (_services.RenderLook.Capabilities.Supported & g) != 0;
    private bool LooksAvailable() => _services.RenderLook.Capabilities.Supported != LookGroups.None;
    private bool IsGroupOpen(LookGroups g) => (_settings.OpenGroups & g) != 0;

    private HudElement BuildLookTab() => new ColumnElement(new HudElement[]
    {
        new ConditionalElement(() => !LooksAvailable(), new RowElement(new HudElement[]
        {
            new TextElement(() => T("ps.look.unavailable"), Color: () => _services.Theme.Colors.Warning),
            new SpacerElement(),
            HelpDot("look.na", () => T("ps.look.unavailable"), () => T("ps.help.lookUnavailable")),
        }, Gap: 6f)),
        HelpToggle("look.pin", () => _settings.Pinned, SetPinned, new HelpText(() => T("ps.look.pin"), () => T("ps.help.look.pin"))),
        new ConditionalElement(() => _settings.Pinned, new TextElement(() => T("ps.look.pinHint"), Color: Muted)),
        new RowElement(new HudElement[]
        {
            new SpacerElement(),
            new ButtonElement(() => T("ps.look.resetAll"), OnClick: ResetAllToPreset, Width: 88f),
        }, Gap: 6f),
        new SeparatorElement(),
        DofGroup(),
        ColorGroup(),
        WhiteBalanceGroup(),
        LutGroup(),
        BloomGroup(),
        VignetteGroup(),
        FilmGrainGroup(),
    }, Gap: 8f);

    private void SetPinned(bool on)
    {
        _settings.SetPinned(on);
        _look.SetPinned(on);
    }

    private void ResetAllToPreset() => _presetSession.ResetToSaved();   // on purpose: nothing is stashed

    private HudElement LookGroup(LookGroups g, string key, bool photoOnly, params HudElement[] rows)
    {
        var header = new List<HudElement>
        {
            new SelectableElement(new RowElement(new HudElement[]
            {
                new TextElement(() => IsGroupOpen(g) ? "▾" : "▸", Width: 14f),
                new TextElement(() => T("ps.look." + key), Emphasis: true, Color: () => Supported(g) ? Normal() : MenuMuted()),
            }, Gap: 4f), OnClick: () => _settings.SetGroupOpen(g, !IsGroupOpen(g))),
        };
        if (photoOnly)
            header.Add(new PillElement(() => T("ps.pill.photoOnly"),
                Color: () => _settings.Pinned ? _services.Theme.Colors.Warning : _services.Theme.Colors.MenuText));
        header.Add(new ConditionalElement(() => !Supported(g), new TextElement(() => T("ps.look.na"), Color: MenuMuted)));
        header.Add(new SpacerElement());
        header.Add(new ToggleElement(() => "", () => Supported(g) && _editor.IsOn(g), on => _editor.SetOn(g, on),
            Enabled: () => Supported(g)));
        header.Add(HelpDot("look." + key, () => T("ps.look." + key),
            () => Supported(g) ? T("ps.help.look." + key) : T("ps.help.lookUnavailable")));

        return new ColumnElement(new HudElement[]
        {
            new RowElement(header, Gap: 6f),
            new ConditionalElement(() => Supported(g) && IsGroupOpen(g), new RowElement(new HudElement[]
            {
                new SpacerElement(Width: 16f),
                new CellElement(new ColumnElement(rows, Gap: 4f), Weight: 1f),
            })),
        }, Gap: 4f);
    }

    private HudElement DofGroup()
    {
        var def = new DofLook();
        return LookGroup(LookGroups.Dof, "dof", photoOnly: true,
            HelpToggle("look.dofTrack", () => _editor.Dof.FocusOnLocalPlayer,
                on => _editor.EditDof(d => d with { FocusOnLocalPlayer = on }),
                new HelpText(() => T("ps.look.focusOnMe"), () => T("ps.help.look.focusOnMe"))),
            SliderRow(() => T("ps.look.focusDistance"),
                new SliderElement(() => _editor.Dof.FocusDistance, v => _editor.EditDof(d => d with { FocusDistance = v }),
                    0.1f, 100f, Enabled: () => !_editor.Dof.FocusOnLocalPlayer),
                () => _editor.Dof.FocusOnLocalPlayer ? T("ps.look.auto") : F(_editor.Dof.FocusDistance, "0.0") + " m",
                () => _editor.EditDof(d => d with { FocusDistance = def.FocusDistance })),
            SliderRow(() => T("ps.look.aperture"),
                new SliderElement(() => _editor.Dof.Aperture, v => _editor.EditDof(d => d with { Aperture = v }), 1f, 22f),
                () => "f/" + F(_editor.Dof.Aperture, "0.0"),
                () => _editor.EditDof(d => d with { Aperture = def.Aperture })),
            SliderRow(() => T("ps.look.focalLength"),
                new SliderElement(() => _editor.Dof.FocalLength, v => _editor.EditDof(d => d with { FocalLength = v }), 10f, 300f),
                () => F(_editor.Dof.FocalLength, "0") + " mm",
                () => _editor.EditDof(d => d with { FocalLength = def.FocalLength })));
    }

    private HudElement ColorGroup() => LookGroup(LookGroups.Color, "color", photoOnly: false,
        SliderRow(() => T("ps.look.exposure"),
            new SliderElement(() => _editor.Color.PostExposure, v => _editor.EditColor(c => c with { PostExposure = v }), -3f, 3f),
            () => F(_editor.Color.PostExposure, "+0.0;-0.0;0.0") + " EV",
            () => _editor.EditColor(c => c with { PostExposure = 0f })),
        SliderRow(() => T("ps.look.contrast"),
            new SliderElement(() => _editor.Color.Contrast, v => _editor.EditColor(c => c with { Contrast = v }), -100f, 100f),
            () => F(_editor.Color.Contrast, Signed),
            () => _editor.EditColor(c => c with { Contrast = 0f })),
        SliderRow(() => T("ps.look.saturation"),
            new SliderElement(() => _editor.Color.Saturation, v => _editor.EditColor(c => c with { Saturation = v }), -100f, 100f),
            () => F(_editor.Color.Saturation, Signed),
            () => _editor.EditColor(c => c with { Saturation = 0f })),
        LabeledRow(() => T("ps.look.colorFilter"),
            new SwatchElement(FilterColor, Size: 16f),
            new ButtonElement(() => _editingFilter ? T("ps.done") : T("ps.edit"), OnClick: () => _editingFilter = !_editingFilter, Width: 56f),
            new ButtonElement(() => "↺", OnClick: () => _editor.EditColor(c => c with { Filter = RgbColor.White }), Width: 28f)),
        new ConditionalElement(() => _editingFilter, new ColorPickerElement(FilterColor,
            c => _editor.EditColor(x => x with { Filter = new RgbColor(c.R, c.G, c.B) }))));

    private ColorRgba FilterColor()
    {
        var f = _editor.Color.Filter;
        return new ColorRgba(f.R, f.G, f.B, 1f);
    }

    private HudElement WhiteBalanceGroup() => LookGroup(LookGroups.WhiteBalance, "whiteBalance", photoOnly: false,
        SliderRow(() => T("ps.look.temperature"),
            new SliderElement(() => _editor.WhiteBalance.Temperature, v => _editor.EditWhiteBalance(w => w with { Temperature = v }), -100f, 100f),
            () => F(_editor.WhiteBalance.Temperature, Signed),
            () => _editor.EditWhiteBalance(w => w with { Temperature = 0f })),
        SliderRow(() => T("ps.look.tint"),
            new SliderElement(() => _editor.WhiteBalance.Tint, v => _editor.EditWhiteBalance(w => w with { Tint = v }), -100f, 100f),
            () => F(_editor.WhiteBalance.Tint, Signed),
            () => _editor.EditWhiteBalance(w => w with { Tint = 0f })));

    private HudElement BloomGroup()
    {
        var def = new BloomLook();
        return LookGroup(LookGroups.Bloom, "bloom", photoOnly: false,
            SliderRow(() => T("ps.look.intensity"),
                new SliderElement(() => _editor.Bloom.Intensity, v => _editor.EditBloom(b => b with { Intensity = v }), 0f, 8f),
                () => F(_editor.Bloom.Intensity, "0.00"),
                () => _editor.EditBloom(b => b with { Intensity = def.Intensity })),
            SliderRow(() => T("ps.look.threshold"),
                new SliderElement(() => _editor.Bloom.Threshold, v => _editor.EditBloom(b => b with { Threshold = v }), 0f, 5f),
                () => F(_editor.Bloom.Threshold, "0.00"),
                () => _editor.EditBloom(b => b with { Threshold = def.Threshold })));
    }

    private HudElement VignetteGroup()
    {
        var def = new VignetteLook();
        return LookGroup(LookGroups.Vignette, "vignette", photoOnly: false,
            SliderRow(() => T("ps.look.intensity"),
                new SliderElement(() => _editor.Vignette.Intensity, v => _editor.EditVignette(x => x with { Intensity = v }), 0f, 1f),
                () => F(_editor.Vignette.Intensity * 100f, Pct),
                () => _editor.EditVignette(x => x with { Intensity = def.Intensity })),
            SliderRow(() => T("ps.look.smoothness"),
                new SliderElement(() => _editor.Vignette.Smoothness, v => _editor.EditVignette(x => x with { Smoothness = v }), 0.01f, 1f),
                () => F(_editor.Vignette.Smoothness * 100f, Pct),
                () => _editor.EditVignette(x => x with { Smoothness = def.Smoothness })));
    }

    private HudElement FilmGrainGroup()
    {
        var def = new FilmGrainLook();
        return LookGroup(LookGroups.FilmGrain, "filmGrain", photoOnly: true,
            SliderRow(() => T("ps.look.intensity"),
                new SliderElement(() => _editor.FilmGrain.Intensity, v => _editor.EditFilmGrain(x => x with { Intensity = v }), 0f, 1f),
                () => F(_editor.FilmGrain.Intensity * 100f, Pct),
                () => _editor.EditFilmGrain(x => x with { Intensity = def.Intensity })),
            SliderRow(() => T("ps.look.response"),
                new SliderElement(() => _editor.FilmGrain.Response, v => _editor.EditFilmGrain(x => x with { Response = v }), 0f, 1f),
                () => F(_editor.FilmGrain.Response * 100f, Pct),
                () => _editor.EditFilmGrain(x => x with { Response = def.Response })));
    }

    private HudElement LutGroup() => LookGroup(LookGroups.Lut, "lut", photoOnly: false,
        LabeledRow(() => T("ps.look.lutFile"),
            new CellElement(new DropdownElement(LutSelectedIndex, LutOptions, SelectLut), Weight: 1f)),
        SliderRow(() => T("ps.look.contribution"),
            new SliderElement(() => _editor.Lut.Contribution, v => _editor.EditLut(l => l with { Contribution = v }), 0f, 1f),
            () => F(_editor.Lut.Contribution * 100f, Pct),
            () => _editor.EditLut(l => l with { Contribution = 1f })),
        new RowElement(new HudElement[]
        {
            new SpacerElement(Width: LabelW),
            new ButtonElement(() => T("ps.look.openLuts"), OnClick: () => OpenFolderSafe(LutFolder), Width: 132f),
            new ButtonElement(() => T("ps.look.rescan"), OnClick: RescanLuts, Width: 72f),
        }, Gap: 6f));

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
