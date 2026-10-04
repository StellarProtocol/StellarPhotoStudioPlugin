using System;
using System.Collections.Generic;
using Stellar.Abstractions.Domain;
using Stellar.Abstractions.Services;
using Stellar.PhotoStudio.ReShade;

namespace Stellar.PhotoStudio;

public sealed partial class Plugin
{
    private const float PresetRowGap = 10f;
    private const float PresetGroupGap = 8f;      // + the section's 8 px gap = 16 px above a group: groups read as sections
    private const float OpenFolderWidth = 120f;   // alone on its row; fil/ja labels outgrew the 104 px status width
    private static readonly TimeSpan CopiedShownFor = TimeSpan.FromSeconds(2.5);
    private static readonly PresetKind[] PresetGroups = { PresetKind.Own, PresetKind.Community, PresetKind.LinkOnly };
    private readonly Dictionary<string, (string Sub, string Cov, string Help)> _presetText = new(StringComparer.Ordinal);
    private readonly Dictionary<(DownloadFailure, string?), string> _failText = new();
    private string? _copiedPresetId;
    private DateTime _copiedAt;

    private HudElement PresetsSection()
    {
        var rows = new List<HudElement>
        {
            new RowElement(new HudElement[]
            {
                new SelectableElement(new RowElement(new HudElement[]
                {
                    new TextElement(() => _settings.PresetsOpen ? "▾" : "▸", Width: 14f),
                    new TextElement(() => T("ps.rs.presets"), Emphasis: true),
                }, Gap: 4f), OnClick: () => _settings.SetPresetsOpen(!_settings.PresetsOpen)),
                new SpacerElement(),
                HelpDot("rs.presets", () => T("ps.rs.presets"), () => T("ps.help.rs.presets")),
            }, Gap: 6f),
            new ConditionalElement(() => _settings.PresetsOpen, Indent(new TextElement(() => T("ps.rs.presets.hint"), Color: Muted))),
        };
        foreach (var kind in PresetGroups)
            rows.Add(new ConditionalElement(() => _settings.PresetsOpen, Indent(PresetGroup(kind))));
        rows.Add(new ConditionalElement(() => _settings.PresetsOpen, Indent(new ColumnElement(new HudElement[]
        {
            new TextElement(() => T("ps.rs.presets.import"), Color: Muted),   // full width: beside the button it wrapped to 5 lines (th)
            new RowElement(new HudElement[]
            {
                new CellElement(new ButtonElement(() => T("ps.cap.openFolder"), OnClick: () => OpenFolderSafe(ReShadePresetFolder)),
                    Width: OpenFolderWidth),   // fixed: an auto-width button clipped its label (sandbox, en/th)
                new SpacerElement(),
            }),
        }, Gap: 4f))));
        return new ColumnElement(rows, Gap: 8f);
    }

    /// <summary>One fold per source: Photo Studio's own (open by default), Community, From the author's page.</summary>
    private HudElement PresetGroup(PresetKind kind)
    {
        var entries = new List<HudElement>();
        foreach (var e in PresetCatalog.All)
            if (e.Kind == kind) entries.Add(PresetRow(e));
        var count = "(" + entries.Count + ")";
        return new ColumnElement(new HudElement[]
        {
            new SpacerElement(Height: PresetGroupGap),
            new SelectableElement(new RowElement(new HudElement[]
            {
                new TextElement(() => _settings.PresetGroupOpen(kind) ? "▾" : "▸", Width: 14f),
                new TextElement(() => PresetGroupLabel(kind), NoWrap: true),
                new TextElement(() => count, Color: Muted, NoWrap: true),
            }, Gap: 4f), OnClick: () => _settings.SetPresetGroupOpen(kind, !_settings.PresetGroupOpen(kind))),
            new ConditionalElement(() => _settings.PresetGroupOpen(kind), Indent(new ColumnElement(entries, Gap: PresetRowGap))),
        }, Gap: 6f);
    }

    private string PresetGroupLabel(PresetKind kind) => kind switch
    {
        PresetKind.Own => T("ps.rs.preset.ours"),
        PresetKind.Community => T("ps.rs.presets.community"),
        _ => T("ps.rs.presets.links"),
    };

    private HudElement PresetRow(PresetEntry e)
    {
        var lines = new List<HudElement>
        {
            new RowElement(new HudElement[]
            {
                new CellElement(new TextElement(() => e.Name, NoWrap: true), Weight: 1f),
                new CellElement(PresetAction(e), Width: PackStatusWidth),
                HelpDot("rs.preset." + e.Id, () => e.Name, () => PresetHelp(e)),
            }, Gap: 6f),
        };
        if (e.Kind == PresetKind.Community && e.Partial)   // full coverage needs no badge; a partial one is worth a glance
            lines.Add(new RowElement(new HudElement[]
            {
                new PillElement(() => PresetTexts(e).Cov),   // menu text on the accent tint: Gold was 2.1:1 on Light (ux-ui)
                new SpacerElement(),
            }));
        lines.Add(new TextElement(() => PresetTexts(e).Sub, Color: Muted));
        if (e.Kind == PresetKind.LinkOnly)
        {
            var shown = DisplayUrl(e.PageUrl);   // the address, as text (Copy link puts the full one on the clipboard)
            lines.Add(new TextElement(() => shown, Color: Muted, NoWrap: true));
        }
        else lines.Add(new ConditionalElement(() => _rsPresets.Status(e) == PresetStatus.Failed,
            new TextElement(() => PresetFailedText(e), Color: () => _services.Theme.Colors.Warning)));
        return new ColumnElement(lines, Gap: 2f);
    }

    internal static string DisplayUrl(string url) =>
        url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? url.Substring(8) : url;

    private HudElement PresetAction(PresetEntry e)
    {
        if (e.Kind == PresetKind.LinkOnly)
            return new ButtonElement(() => ShowsCopied(e) ? T("ps.rs.preset.copied") : T("ps.rs.preset.copyLink"),
                OnClick: () => CopyPresetLink(e));
        return new ConditionalElement(() => PresetButtonKey(e) is not null,
            new ButtonElement(() => T(PresetButtonKey(e) ?? "ps.rs.pack.download"), OnClick: () => _rsPresets.Request(e)),
            new TextElement(() => PresetStatusText(e), Color: Muted, Align: TextAlign.Right, NoWrap: true));
    }

    private void CopyPresetLink(PresetEntry e)
    {
        try { UnityEngine.GUIUtility.systemCopyBuffer = e.PageUrl; }
        catch (Exception) { return; }   // no clipboard (rare under Proton): the address stays readable under the row
        _copiedPresetId = e.Id;
        _copiedAt = DateTime.UtcNow;
    }

    private bool ShowsCopied(PresetEntry e) => _copiedPresetId == e.Id && DateTime.UtcNow - _copiedAt < CopiedShownFor;

    private string? PresetButtonKey(PresetEntry e) => _rsPresets.Status(e) switch
    {
        PresetStatus.NotInstalled => "ps.rs.pack.download",
        PresetStatus.Failed when _rsPresets.FailureKind(e) != DownloadFailure.Changed => "ps.rs.pack.retry",   // only an update fixes Changed
        _ => null,
    };

    private string PresetStatusText(PresetEntry e) => _rsPresets.Status(e) switch
    {
        PresetStatus.Installed => T("ps.rs.pack.installed"),
        PresetStatus.Queued => T("ps.rs.pack.queued"),
        PresetStatus.Downloading => T("ps.rs.preset.downloading"),
        _ => "",
    };

    private string PresetFailedText(PresetEntry e) => DownloadFailedText(_rsPresets.FailureKind(e), _rsPresets.FailedRequirement(e));

    /// <summary>Plain-language failure line for a pack or preset, cached per (kind, requirement): the row asks every frame.
    /// The raw error goes to the row's "?" instead (<see cref="WithLastError"/>).</summary>
    private string DownloadFailedText(DownloadFailure kind, string? needs)
    {
        if (_failText.TryGetValue((kind, needs), out var text)) return text;
        text = kind switch
        {
            DownloadFailure.Requirement => _loc.TFormat("ps.rs.pack.failedDep", needs ?? ""),
            DownloadFailure.Changed => T("ps.rs.fail.changed"),
            DownloadFailure.Network => T("ps.rs.fail.network"),
            _ => T("ps.rs.fail.other"),
        };
        _failText[(kind, needs)] = text;
        return text;
    }

    private string WithLastError(string help, string? error) =>
        string.IsNullOrEmpty(error) ? help : help + "\n\n" + _loc.TFormat("ps.help.rs.lastError", error);

    private string PresetHelp(PresetEntry e) => e.Kind != PresetKind.LinkOnly && _rsPresets.Status(e) == PresetStatus.Failed
        ? WithLastError(PresetTexts(e).Help, _rsPresets.Error(e))
        : PresetTexts(e).Help;

    private (string Sub, string Cov, string Help) PresetTexts(PresetEntry e)
    {
        if (_presetText.TryGetValue(e.Id, out var t)) return t;
        var packs = PresetPackNames(e);
        t = e.Kind switch
        {
            PresetKind.Own => (_loc.TFormat("ps.rs.preset.uses", packs), "",
                _loc.TFormat("ps.help.rs.preset.own", T("ps.rs.preset.desc." + e.Id), packs, e.License)),
            PresetKind.Community => (_loc.TFormat("ps.rs.preset.byLine", e.License, e.Author, packs),
                _loc.TFormat("ps.rs.preset.coverage", e.Covered, e.Total), CommunityPresetHelp(e)),
            _ => (_loc.TFormat("ps.rs.preset.linkLine", e.Author), "",
                _loc.TFormat("ps.help.rs.preset.link", e.Author,
                    T(e.Reason == LinkReason.NoLicence ? "ps.rs.preset.why.noLicence" : "ps.rs.preset.why.ask"), e.PageUrl)),
        };
        _presetText[e.Id] = t;
        return t;
    }

    private string CommunityPresetHelp(PresetEntry e)
    {
        var help = _loc.TFormat("ps.help.rs.preset.community", e.Author, e.License, e.LicenseUrl, e.PageUrl, e.Commit.Substring(0, 7));
        if (e.Partial) help += "\n\n" + _loc.TFormat("ps.help.rs.preset.partial", e.Covered, e.Total);
        if (e.Adjusted) help += "\n\n" + T("ps.help.rs.preset.adjusted");
        return help;
    }

    /// <summary>The packs the preset itself uses (ReShade standard, pulled in as a requirement, is not listed: every
    /// preset would say it, so it said nothing).</summary>
    private static string PresetPackNames(PresetEntry e)
    {
        var names = new List<string>();
        foreach (var id in e.Packs)
            if (PackCatalog.Find(id) is { } p) names.Add(p.Name);
        return string.Join(", ", names);
    }
}
