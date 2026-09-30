using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Stellar.Abstractions.Domain;
namespace Stellar.PhotoStudio.Presets;

/// <summary>Built-in looks (read-only) plus user-saved looks (JSON via <see cref="IPresetFiles"/>).
/// A corrupt user file is skipped and reported through <c>warn</c> rather than throwing.</summary>
internal sealed class PresetStore
{
    private readonly IPresetFiles _files;
    private readonly Action<string> _warn;
    private readonly List<Preset> _user = new();

    public PresetStore(IPresetFiles files, Action<string> warn)
    {
        _files = files;
        _warn = warn;
        Load();
    }

    public IReadOnlyList<Preset> All => BuiltInPresets.All.Concat(_user).ToList();

    public void Save(string name, LookSettings look)
    {
        GuardUser(name);
        _files.Write(name, JsonSerializer.Serialize(PresetDto.From(name, look)));
        _user.RemoveAll(p => p.Name == name);
        _user.Add(new Preset(name, false, look));
    }

    public void Delete(string name)
    {
        GuardUser(name);
        _files.Delete(name);
        _user.RemoveAll(p => p.Name == name);
    }

    public void Rename(string from, string to)
    {
        var p = _user.Single(x => x.Name == from);
        Save(to, p.Look);
        Delete(from);
    }

    public string Export(string name) => JsonSerializer.Serialize(PresetDto.From(name, All.Single(p => p.Name == name).Look));

    public Preset? Import(string json)
    {
        var dto = Parse(json);
        if (dto is null) return null;
        var name = BuiltInPresets.All.Any(b => b.Name == dto.Name) ? dto.Name + " (imported)" : dto.Name;
        Save(name, dto.ToLook());
        return _user.Single(p => p.Name == name);
    }

    private void Load()
    {
        foreach (var name in _files.List())
        {
            var dto = Parse(_files.Read(name));
            if (dto is null) { _warn($"Preset '{name}' could not be read and was skipped."); continue; }
            _user.Add(new Preset(name, false, dto.ToLook()));
        }
    }

    private static PresetDto? Parse(string? json)
    {
        if (json is null) return null;
        try { return JsonSerializer.Deserialize<PresetDto>(json); }
        catch (JsonException) { return null; }
    }

    private static void GuardUser(string name)
    {
        if (BuiltInPresets.All.Any(b => b.Name == name)) throw new InvalidOperationException("Built-in looks cannot be changed.");
    }
}
