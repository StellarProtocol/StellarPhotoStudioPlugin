using System;
using System.Collections.Generic;
using Stellar.Abstractions.Domain;

namespace Stellar.PhotoStudio.Presets;

/// <summary>
/// Which preset is active, whether the look in the editor has been changed since, and the edits the player set
/// aside by applying another preset (the "Unsaved look" rows). Edits are never lost: applying a preset over
/// modified edits stashes them WITH the preset they came from (newest first, up to <see cref="MaxStash"/>),
/// renaming keeps the editor's current look, and restoring a stash entry puts the player back on its preset —
/// swapping any current edits into the stash rather than discarding them.
/// </summary>
/// <summary>How a preset reads and sets the panel's photo shape (spec 2026-10-03: presets carry Shape).</summary>
internal sealed record PresetShapeLink(Func<PhotoShape> Current, Action<PhotoShape> Apply);

internal sealed class PresetSession
{
    private readonly PresetStore _store;
    private readonly LookEditor _editor;
    private readonly PresetShapeLink? _shape;
    private readonly List<(string Origin, LookSettings Look)> _stash = new();
    private bool _loading;

    /// <summary>Oldest set-aside edits beyond this are dropped (the player has walked away from them many times).</summary>
    public const int MaxStash = 10;

    /// <param name="shape">Null = presets neither save nor apply a shape (tests of the look-only behaviour).</param>
    public PresetSession(PresetStore store, LookEditor editor, string activeName, LookSettings? workingLook, PresetShapeLink? shape = null)
    {
        _store = store;
        _editor = editor;
        _shape = shape;
        var preset = Find(activeName) ?? store.All[0];
        ActiveName = preset.Name;
        Load(workingLook ?? preset.Look);
        Modified = workingLook is not null;
        _editor.Changed += OnEdited;
    }

    public string ActiveName { get; private set; }
    public bool Modified { get; private set; }
    /// <summary>Set-aside edits, newest first.</summary>
    public IReadOnlyList<(string Origin, LookSettings Look)> Stash => _stash;
    public bool ActiveIsBuiltIn => Find(ActiveName)?.BuiltIn ?? true;

    /// <summary>Raised when the active preset, the modified flag or the stash changes (not on every edit).</summary>
    public event Action? StateChanged;

    public void Apply(Preset p)
    {
        if (Modified) PushStash();
        ActiveName = p.Name;
        Load(p.Look);
        if (p.Shape is { } shape) _shape?.Apply(shape);   // a preset without a shape leaves the current one alone
        Modified = false;
        StateChanged?.Invoke();
    }

    public void RestoreUnsaved(int index = 0)
    {
        if (index < 0 || index >= _stash.Count) return;
        var u = _stash[index];
        _stash.RemoveAt(index);
        if (Modified) PushStash();   // swap: the edits being replaced are kept too
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
        _store.Save(ActiveName, _editor.Build(), _shape?.Current());
        Modified = false;
        StateChanged?.Invoke();
    }

    public void SaveAs(string name)
    {
        _store.Save(name, _editor.Build(), _shape?.Current());
        ActiveName = Find(name)?.Name ?? name;
        Modified = false;
        StateChanged?.Invoke();
    }

    /// <summary>Renames the stored preset; the editor's current look (and its modified flag) is kept as is.</summary>
    public void Rename(string name)
    {
        var old = ActiveName;
        _store.Rename(old, name);
        ActiveName = Find(name)?.Name ?? name;
        for (var i = 0; i < _stash.Count; i++)
            if (string.Equals(_stash[i].Origin, old, StringComparison.OrdinalIgnoreCase)) _stash[i] = (ActiveName, _stash[i].Look);
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

    private void PushStash()
    {
        _stash.Insert(0, (ActiveName, _editor.Build()));
        if (_stash.Count > MaxStash) _stash.RemoveAt(_stash.Count - 1);
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
