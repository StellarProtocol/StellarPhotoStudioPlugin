using System;
using System.Collections.Generic;
using System.Text;

namespace Stellar.PhotoStudio.ReShade;

/// <summary>
/// Builds the copy of a community preset that ReShade loads (plan D-OVR). Only "PreprocessorDefinitions=" lines change
/// (global and per-effect): every RESHADE_DEPTH_* definition is left out — depth setup tuned for another game; preset
/// definitions beat ReShade.ini's (runtime.cpp l. 1522) — plus the entry's <c>drop</c> names (textures no pack ships).
/// A line left empty is removed. An entry's <c>SetValues</c> (2026-10-04, AcerolaFX's Mask UI) also replace the value of
/// every column-0 "Key=" line of that key, in whichever section it appears. Every other byte — line endings included — is kept, and text with nothing to drop comes
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

    /// <summary>The copy ReShade loads for <paramref name="e"/>: definitions dropped and values set.</summary>
    public static string WorkingCopy(PresetEntry e, string source) => Apply(source, e.DropDefinitions, e.SetValues);

    /// <summary>The copy the transform wrote before <see cref="PresetEntry.SetValues"/> existed (definitions dropped
    /// only) — a working copy still equal to it was never changed by the player and may be brought up to date.</summary>
    public static string PreviousWorkingCopy(PresetEntry e, string source) => Apply(source, e.DropDefinitions);

    /// <summary>True when <paramref name="text"/> has at least one column-0 line of each key in <paramref name="set"/>
    /// and every such line already carries the wanted value (e.g. the player, or ReShade's autosave, already set it).</summary>
    public static bool HasValues(string text, IReadOnlyList<KeyValuePair<string, string>> set)
    {
        foreach (var kv in set)
        {
            var seen = false;
            foreach (var line in text.Split('\n'))
            {
                if (!line.StartsWith(kv.Key + "=", StringComparison.Ordinal)) continue;
                if (!string.Equals(line.Substring(kv.Key.Length + 1).TrimEnd('\r'), kv.Value, StringComparison.Ordinal)) return false;
                seen = true;
            }
            if (!seen) return false;
        }
        return true;
    }

    public static string Apply(string text, IReadOnlyList<string> drop, IReadOnlyList<KeyValuePair<string, string>>? set = null)
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
            // The same column-0 ordinal rule applies to a SetValues key.
            var line = text.Substring(start, end - start);
            var kept = string.CompareOrdinal(text, start, Key, 0, Key.Length) == 0 ? Filter(line, drop) : SetValue(line, set);
            if (sb is null && !ReferenceEquals(kept, line)) sb = new StringBuilder(text.Length).Append(text, 0, start);
            sb?.Append(kept);
            start = end;
        }
        return sb?.ToString() ?? text;
    }

    private static string SetValue(string line, IReadOnlyList<KeyValuePair<string, string>>? set)
    {
        if (set is null) return line;
        foreach (var kv in set)
        {
            if (!line.StartsWith(kv.Key + "=", StringComparison.Ordinal)) continue;
            var eol = line.EndsWith("\r\n", StringComparison.Ordinal) ? "\r\n" : line.EndsWith("\n", StringComparison.Ordinal) ? "\n" : "";
            var value = line.Substring(kv.Key.Length + 1, line.Length - kv.Key.Length - 1 - eol.Length);
            return value == kv.Value ? line : kv.Key + "=" + kv.Value + eol;
        }
        return line;
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
