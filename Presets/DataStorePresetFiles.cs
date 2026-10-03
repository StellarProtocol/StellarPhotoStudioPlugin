using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Stellar.Abstractions.Services;
namespace Stellar.PhotoStudio.Presets;

/// <summary>Adapts <see cref="IPluginServices.Data"/> (<see cref="IPluginDataStore"/>) to
/// <see cref="IPresetFiles"/>: each preset is one file under the <c>presets/</c> subdirectory,
/// named <c>&lt;name&gt;.json</c> — one '/' separator, as <see cref="IPluginDataStore"/> requires.</summary>
internal sealed class DataStorePresetFiles : IPresetFiles
{
    private const string Prefix = "presets/";
    private const string Suffix = ".json";

    private readonly IPluginDataStore _store;

    public DataStorePresetFiles(IPluginDataStore store) => _store = store;

    public IEnumerable<string> List() =>
        _store.List(Prefix)
            .Where(n => n.EndsWith(Suffix, StringComparison.Ordinal))
            .Select(n => n.Substring(Prefix.Length, n.Length - Prefix.Length - Suffix.Length));

    public string? Read(string name)
    {
        var bytes = _store.Read(Prefix + name + Suffix);
        return bytes is null ? null : Encoding.UTF8.GetString(bytes);
    }

    public void Write(string name, string json) => _store.Write(Prefix + name + Suffix, Encoding.UTF8.GetBytes(json));

    public void Delete(string name) => _store.Delete(Prefix + name + Suffix);
}
