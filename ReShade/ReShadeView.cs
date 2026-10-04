using System;
using System.Collections.Generic;
using Stellar.Abstractions.Domain;

namespace Stellar.PhotoStudio.ReShade;

/// <summary>R3: which body the Look tab's ReShade group shows.</summary>
internal enum ReShadePanel { NotInstalled, Unreachable, Loading, Ready }

/// <summary>R4 / D8 (spec § 11.5): which depth hint the group shows.</summary>
internal enum DepthNote { None, SkippedInShape, ScreenDetail }

/// <summary>One effect row: the technique and its label ("Sharpen (CAS)" when the name repeats across effect files).</summary>
/// <summary><paramref name="InPreset"/> = enabled when the rows were arranged (frozen until the technique set changes).</summary>
internal sealed record FxRow(ReShadeTechnique Technique, string Label, bool InPreset);

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
    /// Screen at 2×/4× → depth is upscaled screen detail (§ 11.5). <paramref name="isOn"/> includes pending wishes; pass a
    /// cached delegate (<see cref="ReShadeControl.IsOnFunc"/>) — this runs every panel refresh.</summary>
    public static DepthNote Depth(PhotoShape shape, int scale, IReadOnlyList<ReShadeTechnique> techniques, Func<ReShadeTechnique, bool> isOn)
    {
        var anyDepthOn = false;
        for (var i = 0; i < techniques.Count; i++)   // by index: foreach over the interface boxes an enumerator
        {
            var t = techniques[i];
            if (t.UsesDepth && isOn(t)) { anyDepthOn = true; break; }
        }
        if (!anyDepthOn) return DepthNote.None;
        if (shape != PhotoShape.Screen) return DepthNote.SkippedInShape;
        return scale > 1 ? DepthNote.ScreenDetail : DepthNote.None;
    }

    /// <summary>Longest effect label shown; longer names end in "…" (a NoWrap label in a weighted cell is not clipped).</summary>
    public const int MaxLabel = 24;

    /// <summary>Enabled techniques first (the preset's effects), each block by label; labels disambiguate repeated names.
    /// Pass the <paramref name="previous"/> rows: when the technique SET is unchanged (only on/off moved), the order and
    /// the "in this preset" set are kept and each row just takes its live technique — toggling never re-sorts.</summary>
    public static IReadOnlyList<FxRow> Rows(IReadOnlyList<ReShadeTechnique> techniques, IReadOnlyList<FxRow>? previous = null)
    {
        if (previous is not null && Rebind(previous, techniques) is { } kept) return kept;
        var names = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in techniques) names[t.Name] = names.TryGetValue(t.Name, out var n) ? n + 1 : 1;
        var rows = new List<FxRow>(techniques.Count);
        foreach (var t in techniques)
            rows.Add(new FxRow(t, Label(t, names[t.Name] > 1), t.Enabled));
        rows.Sort((a, b) => a.InPreset != b.InPreset
            ? (a.InPreset ? -1 : 1)
            : string.Compare(a.Label, b.Label, StringComparison.OrdinalIgnoreCase));
        return rows;
    }

    /// <summary>Rows in the preset (shown when "All effects" is folded) — they lead the list.</summary>
    public static int EnabledCount(IReadOnlyList<FxRow> rows)
    {
        var n = 0;
        while (n < rows.Count && rows[n].InPreset) n++;
        return n;
    }

    public static string Ellipsize(string s, int max) => s.Length <= max ? s : s.Substring(0, max - 1) + "…";

    /// <summary>A repeated name keeps its "(File)" suffix when shortened — the suffix is what tells the two apart; only a
    /// suffix longer than half the label is shortened along with the name.</summary>
    private static string Label(ReShadeTechnique t, bool repeated)
    {
        if (!repeated) return Ellipsize(t.Name, MaxLabel);
        var suffix = $" ({EffectName(t.EffectFile)})";
        return suffix.Length <= MaxLabel / 2
            ? Ellipsize(t.Name, MaxLabel - suffix.Length) + suffix
            : Ellipsize(t.Name + suffix, MaxLabel);
    }

    private static IReadOnlyList<FxRow>? Rebind(IReadOnlyList<FxRow> previous, IReadOnlyList<ReShadeTechnique> live)
    {
        if (previous.Count != live.Count) return null;
        var byKey = new Dictionary<(string, string), ReShadeTechnique>(live.Count);
        foreach (var t in live) byKey[(t.EffectFile, t.Name)] = t;
        var rows = new List<FxRow>(previous.Count);
        foreach (var r in previous)
        {
            if (!byKey.TryGetValue((r.Technique.EffectFile, r.Technique.Name), out var t)) return null;
            rows.Add(r with { Technique = t });
        }
        return rows;
    }

    private static string EffectName(string file) =>
        file.EndsWith(".fx", StringComparison.OrdinalIgnoreCase) ? file.Substring(0, file.Length - 3) : file;
}
