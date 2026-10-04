using System;
using System.Collections.Generic;
using Stellar.PhotoStudio.ReShade;

namespace Stellar.PhotoStudio.Tests.ReShade;

internal sealed class FakePresetFiles : IPresetFiles
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

    public bool WriteNew(string path, string text)
    {
        if (Files.ContainsKey(path)) return false;
        Files[path] = text;
        Writes.Add(path);
        return true;
    }
}
