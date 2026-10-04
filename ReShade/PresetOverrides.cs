using System;
using System.Collections.Generic;
using System.Text;

namespace Stellar.PhotoStudio.ReShade;

/// <summary>
/// Builds the copy of a community preset that ReShade loads (plan D-OVR). Only "PreprocessorDefinitions=" lines change
/// (global and per-effect): every RESHADE_DEPTH_* definition is left out — depth setup tuned for another game; preset
/// definitions beat ReShade.ini's (runtime.cpp l. 1522) — plus the entry's <c>drop</c> names (textures no pack ships).
/// A line left empty is removed. Every other byte — line endings included — is kept, and text with nothing to drop comes
/// back as the same instance, so the checked download and an unchanged copy share one sha256. The download itself is
/// never written: the bridge has no definition call, and ReShade's own API would write the preset file anyway
/// (runtime_api.cpp set_preprocessor_definition_for_effect).
/// Licence note: the Stella presets (stella-medium, stella-high) are CC BY-SA 4.0, which permits a modified copy; this
/// adjusted working copy stays on the player's own machine under reshade/presets/ and is never redistributed by Photo
/// Studio — the sha256-checked original at reshade/preset-sources/ is the only copy that could be shared, and it is
/// never touched by this transform.
/// </summary>
internal static class PresetOverrides
{
    public const string DepthPrefix = "RESHADE_DEPTH_";
    private const string Key = "PreprocessorDefinitions=";

    public static string Apply(string text, IReadOnlyList<string> drop)
    {
        StringBuilder? sb = null;
        var start = 0;
        while (start < text.Length)
        {
            var nl = text.IndexOf('\n', start);
            var end = nl < 0 ? text.Length : nl + 1;   // the line including its '\n'
            // Relies on the exact column-0 spelling "PreprocessorDefinitions=" the pinned files use (ReShade's own ini
            // writer always starts a key at column 0, never indented): a line is matched only by this literal ordinal
            // prefix, never by a looser "contains" or case-insensitive check, so a key written any other way is left alone.
            if (string.CompareOrdinal(text, start, Key, 0, Key.Length) == 0)
            {
                var line = text.Substring(start, end - start);
                var kept = Filter(line, drop);
                if (sb is null && !ReferenceEquals(kept, line)) sb = new StringBuilder(text.Length).Append(text, 0, start);
                sb?.Append(kept);
            }
            else sb?.Append(text, start, end - start);
            start = end;
        }
        return sb?.ToString() ?? text;
    }

    private static string Filter(string line, IReadOnlyList<string> drop)
    {
        var eol = line.EndsWith("\r\n", StringComparison.Ordinal) ? "\r\n" : line.EndsWith("\n", StringComparison.Ordinal) ? "\n" : "";
        var defs = line.Substring(Key.Length, line.Length - Key.Length - eol.Length).Split(',');
        var kept = new List<string>(defs.Length);
        foreach (var d in defs)
            if (!Dropped(d, drop)) kept.Add(d);
        if (kept.Count == defs.Length) return line;
        return kept.Count == 0 ? "" : Key + string.Join(",", kept) + eol;
    }

    private static bool Dropped(string definition, IReadOnlyList<string> drop)
    {
        var eq = definition.IndexOf('=');
        var name = (eq < 0 ? definition : definition.Substring(0, eq)).Trim();
        if (name.StartsWith(DepthPrefix, StringComparison.Ordinal)) return true;
        foreach (var d in drop)
            if (string.Equals(name, d, StringComparison.Ordinal)) return true;
        return false;
    }
}
