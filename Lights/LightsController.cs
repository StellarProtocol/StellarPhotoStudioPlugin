using System;
using System.Collections.Generic;
using System.Numerics;
using Stellar.Abstractions.Domain;
using Stellar.Abstractions.Services;

namespace Stellar.PhotoStudio.Lights;

/// <summary>What the Lights tab reads from the game: the selected person, a person's anchor (where they stand + face), and
/// the free camera's pose (null while it is off).</summary>
internal sealed record LightsPorts(
    ILights Lights, Func<EntityId> SelectedPerson, Func<EntityId, LightAnchor?> Anchor, Func<(Vector3 Position, float Yaw)?> Camera);

/// <summary>Why a lamp action did nothing (the UI toasts it).</summary>
internal enum LightsResult
{
    Ok,
    /// <summary>Already <see cref="LightsController.MaxLamps"/> lamps.</summary>
    Full,
    /// <summary>Lights are not available (not in the world / scene settling) or the game refused the lamp.</summary>
    Unavailable,
    /// <summary>The free camera is off (drop / move "at camera" need it).</summary>
    NoCamera,
    /// <summary>No lamp is selected.</summary>
    NoLamp,
}

/// <summary>The role label a lamp gets by its order in the list (key / fill / back for the first three).</summary>
internal enum LampRole { None, Key, Fill, Back }

/// <summary>One lamp as the Lights tab shows it.</summary>
internal sealed record LampView(int Number, RgbColor Color, float Strength, float Range, bool On, Vector3 Position, LampRole Role);

/// <summary>
/// The Lights tab's logic (lights spec 2026-10-03, owner-approved mockup v2): up to <see cref="MaxLamps"/> lamps with a
/// selection; drop at the free camera / move the selected lamp there; Around / Height / Distance relative to the selected
/// person (<see cref="LightsMath"/>); duplicate; the Light-people level; a key light and rim per person
/// (People partial); presets (Presets partial). Lights belong to the scene: they stay when the free camera exits;
/// <see cref="Clear"/> (Reset scene, Photo Studio off) removes them, and <see cref="ILights.Released"/> (zone change,
/// cutscene, disconnect — the framework already removed and restored everything) forgets them. Pure over
/// <see cref="LightsPorts"/> (unit-tested with a fake <see cref="ILights"/>). Main thread.
/// </summary>
internal sealed partial class LightsController : IDisposable
{
    /// <summary>The owner's cap ("Up to 8"); the framework enforces the same number per plugin.</summary>
    public const int MaxLamps = LightLimits.MaxLampsPerPlugin;

    /// <summary>Defaults (recon Run 13: i = 40, range 6 m light a character from ~1–5 m; Light people 2 = a natural tint).</summary>
    internal const float DefaultStrength = 40f, DefaultRange = 6f, DefaultPeopleLevel = 2f;

    /// <summary>A duplicate is placed this many degrees further around the selected person.</summary>
    internal const float DuplicateStep = 45f;

    internal static readonly RgbColor DefaultColor = new(1f, 0.85f, 0.65f);

    private readonly LightsPorts _p;
    private readonly List<Lamp> _lamps = new();
    private readonly Action _onReleased;

    private sealed class Lamp
    {
        public LampId Id;
        public LampSettings Settings;
    }

    /// <param name="ports">The game reads.</param>
    /// <param name="peopleLevel">The stored Light-people level (StudioSettings — a setting, kept across sessions).</param>
    public LightsController(LightsPorts ports, float peopleLevel = DefaultPeopleLevel)
    {
        _p = ports;
        _onReleased = OnReleased;
        _p.Lights.Released += _onReleased;
        PeopleLevel = Math.Clamp(float.IsFinite(peopleLevel) ? peopleLevel : DefaultPeopleLevel, 0f, LightLimits.MaxPeopleLevel);
        _p.Lights.PeopleLevel = PeopleLevel;
    }

    /// <summary>Raised whenever anything the Lights tab or the markers show changed.</summary>
    public event Action? Changed;

    public int Count => _lamps.Count;
    public bool CanAdd => _lamps.Count < MaxLamps;
    public int SelectedIndex { get; private set; } = -1;

    /// <summary>Lamps + lit people: what makes the scene "set" (StudioScene).</summary>
    public int SceneCount => _lamps.Count + LitCount;

    public IReadOnlyList<LampView> Lamps
    {
        get
        {
            var list = new LampView[_lamps.Count];
            for (var i = 0; i < _lamps.Count; i++) list[i] = View(i);
            return list;
        }
    }

    public LampView? SelectedLamp => SelectedIndex >= 0 ? View(SelectedIndex) : null;

    /// <summary>One lamp row without building the whole list (the panel reads each row's colour / on state per frame —
    /// lights review minor); null past the end.</summary>
    public LampView? LampAt(int index) => index >= 0 && index < _lamps.Count ? View(index) : null;

    /// <summary>How strongly lamps tint characters (0–20; framework-clamped). Kept across scenes — a setting.</summary>
    public float PeopleLevel { get; private set; } = DefaultPeopleLevel;

    public static LampRole RoleOf(int index) => index switch { 0 => LampRole.Key, 1 => LampRole.Fill, 2 => LampRole.Back, _ => LampRole.None };

    public void SetPeopleLevel(float level)
    {
        PeopleLevel = Math.Clamp(float.IsNaN(level) ? 0f : level, 0f, LightLimits.MaxPeopleLevel);
        _p.Lights.PeopleLevel = PeopleLevel;
        Raise();
    }

    public void Select(int index)
    {
        if (index < -1 || index >= _lamps.Count || index == SelectedIndex) return;
        SelectedIndex = index;
        Raise();
    }

    /// <summary>＋ Lamp at camera / L: a new lamp where the free camera is, selected.</summary>
    public LightsResult AddAtCamera()
    {
        if (_p.Camera() is not { } cam) return LightsResult.NoCamera;
        return Add(new LampSettings(ToPos(cam.Position), DefaultColor, DefaultStrength, DefaultRange, true));
    }

    /// <summary>Move here (camera) / Shift+L: the selected lamp jumps to the free camera.</summary>
    public LightsResult MoveSelectedToCamera()
    {
        if (SelectedIndex < 0) return LightsResult.NoLamp;
        if (_p.Camera() is not { } cam) return LightsResult.NoCamera;
        return Update(SelectedIndex, _lamps[SelectedIndex].Settings with { Position = ToPos(cam.Position) });
    }

    /// <summary>A copy of the selected lamp, <see cref="DuplicateStep"/>° further around the selected person (same place
    /// when no person can be read), selected.</summary>
    public LightsResult Duplicate()
    {
        if (SelectedIndex < 0) return LightsResult.NoLamp;
        var s = _lamps[SelectedIndex].Settings;
        if (SelectedPlacement is { } at && Anchor() is { } a)
            s = s with { Position = ToPos(LightsMath.ToWorld(a, at with { Around = at.Around + DuplicateStep })) };
        return Add(s);
    }

    public void Remove(int index)
    {
        if (index < 0 || index >= _lamps.Count) return;
        _p.Lights.RemoveLamp(_lamps[index].Id);
        _lamps.RemoveAt(index);
        if (SelectedIndex >= _lamps.Count || SelectedIndex > index) SelectedIndex--;
        Raise();
    }

    public LightsResult SetOn(int index, bool on) =>
        index < 0 || index >= _lamps.Count ? LightsResult.NoLamp : Update(index, _lamps[index].Settings with { Enabled = on });

    public LightsResult SetColor(RgbColor color) => UpdateSelected(s => s with { Color = color });
    public LightsResult SetStrength(float strength) => UpdateSelected(s => s with { Strength = Math.Clamp(strength, 0f, LightLimits.MaxStrength) });
    public LightsResult SetRange(float range) => UpdateSelected(s => s with { Range = Math.Clamp(range, LightLimits.MinRange, LightLimits.MaxRange) });

    /// <summary>The selected lamp around the selected person; null when no lamp is selected or the person cannot be read.</summary>
    public LampPlacement? SelectedPlacement =>
        SelectedIndex >= 0 && Anchor() is { } a ? LightsMath.FromWorld(a, ToVec(_lamps[SelectedIndex].Settings.Position)) : null;

    public LightsResult SetAround(float degrees) => Place(p => p with { Around = degrees });
    public LightsResult SetHeight(float metres) => Place(p => p with { Height = metres });
    public LightsResult SetDistance(float metres) => Place(p => p with { Distance = metres });

    /// <summary>Reset scene / Photo Studio off: every lamp removed and every lit person back to normal.</summary>
    public void Clear()
    {
        foreach (var l in _lamps) _p.Lights.RemoveLamp(l.Id);
        ClearPeople(restore: true);
        var had = _lamps.Count > 0;
        _lamps.Clear();
        SelectedIndex = -1;
        if (had || _peopleChanged) Raise();
        _peopleChanged = false;
    }

    public void Dispose()
    {
        _p.Lights.Released -= _onReleased;
        Clear();
    }

    /// <summary>The framework ended the scene's lights (everything already removed and restored): forget them.</summary>
    private void OnReleased()
    {
        _lamps.Clear();
        SelectedIndex = -1;
        ClearPeople(restore: false);
        Raise();
    }

    private LightsResult Add(LampSettings s)
    {
        if (!CanAdd) return LightsResult.Full;
        var id = _p.Lights.AddLamp(s);
        if (id.IsNone) return LightsResult.Unavailable;
        _lamps.Add(new Lamp { Id = id, Settings = s });
        SelectedIndex = _lamps.Count - 1;
        Raise();
        return LightsResult.Ok;
    }

    private LightsResult Update(int index, LampSettings s)
    {
        if (!_p.Lights.UpdateLamp(_lamps[index].Id, s)) return LightsResult.Unavailable;
        _lamps[index].Settings = s;
        Raise();
        return LightsResult.Ok;
    }

    private LightsResult UpdateSelected(Func<LampSettings, LampSettings> change) =>
        SelectedIndex < 0 ? LightsResult.NoLamp : Update(SelectedIndex, change(_lamps[SelectedIndex].Settings));

    private LightsResult Place(Func<LampPlacement, LampPlacement> change)
    {
        if (SelectedPlacement is not { } at || Anchor() is not { } a) return LightsResult.NoLamp;
        var world = LightsMath.ToWorld(a, change(at));
        return Update(SelectedIndex, _lamps[SelectedIndex].Settings with { Position = ToPos(world) });
    }

    private LightAnchor? Anchor() => _p.Anchor(_p.SelectedPerson());

    private LampView View(int i)
    {
        var s = _lamps[i].Settings;
        return new LampView(i + 1, s.Color, s.Strength, s.Range, s.Enabled, ToVec(s.Position), RoleOf(i));
    }

    private void Raise() => Changed?.Invoke();

    internal static Position3D ToPos(Vector3 v) => new(v.X, v.Y, v.Z);
    internal static Vector3 ToVec(Position3D p) => new(p.X, p.Y, p.Z);
}
