using System;
using System.Collections.Generic;
using System.IO;
using Stellar.Abstractions.Domain;
using Stellar.Abstractions.Services;
using Stellar.PhotoStudio.ReShade;

namespace Stellar.PhotoStudio;

// ReShade section (2.0.0 — the 1.x Look tab's ReShade group, 4 fold levels deep, as its own section; spec 2026-10-03
// reshade § 6 + § 11, approved prototype "ReShade · ready / not installed"): RESHADE (On chip, Preset pill dropdown +
// Rescan), EFFECTS IN THIS PRESET (tiles with a Depth tag + On/Off; "All effects" opens the grouped virtual list),
// DOWNLOAD PRESETS (Plugin.Section.ReShadePresets.cs) and the SHADER PACKS fold. R3: one panel for NotInstalled /
// Unreachable / first Loading, the full body when Ready — and it STAYS up through a later reload (a preset pick or a pack
// install reloads ReShade). R4: the depth note follows the photo SHAPE. All rules live in ReShade/ReShadeView.cs.
public sealed partial class Plugin
{
    private const string SecReShade = "ps.sec.reshade";
    private const int FxPool = 10;             // virtual list: 8 visible rows + margin for a part-scrolled row
    private const int FxVisibleRows = 8;
    private const int FxPresetRows = 8;        // plain rows for the preset's effects (more → "All effects")
    private const float PackStatusWidth = 104f;
    private int _fxOffset;
    private bool _rsWasReady;                  // body stays up through a reload once ReShade has been Ready
    private IReadOnlyList<ReShadeTechnique>? _rsRowsSource;
    private string? _rsRowsPreset;
    private bool _rsRowsReset;
    private IReadOnlyList<FxRow> _rsRows = Array.Empty<FxRow>();
    private (int Count, string? Lang, string Text) _allFxText;

    private ReShadePanel RsPanel()
    {
        var p = ReShadeView.Panel(_services.ReShade.State, _dxgiPresent);
        if (p == ReShadePanel.Ready) _rsWasReady = true;
        else if (p != ReShadePanel.Loading) _rsWasReady = false;
        return p;
    }

    private bool RsBody() => RsPanel() == ReShadePanel.Ready || (_rsWasReady && RsPanel() == ReShadePanel.Loading);
    private bool RsReloading() => _rsWasReady && RsPanel() == ReShadePanel.Loading;

    private HudElement BuildReShadeSection() => SectionColumn(
        new ConditionalElement(() => RsPanel() == ReShadePanel.NotInstalled,
            SectionPanel("ps.look.reshade", null, new HudElement[] { BodyText(Loc("ps.rs.notInstalled")) })),
        new ConditionalElement(() => RsPanel() == ReShadePanel.Unreachable,
            SectionPanel("ps.look.reshade", null, new HudElement[] { NoteLine(Loc("ps.rs.unreachable"), CautionColor) })),
        new ConditionalElement(() => RsPanel() == ReShadePanel.Loading && !_rsWasReady,
            SectionPanel("ps.look.reshade", null, new HudElement[] { NoteLine(Loc("ps.rs.loading")) })),
        new ConditionalElement(RsBody, new ColumnElement(new HudElement[]
        {
            ReShadeMainPanel(), EffectsPanel(), DownloadPresetsPanel(), PacksPanel(),
        }, Gap: 8f)));

    // ── on/off + preset ─────────────────────────────────────────────────────────────────────────────────────────

    private HudElement ReShadeMainPanel() => LivePanel(UpperLoc("ps.look.reshade"),
        // A preset outside our folder (ReShade's own) is not stored by a look — only on/off (qa re-review).
        () => T(OwnPresetInUse() ? "ps.rs.hint.onOff" : "ps.rs.hint.saved"),
        new HudElement[]
        {
            OnOffChip(() => _rs.Enabled, OnUseReShade, () => !RsReloading()),
            HelpButton(new HelpKeys("ps.rs.use", "ps.help.rs.use", SecReShade)),
        }, new HudElement[]
        {
            new RowElement(new HudElement[]
            {
                new SpacerElement(0f, PillH),
                RowLabel("ps.rs.preset", null, RowLabelW),
                new CellElement(PillDropdown(() => RsOptions().Selected, () => RsOptions().Labels, SelectReShadePreset), Weight: 1f),
                Pill(PillKind.Neutral, "ps.look.rescan", RescanReShadePresets, 56f),
            }, Gap: RowGap),
        });

    private void OnUseReShade(bool on)
    {
        _rs.SetEnabled(on);
        _presetSession.OnReShadeEdited();   // the Look preset remembers on/off (R8)
    }

    private const int MaxPresetLabel = 28;

    private PresetOptions RsOptions() => _rsOptionsCache ??= ShortLabels(ReShadePresets.Options(ReShadePresetFolder, _rsPresetFiles,
        _rs.PresetPath, name => _loc.TFormat("ps.rs.presetOther", name), T("ps.rs.presetNone")));

    private static PresetOptions ShortLabels(PresetOptions o)
    {
        var labels = new string[o.Labels.Count];
        for (var i = 0; i < labels.Length; i++) labels[i] = ReShadeView.Ellipsize(o.Labels[i], MaxPresetLabel);
        return o with { Labels = labels };
    }

    private void SelectReShadePreset(int index)
    {
        var o = RsOptions();
        if (index < 0 || index >= o.Paths.Count || index == o.Selected) return;
        if (ReShadePaths.Same(o.Paths[index], ReShadePaths.PathFor(ReShadePresetFolder, ReShadePresets.NoneFile))) WriteNonePreset();
        _rs.SetPresetPath(o.Paths[index]);
        _rsOptionsCache = null;
        _presetSession.OnReShadeEdited();   // the Look preset remembers the ReShade preset (R8)
    }

    /// <summary>"None" is an empty preset: (re)written empty before it is used, so effects ReShade auto-saved into it while it
    /// was active never come back. A write failure leaves ReShade to create the file itself (it does for a missing preset).</summary>
    private void WriteNonePreset()
    {
        try
        {
            Directory.CreateDirectory(ReShadePresetFolder);
            File.WriteAllText(Path.Combine(ReShadePresetFolder, ReShadePresets.NoneFile), ReShadePresets.NoneContent);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _services.Log.Warning($"[PhotoStudio] could not write the None preset: {ex.Message}");
        }
    }

    private (string? Path, bool Own) _ownPreset;

    /// <summary>Cached on the preset path instance (InPresetsFolder allocates; this runs every refresh).</summary>
    private bool OwnPresetInUse()
    {
        var p = _rs.PresetPath;
        if (!ReferenceEquals(p, _ownPreset.Path)) _ownPreset = (p, p is { Length: > 0 } && !ReShadePaths.InPresetsFolder(p));
        return _ownPreset.Own;
    }

    // ── effects ─────────────────────────────────────────────────────────────────────────────────────────────

    private HudElement EffectsPanel() => LivePanel(UpperLoc("ps.rs.effectsOn"), FxOnCountText, null, new HudElement[]
    {
        new ConditionalElement(RsReloading, NoteLine(Loc("ps.rs.reloading"))),
        new ConditionalElement(() => !RsReloading() && _services.ReShade.Techniques.Count == 0, NoteLine(Loc("ps.rs.noEffects"))),
        new ConditionalElement(() => !RsReloading() && !_settings.AllFxOpen && _services.ReShade.Techniques.Count > 0
            && ReShadeView.EnabledCount(RsRows()) == 0, NoteLine(Loc("ps.rs.presetEmpty"))),   // e.g. None
        new ConditionalElement(() => !RsReloading() && !_settings.AllFxOpen, PresetFxRows()),
        new ConditionalElement(() => !RsReloading() && !_settings.AllFxOpen && ReShadeView.EnabledCount(RsRows()) > FxPresetRows,
            NoteLine(MoreFxText)),
        new ConditionalElement(() => !RsReloading() && _settings.AllFxOpen && RsRows().Count > 0,
            new VirtualListElement(() => FxItems().Count, ListRowPitch, FxPoolRows(), o => _fxOffset = o, Height: ListHeight(FxVisibleRows))
            { ResetScroll = TakeRowsReset }),
        new ConditionalElement(() => RsDepth() == DepthNote.SkippedInShape, NoteLine(DepthSkippedText, CautionColor)),
        new ConditionalElement(() => RsDepth() == DepthNote.ScreenDetail, NoteLine(Loc("ps.rs.depthDetail"))),
        new RowElement(new HudElement[]
        {
            new CellElement(NoteLine(Loc("ps.rs.fineTune")), Weight: 1f),
            // Shown whenever some effect is not on the preset rows — incl. a preset with more effects on than FxPresetRows.
            new ConditionalElement(() => !RsReloading() && RsRows().Count > Math.Min(ReShadeView.EnabledCount(RsRows()), FxPresetRows),
                Pill(PillKind.Neutral, AllFxText, () => _settings.SetAllFxOpen(!_settings.AllFxOpen))),
        }, Gap: RowGap),
    });

    /// <summary>Rows re-arranged only when the framework hands out a new list; the order and the preset's set stay frozen
    /// while only on/off changes (ReShadeView.Rows) and re-sort when the ReShade preset changes.</summary>
    private IReadOnlyList<FxRow> RsRows()
    {
        var live = _services.ReShade.Techniques;
        if (ReferenceEquals(live, _rsRowsSource)) return _rsRows;
        var preset = _services.ReShade.CurrentPreset;
        var samePreset = string.Equals(preset, _rsRowsPreset, StringComparison.OrdinalIgnoreCase);
        _rsRowsSource = live;
        _rsRowsPreset = preset;
        if (!samePreset) _rsRowsReset = true;
        return _rsRows = ReShadeView.Rows(live, samePreset ? _rsRows : null);
    }

    private bool TakeRowsReset()
    {
        if (!_rsRowsReset) return false;
        _rsRowsReset = false;
        return true;
    }

    // "All effects (38)" on the pill; the open state shows as the list itself.
    private string AllFxText()
    {
        var count = RsRows().Count;
        var lang = T("ps.rs.allEffects");
        if (_allFxText.Text is null || _allFxText.Count != count || !ReferenceEquals(_allFxText.Lang, lang))
            _allFxText = (count, lang, _loc.TFormat("ps.rs.allEffects", count));
        return _allFxText.Text;
    }

    private Cached<(int, string)>? _moreFxText, _fxOnText;
    private Cached<(PhotoShape, string)>? _depthSkippedText;

    private string MoreFxText() => (_moreFxText ??= new Cached<(int, string)>(k => _loc.TFormat("ps.rs.moreFx", k.Item1)))
        .Get((ReShadeView.EnabledCount(RsRows()) - FxPresetRows, T("ps.rs.moreFx")));

    private string FxOnCountText()
    {
        var on = 0;
        if (_rs.Enabled)
            foreach (var r in RsRows()) if (_rs.IsOn(r.Technique)) on++;
        return (_fxOnText ??= new Cached<(int, string)>(k => _loc.TFormat("ps.rs.onCount", k.Item1))).Get((on, T("ps.rs.onCount")));
    }

    private string DepthSkippedText() => (_depthSkippedText ??= new Cached<(PhotoShape, string)>(k =>
        _loc.TFormat("ps.rs.depthSkipped", PhotoShapes.RatioLabel(k.Item1)))).Get((_settings.Shape, T("ps.rs.depthSkipped")));

    /// <summary>Localized caches dropped on a language change (perf re-review).</summary>
    private void ResetReShadeTextCaches()
    {
        _allFxText = default;
        _rsOptionsCache = null;
        _presetText.Clear();
        _failText.Clear();
        _fxHeaderText.Clear();
    }

    private DepthNote RsDepth() => ReShadeView.Depth(_settings.Shape, EffectiveScale(), _services.ReShade.Techniques, _rs.IsOnFunc);

    private HudElement PresetFxRows()
    {
        var rows = new HudElement[FxPresetRows];
        for (var i = 0; i < FxPresetRows; i++)
        {
            var slot = i;
            rows[i] = new ConditionalElement(() => slot < ReShadeView.EnabledCount(RsRows()), FxRowElement(() => PresetFxAt(slot)));
        }
        return new ColumnElement(rows, Gap: ListRowGap);
    }

    private FxRow? PresetFxAt(int slot)
    {
        var rows = RsRows();
        return slot < ReShadeView.EnabledCount(rows) ? rows[slot] : null;
    }

    private HudElement[] FxPoolRows()
    {
        var pool = new HudElement[FxPool];
        for (var i = 0; i < FxPool; i++) pool[i] = ListSlot(FxPoolItem(i));   // pack heading or effect row (Plugin.Section.ReShadeGroups.cs)
        return pool;
    }

    // [effect name] [Depth tag] [On/Off] in a raised tile; the name is muted while the effect (or ReShade) is off.
    private HudElement FxRowElement(Func<FxRow?> row) => ListTile(new RowElement(new HudElement[]
    {
        new SpacerElement(8f, ListTileH),
        new CellElement(new TextElement(() => row()?.Label ?? "", NoWrap: true,
            Color: () => _rs.Enabled && row() is { } r && _rs.IsOn(r.Technique) ? TextColor() : MutedColor()) { Ellipsize = true }, Weight: 1f),
        new ConditionalElement(() => row()?.Technique.UsesDepth == true, TagChip("ps.rs.depthTag", CautionColor, TileColor)),
        Chip(() => row() is { } r && _rs.IsOn(r.Technique), on =>
        {
            if (row() is { } r) _rs.SetTechnique(r.Technique, on);   // saved in the ReShade preset (owner O2); arms the R1 gate
        }, ChipStyle.OnOff, () => _rs.Enabled && !RsReloading(), TileColor),
        new SpacerElement(1f, ListTileH),
    }, Gap: 8f));

    // ── shader packs ────────────────────────────────────────────────────────────────────────────────────

    private HudElement PacksPanel()
    {
        var rows = new List<HudElement> { NoteLine(Loc("ps.rs.packsFrom")) };
        foreach (var p in PackCatalog.All) rows.Add(PackRow(p));
        return FoldPanelLive(UpperLoc("ps.rs.packs"), PacksCountText, new FoldFlag(() => _settings.PacksOpen, o => _settings.SetPacksOpen(o)),
            new HudElement[] { HelpButton(new HelpKeys("ps.rs.packs", "ps.help.rs.packs", SecReShade)) }, rows.ToArray());
    }

    private Cached<string>? _packsCount;
    private string PacksCountText() => (_packsCount ??= new Cached<string>(_ => _loc.TFormat("ps.rs.packsCount", PackCatalog.All.Count)))
        .Get(T("ps.rs.packsCount"));

    // [name / muted licence] [Download / Update / Retry or the status, one fixed column so every row lines up] [?].
    private HudElement PackRow(ShaderPack p) => new ColumnElement(new HudElement[]
    {
        new RowElement(new HudElement[]
        {
            new CellElement(new ColumnElement(new HudElement[]
            {
                new TextElement(() => p.Name, Color: () => TextColor(), NoWrap: true) { Ellipsize = true },
                NoteLine(() => LicenseLabel(p)),
            }, Gap: 1f), Weight: 1f),
            new CellElement(new ConditionalElement(() => PackButtonKey(p) is not null,
                Pill(PillKind.Neutral, () => T(PackButtonKey(p) ?? "ps.rs.pack.download"), () => _packs.Request(p)),
                new TextElement(PackStatusFn(p), Color: () => MutedColor(), Align: TextAlign.Center, NoWrap: true)), Width: PackStatusWidth),
            HelpButton(new HelpKeys("ps.rs.packs", "ps.help.rs.pack", SecReShade, "ps.rs.packs") { Body = PackHelpFn(p) }),
        }, Gap: RowGap),
        new ConditionalElement(() => _packs.Status(p) == PackStatus.Failed, NoteLine(() => PackFailedText(p), CautionColor)),
    }, Gap: 2f);

    private Func<string> PackHelpFn(ShaderPack p)
    {
        var cache = new Cached<(PackStatus, string, string)>(_ => PackHelp(p));
        return () => cache.Get((_packs.Status(p), _packs.Error(p) ?? string.Empty, T("ps.help.rs.pack")));
    }

    private string PackHelp(ShaderPack p)
    {
        var help = _loc.TFormat("ps.help.rs.pack", LicenseLabel(p), p.SourceUrl, p.Commit.Substring(0, 7));
        if (p.HelpNoteKey is { } note) help = T(note) + "\n\n" + help;   // the pack's own usage line first, then the facts
        return _packs.Status(p) == PackStatus.Failed ? WithLastError(help, _packs.Error(p)) : help;
    }

    private string PackFailedText(ShaderPack p) => DownloadFailedText(_packs.FailureKind(p), _packs.FailedRequirement(p));

    private Func<string> PackStatusFn(ShaderPack p)
    {
        var cache = new Cached<(PackStatus, int, string)>(k => k.Item1 switch
        {
            PackStatus.Installed => T("ps.rs.pack.installed"),
            PackStatus.Queued => T("ps.rs.pack.queued"),
            PackStatus.Downloading => k.Item2.ToString(System.Globalization.CultureInfo.InvariantCulture) + "%",
            _ => "",
        });
        return () =>
        {
            var s = _packs.Status(p);
            return cache.Get((s, s == PackStatus.Downloading ? (int)Math.Round(_packs.Progress(p) * 100d) : 0, T("ps.rs.pack.installed")));
        };
    }

    private string LicenseLabel(ShaderPack p) => p.License == PackCatalog.MixedLicense ? T("ps.rs.pack.mixed") : p.License;

    private string? PackButtonKey(ShaderPack p) => _packs.Status(p) switch
    {
        PackStatus.NotInstalled => "ps.rs.pack.download",
        PackStatus.UpdateAvailable => "ps.rs.pack.update",
        PackStatus.Failed when _packs.FailureKind(p) != DownloadFailure.Changed => "ps.rs.pack.retry",   // only an update fixes Changed
        _ => null,
    };
}
