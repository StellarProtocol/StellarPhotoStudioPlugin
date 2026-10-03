using System;
using System.Collections.Generic;
using Stellar.Abstractions.Domain;

namespace Stellar.PhotoStudio.ReShade;

/// <summary>R3: which body the Look tab's ReShade group shows.</summary>
internal enum ReShadePanel { NotInstalled, Unreachable, Loading, Ready }

/// <summary>R4 / D8 (spec § 11.5): which depth hint the group shows.</summary>
internal enum DepthNote { None, SkippedInShape, ScreenDetail }

/// <summary>One effect row: the technique and its label ("Sharpen (CAS)" when the name repeats across effect files).</summary>
internal sealed record FxRow(ReShadeTechnique Technique, string Label);

/// <summary>Pure view rules for the ReShade group (unit-tested; the Plugin partial only draws them).</summary>
internal static class ReShadeView
{
    public static ReShadePanel Panel(ReShadeState state, bool dxgiPresent) => state switch
    {
        ReShadeState.Ready => ReShadePanel.Ready,
        ReShadeState.Loading => ReShadePanel.Loading,
        _ => dxgiPresent ? ReShadePanel.Unreachable : ReShadePanel.NotInstalled,
    };

    /// <summary>A non-Screen shape with an enabled depth effect → the framework skips it for that photo (D8);
    /// Screen at 2×/4× → depth is upscaled screen detail (§ 11.5). <paramref name="isOn"/> includes pending wishes.</summary>
    public static DepthNote Depth(PhotoShape shape, int scale, IReadOnlyList<ReShadeTechnique> techniques, Func<ReShadeTechnique, bool> isOn)
    {
        var anyDepthOn = false;
        foreach (var t in techniques)
            if (t.UsesDepth && isOn(t)) { anyDepthOn = true; break; }
        if (!anyDepthOn) return DepthNote.None;
        if (shape != PhotoShape.Screen) return DepthNote.SkippedInShape;
        return scale > 1 ? DepthNote.ScreenDetail : DepthNote.None;
    }

    /// <summary>Enabled techniques first (the preset's effects), each block by label; labels disambiguate repeated names.</summary>
    public static IReadOnlyList<FxRow> Rows(IReadOnlyList<ReShadeTechnique> techniques)
    {
        var names = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in techniques) names[t.Name] = names.TryGetValue(t.Name, out var n) ? n + 1 : 1;
        var rows = new List<FxRow>(techniques.Count);
        foreach (var t in techniques)
            rows.Add(new FxRow(t, names[t.Name] > 1 ? $"{t.Name} ({EffectName(t.EffectFile)})" : t.Name));
        rows.Sort((a, b) => a.Technique.Enabled != b.Technique.Enabled
            ? (a.Technique.Enabled ? -1 : 1)
            : string.Compare(a.Label, b.Label, StringComparison.OrdinalIgnoreCase));
        return rows;
    }

    public static int EnabledCount(IReadOnlyList<FxRow> rows)
    {
        var n = 0;
        while (n < rows.Count && rows[n].Technique.Enabled) n++;
        return n;
    }

    private static string EffectName(string file) =>
        file.EndsWith(".fx", StringComparison.OrdinalIgnoreCase) ? file.Substring(0, file.Length - 3) : file;
}
