using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Stellar.Abstractions.Domain;
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

    public void Save(string name, LookSettings look)
    {
        GuardUser(name);
        _files.Write(name, JsonSerializer.Serialize(PresetDto.From(name, look)));
        _user.RemoveAll(p => NameEquals(p.Name, name));
        _user.Add(new Preset(name, false, look));
        Invalidate();
    }

    public void Delete(string name)
    {
        GuardUser(name);
        _files.Delete(name);
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
        Save(to, p.Look);
        Delete(from);
    }

    public string Export(string name) => JsonSerializer.Serialize(PresetDto.From(name, All.Single(p => NameEquals(p.Name, name)).Look));

    /// <summary>Imports a preset. A name collision (built-in or user, case-insensitive) is resolved
    /// the same way for both: auto-suffix " (imported)", then " (imported 2)", " (imported 3)", ...</summary>
    public Preset? Import(string json)
    {
        var dto = Parse(json);
        if (dto is null) return null;
        var name = UniqueName(dto.Name);
        Save(name, dto.ToLook());
        return _user.Single(p => NameEquals(p.Name, name));
    }

    private void Load()
    {
        foreach (var name in _files.List())
        {
            if (BuiltInPresets.All.Any(b => NameEquals(b.Name, name)))
            {
                _warn($"Preset '{name}' has the same name as a built-in look and was skipped.");
                continue;
            }
            var dto = Parse(_files.Read(name));
            if (dto is null) { _warn($"Preset '{name}' could not be read and was skipped."); continue; }
            _user.Add(new Preset(name, false, dto.ToLook()));
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
