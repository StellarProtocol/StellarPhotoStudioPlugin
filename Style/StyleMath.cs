using System;
using System.Collections.Generic;
using Stellar.Abstractions.Domain;

namespace Stellar.PhotoStudio;

/// <summary>
/// The pure maths behind the house-style kit (Style\Plugin.Style*.cs): colour compositing and contrast, the recessed
/// label walk, icon-size picking and the segmented-pill geometry. Static and allocation-free on the per-refresh paths,
/// so it is unit-tested directly (tests\StyleMathTests.cs). Formulas: Knowledge Base\Stellar-Design-Language.md §3.
/// </summary>
internal static class StyleMath
{
    internal static readonly ColorRgba White = new(1f, 1f, 1f);
    internal static readonly ColorRgba Black = new(0f, 0f, 0f);
    /// <summary>#1F1F1F: the solid-fill label when white can't reach 3:1 (never a "darkened white" mid grey, which bold
    /// thin text can't carry — fw 8df7f29).</summary>
    internal static readonly ColorRgba SolidDarkLabel = new(0.12f, 0.12f, 0.12f);
    internal const float SolidMinContrast = 3.0f;

    // ── colour ────────────────────────────────────────────────────────────────────────────────────────────────────
    /// <summary>a → b by t (0 = a, 1 = b), opaque.</summary>
    internal static ColorRgba Mix(ColorRgba a, ColorRgba b, float t)
        => new(a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t, 1f);

    /// <summary>Alpha-composite fg (its A = coverage) over an opaque bg.</summary>
    internal static ColorRgba Over(ColorRgba fg, ColorRgba bg) => Mix(bg, fg with { A = 1f }, fg.A);

    internal static float Luminance(ColorRgba c) => 0.2126f * Lin(c.R) + 0.7152f * Lin(c.G) + 0.0722f * Lin(c.B);
    private static float Lin(float v) => v <= 0.04045f ? v / 12.92f : MathF.Pow((v + 0.055f) / 1.055f, 2.4f);

    internal static float ContrastRatio(float la, float lb) => (MathF.Max(la, lb) + 0.05f) / (MathF.Min(la, lb) + 0.05f);

    internal static float Contrast(ColorRgba a, ColorRgba b) => ContrastRatio(Luminance(a), Luminance(b));

    /// <summary>Nudges fg toward white (dark bg) or black (light bg) in 15 % steps until it reaches minRatio against the
    /// opaque bg (ChatTools Plugin.Tabs.cs → every house kit since). At most 12 steps; the result is opaque.</summary>
    internal static ColorRgba EnsureContrast(ColorRgba fg, ColorRgba bg, float minRatio)
    {
        float lb = Luminance(bg);
        var target = lb < 0.179f ? White : Black;
        for (int i = 0; i < 12 && ContrastRatio(Luminance(fg), lb) < minRatio; i++) fg = Mix(fg, target, 0.15f);
        return fg with { A = 1f };
    }

    /// <summary>Label on a SOLID fill (Primary, Danger, the selected segment): white when white reaches 3:1 on the fill
    /// as drawn, otherwise near-black.</summary>
    internal static ColorRgba SolidLabel(ColorRgba fill) => Contrast(White, fill) >= SolidMinContrast ? White : SolidDarkLabel;

    /// <summary>The alpha of a RECESSED (disabled) label (house rule 22, the RaidManager 2026-11-12 recipe): start at
    /// <paramref name="start"/> and walk in 0.04 steps until the label composited over <paramref name="sunken"/> sits
    /// between <paramref name="min"/>:1 and <paramref name="max"/>:1. The cap keeps it clearly dimmer than any
    /// pressable label (those are pushed to ≥ 3:1); the floor lifts it on light themes where it would vanish.</summary>
    internal static float RecessedLabelAlpha(ColorRgba text, ColorRgba sunken, float start, float min, float max)
    {
        float lb = Luminance(sunken), a = start;
        while (a < 1f && ContrastRatio(Luminance(Over(text with { A = a }, sunken)), lb) < min) a = MathF.Min(1f, a + 0.04f);
        while (a > 0.15f && ContrastRatio(Luminance(Over(text with { A = a }, sunken)), lb) > max) a = MathF.Max(0.15f, a - 0.04f);
        return a;
    }

    // ── icons ─────────────────────────────────────────────────────────────────────────────────────────────────────
    /// <summary>The pre-drawn size to show for <paramref name="wantPx"/> screen pixels (the framework IconSizePicker rule,
    /// Stellar-Design-Language §3): an exact drawing (±0.05 px) draws 1:1, else the smallest LARGER one (downscaling stays
    /// crisper than blowing a smaller one up), else the largest. <paramref name="ascending"/> must be sorted; empty → -1.</summary>
    internal static int PickIconSize(IReadOnlyList<int> ascending, float wantPx)
    {
        if (ascending.Count == 0) return -1;
        for (int i = 0; i < ascending.Count; i++)
            if (ascending[i] >= wantPx - 0.05f) return ascending[i];
        return ascending[ascending.Count - 1];
    }

    /// <summary>Indexes the embedded icon resources: "&lt;prefix&gt;&lt;size&gt;.&lt;name&gt;.png" (from
    /// Resources\icons\&lt;size&gt;\&lt;name&gt;.png, csproj LogicalName) → name → its sizes, ascending. Anything else
    /// (a non-numeric size folder, another resource) is skipped.</summary>
    internal static Dictionary<string, int[]> IndexIcons(IEnumerable<string> resourceNames, string prefix)
    {
        var sizes = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        foreach (var res in resourceNames)
        {
            if (!res.StartsWith(prefix, StringComparison.Ordinal) || !res.EndsWith(".png", StringComparison.Ordinal)) continue;
            var rest = res.Substring(prefix.Length, res.Length - prefix.Length - 4);   // "<size>.<name>"
            int dot = rest.IndexOf('.');
            if (dot <= 0 || dot == rest.Length - 1 || !int.TryParse(rest.AsSpan(0, dot), out var px) || px <= 0) continue;
            var name = rest.Substring(dot + 1);
            if (!sizes.TryGetValue(name, out var list)) sizes[name] = list = new List<int>();
            if (!list.Contains(px)) list.Add(px);
        }
        var index = new Dictionary<string, int[]>(sizes.Count, StringComparer.Ordinal);
        foreach (var (name, list) in sizes) { list.Sort(); index[name] = list.ToArray(); }
        return index;
    }

    // ── segmented pills ───────────────────────────────────────────────────────────────────────────────────────────
    // Geometry of the approved prototype's .seg: a recessed 28 px track with a 2 px inset holding 24 px option pills,
    // 2 px apart, each = label box + 9 px either side. Every option's label box is FIXED, so selecting never moves anything.
    internal const float SegTrackInset = 2f;
    internal const float SegGap = 2f;
    internal const float SegPadX = 9f;

    internal static float SegmentPillWidth(float labelWidth) => labelWidth + 2f * SegPadX;

    /// <summary>The whole track's width for the given label boxes (insets + pills + the gaps between them).</summary>
    internal static float SegmentTrackWidth(IReadOnlyList<float> labelWidths)
    {
        float w = 2f * SegTrackInset;
        for (int i = 0; i < labelWidths.Count; i++) w += SegmentPillWidth(labelWidths[i]) + (i > 0 ? SegGap : 0f);
        return w;
    }

    /// <summary>A label box that fits every given string in bold 12 (all locales and states — house rule 10), from the
    /// in-game budget (Stellar-Design-Language §6: ≈7.5 px per Latin char, CJK ≈12.5, Thai ≈8 per code point; Thai
    /// combining marks take no width) plus 10 px spare. Rounded up to a whole unit. An estimate: verify in-game.</summary>
    internal static float FitBoldLabelWidth(IEnumerable<string> texts)
    {
        float widest = 0f;
        foreach (var s in texts) widest = MathF.Max(widest, EstimateBold12(s));
        return MathF.Ceiling(widest + 10f);
    }

    internal static float EstimateBold12(string s)
    {
        float w = 0f;
        foreach (var ch in s)
        {
            if (IsThaiMark(ch)) continue;
            w += ch >= 0x0E00 && ch <= 0x0E7F ? 8f
               : ch >= 0x1100 && (ch <= 0x11FF || (ch >= 0x2E80 && ch <= 0xD7AF) || (ch >= 0xF900 && ch <= 0xFAFF) || (ch >= 0xFF00 && ch <= 0xFFEF)) ? 12.5f
               : 7.5f;
        }
        return w;
    }

    // Thai above/below vowels and tone marks stack on the previous consonant (no advance of their own).
    private static bool IsThaiMark(char ch) => ch == 0x0E31 || (ch >= 0x0E34 && ch <= 0x0E3A) || (ch >= 0x0E47 && ch <= 0x0E4E);
}
