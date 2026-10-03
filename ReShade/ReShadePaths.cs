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
    public static bool Same(string? reported, string ours)
    {
        if (reported is null) return false;
        var a = Norm(reported);
        var b = Norm(ours);
        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return true;
        var tail = PresetsTail + FileName(b);
        return a.EndsWith(tail, StringComparison.OrdinalIgnoreCase) && b.EndsWith(tail, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The preset file <paramref name="fileName"/> (file name only — a crafted "../x.ini" stays inside) in <paramref name="folder"/>.</summary>
    public static string PathFor(string folder, string fileName) => Path.Combine(folder, FileName(fileName));

    private static string Norm(string path) => path.Replace('\\', '/').TrimEnd('/');
}
