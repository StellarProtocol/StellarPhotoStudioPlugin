using System;
using System.Collections.Generic;
using Stellar.Abstractions.Domain;
using Stellar.Abstractions.Services;
using Stellar.PhotoStudio.ReShade;

namespace Stellar.PhotoStudio;

// Look tab → ReShade group (spec 2026-10-03 reshade § 6 + § 11; mockup assets/2026-10-03-reshade-mockup.html, state
// "In game — Photo Studio Look tab"). R3: one line for NotInstalled / Unreachable / first Loading, the full body when
// Ready — and it STAYS up through a later reload (a preset pick or a pack install reloads ReShade; hiding the body then
// hid the dropdown and the pack progress mid-use — ux-ui review). R4: the depth note follows the photo SHAPE. The preset's
// own effects are plain rows (no inner scroll); "All effects" is a virtual list. All rules live in ReShade/ReShadeView.cs.
public sealed partial class Plugin
{
    private const int FxPool = 8;              // virtual list: 6 visible rows + margin for a part-scrolled row
    private const int FxPresetRows = 8;        // plain rows for the preset's effects (more → "All effects")
    private const float FxRowHeight = 26f;
    private int _fxOffset;
    private bool _rsWasReady;                  // body stays up through a reload once ReShade has been Ready
    private IReadOnlyList<ReShadeTechnique>? _rsRowsSource;
    private string? _rsRowsPreset;
    private bool _rsRowsReset;
    private IReadOnlyList<FxRow> _rsRows = Array.Empty<FxRow>();
    private (bool Open, int Count, string Text) _allFxText;

    private ReShadePanel RsPanel()
    {
        var p = ReShadeView.Panel(_services.ReShade.State, _dxgiPresent);
        if (p == ReShadePanel.Ready) _rsWasReady = true;
        else if (p != ReShadePanel.Loading) _rsWasReady = false;
        return p;
    }

    private bool RsBody() => RsPanel() == ReShadePanel.Ready || (_rsWasReady && RsPanel() == ReShadePanel.Loading);
    private bool RsReloading() => _rsWasReady && RsPanel() == ReShadePanel.Loading;

    private HudElement ReShadeGroup() => new ColumnElement(new HudElement[]
    {
        new RowElement(new HudElement[]
        {
            // Only a state with a body folds; the one-line states have no chevron and no click (ux-ui review).
            new SelectableElement(new RowElement(new HudElement[]
            {
                new TextElement(() => !RsBody() ? "" : _settings.ReShadeOpen ? "▾" : "▸", Width: 14f),
                new TextElement(() => T("ps.look.reshade"), Emphasis: true),
            }, Gap: 4f), OnClick: () => { if (RsBody()) _settings.SetReShadeOpen(!_settings.ReShadeOpen); }),
            new SpacerElement(),
            HelpDot("look.reshade", () => T("ps.look.reshade"), () => T("ps.help.look.reshade")),
        }, Gap: 6f),
        new ConditionalElement(() => RsPanel() == ReShadePanel.NotInstalled, Indent(new TextElement(() => T("ps.rs.notInstalled"), Color: Muted))),
        new ConditionalElement(() => RsPanel() == ReShadePanel.Unreachable,
            Indent(new TextElement(() => T("ps.rs.unreachable"), Color: () => _services.Theme.Colors.Warning))),
        new ConditionalElement(() => RsPanel() == ReShadePanel.Loading && !_rsWasReady, Indent(new TextElement(() => T("ps.rs.loading"), Color: Muted))),
        new ConditionalElement(() => RsBody() && _settings.ReShadeOpen, Indent(ReShadeReadyBody())),
    }, Gap: 4f);

    private static HudElement Indent(HudElement e) => new RowElement(new HudElement[]
    {
        new SpacerElement(Width: 16f),
        new CellElement(e, Weight: 1f),
    });

    private HudElement ReShadeReadyBody() => new ColumnElement(new HudElement[]
    {
        HelpToggle("rs.use", () => _rs.Enabled, OnUseReShade, new HelpText(() => T("ps.rs.use"), () => T("ps.help.rs.use")),
            enabled: () => !RsReloading()),
        LabeledRow(() => T("ps.rs.preset"),
            new CellElement(new DropdownElement(() => RsOptions().Selected, () => RsOptions().Labels, SelectReShadePreset), Weight: 1f),
            new ButtonElement(() => T("ps.look.rescan"), OnClick: RescanReShadePresets, Width: 96f)),
        // A preset outside our folder (ReShade's own) is not stored by a look — only on/off (qa re-review).
        new TextElement(() => OwnPresetInUse() ? T("ps.rs.savedOnOff") : T("ps.rs.savedWithLook"), Color: Muted),
        new ConditionalElement(() => !_settings.AllFxOpen, new TextElement(() => T("ps.rs.effectsOn"))),
        new ConditionalElement(() => RsReloading(), new TextElement(() => T("ps.rs.reloading"), Color: Muted)),
        new ConditionalElement(() => !RsReloading() && _services.ReShade.Techniques.Count == 0,
            new TextElement(() => T("ps.rs.noEffects"), Color: Muted)),
        new ConditionalElement(() => !RsReloading() && !_settings.AllFxOpen, PresetFxRows()),
        new ConditionalElement(() => !RsReloading() && !_settings.AllFxOpen && ReShadeView.EnabledCount(RsRows()) > FxPresetRows,
            new TextElement(MoreFxText, Color: Muted)),
        // Shown whenever some effect is not on the preset rows — incl. a preset with more effects on than FxPresetRows.
        new ConditionalElement(() => !RsReloading() && RsRows().Count > Math.Min(ReShadeView.EnabledCount(RsRows()), FxPresetRows),
            new SelectableElement(new TextElement(AllFxText, Color: Muted), OnClick: () => _settings.SetAllFxOpen(!_settings.AllFxOpen))),
        new ConditionalElement(() => !RsReloading() && _settings.AllFxOpen && RsRows().Count > 0,
            new VirtualListElement(() => FxItems().Count, FxRowHeight, FxPoolRows(), o => _fxOffset = o, Height: FxRowHeight * 6)
            { ResetScroll = TakeRowsReset }),
        new ConditionalElement(() => RsDepth() == DepthNote.SkippedInShape, new TextElement(
            () => _loc.TFormat("ps.rs.depthSkipped", PhotoShapes.RatioLabel(_settings.Shape)), Color: () => _services.Theme.Colors.Warning)),
        new ConditionalElement(() => RsDepth() == DepthNote.ScreenDetail, new TextElement(() => T("ps.rs.depthDetail"), Color: Muted)),
        new TextElement(() => T("ps.rs.fineTune"), Color: Muted),
        new SeparatorElement(),
        PresetsSection(),   // Plugin.Panel.ReShadePresets.cs
        PacksSection(),
    }, Gap: 4f);

    // ── effects ─────────────────────────────────────────────────────────────────────────────────────────

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

    private string AllFxText()
    {
        var count = RsRows().Count;
        if (_allFxText.Text is null || _allFxText.Open != _settings.AllFxOpen || _allFxText.Count != count)
            _allFxText = (_settings.AllFxOpen, count, (_settings.AllFxOpen ? "▾ " : "▸ ") + _loc.TFormat("ps.rs.allEffects", count));
        return _allFxText.Text;
    }

    private (int Count, string Text) _moreFxText;

    private string MoreFxText()
    {
        var more = ReShadeView.EnabledCount(RsRows()) - FxPresetRows;
        if (_moreFxText.Text is null || _moreFxText.Count != more) _moreFxText = (more, _loc.TFormat("ps.rs.moreFx", more));
        return _moreFxText.Text;
    }

    /// <summary>Localized caches dropped on a language change (perf re-review).</summary>
    private void ResetReShadeTextCaches()
    {
        _allFxText = default;
        _moreFxText = default;
        _rsOptionsCache = null;
        _presetText.Clear();
        _failText.Clear();
        _fxHeaderText.Clear();
    }

    private (string? Path, bool Own) _ownPreset;

    /// <summary>Cached on the preset path instance (InPresetsFolder allocates; this runs every refresh).</summary>
    private bool OwnPresetInUse()
    {
        var p = _rs.PresetPath;
        if (!ReferenceEquals(p, _ownPreset.Path)) _ownPreset = (p, p is { Length: > 0 } && !ReShadePaths.InPresetsFolder(p));
        return _ownPreset.Own;
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
        return new ColumnElement(rows, Gap: 0f);
    }

    private FxRow? PresetFxAt(int slot)
    {
        var rows = RsRows();
        return slot < ReShadeView.EnabledCount(rows) ? rows[slot] : null;
    }

    private HudElement[] FxPoolRows()
    {
        var pool = new HudElement[FxPool];
        for (var i = 0; i < FxPool; i++)
        {
            var slot = i;
            pool[i] = FxPoolItem(slot);   // pack heading or effect row (Plugin.Panel.ReShadeGroups.cs)
        }
        return pool;
    }


    private HudElement FxRowElement(Func<FxRow?> row) => new RowElement(new HudElement[]
    {
        new CellElement(new TextElement(() => row()?.Label ?? "", NoWrap: true,
            Color: () => _rs.Enabled && row() is { } r && _rs.IsOn(r.Technique) ? Normal() : MenuMuted()), Weight: 1f),
        new ConditionalElement(() => row()?.Technique.UsesDepth == true,
            new PillElement(() => T("ps.rs.depthTag"), Color: () => _services.Theme.Colors.Warning)),
        new ToggleElement(() => "", () => row() is { } r && _rs.IsOn(r.Technique), on =>
        {
            if (row() is { } r) _rs.SetTechnique(r.Technique, on);   // saved in the ReShade preset (owner O2); arms the R1 gate
        }, Enabled: () => _rs.Enabled && !RsReloading()),
    }, Gap: 6f);

    // ── on/off + preset ─────────────────────────────────────────────────────────────────────────────────

    private void OnUseReShade(bool on)
    {
        _rs.SetEnabled(on);
        _presetSession.OnReShadeEdited();   // the Look preset remembers on/off (R8)
    }

    private const int MaxPresetLabel = 18;   // measured: 24 overflowed the 139 px dropdown at 400 px

    private PresetOptions RsOptions() => _rsOptionsCache ??= ShortLabels(ReShadePresets.Options(ReShadePresetFolder, _rsPresetFiles,
        _rs.PresetPath, name => _loc.TFormat("ps.rs.presetOther", name)));

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
        _rs.SetPresetPath(o.Paths[index]);
        _rsOptionsCache = null;
        _presetSession.OnReShadeEdited();   // the Look preset remembers the ReShade preset (R8)
    }

    // ── shader packs ────────────────────────────────────────────────────────────────────────────────────

    private HudElement PacksSection()
    {
        var rows = new List<HudElement>
        {
            new RowElement(new HudElement[]
            {
                new SelectableElement(new RowElement(new HudElement[]
                {
                    new TextElement(() => _settings.PacksOpen ? "▾" : "▸", Width: 14f),
                    new TextElement(() => T("ps.rs.packs"), Emphasis: true),
                }, Gap: 4f), OnClick: () => _settings.SetPacksOpen(!_settings.PacksOpen)),
                new SpacerElement(),
                HelpDot("rs.packs", () => T("ps.rs.packs"), () => T("ps.help.rs.packs")),
            }, Gap: 6f),
            // Its own wrapping line: on the header row it ran under the "?" at 400 px (sandbox, fil).
            new ConditionalElement(() => _settings.PacksOpen, Indent(new TextElement(() => T("ps.rs.packsFrom"), Color: Muted))),
        };
        foreach (var p in PackCatalog.All)
            rows.Add(new ConditionalElement(() => _settings.PacksOpen, Indent(PackRow(p))));
        return new ColumnElement(rows, Gap: 8f);
    }

    // Status/button sit in ONE fixed-width column so every pack row lines up, and the licence goes on its own muted
    // line: on one line the name + a long localized licence + the status overlapped at 400 px (sandbox, th).
    private const float PackStatusWidth = 116f;

    private HudElement PackRow(ShaderPack p) => new ColumnElement(new HudElement[]
    {
        new RowElement(new HudElement[]
        {
            new CellElement(new TextElement(() => p.Name, NoWrap: true), Weight: 1f),
            new CellElement(new ConditionalElement(() => PackButtonKey(p) is not null,
                new ButtonElement(() => T(PackButtonKey(p) ?? "ps.rs.pack.download"), OnClick: () => _packs.Request(p)),
                new TextElement(() => PackStatusText(p), Color: Muted, Align: TextAlign.Right, NoWrap: true)), Width: PackStatusWidth),
            HelpDot("rs.pack." + p.Id, () => p.Name,
                () => PackHelp(p)),
        }, Gap: 6f),
        new TextElement(() => LicenseLabel(p), Color: Muted),
        new ConditionalElement(() => _packs.Status(p) == PackStatus.Failed, new TextElement(() => PackFailedText(p),
            Color: () => _services.Theme.Colors.Warning)),
    }, Gap: 2f);

    private string PackHelp(ShaderPack p)
    {
        var help = _loc.TFormat("ps.help.rs.pack", LicenseLabel(p), p.SourceUrl, p.Commit.Substring(0, 7));
        if (p.HelpNoteKey is { } note) help = T(note) + "\n\n" + help;   // the pack's own usage line first, then the facts
        return _packs.Status(p) == PackStatus.Failed ? WithLastError(help, _packs.Error(p)) : help;
    }

    private string PackFailedText(ShaderPack p) => DownloadFailedText(_packs.FailureKind(p), _packs.FailedRequirement(p));

    private string PackStatusText(ShaderPack p) => _packs.Status(p) switch
    {
        PackStatus.Installed => T("ps.rs.pack.installed"),
        PackStatus.Queued => T("ps.rs.pack.queued"),
        PackStatus.Downloading => F((float)(_packs.Progress(p) * 100d), "0") + "%",
        _ => "",
    };

    private string LicenseLabel(ShaderPack p) => p.License == PackCatalog.MixedLicense ? T("ps.rs.pack.mixed") : p.License;

    private string? PackButtonKey(ShaderPack p) => _packs.Status(p) switch
    {
        PackStatus.NotInstalled => "ps.rs.pack.download",
        PackStatus.UpdateAvailable => "ps.rs.pack.update",
        PackStatus.Failed when _packs.FailureKind(p) != DownloadFailure.Changed => "ps.rs.pack.retry",   // only an update fixes Changed
        _ => null,
    };
}
