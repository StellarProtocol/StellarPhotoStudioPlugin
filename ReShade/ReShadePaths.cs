using System;
using System.IO;

namespace Stellar.PhotoStudio.ReShade;

/// <summary>Path helpers that work the same on Windows and under Wine, and in Linux-hosted unit tests (ReShade reports
/// Windows paths; <see cref="Path.GetFileName(string)"/> on Linux would not split on '\').</summary>
internal static class ReShadePaths
{
    private const string PresetsTail = "/reshade/presets/";

    public static string FileName(string path) => path.Substring(Math.Max(path.LastIndexOf('/'), path.LastIndexOf('\\')) + 1);

    /// <summary>Whether ReShade's <paramref name="reported"/> preset is our preset file <paramref name="ours"/>: equal
    /// ignoring separators and case, or (ReShade may report it relative / under another drive root) both end in
    /// "reshade/presets/&lt;same file&gt;".</summary>
    public static bool Same(string? reported, string ours) => reported is not null && SameNorm(Norm(reported), Norm(ours));

    /// <summary><see cref="Same"/> with both sides already <see cref="Norm"/>alized — the settle check normalizes the
    /// requested path once, when it is requested (perf review).</summary>
    public static bool SameNorm(string a, string b)
    {
        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return true;
        var name = FileName(b);
        // Both end in "/reshade/presets/<same file>" (the tail is compared in place, not concatenated).
        return EndsWithTail(a, name) && EndsWithTail(b, name);
    }

    /// <summary>Whether <paramref name="path"/> has a folder part (a full or relative path, not a bare file name).</summary>
    public static bool HasFolder(string path) => path.IndexOf('/') >= 0 || path.IndexOf('\\') >= 0;

    /// <summary>Whether <paramref name="path"/> is a file in a ".../reshade/presets/" folder (Photo Studio's own presets
    /// folder, however ReShade roots it) — the same rule <see cref="Same"/> uses.</summary>
    public static bool InPresetsFolder(string path)
    {
        var n = Norm(path);
        return EndsWithTail(n, FileName(n));
    }

    public static string Norm(string path) => path.Replace('\\', '/').TrimEnd('/');

    private static bool EndsWithTail(string normPath, string name) =>
        name.Length > 0 && normPath.Length >= name.Length + PresetsTail.Length
        && normPath.EndsWith(name, StringComparison.OrdinalIgnoreCase)
        && string.Compare(normPath, normPath.Length - name.Length - PresetsTail.Length, PresetsTail, 0, PresetsTail.Length,
            StringComparison.OrdinalIgnoreCase) == 0;

    /// <summary>The preset file <paramref name="fileName"/> (file name only — a crafted "../x.ini" stays inside) in <paramref name="folder"/>.</summary>
    public static string PathFor(string folder, string fileName) => Path.Combine(folder, FileName(fileName));
}
