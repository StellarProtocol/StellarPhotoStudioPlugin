using System;
using System.Collections.Generic;
using System.IO;
using Stellar.Abstractions.Domain;
using Stellar.Abstractions.Services;

namespace Stellar.PhotoStudio;

// Presets tab: a pooled list (built-ins first, then user presets A→Z, plus an "Unsaved look" row when edits were
// set aside), Save / Save as / Rename / Delete, Import from a folder, Export to a folder. There is no native file
// dialog, so import/export use folders under stellar/photostudio/ with an "Open folder" button.
public sealed partial class Plugin
{
    private const int PresetSlots = 64;
    private enum NameMode { None, SaveAs, Rename, ConfirmDelete, Import }

    private NameMode _nameMode;
    private string _nameDraft = "";
    private List<string> _importFiles = new();
    private int _importIndex;

    private string ImportFolder => Path.Combine(_studioFolder, "presets-import");
    private string ExportFolder => Path.Combine(_studioFolder, "exports");

    private HudElement BuildPresetsTab()
    {
        var slots = new HudElement[PresetSlots];
        for (var i = 0; i < PresetSlots; i++) slots[i] = PresetSlot(i);
        return new ColumnElement(new HudElement[]
        {
            new RowElement(new HudElement[]
            {
                new TextElement(() => T("ps.tab.presets"), Emphasis: true),
                new SpacerElement(),
                new TextElement(() => T("ps.pre.clickToApply"), Color: Muted),
            }, Gap: 6f),
            new ScrollElement(new ListElement(PresetRowCount, slots), Height: 220f),
            new RowElement(new HudElement[]
            {
                new ButtonElement(() => T("ps.pre.save"), OnClick: SaveActive, Enabled: () => !ActiveIsBuiltIn && _modified, Width: 64f),
                new ButtonElement(() => T("ps.pre.saveAs"), OnClick: () => BeginName(NameMode.SaveAs, _activePresetName + T("ps.pre.mineSuffix")), Width: 84f),
                new ButtonElement(() => T("ps.pre.rename"), OnClick: () => BeginName(NameMode.Rename, _activePresetName), Enabled: () => !ActiveIsBuiltIn, Width: 84f),
                new ButtonElement(() => T("ps.pre.delete"), OnClick: () => _nameMode = NameMode.ConfirmDelete, Enabled: () => !ActiveIsBuiltIn, Width: 68f),
            }, Gap: 4f),
            new RowElement(new HudElement[]
            {
                new ButtonElement(() => T("ps.pre.import"), OnClick: BeginImport, Width: 84f),
                new ButtonElement(() => T("ps.pre.export"), OnClick: ExportActive, Width: 72f),
                new SpacerElement(),
                HelpDot("presets", () => T("ps.tab.presets"), () => T("ps.help.presets")),
            }, Gap: 4f),
            new ConditionalElement(() => _nameMode is NameMode.SaveAs or NameMode.Rename, NameEditor()),
            new ConditionalElement(() => _nameMode == NameMode.ConfirmDelete, DeleteConfirm()),
            new ConditionalElement(() => _nameMode == NameMode.Import, ImportPicker()),
        }, Gap: 6f);
    }

    // ── list rows ────────────────────────────────────────────────────────────────────────────────────────────

    private int UnsavedRows => _unsavedLook is null ? 0 : 1;
    private int PresetRowCount() => Math.Min(PresetSlots, UnsavedRows + _presets.All.Count);

    private HudElement PresetSlot(int i) => new SelectableElement(new RowElement(new HudElement[]
    {
        new CellElement(new TextElement(() => RowName(i), NoWrap: true, Color: () => IsUnsavedRow(i) ? Muted() : Normal()), Weight: 1f),
        new ConditionalElement(() => RowBuiltIn(i), new PillElement(() => T("ps.pill.builtIn"))),
        new ConditionalElement(() => IsUnsavedRow(i) || (RowIsActive(i) && _modified),
            new PillElement(() => T("ps.pill.modified"), Color: () => _services.Theme.Colors.Accent)),
    }, Gap: 6f), OnClick: () => ClickRow(i), Selected: () => RowIsActive(i));

    private bool IsUnsavedRow(int i) => UnsavedRows == 1 && i == 0;

    private Presets.Preset? RowPreset(int i)
    {
        var k = i - UnsavedRows;
        var all = _presets.All;
        return k >= 0 && k < all.Count ? all[k] : null;
    }

    private string RowName(int i) => IsUnsavedRow(i) ? T("ps.pre.unsaved") : RowPreset(i)?.Name ?? "";
    private bool RowBuiltIn(int i) => !IsUnsavedRow(i) && (RowPreset(i)?.BuiltIn ?? false);
    private bool RowIsActive(int i) => !IsUnsavedRow(i)
        && string.Equals(RowPreset(i)?.Name, _activePresetName, StringComparison.OrdinalIgnoreCase);

    private void ClickRow(int i)
    {
        _nameMode = NameMode.None;
        if (IsUnsavedRow(i)) { RestoreUnsavedLook(); return; }
        if (RowPreset(i) is { } p) ApplyPreset(p);
    }

    // ── save / rename / delete ───────────────────────────────────────────────────────────────────────────────

    private void SaveActive()
    {
        if (ActiveIsBuiltIn) return;
        _presets.Save(_activePresetName, _editor.Build());
        _modified = false;
        _settings.SetWorkingJson(null);
    }

    private void BeginName(NameMode mode, string initial)
    {
        _nameMode = mode;
        _nameDraft = initial;
    }

    private HudElement NameEditor() => new ColumnElement(new HudElement[]
    {
        new RowElement(new HudElement[]
        {
            new InputElement(() => _nameDraft, s => { _nameDraft = s; CommitName(); }, Width: 220f, OnChange: s => _nameDraft = s),
            new ButtonElement(() => T("ps.ok"), OnClick: CommitName, Enabled: () => NameError() is null, Width: 48f),
            new ButtonElement(() => T("ps.cancel"), OnClick: () => _nameMode = NameMode.None, Width: 72f),
        }, Gap: 6f),
        new ConditionalElement(() => NameError() is not null, new TextElement(() => NameError() ?? "", Color: Muted)),
    }, Gap: 4f);

    private string? NameError()
    {
        var name = _nameDraft.Trim();
        if (name.Length == 0) return T("ps.pre.errEmpty");
        var existing = FindPreset(name);
        if (existing is null) return null;
        if (_nameMode == NameMode.Rename && string.Equals(existing.Name, _activePresetName, StringComparison.OrdinalIgnoreCase)) return null;
        return existing.BuiltIn ? T("ps.pre.errBuiltIn") : T("ps.pre.errTaken");
    }

    private void CommitName()
    {
        if (NameError() is not null) return;
        var name = _nameDraft.Trim();
        try
        {
            if (_nameMode == NameMode.SaveAs) _presets.Save(name, _editor.Build());
            else _presets.Rename(_activePresetName, name);
        }
        catch (InvalidOperationException ex)
        {
            _view.ShowError(ex.Message);
            return;
        }
        _nameMode = NameMode.None;
        _modified = false;
        if (FindPreset(name) is { } p) ApplyPreset(p);
    }

    private HudElement DeleteConfirm() => new RowElement(new HudElement[]
    {
        new TextElement(() => _loc.TFormat("ps.pre.confirmDelete", _activePresetName), Color: () => _services.Theme.Colors.Warning),
        new SpacerElement(),
        new ButtonElement(() => T("ps.pre.delete"), OnClick: DeleteActive, Width: 68f),
        new ButtonElement(() => T("ps.cancel"), OnClick: () => _nameMode = NameMode.None, Width: 72f),
    }, Gap: 6f);

    private void DeleteActive()
    {
        _nameMode = NameMode.None;
        if (ActiveIsBuiltIn) return;
        _presets.Delete(_activePresetName);
        _modified = false;
        ApplyPreset(_presets.All[0]);
    }

    // ── import / export ──────────────────────────────────────────────────────────────────────────────────────

    private void BeginImport()
    {
        try { Directory.CreateDirectory(ImportFolder); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        _importFiles = ListJson(ImportFolder);
        _importIndex = 0;
        _nameMode = NameMode.Import;
    }

    private HudElement ImportPicker() => new ColumnElement(new HudElement[]
    {
        new RowElement(new HudElement[]
        {
            new CellElement(new DropdownElement(() => _importIndex, ImportOptions, i => _importIndex = i), Weight: 1f),
            new ButtonElement(() => T("ps.pre.importOne"), OnClick: ImportSelected, Enabled: () => _importFiles.Count > 0, Width: 72f),
            new ButtonElement(() => T("ps.cancel"), OnClick: () => _nameMode = NameMode.None, Width: 72f),
        }, Gap: 6f),
        new RowElement(new HudElement[]
        {
            new ButtonElement(() => T("ps.pre.openImport"), OnClick: () => OpenFolderSafe(ImportFolder), Width: 140f),
            new ButtonElement(() => T("ps.look.rescan"), OnClick: BeginImport, Width: 72f),
        }, Gap: 6f),
        new TextElement(() => T("ps.pre.importHint"), Color: Muted),
    }, Gap: 4f);

    private IReadOnlyList<string> ImportOptions()
    {
        if (_importFiles.Count == 0) return new[] { T("ps.pre.importNone") };
        var names = new List<string>(_importFiles.Count);
        foreach (var f in _importFiles) names.Add(Path.GetFileName(f));
        return names;
    }

    private void ImportSelected()
    {
        if (_importIndex < 0 || _importIndex >= _importFiles.Count) return;
        Presets.Preset? p = null;
        try { p = _presets.Import(File.ReadAllText(_importFiles[_importIndex])); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        if (p is null) { _view.ShowError(T("preset.bad")); return; }
        _nameMode = NameMode.None;
        ApplyPreset(p);
    }

    private void ExportActive()
    {
        try
        {
            Directory.CreateDirectory(ExportFolder);
            var path = Path.Combine(ExportFolder, SafeFileName(_activePresetName) + ".json");
            var p = FindPreset(_activePresetName);
            var json = p is not null && !_modified
                ? _presets.Export(p.Name)
                : System.Text.Json.JsonSerializer.Serialize(Presets.PresetDto.From(_activePresetName, _editor.Build()));
            File.WriteAllText(path, json);
            ShowFileToast(T("ps.toast.exported"), path, T("ps.toast.exportedDetail"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _view.ShowError(T("ps.pre.exportFailed"));
        }
    }

    private static List<string> ListJson(string dir)
    {
        var list = new List<string>();
        if (!Directory.Exists(dir)) return list;
        foreach (var f in Directory.GetFiles(dir))
            if (string.Equals(Path.GetExtension(f), ".json", StringComparison.OrdinalIgnoreCase)) list.Add(f);
        list.Sort(StringComparer.OrdinalIgnoreCase);
        return list;
    }

    private static string SafeFileName(string name)
    {
        var chars = name.ToCharArray();
        var bad = Path.GetInvalidFileNameChars();
        for (var i = 0; i < chars.Length; i++)
            if (Array.IndexOf(bad, chars[i]) >= 0 || chars[i] is ':' or '\\' or '/') chars[i] = '_';
        return new string(chars);
    }
}
