using System;
using System.Collections.Generic;
using Stellar.Abstractions.Domain;
using Stellar.PhotoStudio.Lights;
using Stellar.PhotoStudio.ReShade;

namespace Stellar.PhotoStudio.Presets;

/// <summary>How a preset reads and sets the panel's photo shape (spec 2026-10-03: presets carry Shape).</summary>
internal sealed record PresetShapeLink(Func<PhotoShape> Current, Action<PhotoShape> Apply);

/// <summary>How a preset reads and sets the scene's lights (spec 2026-10-03 lights § 5). <paramref name="Capture"/> returns
/// null when there are no lights to save; <paramref name="Apply"/> says whether the lamps could be placed
/// (<see cref="LightsResult.Unavailable"/> when the selected person cannot be read). Lights are scene objects, not part of
/// the look: changing them never marks the preset modified — but lights that differ from the active preset's enable Save
/// (<see cref="PresetSession.CanSave"/>), and applying a preset over hand-placed lights sets them aside in the stash.</summary>
internal sealed record PresetLightsLink(Func<LightsPreset?> Capture, Func<LightsPreset, LightsResult> Apply);

/// <summary>How a preset reads and applies ReShade's preset + on/off (spec 2026-10-03 reshade § 6). <paramref name="Current"/>
/// is null when ReShade is not installed (then Save keeps the stored choice, like lights review I-5).</summary>
internal sealed record PresetReShadeLink(Func<ReShadeChoice?> Current, Action<ReShadeChoice> Apply);

/// <summary>One set-aside "Unsaved look" row: the look, the preset it came from, the photo shape it was framed in
/// (null = no shape link), and the shape the preset state started from (so re-saving after a restore knows whether the
/// shape was the player's change). <paramref name="Lights"/>: the hand-placed lights a preset's lights replaced (owner
/// ruling 2026-10-03, "Replace, with undo" — null when none were set aside); <paramref name="LookEdited"/>: whether the
/// look itself had been changed (a lights-only row restores an unmodified look).</summary>
internal sealed record UnsavedLook(string Origin, LookSettings Look, PhotoShape? Shape, PhotoShape? BaseShape,
    LightsPreset? Lights = null, bool LookEdited = true)
{
    /// <summary>The ReShade choice at the moment the row was set aside (null = none / not installed).</summary>
    public ReShadeChoice? ReShade { get; init; }
}

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
    private ReShadeChoice? _baseReShade;   // the ReShade choice when the preset state was last clean
    private bool _reShadeEdited;           // the player changed ReShade's preset / on-off in Photo Studio since then

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

    /// <summary>Set once at start: presets save and apply ReShade's preset + on/off through it (null = they do not).</summary>
    public PresetReShadeLink? ReShadeLink { get; set; }

    /// <summary>The player changed ReShade's preset or on/off in Photo Studio: marks the look modified, like a shape change.</summary>
    public void OnReShadeEdited()
    {
        if (ReShadeLink is null) return;
        _reShadeEdited = true;
        OnEdited();
    }

    public string ActiveName { get; private set; }
    public bool Modified { get; private set; }
    /// <summary>Set-aside edits, newest first.</summary>
    public IReadOnlyList<UnsavedLook> Stash => _stash;
    public bool ActiveIsBuiltIn => Find(ActiveName)?.BuiltIn ?? true;

    /// <summary>Raised when the active preset, the modified flag or the stash changes (not on every edit).</summary>
    public event Action? StateChanged;

    /// <summary>Lights in the scene that differ from the active preset's (hand-placed or moved since it was applied /
    /// saved). No lights in the scene never differ: Save keeps the stored ones then (lights review I-5).</summary>
    public bool LightsDiffer => Lights?.Capture() is { } now && !now.Equivalent(Find(ActiveName)?.Lights);

    /// <summary>Save has something to store: a user preset whose look was changed or whose lights differ.</summary>
    public bool CanSave => !ActiveIsBuiltIn && (Modified || LightsDiffer);

    /// <summary>Applies <paramref name="p"/>; the result is the lights' (<see cref="LightsResult.Ok"/> when the preset
    /// carries none) — <see cref="LightsResult.Unavailable"/> means its lamps could not be placed.</summary>
    public LightsResult Apply(Preset p) => Apply(p, withLights: true);

    // Reset all re-applies the look only: the lights are scene objects the player placed, not edits of the look.
    private LightsResult Apply(Preset p, bool withLights)
    {
        var replacesLights = withLights && p.Lights is not null && Lights is not null;
        var setAside = replacesLights && LightsDiffer;   // owner ruling 2026-10-03: hand-placed lights go to the stash
        if (Modified || setAside) PushStash(setAside);
        ActiveName = p.Name;
        Load(p.Look);
        if (p.Shape is { } shape) ApplyShape(shape);   // a preset without a shape leaves the current one alone
        if (p.ReShade is { } rs && ReShadeLink is not null) ReShadeLink.Apply(rs);   // none: ReShade stays as it is
        var result = replacesLights ? Lights!.Apply(p.Lights!) : LightsResult.Ok;   // without lights: the scene's stay
        _baseShape = _shape?.Current();
        _baseReShade = p.ReShade ?? ReShadeLink?.Current();   // Apply is asynchronous: the preset's own choice is the truth
        _reShadeEdited = false;
        Modified = false;
        StateChanged?.Invoke();
        return result;
    }

    /// <summary>Puts a stash row back (its look, shape and — when it carries them — its lights).</summary>
    public LightsResult RestoreUnsaved(int index = 0)
    {
        if (index < 0 || index >= _stash.Count) return LightsResult.Ok;
        var u = _stash[index];
        _stash.RemoveAt(index);
        var swapLights = u.Lights is not null && Lights is not null && LightsDiffer;
        if (Modified || swapLights) PushStash(swapLights);   // swap: the edits being replaced are kept too
        ActiveName = Find(u.Origin)?.Name ?? _store.All[0].Name;
        Load(u.Look);
        if (u.Shape is { } shape) ApplyShape(shape);
        if (u.ReShade is { } rs && ReShadeLink is not null) ReShadeLink.Apply(rs);
        var result = u.Lights is { } lights && Lights is not null ? Lights.Apply(lights) : LightsResult.Ok;
        _baseShape = u.BaseShape;
        Modified = u.LookEdited;
        StateChanged?.Invoke();
        return result;
    }

    /// <summary>Throws away the edits on purpose (Reset all) — nothing is stashed.</summary>
    public void ResetToSaved()
    {
        var preset = Find(ActiveName) ?? _store.All[0];
        var revert = preset.Shape is null ? _baseShape : null;   // a shapeless preset: undo the player's shape change
        var revertReShade = preset.ReShade is null && _reShadeEdited ? _baseReShade : null;
        Modified = false;
        Apply(preset, withLights: false);
        if (revertReShade is { } rs) { ReShadeLink?.Apply(rs); _baseReShade = rs; }
        if (revert is not { } r) return;
        ApplyShape(r);
        _baseShape = r;
    }

    public void Save()
    {
        if (ActiveIsBuiltIn) return;
        var stored = Find(ActiveName);
        var keepsShape = stored?.Shape is not null || ShapeChanged;
        // Lights review I-5: a scene with no lights keeps the preset's stored lights (as an untouched shape keeps none).
        var lights = Lights?.Capture() ?? stored?.Lights;
        var keepsReShade = stored?.ReShade is not null || _reShadeEdited;
        var reshade = keepsReShade ? ReShadeLink?.Current() ?? stored?.ReShade : null;
        _store.Save(ActiveName, _editor.Build(), keepsShape ? _shape?.Current() : null, lights, reshade);
        _baseShape = _shape?.Current();
        _baseReShade = reshade ?? ReShadeLink?.Current();
        _reShadeEdited = false;
        Modified = false;
        StateChanged?.Invoke();
    }

    public void SaveAs(string name)
    {
        var reshade = ReShadeLink?.Current();
        _store.Save(name, _editor.Build(), _shape?.Current(), Lights?.Capture(), reshade);   // a new preset captures it all as it is
        _baseShape = _shape?.Current();
        _baseReShade = reshade;
        _reShadeEdited = false;
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

    private void PushStash(bool withLights = false)
    {
        var lights = withLights ? Lights?.Capture() : null;
        _stash.Insert(0, new UnsavedLook(ActiveName, _editor.Build(), _shape?.Current(), _baseShape, lights, Modified)
        {
            ReShade = _reShadeEdited ? ReShadeLink?.Current() : null,
        });
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
