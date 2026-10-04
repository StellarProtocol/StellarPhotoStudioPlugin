using System;
using System.Collections.Generic;
using Stellar.Abstractions.Domain;
using Stellar.Abstractions.Services;
using Stellar.PhotoStudio.ReShade;

namespace Stellar.PhotoStudio;

// "All effects", grouped under each shader pack (owner 2026-10-05). The virtual list shows FxItems: a fold heading per
// pack, then — when open — its effects. Built only when the rows, the pack index or a fold changes; the per-frame text is
// cached per (group, open, on, total).
public sealed partial class Plugin
{
    private IReadOnlyDictionary<string, string>? _fxIndex;   // effect file → pack; null = rebuild (ApplySearchPaths)
    private readonly HashSet<string> _fxOpenGroups = new(StringComparer.Ordinal);
    private string? _fxSeededPreset = "\0";                  // the ReShade preset the open groups were seeded for
    private int _fxGroupsVersion;
    private (IReadOnlyList<FxRow>? Rows, IReadOnlyDictionary<string, string>? Index, int Version) _fxItemsKey;
    private IReadOnlyList<FxItem> _fxItems = Array.Empty<FxItem>();
    private readonly Dictionary<(string, bool, int, int), (string Title, string Count)> _fxHeaderText = new();
    private static readonly string[] FxPackOrder = PackNames();

    private static string[] PackNames()
    {
        var names = new string[PackCatalog.All.Count];
        for (var i = 0; i < names.Length; i++) names[i] = PackCatalog.All[i].Name;
        return names;
    }

    private IReadOnlyList<FxItem> FxItems()
    {
        var rows = RsRows();
        _fxIndex ??= EffectGroups.Index(_packs.InstalledEffectFolders());
        if (!string.Equals(_fxSeededPreset, _rsRowsPreset, StringComparison.OrdinalIgnoreCase)) SeedOpenGroups(rows);
        if (ReferenceEquals(rows, _fxItemsKey.Rows) && ReferenceEquals(_fxIndex, _fxItemsKey.Index) && _fxGroupsVersion == _fxItemsKey.Version)
            return _fxItems;
        _fxItemsKey = (rows, _fxIndex, _fxGroupsVersion);
        return _fxItems = EffectGroups.Build(rows, _fxIndex, FxPackOrder, _fxOpenGroups.Contains);
    }

    // A new ReShade preset opens the packs its effects come from; the rest start folded.
    private void SeedOpenGroups(IReadOnlyList<FxRow> rows)
    {
        _fxSeededPreset = _rsRowsPreset;
        _fxOpenGroups.Clear();
        foreach (var r in rows)
            if (r.InPreset) _fxOpenGroups.Add(_fxIndex!.TryGetValue(r.Technique.EffectFile, out var pack) ? pack : EffectGroups.Other);
        _fxGroupsVersion++;
    }

    private FxItem? FxItemAt(int slot)
    {
        var items = FxItems();
        var i = _fxOffset + slot;
        return i < items.Count ? items[i] : null;
    }

    private HudElement FxPoolItem(int slot) => new ConditionalElement(() => FxItemAt(slot)?.IsHeader == true,
        new SelectableElement(new RowElement(new HudElement[]
        {
            new TextElement(() => FxHeaderText(slot).Title, NoWrap: true),
            new SpacerElement(),
            new TextElement(() => FxHeaderText(slot).Count, Color: Muted, NoWrap: true),
        }, Gap: 6f), OnClick: () => ToggleFxGroup(slot)),
        new RowElement(new HudElement[]   // effects sit one chevron's width in from their pack heading
        {
            new SpacerElement(Width: FxGroupIndent),
            new CellElement(FxRowElement(() => FxItemAt(slot)?.Row), Weight: 1f),
        }));

    private const float FxGroupIndent = 14f;

    private (string Title, string Count) FxHeaderText(int slot)
    {
        if (FxItemAt(slot) is not { IsHeader: true } item) return ("", "");
        var open = _fxOpenGroups.Contains(item.Group);
        var total = item.Rows!.Count;
        var on = 0;
        for (var i = 0; i < total; i++)
            if (_rs.IsOn(item.Rows[i].Technique)) on++;
        var key = (item.Group, open, on, total);
        if (_fxHeaderText.TryGetValue(key, out var text)) return text;
        var name = item.Group.Length == 0 ? T("ps.rs.fx.other") : item.Group;
        text = ((open ? "▾ " : "▸ ") + name,
            on > 0 ? _loc.TFormat("ps.rs.fx.groupCountOn", total, on) : _loc.TFormat("ps.rs.fx.groupCount", total));
        _fxHeaderText[key] = text;
        return text;
    }

    private void ToggleFxGroup(int slot)
    {
        if (FxItemAt(slot) is not { IsHeader: true } item) return;
        if (!_fxOpenGroups.Remove(item.Group)) _fxOpenGroups.Add(item.Group);
        _fxGroupsVersion++;
    }
}
