using System;
using System.Collections.Generic;
using Stellar.Abstractions.Domain;

namespace Stellar.PhotoStudio.Presets;

/// <summary>How a preset reads and sets the panel's photo shape (spec 2026-10-03: presets carry Shape).</summary>
internal sealed record PresetShapeLink(Func<PhotoShape> Current, Action<PhotoShape> Apply);

/// <summary>How a preset reads and sets the scene's lights (spec 2026-10-03 lights § 5). <paramref name="Capture"/> returns
/// null when there are no lights to save. Lights are scene objects, not part of the look: changing them never marks the
/// preset modified and the "Unsaved look" stash does not carry them.</summary>
internal sealed record PresetLightsLink(Func<Lights.LightsPreset?> Capture, Action<Lights.LightsPreset> Apply);

/// <summary>One set-aside "Unsaved look" row: the look, the preset it came from, the photo shape it was framed in
/// (null = no shape link), and the shape the preset state started from (so re-saving after a restore knows whether the
/// shape was the player's change).</summary>
internal sealed record UnsavedLook(string Origin, LookSettings Look, PhotoShape? Shape, PhotoShape? BaseShape);

/// <summary>
/// Which preset is active, whether the look in the editor has been changed since, and the edits the player set
/// aside by applying another preset (the "Unsaved look" rows). Edits are never lost: applying a preset over
/// modified edits stashes them WITH the preset they came from (newest first, up to <see cref="MaxStash"/>),
/// renaming keeps the editor's current look, and restoring a stash entry puts the player back on its preset —
/// swapping any current edits into the stash rather than discarding them.
/// The photo shape counts as part of the look for all of this (review 2026-10-03): changing it marks the preset
/// modified, a stashed row carries it back, and Reset puts it back. Saving over a preset stores the current shape
/// when the preset already has one or the player changed it; a preset saved before shapes existed keeps having none
/// while the shape is untouched, so applying it still leaves the player's shape alone.
/// </summary>
internal sealed class PresetSession
{
    private readonly PresetStore _store;
    private readonly LookEditor _editor;
    private readonly PresetShapeLink? _shape;
    private readonly List<UnsavedLook> _stash = new();
    private bool _loading;
    private PhotoShape? _baseShape;   // the shape when the preset state was last clean (loaded / applied / saved)

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
        _baseShape = _shape?.Current();
        _editor.Changed += OnEdited;
    }

    /// <summary>Set once at start: presets save and apply the scene's lights through it (null = they do not).</summary>
    public PresetLightsLink? Lights { get; set; }

    public string ActiveName { get; private set; }
    public bool Modified { get; private set; }
    /// <summary>Set-aside edits, newest first.</summary>
    public IReadOnlyList<UnsavedLook> Stash => _stash;
    public bool ActiveIsBuiltIn => Find(ActiveName)?.BuiltIn ?? true;

    /// <summary>Raised when the active preset, the modified flag or the stash changes (not on every edit).</summary>
    public event Action? StateChanged;

    public void Apply(Preset p) => Apply(p, withLights: true);

    // Reset all re-applies the look only: the lights are scene objects the player placed, not edits of the look.
    private void Apply(Preset p, bool withLights)
    {
        if (Modified) PushStash();
        ActiveName = p.Name;
        Load(p.Look);
        if (p.Shape is { } shape) ApplyShape(shape);   // a preset without a shape leaves the current one alone
        if (withLights && p.Lights is { } lights) Lights?.Apply(lights);   // a preset without lights leaves the scene's alone
        _baseShape = _shape?.Current();
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
        if (u.Shape is { } shape) ApplyShape(shape);
        _baseShape = u.BaseShape;
        Modified = true;
        StateChanged?.Invoke();
    }

    /// <summary>Throws away the edits on purpose (Reset all) — nothing is stashed.</summary>
    public void ResetToSaved()
    {
        var preset = Find(ActiveName) ?? _store.All[0];
        var revert = preset.Shape is null ? _baseShape : null;   // a shapeless preset: undo the player's shape change
        Modified = false;
        Apply(preset, withLights: false);
        if (revert is not { } r) return;
        ApplyShape(r);
        _baseShape = r;
    }

    public void Save()
    {
        if (ActiveIsBuiltIn) return;
        var keepsShape = Find(ActiveName)?.Shape is not null || ShapeChanged;
        _store.Save(ActiveName, _editor.Build(), keepsShape ? _shape?.Current() : null, Lights?.Capture());
        _baseShape = _shape?.Current();
        Modified = false;
        StateChanged?.Invoke();
    }

    public void SaveAs(string name)
    {
        _store.Save(name, _editor.Build(), _shape?.Current(), Lights?.Capture());   // a new preset captures the shape as it is
        _baseShape = _shape?.Current();
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
            if (string.Equals(_stash[i].Origin, old, StringComparison.OrdinalIgnoreCase)) _stash[i] = _stash[i] with { Origin = ActiveName };
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
        _stash.Insert(0, new UnsavedLook(ActiveName, _editor.Build(), _shape?.Current(), _baseShape));
        if (_stash.Count > MaxStash) _stash.RemoveAt(_stash.Count - 1);
    }

    private void Load(LookSettings look)
    {
        _loading = true;
        try { _editor.Load(look); }
        finally { _loading = false; }
    }

    /// <summary>The player changed the photo shape (wired to the settings' change event): it marks the preset modified
    /// like a look edit. Shapes set by applying / restoring / resetting a preset do not.</summary>
    public void OnShapeChanged()
    {
        if (_shape is null) return;
        OnEdited();
    }

    private bool ShapeChanged => _shape is not null && _shape.Current() != _baseShape;

    private void ApplyShape(PhotoShape shape)
    {
        if (_shape is null) return;
        _loading = true;
        try { _shape.Apply(shape); }
        finally { _loading = false; }
    }

    private void OnEdited()
    {
        if (_loading || Modified) return;
        Modified = true;
        StateChanged?.Invoke();
    }
}
