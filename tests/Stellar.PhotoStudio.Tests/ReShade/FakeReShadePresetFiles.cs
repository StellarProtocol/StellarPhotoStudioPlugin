using System;
using System.Collections.Generic;
using Stellar.PhotoStudio.ReShade;

namespace Stellar.PhotoStudio.Tests.ReShade;

internal sealed class FakeReShadePresetFiles : IReShadePresetFiles
{
    public readonly Dictionary<string, string> Files = new(StringComparer.Ordinal);
    public readonly List<string> Writes = new();
    public int ExistsCalls;

    public bool Exists(string path)
    {
        ExistsCalls++;
        return Files.ContainsKey(path);
    }

    public string ReadAllText(string path) => Files[path];

    public readonly List<string> Replaces = new();

    public bool ReplaceIfEqual(string path, string expected, string text)
    {
        if (!Files.TryGetValue(path, out var now) || !string.Equals(now, expected, StringComparison.Ordinal)) return false;
        Files[path] = text;
        Replaces.Add(path);
        return true;
    }

    public bool WriteNew(string path, string text)
    {
        if (Files.ContainsKey(path)) return false;
        Files[path] = text;
        Writes.Add(path);
        return true;
    }
}
