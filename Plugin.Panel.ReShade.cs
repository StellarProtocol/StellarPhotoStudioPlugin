using System;
using System.Collections.Generic;
using Stellar.Abstractions.Domain;
using Stellar.Abstractions.Services;
using Stellar.PhotoStudio.ReShade;

namespace Stellar.PhotoStudio;

// Look tab → ReShade group (spec 2026-10-03 reshade § 6 + § 11; mockup assets/2026-10-03-reshade-mockup.html, state
// "In game — Photo Studio Look tab"). R3: one line for NotInstalled / Unreachable / Loading, the full body when Ready.
// R4: the depth note follows the photo SHAPE. Effects use the Presets-tab virtual-list recipe (a pack set can list
// dozens of techniques; a full column would rebuild them all). All rules live in ReShade/ReShadeView.cs.
public sealed partial class Plugin
{
    private const int FxPool = 8;              // 6 visible rows + margin for a part-scrolled row
    private const float FxRowHeight = 26f;
    private int _fxOffset;
    private IReadOnlyList<ReShadeTechnique>? _rsRowsSource;
    private IReadOnlyList<FxRow> _rsRows = Array.Empty<FxRow>();

    private ReShadePanel RsPanel() => ReShadeView.Panel(_services.ReShade.State, _dxgiPresent);

    private HudElement ReShadeGroup() => new ColumnElement(new HudElement[]
    {
        new RowElement(new HudElement[]
        {
            new SelectableElement(new RowElement(new HudElement[]
            {
                new TextElement(() => _settings.ReShadeOpen ? "▾" : "▸", Width: 14f),
                new TextElement(() => T("ps.look.reshade"), Emphasis: true),
            }, Gap: 4f), OnClick: () => _settings.SetReShadeOpen(!_settings.ReShadeOpen)),
            new SpacerElement(),
            HelpDot("look.reshade", () => T("ps.look.reshade"), () => T("ps.help.look.reshade")),
        }, Gap: 6f),
        // The one-line states stay visible even when the group is folded (mockup "ReShade not installed").
        new ConditionalElement(() => RsPanel() == ReShadePanel.NotInstalled, Indent(new TextElement(() => T("ps.rs.notInstalled"), Color: Muted))),
        new ConditionalElement(() => RsPanel() == ReShadePanel.Unreachable,
            Indent(new TextElement(() => T("ps.rs.unreachable"), Color: () => _services.Theme.Colors.Warning))),
        new ConditionalElement(() => RsPanel() == ReShadePanel.Loading, Indent(new TextElement(() => T("ps.rs.loading"), Color: Muted))),
        new ConditionalElement(() => RsPanel() == ReShadePanel.Ready && _settings.ReShadeOpen, Indent(ReShadeReadyBody())),
    }, Gap: 4f);

    private static HudElement Indent(HudElement e) => new RowElement(new HudElement[]
    {
        new SpacerElement(Width: 16f),
        new CellElement(e, Weight: 1f),
    });

    private HudElement ReShadeReadyBody() => new ColumnElement(new HudElement[]
    {
        HelpToggle("rs.use", () => _rs.Enabled, OnUseReShade, new HelpText(() => T("ps.rs.use"), () => T("ps.help.rs.use"))),
        LabeledRow(() => T("ps.rs.preset"),
            new CellElement(new DropdownElement(() => RsOptions().Selected, () => RsOptions().Labels, SelectReShadePreset), Weight: 1f),
            new ButtonElement(() => "↻", OnClick: RescanReShadePresets, Width: 28f)),
        new TextElement(() => T("ps.rs.savedWithLook"), Color: Muted),
        new TextElement(() => T("ps.rs.effectsOn"), Color: Muted),
        new ConditionalElement(() => _services.ReShade.Techniques.Count == 0, new TextElement(() => T("ps.rs.noEffects"), Color: Muted)),
        new ConditionalElement(() => FxCount() > 0, new VirtualListElement(FxCount, FxRowHeight, FxPoolRows(), o => _fxOffset = o,
            Height: FxRowHeight * 6)),
        new ConditionalElement(() => RsRows().Count > ReShadeView.EnabledCount(RsRows()), new SelectableElement(new TextElement(
            () => (_settings.AllFxOpen ? "▾ " : "▸ ") + _loc.TFormat("ps.rs.allEffects", RsRows().Count), Color: Muted),
            OnClick: () => _settings.SetAllFxOpen(!_settings.AllFxOpen))),
        new ConditionalElement(() => RsDepth() == DepthNote.SkippedInShape, new TextElement(
            () => _loc.TFormat("ps.rs.depthSkipped", PhotoShapes.RatioLabel(_settings.Shape)), Color: () => _services.Theme.Colors.Warning)),
        new ConditionalElement(() => RsDepth() == DepthNote.ScreenDetail, new TextElement(() => T("ps.rs.depthDetail"), Color: Muted)),
        new TextElement(() => T("ps.rs.fineTune"), Color: Muted),
        new SeparatorElement(),
        PacksSection(),
    }, Gap: 4f);

    // ── effects ─────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Rows rebuilt only when the framework hands out a new technique list (it builds one only on change).</summary>
    private IReadOnlyList<FxRow> RsRows()
    {
        var live = _services.ReShade.Techniques;
        if (ReferenceEquals(live, _rsRowsSource)) return _rsRows;
        _rsRowsSource = live;
        return _rsRows = ReShadeView.Rows(live);
    }

    private int FxCount() => _settings.AllFxOpen ? RsRows().Count : ReShadeView.EnabledCount(RsRows());

    private DepthNote RsDepth() => ReShadeView.Depth(_settings.Shape, EffectiveScale(), _services.ReShade.Techniques, _rs.IsOn);

    private HudElement[] FxPoolRows()
    {
        var pool = new HudElement[FxPool];
        for (var i = 0; i < FxPool; i++)
        {
            var slot = i;
            pool[i] = FxRowElement(slot);
        }
        return pool;
    }

    private FxRow? FxAt(int slot)
    {
        var rows = RsRows();
        var i = _fxOffset + slot;
        return i < rows.Count ? rows[i] : null;
    }

    private HudElement FxRowElement(int slot) => new RowElement(new HudElement[]
    {
        new CellElement(new TextElement(() => FxAt(slot)?.Label ?? "", NoWrap: true,
            Color: () => FxAt(slot) is { } r && _rs.IsOn(r.Technique) ? Normal() : MenuMuted()), Weight: 1f),
        new ConditionalElement(() => FxAt(slot)?.Technique.UsesDepth == true,
            new PillElement(() => T("ps.rs.depthTag"), Color: () => _services.Theme.Colors.Warning)),
        new ToggleElement(() => "", () => FxAt(slot) is { } r && _rs.IsOn(r.Technique), on =>
        {
            if (FxAt(slot) is { } r) _rs.SetTechnique(r.Technique, on);   // saved in the ReShade preset; arms the R1 gate
        }),
    }, Gap: 6f);

    // ── on/off + preset ─────────────────────────────────────────────────────────────────────────────────

    private void OnUseReShade(bool on)
    {
        _rs.SetEnabled(on);
        _presetSession.OnReShadeEdited();   // the Look preset remembers on/off (R8)
    }

    private PresetOptions RsOptions() => _rsOptionsCache ??= ReShadePresets.Options(ReShadePresetFolder, _rsPresetFiles,
        _rs.PresetPath, name => _loc.TFormat("ps.rs.presetOther", name));

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
            Indent(new TextElement(() => T("ps.rs.packsFrom"), Color: Muted)),
        };
        foreach (var p in PackCatalog.All)
            rows.Add(new ConditionalElement(() => _settings.PacksOpen, Indent(PackRow(p))));
        return new ColumnElement(rows, Gap: 4f);
    }

    // Status/button sit in ONE fixed-width column so every pack row lines up, and the licence goes on its own muted
    // line: on one line the name + a long localized licence + the status overlapped at 400 px (sandbox, th).
    private const float PackStatusWidth = 104f;

    private HudElement PackRow(ShaderPack p) => new ColumnElement(new HudElement[]
    {
        new RowElement(new HudElement[]
        {
            new CellElement(new TextElement(() => p.Name, NoWrap: true), Weight: 1f),
            new CellElement(new ConditionalElement(() => PackButtonKey(p) is not null,
                new ButtonElement(() => T(PackButtonKey(p) ?? "ps.rs.pack.download"), OnClick: () => _packs.Request(p)),
                new TextElement(() => PackStatusText(p), Color: Muted, Align: TextAlign.Right, NoWrap: true)), Width: PackStatusWidth),
            HelpDot("rs.pack." + p.Id, () => p.Name,
                () => _loc.TFormat("ps.help.rs.pack", LicenseLabel(p), p.SourceUrl, p.Commit.Substring(0, 7))),
        }, Gap: 6f),
        new TextElement(() => LicenseLabel(p), Color: Muted),
        new ConditionalElement(() => _packs.Status(p) == PackStatus.Failed, new TextElement(
            () => _loc.TFormat("ps.rs.pack.failed", _packs.Error(p) ?? ""), Color: () => _services.Theme.Colors.Warning)),
    }, Gap: 2f);

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
        PackStatus.Failed => "ps.rs.pack.retry",
        _ => null,
    };
}
