using System;
using System.Collections.Generic;
using System.IO;
using Stellar.Abstractions.Domain;
using Stellar.Abstractions.Services;
using Stellar.PhotoStudio.Presets;

namespace Stellar.PhotoStudio;

// Presets tab: a virtual list (built-ins first, then user presets A→Z, plus an "Unsaved look" row when edits were
// set aside), Save / Save as / Rename / Delete, Import from a folder, Export to a folder. There is no native file
// dialog, so import/export use folders under stellar/photostudio/ with an "Open folder" button. All preset state
// lives in PresetSession (Presets/PresetSession.cs, unit-tested); this file is the view.
public sealed partial class Plugin
{
    private const int PresetPool = 10;         // 220/28 ≈ 7.9 visible rows + margin for a part-scrolled row (CooldownBar recipe)
    private const float PresetRowHeight = 28f;
    private enum NameMode { None, SaveAs, Rename, ConfirmDelete, Import }

    private NameMode _nameMode;
    private string _nameDraft = "";
    private int _presetOffset;
    private List<string> _importFiles = new();
    private List<string>? _importOptionsCache;
    private int _importIndex;

    private string ImportFolder => Path.Combine(_studioFolder, "presets-import");
    private string ExportFolder => Path.Combine(_studioFolder, "exports");

    private HudElement BuildPresetsTab()
    {
        var pool = new HudElement[PresetPool];
        for (var i = 0; i < PresetPool; i++)
        {
            var slot = i;
            pool[i] = new ConditionalElement(() => _presetOffset + slot < PresetRowCount(), PresetSlot(slot));
        }
        return new ColumnElement(new HudElement[]
        {
            new RowElement(new HudElement[]
            {
                new TextElement(() => T("ps.tab.presets"), Emphasis: true),
                new SpacerElement(),
                new TextElement(() => T("ps.pre.clickToApply"), Color: Muted),
            }, Gap: 6f),
            new VirtualListElement(PresetRowCount, PresetRowHeight, pool, o => _presetOffset = o, Height: 220f),
            // No fixed widths: fixed-width buttons wrap their label by a hair at non-1.0 UI scales and render
            // taller (D5, measured in game); auto-sized buttons never wrap and also fit every locale.
            new RowElement(new HudElement[]
            {
                new ButtonElement(() => T("ps.pre.save"), OnClick: _presetSession.Save, Enabled: () => !ActiveIsBuiltIn && _modified),
                new ButtonElement(() => T("ps.pre.saveAs"), OnClick: () => BeginName(NameMode.SaveAs, _activePresetName + T("ps.pre.mineSuffix"))),
                new ButtonElement(() => T("ps.pre.rename"), OnClick: () => BeginName(NameMode.Rename, _activePresetName), Enabled: () => !ActiveIsBuiltIn),
                new ButtonElement(() => T("ps.pre.delete"), OnClick: () => _nameMode = NameMode.ConfirmDelete, Enabled: () => !ActiveIsBuiltIn),
            }, Gap: 4f),
            new RowElement(new HudElement[]
            {
                new ButtonElement(() => T("ps.pre.import"), OnClick: BeginImport),
                new ButtonElement(() => T("ps.pre.export"), OnClick: ExportActive),
                new SpacerElement(),
                HelpDot("presets", () => T("ps.tab.presets"), () => T("ps.help.presets")),
            }, Gap: 4f),
            new ConditionalElement(() => _nameMode is NameMode.SaveAs or NameMode.Rename, NameEditor()),
            new ConditionalElement(() => _nameMode == NameMode.ConfirmDelete, DeleteConfirm()),
            new ConditionalElement(() => _nameMode == NameMode.Import, ImportPicker()),
        }, Gap: 6f);
    }

    // ── list rows ────────────────────────────────────────────────────────────────────────────────────────────

    private int UnsavedRows => _presetSession.Stash.Count;
    private int PresetRowCount() => UnsavedRows + _presets.All.Count;

    private HudElement PresetSlot(int slot) => new SelectableElement(new RowElement(new HudElement[]
    {
        new CellElement(new TextElement(() => RowName(slot), NoWrap: true, Color: () => IsUnsavedRow(slot) ? Muted() : Normal()), Weight: 1f),
        new ConditionalElement(() => RowBuiltIn(slot), new PillElement(() => T("ps.pill.builtIn"))),
        new ConditionalElement(() => IsUnsavedRow(slot) || (RowIsActive(slot) && _modified),
            new PillElement(() => T("ps.pill.modified"), Color: () => _services.Theme.Colors.Accent)),
    }, Gap: 6f), OnClick: () => ClickRow(slot), Selected: () => RowIsActive(slot));

    private bool IsUnsavedRow(int slot) => _presetOffset + slot < UnsavedRows;

    private Preset? RowPreset(int slot)
    {
        var k = _presetOffset + slot - UnsavedRows;
        var all = _presets.All;
        return k >= 0 && k < all.Count ? all[k] : null;
    }

    private string RowName(int slot) => IsUnsavedRow(slot)
        ? _loc.TFormat("ps.pre.unsavedFrom", _presetSession.Stash[_presetOffset + slot].Origin)
        : RowPreset(slot)?.Name ?? "";

    private bool RowBuiltIn(int slot) => !IsUnsavedRow(slot) && (RowPreset(slot)?.BuiltIn ?? false);
    private bool RowIsActive(int slot) => !IsUnsavedRow(slot)
        && string.Equals(RowPreset(slot)?.Name, _activePresetName, StringComparison.OrdinalIgnoreCase);

    private void ClickRow(int slot)
    {
        _nameMode = NameMode.None;
        if (IsUnsavedRow(slot)) { _presetSession.RestoreUnsaved(_presetOffset + slot); return; }
        if (RowPreset(slot) is { } p) ApplyPreset(p);
    }

    // ── save as / rename / delete ────────────────────────────────────────────────────────────────────────────

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
            new ButtonElement(() => T("ps.ok"), OnClick: CommitName, Enabled: () => NameError() is null),
            new ButtonElement(() => T("ps.cancel"), OnClick: () => _nameMode = NameMode.None),
        }, Gap: 6f),
        new ConditionalElement(() => NameError() is not null,
            new TextElement(() => NameError() ?? "", Color: () => _services.Theme.Colors.Warning)),
    }, Gap: 4f);

    private string? NameError()
    {
        switch (PresetNames.Check(_nameDraft))
        {
            case NameProblem.Empty: return T("ps.pre.errEmpty");
            case NameProblem.TooLong: return _loc.TFormat("ps.pre.errTooLong", PresetNames.MaxLength);
            case NameProblem.BadCharacters: return T("ps.pre.errBadChars");
        }
        var existing = FindPreset(_nameDraft.Trim());
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
            if (_nameMode == NameMode.SaveAs) _presetSession.SaveAs(name);
            else _presetSession.Rename(name);
        }
        catch (InvalidOperationException ex)
        {
            _view.ShowError(ex.Message);
            return;
        }
        _nameMode = NameMode.None;
    }

    private HudElement DeleteConfirm() => new RowElement(new HudElement[]
    {
        new TextElement(() => _loc.TFormat("ps.pre.confirmDelete", _activePresetName), Color: () => _services.Theme.Colors.Warning),
        new SpacerElement(),
        new ButtonElement(() => T("ps.pre.delete"), OnClick: () => { _nameMode = NameMode.None; _presetSession.Delete(); }),
        new ButtonElement(() => T("ps.cancel"), OnClick: () => _nameMode = NameMode.None),
    }, Gap: 6f);

    // ── import / export ──────────────────────────────────────────────────────────────────────────────────────

    private void BeginImport()
    {
        try { Directory.CreateDirectory(ImportFolder); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        _importFiles = ListJson(ImportFolder);
        _importOptionsCache = null;
        _importIndex = 0;
        _nameMode = NameMode.Import;
    }

    private HudElement ImportPicker() => new ColumnElement(new HudElement[]
    {
        new RowElement(new HudElement[]
        {
            new CellElement(new DropdownElement(() => _importIndex, ImportOptions, i => _importIndex = i), Weight: 1f),
            new ButtonElement(() => T("ps.pre.importOne"), OnClick: ImportSelected, Enabled: () => _importFiles.Count > 0),
            new ButtonElement(() => T("ps.cancel"), OnClick: () => _nameMode = NameMode.None),
        }, Gap: 6f),
        new RowElement(new HudElement[]
        {
            new ButtonElement(() => T("ps.pre.openImport"), OnClick: () => OpenFolderSafe(ImportFolder)),
            new ButtonElement(() => T("ps.look.rescan"), OnClick: BeginImport),
        }, Gap: 6f),
        new TextElement(() => T("ps.pre.importHint"), Color: Muted),
    }, Gap: 4f);

    private IReadOnlyList<string> ImportOptions()
    {
        if (_importOptionsCache is not null) return _importOptionsCache;
        var names = new List<string>(Math.Max(1, _importFiles.Count));
        if (_importFiles.Count == 0) names.Add(T("ps.pre.importNone"));
        foreach (var f in _importFiles) names.Add(Path.GetFileName(f));
        return _importOptionsCache = names;
    }

    private void ImportSelected()
    {
        if (_importIndex < 0 || _importIndex >= _importFiles.Count) return;
        Preset? p = null;
        try { p = _presets.Import(File.ReadAllText(_importFiles[_importIndex])); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        if (p is null) { _view.ShowError(_loc.TFormat("ps.pre.importFailed", Path.GetFileName(_importFiles[_importIndex]))); return; }
        _nameMode = NameMode.None;
        _presetNamesCache = null;
        ApplyPreset(p);
    }

    private void ExportActive()
    {
        try
        {
            Directory.CreateDirectory(ExportFolder);
            var path = UniqueExportPath(PresetNames.Sanitize(_activePresetName));
            var p = FindPreset(_activePresetName);
            var json = p is not null && !_modified
                ? _presets.Export(p.Name)
                : System.Text.Json.JsonSerializer.Serialize(PresetDto.From(_activePresetName, _editor.Build()));
            File.WriteAllText(path, json);
            _toastWarning = "";
            ShowFileToast(T("ps.toast.exported"), path, T("ps.toast.exportedDetail"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _view.ShowError(T("ps.pre.exportFailed"));
        }
    }

    private string UniqueExportPath(string stem)
    {
        var path = Path.Combine(ExportFolder, stem + ".json");
        for (var n = 2; File.Exists(path); n++) path = Path.Combine(ExportFolder, $"{stem} ({n}).json");
        return path;
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
}
