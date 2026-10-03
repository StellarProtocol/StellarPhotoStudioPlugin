using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Stellar.Abstractions.Domain;
using Stellar.PhotoStudio.ReShade;
namespace Stellar.PhotoStudio.Presets;

/// <summary>Built-in looks (read-only) plus user-saved looks (JSON via <see cref="IPresetFiles"/>).
/// Preset names are compared case-insensitively everywhere — the same identity a case-insensitive
/// file system would give two differently-cased file names. A corrupt user file, or one whose name
/// collides with a built-in, is skipped and reported through <c>warn</c> rather than throwing.
/// <see cref="All"/> is cached and rebuilt only when the user preset list changes.</summary>
internal sealed class PresetStore
{
    private readonly IPresetFiles _files;
    private readonly Action<string> _warn;
    private readonly List<Preset> _user = new();
    private List<Preset>? _allCache;

    public PresetStore(IPresetFiles files, Action<string> warn)
    {
        _files = files;
        _warn = warn;
        Load();
    }

    public IReadOnlyList<Preset> All => _allCache ??= BuiltInPresets.All.Concat(_user).ToList();

    /// <summary>Saves a user preset under <paramref name="name"/>. <see cref="IPresetFiles"/> keys
    /// are exact strings with no case-folding, so if an already-tracked entry matches
    /// <paramref name="name"/> only case-insensitively (a case-only re-save), its OLD stored key is
    /// deleted first — otherwise the old and new casings would both persist as separate files. The
    /// new casing becomes the stored name.</summary>
    public void Save(string name, LookSettings look, PhotoShape? shape = null, Lights.LightsPreset? lights = null, ReShadeChoice? reshade = null)
    {
        GuardUser(name);
        var existing = _user.FirstOrDefault(p => NameEquals(p.Name, name));
        if (existing is not null && existing.Name != name) _files.Delete(existing.Name);
        _files.Write(name, JsonSerializer.Serialize(PresetDto.From(name, look, shape, lights, reshade)));
        _user.RemoveAll(p => NameEquals(p.Name, name));
        _user.Add(new Preset(name, false, look, shape, lights) { ReShade = reshade });
        Invalidate();
    }

    /// <summary>Deletes a user preset. Looks up the tracked entry's OWN stored name (which may differ
    /// in case from <paramref name="name"/>) and deletes THAT key — deleting by the caller's exact
    /// casing would silently no-op against <see cref="IPresetFiles"/>'s exact-string keys, leaving
    /// the file (and the preset, after a restart) alive.</summary>
    public void Delete(string name)
    {
        GuardUser(name);
        var existing = _user.FirstOrDefault(p => NameEquals(p.Name, name));
        _files.Delete(existing?.Name ?? name);
        _user.RemoveAll(p => NameEquals(p.Name, name));
        Invalidate();
    }

    /// <summary>Renames a user preset. A no-op when <paramref name="to"/> is the same name as
    /// <paramref name="from"/> (exactly or only in case) — otherwise a save-then-delete of the same
    /// underlying file, on a case-insensitive file system, would delete the very entry it just wrote.
    /// Throws (with nothing changed) when <paramref name="to"/> collides with a different existing
    /// preset, built-in or user.</summary>
    public void Rename(string from, string to)
    {
        if (NameEquals(from, to)) return;
        if (All.Any(p => NameEquals(p.Name, to)))
            throw new InvalidOperationException("A preset with that name already exists.");
        var p = _user.Single(x => NameEquals(x.Name, from));
        Save(to, p.Look, p.Shape, p.Lights, p.ReShade);
        Delete(from);
    }

    /// <summary>Exports a preset as JSON, keyed on the MATCHED entry's own stored name (not the
    /// caller's possibly differently-cased <paramref name="name"/>) so a re-import round-trips the
    /// real identity.</summary>
    public string Export(string name)
    {
        var p = All.Single(x => NameEquals(x.Name, name));
        return JsonSerializer.Serialize(PresetDto.From(p.Name, p.Look, p.Shape, p.Lights, p.ReShade));
    }

    /// <summary>Imports a preset. A name collision (built-in or user, case-insensitive) is resolved
    /// the same way for both: auto-suffix " (imported)", then " (imported 2)", " (imported 3)", ...</summary>
    public Preset? Import(string json)
    {
        var dto = Parse(json);
        if (dto is null) return null;
        var name = UniqueName(PresetNames.Sanitize(dto.Name));
        Save(name, dto.ToLook(), dto.ToShape(), dto.ToLights(), dto.ToReShade());
        return _user.Single(p => NameEquals(p.Name, name));
    }

    /// <summary>Loads user presets from storage, in ordinal name order so that on-disk case
    /// variants of the same identity (which <see cref="IPresetFiles"/> cannot itself prevent — see
    /// <see cref="Save"/>/<see cref="Delete"/>) resolve deterministically: the ordinal-first one is
    /// kept, the rest are skipped and warned about, same as a built-in-name collision.</summary>
    private void Load()
    {
        foreach (var name in _files.List().OrderBy(n => n, StringComparer.Ordinal))
        {
            if (BuiltInPresets.All.Any(b => NameEquals(b.Name, name)))
            {
                _warn($"Preset '{name}' has the same name as a built-in look and was skipped.");
                continue;
            }
            if (_user.Any(p => NameEquals(p.Name, name)))
            {
                _warn($"Preset '{name}' has the same name as another saved look and was skipped.");
                continue;
            }
            var dto = Parse(_files.Read(name));
            if (dto is null) { _warn($"Preset '{name}' could not be read and was skipped."); continue; }
            _user.Add(new Preset(name, false, dto.ToLook(), dto.ToShape(), dto.ToLights()) { ReShade = dto.ToReShade() });
        }
    }

    private string UniqueName(string baseName)
    {
        if (!NameTaken(baseName)) return baseName;
        var suffixed = baseName + " (imported)";
        if (!NameTaken(suffixed)) return suffixed;
        for (var n = 2; ; n++)
        {
            var candidate = baseName + $" (imported {n})";
            if (!NameTaken(candidate)) return candidate;
        }
    }

    private bool NameTaken(string name) => All.Any(p => NameEquals(p.Name, name));

    private static PresetDto? Parse(string? json)
    {
        if (json is null) return null;
        try { return JsonSerializer.Deserialize<PresetDto>(json); }
        catch (JsonException) { return null; }
    }

    private static void GuardUser(string name)
    {
        if (BuiltInPresets.All.Any(b => NameEquals(b.Name, name))) throw new InvalidOperationException("Built-in looks cannot be changed.");
    }

    private static bool NameEquals(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private void Invalidate() => _allCache = null;
}
