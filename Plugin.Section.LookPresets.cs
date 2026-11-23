using System;
using System.Collections.Generic;
using System.IO;
using Stellar.Abstractions.Domain;
using Stellar.Abstractions.Services;
using Stellar.PhotoStudio.Presets;

namespace Stellar.PhotoStudio;

// The PRESET bar at the top of the Look section (2.0.0 — it replaces the 1.x Presets tab): ‹ [pill dropdown] › Save More,
// a Modified tag, Reset all. The dropdown lists what the old list did: any set-aside "Unsaved look (from X)" first, then
// the built-ins and the player's presets A→Z. "More" opens the in-window popover (Plugin.Panel.Popovers.cs); Delete asks
// through the modal confirm (ConfirmHostElement, Danger). There is no native file dialog, so
// import / export use folders under stellar/photostudio/ with an "Open folder" button. All preset state lives in
// PresetSession (Presets/PresetSession.cs, unit-tested); this file is the view.
public sealed partial class Plugin
{
    private const float PresetBarPillW = 48f;
    private enum NameMode { None, SaveAs, Rename, Import }

    private NameMode _nameMode;
    private bool _moreOpen;
    private string _nameDraft = "";
    private List<string> _importFiles = new();
    private List<string>? _importOptionsCache;
    private List<string>? _presetBarOptions;   // dropped on every preset-state change (OnPresetStateChanged)
    private int _importIndex;

    private string ImportFolder => Path.Combine(_studioFolder, "presets-import");
    private string ExportFolder => Path.Combine(_studioFolder, "exports");

    private HudElement PresetBarPanel() => LivePanel(UpperLoc("ps.look.preset"), null, new HudElement[]
    {
        new ConditionalElement(() => _modified, TagChip("ps.pill.modified", AccentColor)),
        HelpButton(new HelpKeys("ps.tab.presets", "ps.help.presets", SecLook)),
    }, new HudElement[]
    {
        new RowElement(new HudElement[]
        {
            IconButton("‹", PrevPreset),
            new CellElement(PillDropdown(PresetBarSelected, PresetBarOptions, SelectPresetBarRow), Weight: 1f),
            IconButton("›", NextPreset),
            Pill(PillKind.Primary, "ps.pre.save", () => _presetSession.Save(), PresetBarPillW, enabled: () => _canSaveCached),   // lights review I-5
            Pill(PillKind.Neutral, "ps.look.more", ToggleMore, PresetBarPillW),   // the More popover, Plugin.Panel.Popovers.cs
        }, Gap: RowGap),
        new RowElement(new HudElement[]
        {
            Pill(PillKind.Neutral, "ps.look.resetAll", ResetAllToPreset, 72f),
            new CellElement(NoteLine(Loc("ps.look.resetNote")), Weight: 1f),
        }, Gap: RowGap),
        new ConditionalElement(() => _nameMode is NameMode.SaveAs or NameMode.Rename, NameEditor()),
        new ConditionalElement(() => _nameMode == NameMode.Import, ImportPicker()),
    });

    // ── the dropdown ─────────────────────────────────────────────────────────────────────────────────────────────

    private int UnsavedRows => _presetSession.Stash.Count;

    private IReadOnlyList<string> PresetBarOptions()
    {
        if (_presetBarOptions is not null) return _presetBarOptions;
        var all = _presets.All;
        var names = new List<string>(UnsavedRows + all.Count);
        for (var i = 0; i < UnsavedRows; i++) names.Add(_loc.TFormat("ps.pre.unsavedFrom", _presetSession.Stash[i].Origin));
        foreach (var p in all) names.Add(p.Name);
        return _presetBarOptions = names;
    }

    private int PresetBarSelected() => UnsavedRows + IndexOfPreset(_activePresetName);

    private void SelectPresetBarRow(int row)
    {
        _nameMode = NameMode.None;
        if (row < UnsavedRows) { LampToast(_presetSession.RestoreUnsaved(row)); return; }
        var all = _presets.All;
        var k = row - UnsavedRows;
        if (k >= 0 && k < all.Count) ApplyPreset(all[k]);
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
            new CellElement(new InputElement(() => _nameDraft, s => { _nameDraft = s; CommitName(); }, Width: 240f,
                OnChange: s => _nameDraft = s), Weight: 1f),
            Pill(PillKind.Primary, "ps.ok", CommitName, 40f, enabled: () => NameError().Length == 0),
            Pill(PillKind.Neutral, "ps.cancel", CloseNameMode, 56f),
        }, Gap: RowGap),
        new ConditionalElement(() => NameError().Length > 0, NoteLine(NameError, CautionColor)),
    }, Gap: 6f);

    private void CloseNameMode()
    {
        _nameMode = NameMode.None;
        _moreOpen = false;
    }

    // Polled while the editor is open: cached on the draft (TooLong formats a number).
    private Cached<(string, NameMode, string, string)>? _nameError;

    private string NameError()
    {
        _nameError ??= new Cached<(string, NameMode, string, string)>(_ => ComputeNameError() ?? string.Empty);
        return _nameError.Get((_nameDraft, _nameMode, _activePresetName, T("ps.pre.errEmpty")));
    }

    private string? ComputeNameError()
    {
        switch (PresetNames.Check(_nameDraft))
        {
            case NameProblem.Empty: return T("ps.pre.errEmpty");
            case NameProblem.TooLong: return _loc.TFormat("ps.pre.errTooLong", PresetNames.MaxLength);
            case NameProblem.BadCharacters: return T("ps.pre.errBadChars");
            case NameProblem.Reserved: return T("ps.pre.errReserved");
        }
        var existing = FindPreset(_nameDraft.Trim());
        if (existing is null) return null;
        if (_nameMode == NameMode.Rename && string.Equals(existing.Name, _activePresetName, StringComparison.OrdinalIgnoreCase)) return null;
        return existing.BuiltIn ? T("ps.pre.errBuiltIn") : T("ps.pre.errTaken");
    }

    private void CommitName()
    {
        if (NameError().Length > 0) return;
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
        CloseNameMode();
    }

    /// <summary>More › Delete…: the framework's modal confirm over the whole panel (Danger: the preset file is removed).</summary>
    private void ConfirmDeletePreset()
    {
        var name = _activePresetName;
        _panelConfirm.Show(new ConfirmRequest(_loc.TFormat("ps.pre.confirmDelete", name), T("ps.pre.deleteMsg"), () =>
        {
            if (string.Equals(name, _activePresetName, StringComparison.OrdinalIgnoreCase) && !ActiveIsBuiltIn) _presetSession.Delete();
        })
        {
            Subtitle = T("ps.title"),
            Emphasis = T("ps.pre.deleteEmphasis"),
            Severity = ConfirmSeverity.Danger,
            ConfirmLabel = T("ps.pre.delete"),
            CancelLabel = T("ps.cancel"),
        });
        _panelWin.MarkDirty();
    }

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
            new CellElement(PillDropdown(() => _importIndex, ImportOptions, i => _importIndex = i), Weight: 1f),
            Pill(PillKind.Primary, "ps.pre.importOne", ImportSelected, 56f, enabled: () => _importFiles.Count > 0),
            Pill(PillKind.Neutral, "ps.cancel", CloseNameMode, 56f),
        }, Gap: RowGap),
        new RowElement(new HudElement[]
        {
            Pill(PillKind.Neutral, "ps.pre.openImport", () => OpenFolderSafe(ImportFolder)),
            Pill(PillKind.Neutral, "ps.look.rescan", BeginImport),
        }, Gap: RowGap),
        NoteLine(Loc("ps.pre.importHint")),
    }, Gap: 6f);

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
        CloseNameMode();
        _presetNamesCache = null;
        _presetBarOptions = null;
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
