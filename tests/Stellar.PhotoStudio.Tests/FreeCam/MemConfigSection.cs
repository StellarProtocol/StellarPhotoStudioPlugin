using System.Collections.Generic;
using Stellar.Abstractions.Services;

namespace Stellar.PhotoStudio.Tests.FreeCam;

internal sealed class MemConfigSection : IConfigSection
{
    public readonly Dictionary<string, object?> Values = new();
    public int Saves;
    public T? Get<T>(string key, T? defaultValue) => Values.TryGetValue(key, out var v) && v is T t ? t : defaultValue;
    public void Set<T>(string key, T value) => Values[key] = value;
    public void Save() => Saves++;
    public void SaveQuiet() => Saves++;
    public void RemoveByPrefix(string prefix) { }
}
