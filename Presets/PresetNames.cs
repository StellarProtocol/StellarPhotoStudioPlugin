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
    private static readonly string[] Reserved =
    {
        "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>Why <paramref name="name"/> can't be used, or null when it's fine.</summary>
    public static NameProblem? Check(string name)
    {
        var n = name.Trim();
        if (n.Length == 0) return NameProblem.Empty;
        if (n.Length > MaxLength) return NameProblem.TooLong;
        if (n.IndexOfAny(Extra) >= 0 || n.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return NameProblem.BadCharacters;
        if (n.EndsWith(".", StringComparison.Ordinal) || n.Contains("..", StringComparison.Ordinal)) return NameProblem.BadCharacters;
        if (Array.Exists(Reserved, r => string.Equals(r, n, StringComparison.OrdinalIgnoreCase))) return NameProblem.Reserved;   // Windows device names
        return null;
    }

    /// <summary>Turns any name (e.g. one read from an imported file) into a usable one.</summary>
    public static string Sanitize(string name)
    {
        var s = ReplaceBadChars(name.Trim()).TrimEnd('.', ' ').Replace("..", "_");
        if (s.Length > MaxLength) s = s.Substring(0, MaxLength).TrimEnd('.', ' ');
        if (s.Length == 0) return "Imported";
        return Array.Exists(Reserved, r => string.Equals(r, s, StringComparison.OrdinalIgnoreCase)) ? s + "_" : s;
    }

    /// <summary>Whether <paramref name="stem"/> (a file name without its extension, e.g. a ReShade preset's) is one
    /// <see cref="Sanitize"/> would leave unchanged — valid on Windows and Linux — with no length limit (it is not a
    /// preset name; the OS limits a path).</summary>
    public static bool IsSafeFileStem(string stem) =>
        stem.Length > 0 && stem == stem.Trim() && ReplaceBadChars(stem) == stem
        && !stem.EndsWith(".", StringComparison.Ordinal) && !stem.Contains("..", StringComparison.Ordinal)
        && !Array.Exists(Reserved, r => string.Equals(r, stem, StringComparison.OrdinalIgnoreCase));

    private static string ReplaceBadChars(string name)
    {
        var chars = name.ToCharArray();
        var bad = Path.GetInvalidFileNameChars();
        for (var i = 0; i < chars.Length; i++)
            if (Array.IndexOf(Extra, chars[i]) >= 0 || Array.IndexOf(bad, chars[i]) >= 0) chars[i] = '_';
        return new string(chars);
    }
}

internal enum NameProblem { Empty, TooLong, BadCharacters, Reserved }
