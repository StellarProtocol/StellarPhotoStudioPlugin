using System.Collections.Generic;
using Stellar.Abstractions.Domain;

namespace Stellar.PhotoStudio.Lights;

/// <summary>A person's key light and rim as the Person light group shows them. Off = not shown (the values are kept so
/// switching back on restores the player's last choice).</summary>
internal sealed record PersonLightState(bool KeyOn, float KeyDirection, float KeyHeight, bool RimOn, RgbColor RimColor, float RimStrength)
{
    /// <summary>Mockup defaults: key from −60° at +25°, a warm rim at 0.45 — both off until touched.</summary>
    public static readonly PersonLightState Default = new(false, -60f, 25f, false, new RgbColor(1f, 0.45f, 0.15f), 0.45f);

    public PersonLight ToLight() => new(
        KeyOn ? new KeyLight(KeyDirection, KeyHeight) : null,
        RimOn && RimStrength > 0f ? new RimLight(RimColor, RimStrength) : null);
}

/// <summary>Person light (the Lights tab's lower group): key light + rim for the selected person — you, a player (their
/// posed copy when posed) or an NPC (their stand-in). Touching a slider turns that part on.</summary>
internal sealed partial class LightsController
{
    private readonly Dictionary<EntityId, PersonLightState> _people = new();
    private bool _peopleChanged;

    /// <summary>People shown with a key light or rim.</summary>
    public int LitCount
    {
        get
        {
            var n = 0;
            foreach (var s in _people.Values) if (!s.ToLight().IsNone) n++;
            return n;
        }
    }

    /// <summary>The selected person's state (defaults when untouched).</summary>
    public PersonLightState SelectedPersonLight => StateOf(_p.SelectedPerson());

    public PersonLightState StateOf(EntityId person) => _people.TryGetValue(person, out var s) ? s : PersonLightState.Default;

    public LightsResult SetKeyOn(bool on) => Change(s => s with { KeyOn = on });
    public LightsResult SetKeyDirection(float degrees) => Change(s => s with { KeyOn = true, KeyDirection = LightsMath.Wrap(degrees) });
    public LightsResult SetKeyHeight(float degrees) => Change(s => s with { KeyOn = true, KeyHeight = System.Math.Clamp(degrees, LightsMath.MinKeyHeight, LightsMath.MaxKeyHeight) });
    public LightsResult SetRimOn(bool on) => Change(s => s with { RimOn = on });
    public LightsResult SetRimColor(RgbColor color) => Change(s => s with { RimOn = true, RimColor = color });

    public LightsResult SetRimStrength(float strength) =>
        Change(s => s with { RimOn = true, RimStrength = System.Math.Clamp(strength, 0f, LightLimits.MaxRimStrength) });

    /// <summary>The selected person back to normal (both off, defaults).</summary>
    public LightsResult ResetPersonLight()
    {
        var person = _p.SelectedPerson();
        if (person.IsNone) return LightsResult.NoLamp;
        _p.Lights.SetPersonLight(person, PersonLight.None);
        _people.Remove(person);
        Raise();
        return LightsResult.Ok;
    }

    private LightsResult Change(System.Func<PersonLightState, PersonLightState> change)
    {
        var person = _p.SelectedPerson();
        if (person.IsNone) return LightsResult.NoLamp;
        return SetPerson(person, change(StateOf(person)));
    }

    private LightsResult SetPerson(EntityId person, PersonLightState next)
    {
        if (!_p.Lights.SetPersonLight(person, next.ToLight()) && !next.ToLight().IsNone) return LightsResult.Unavailable;
        _people[person] = next;
        _peopleChanged = true;
        Raise();
        return LightsResult.Ok;
    }

    /// <summary>Forgets the lit people the game no longer shows (<paramref name="seen"/> false — they left, or their copy /
    /// model is gone), so <see cref="LitCount"/> and the scene stop counting them (lights review minor). The framework is
    /// told too (its write-back checks every material live). Call on <c>IPosing.Changed</c> and the panel's poll. True when
    /// anyone was dropped.</summary>
    public bool PrunePeople(System.Func<EntityId, bool> seen)
    {
        List<EntityId>? gone = null;
        foreach (var person in _people.Keys)
            if (!seen(person)) (gone ??= new List<EntityId>()).Add(person);
        if (gone is null) return false;
        foreach (var person in gone)
        {
            _people.Remove(person);
            _p.Lights.SetPersonLight(person, PersonLight.None);
        }
        Raise();
        return true;
    }

    private void ClearPeople(bool restore)
    {
        if (restore)
            foreach (var person in _people.Keys) _p.Lights.SetPersonLight(person, PersonLight.None);
        _peopleChanged |= _people.Count > 0;
        _people.Clear();
    }
}
