using System;
using System.IO;

namespace Stellar.PhotoStudio.Presets;

/// <summary>
/// A preset name becomes a file name (<c>presets/&lt;name&gt;.json</c> in the plugin data store, and
/// <c>exports/&lt;name&gt;.json</c>), so it must be valid on Windows AND Linux: the store silently rejects a bad key,
/// which would show the preset in the list and lose it on relaunch.
/// </summary>
internal static class PresetNames
{
    public const int MaxLength = 48;
    private static readonly char[] Extra = { '/', '\\', ':', '*', '?', '"', '<', '>', '|' };

    /// <summary>Why <paramref name="name"/> can't be used, or null when it's fine.</summary>
    public static NameProblem? Check(string name)
    {
        var n = name.Trim();
        if (n.Length == 0) return NameProblem.Empty;
        if (n.Length > MaxLength) return NameProblem.TooLong;
        if (n.IndexOfAny(Extra) >= 0 || n.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return NameProblem.BadCharacters;
        if (n.EndsWith(".", StringComparison.Ordinal) || n.Contains("..", StringComparison.Ordinal)) return NameProblem.BadCharacters;
        return null;
    }

    /// <summary>Turns any name (e.g. one read from an imported file) into a usable one.</summary>
    public static string Sanitize(string name)
    {
        var chars = name.Trim().ToCharArray();
        var bad = Path.GetInvalidFileNameChars();
        for (var i = 0; i < chars.Length; i++)
            if (Array.IndexOf(Extra, chars[i]) >= 0 || Array.IndexOf(bad, chars[i]) >= 0) chars[i] = '_';
        var s = new string(chars).TrimEnd('.', ' ').Replace("..", "_");
        if (s.Length > MaxLength) s = s.Substring(0, MaxLength).TrimEnd('.', ' ');
        return s.Length == 0 ? "Imported" : s;
    }
}

internal enum NameProblem { Empty, TooLong, BadCharacters }
