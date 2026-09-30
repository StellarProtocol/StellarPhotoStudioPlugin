using System;
using Stellar.Abstractions.Domain;

namespace Stellar.PhotoStudio.Presets;

/// <summary>
/// Which preset is active, whether the look in the editor has been changed since, and the edits the player set
/// aside by applying another preset (the "Unsaved look" row). Edits are never lost: applying a preset over
/// modified edits stashes them WITH the preset they came from, renaming keeps the editor's current look, and
/// restoring the stash puts the player back on the preset those edits belong to.
/// </summary>
internal sealed class PresetSession
{
    private readonly PresetStore _store;
    private readonly LookEditor _editor;
    private bool _loading;

    public PresetSession(PresetStore store, LookEditor editor, string activeName, LookSettings? workingLook)
    {
        _store = store;
        _editor = editor;
        var preset = Find(activeName) ?? store.All[0];
        ActiveName = preset.Name;
        Load(workingLook ?? preset.Look);
        Modified = workingLook is not null;
        _editor.Changed += OnEdited;
    }

    public string ActiveName { get; private set; }
    public bool Modified { get; private set; }
    public (string Origin, LookSettings Look)? Unsaved { get; private set; }
    public bool ActiveIsBuiltIn => Find(ActiveName)?.BuiltIn ?? true;

    /// <summary>Raised when the active preset, the modified flag or the stash changes (not on every edit).</summary>
    public event Action? StateChanged;

    public void Apply(Preset p)
    {
        if (Modified) Unsaved = (ActiveName, _editor.Build());
        ActiveName = p.Name;
        Load(p.Look);
        Modified = false;
        StateChanged?.Invoke();
    }

    public void RestoreUnsaved()
    {
        if (Unsaved is not { } u) return;
        Unsaved = null;
        ActiveName = Find(u.Origin)?.Name ?? _store.All[0].Name;
        Load(u.Look);
        Modified = true;
        StateChanged?.Invoke();
    }

    /// <summary>Throws away the edits on purpose (Reset all) — nothing is stashed.</summary>
    public void ResetToSaved()
    {
        Modified = false;
        Apply(Find(ActiveName) ?? _store.All[0]);
    }

    public void Save()
    {
        if (ActiveIsBuiltIn) return;
        _store.Save(ActiveName, _editor.Build());
        Modified = false;
        StateChanged?.Invoke();
    }

    public void SaveAs(string name)
    {
        _store.Save(name, _editor.Build());
        ActiveName = Find(name)?.Name ?? name;
        Modified = false;
        StateChanged?.Invoke();
    }

    /// <summary>Renames the stored preset; the editor's current look (and its modified flag) is kept as is.</summary>
    public void Rename(string name)
    {
        _store.Rename(ActiveName, name);
        ActiveName = Find(name)?.Name ?? name;
        StateChanged?.Invoke();
    }

    public void Delete()
    {
        if (ActiveIsBuiltIn) return;
        _store.Delete(ActiveName);
        Modified = false;
        Apply(_store.All[0]);
    }

    public Preset? Find(string name)
    {
        foreach (var p in _store.All)
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) return p;
        return null;
    }

    private void Load(LookSettings look)
    {
        _loading = true;
        try { _editor.Load(look); }
        finally { _loading = false; }
    }

    private void OnEdited()
    {
        if (_loading || Modified) return;
        Modified = true;
        StateChanged?.Invoke();
    }
}
