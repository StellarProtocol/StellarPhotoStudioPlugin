using System;
using System.Collections.Generic;
using System.IO;

namespace Stellar.PhotoStudio.ReShade;

/// <summary>One line of the grouped "All effects" list: a pack heading (<see cref="Rows"/> set, <see cref="Row"/> null) or
/// one effect (<see cref="Row"/> set).</summary>
internal sealed record FxItem(string Group, FxRow? Row, IReadOnlyList<FxRow>? Rows)
{
    public bool IsHeader => Row is null;
}

/// <summary>Groups the effect rows under their shader pack (owner 2026-10-05: "where are these 2" — FXShaders' and OtisFX's
/// effects were lost in one alphabetical list of 151). Pure: the pack of an effect file comes from <see cref="Index"/>.</summary>
internal static class EffectGroups
{
    /// <summary>Group key for effects from no known pack (the player's own files, or a pack folder that could not be read).</summary>
    public const string Other = "";

    /// <summary>Effect file name → pack name, from each installed pack's effects folder (recursive, case-insensitive). A
    /// file name found in two packs keeps the first, in <paramref name="packs"/> order. Unreadable folders are skipped.</summary>
    public static IReadOnlyDictionary<string, string> Index(IEnumerable<(string Name, string EffectsFolder)> packs)
    {
        var index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, folder) in packs)
        {
            try
            {
                if (!Directory.Exists(folder)) continue;
                foreach (var file in Directory.EnumerateFiles(folder, "*.fx", SearchOption.AllDirectories))
                    index.TryAdd(Path.GetFileName(file), name);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // a pack folder that cannot be listed: its effects fall under Other
            }
        }
        return index;
    }

    /// <summary>The list: one heading per pack that has effects, in <paramref name="packOrder"/> order, Other last; under an
    /// open heading its effects, A–Z by label.</summary>
    public static IReadOnlyList<FxItem> Build(IReadOnlyList<FxRow> rows, IReadOnlyDictionary<string, string> index,
        IReadOnlyList<string> packOrder, Func<string, bool> isOpen)
    {
        var byGroup = new Dictionary<string, List<FxRow>>(StringComparer.Ordinal);
        foreach (var r in rows)
        {
            var group = index.TryGetValue(r.Technique.EffectFile, out var pack) ? pack : Other;
            if (!byGroup.TryGetValue(group, out var list)) byGroup[group] = list = new List<FxRow>();
            list.Add(r);
        }
        var items = new List<FxItem>(rows.Count + byGroup.Count);
        foreach (var group in packOrder) Append(items, group, byGroup, isOpen);
        Append(items, Other, byGroup, isOpen);
        return items;
    }

    private static void Append(List<FxItem> items, string group, Dictionary<string, List<FxRow>> byGroup, Func<string, bool> isOpen)
    {
        if (!byGroup.Remove(group, out var rows)) return;
        rows.Sort((a, b) => string.Compare(a.Label, b.Label, StringComparison.OrdinalIgnoreCase));
        items.Add(new FxItem(group, null, rows));
        if (!isOpen(group)) return;
        foreach (var r in rows) items.Add(new FxItem(group, r, null));
    }
}
