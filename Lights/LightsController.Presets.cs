using System.Collections.Generic;
using Stellar.Abstractions.Domain;

namespace Stellar.PhotoStudio.Lights;

/// <summary>Presets carry the lights (lights spec § 5): lamps RELATIVE to the selected person, the Light-people level, and
/// the selected person's key light and rim. Applying places the lamps around whoever is selected now.</summary>
internal sealed partial class LightsController
{
    /// <summary>The lights as a preset; null when there is nothing to save (no lamp, no key, no rim) or the selected person
    /// cannot be read while lamps exist (their placement would be meaningless).</summary>
    public LightsPreset? Capture()
    {
        var person = SelectedPersonLight;
        var key = person.KeyOn ? new KeyLight(person.KeyDirection, person.KeyHeight) : (KeyLight?)null;
        var rim = person.RimOn && person.RimStrength > 0f ? new RimLight(person.RimColor, person.RimStrength) : (RimLight?)null;
        if (_lamps.Count == 0 && key is null && rim is null) return null;
        var lamps = new List<PresetLamp>(_lamps.Count);
        if (_lamps.Count > 0)
        {
            if (Anchor() is not { } a) return null;
            foreach (var l in _lamps)
            {
                var s = l.Settings;
                lamps.Add(new PresetLamp(LightsMath.FromWorld(a, ToVec(s.Position)), s.Color, s.Strength, s.Range, s.Enabled));
            }
        }
        return new LightsPreset(lamps, PeopleLevel, key, rim);
    }

    /// <summary>Replaces the scene's lamps with the preset's, around the selected person, and sets the level and the
    /// selected person's key / rim. Unavailable when the selected person cannot be read (lamps need a place).</summary>
    public LightsResult Apply(LightsPreset preset)
    {
        var anchor = Anchor();
        if (preset.Lamps.Count > 0 && anchor is null) return LightsResult.Unavailable;
        foreach (var l in _lamps) _p.Lights.RemoveLamp(l.Id);
        _lamps.Clear();
        SelectedIndex = -1;
        SetPeopleLevel(preset.PeopleLevel);
        var result = LightsResult.Ok;
        foreach (var l in preset.Lamps)
        {
            var at = ToPos(LightsMath.ToWorld(anchor!.Value, l.Placement));
            var r = Add(new LampSettings(at, l.Color, l.Strength, l.Range, l.On));
            if (r != LightsResult.Ok) result = r;
        }
        var person = _p.SelectedPerson();
        if (!person.IsNone)
        {
            var s = StateOf(person) with { KeyOn = preset.Key is not null, RimOn = preset.Rim is not null };
            if (preset.Key is { } k) s = s with { KeyDirection = k.Direction, KeyHeight = k.Height };
            if (preset.Rim is { } rim) s = s with { RimColor = rim.Color, RimStrength = rim.Strength };
            SetPerson(person, s);
        }
        Raise();
        return result;
    }
}
