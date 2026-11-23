using System;
using System.Collections.Generic;
using Stellar.Abstractions.Domain;
using Stellar.Abstractions.Services;
using Stellar.PhotoStudio.ReShade;

namespace Stellar.PhotoStudio;

// ReShade › DOWNLOAD PRESETS (approved prototype): a segmented source picker — Photo Studio · Community · Author's page —
// then that source's presets as rows [name / muted "uses …" line] [Download / status / Copy link] [?], and the "got a
// preset .ini?" line with Open folder. The segment writes the three 1.x group folds (ui.reshade.presets.ownOpen /
// communityOpen / linksOpen) so exactly the shown source is "open" — the keys keep working for a rollback.
public sealed partial class Plugin
{
    private static readonly TimeSpan CopiedShownFor = TimeSpan.FromSeconds(2.5);
    private static readonly PresetKind[] PresetGroups = { PresetKind.Own, PresetKind.Community, PresetKind.LinkOnly };
    private static readonly float[] PresetSourceSegW = { 90f, 76f, 100f };
    private readonly Dictionary<string, (string Sub, string Cov, string Help)> _presetText = new(StringComparer.Ordinal);
    private readonly Dictionary<(DownloadFailure, string?), string> _failText = new();
    private string? _copiedPresetId;
    private DateTime _copiedAt;

    private HudElement DownloadPresetsPanel()
    {
        var items = new List<HudElement>
        {
            Segmented(new[] { "ps.rs.preset.ours", "ps.rs.presets.community", "ps.rs.src.author" }, PresetSourceSegW,
                PresetSourceIndex, SetPresetSource),
            NoteLine(Loc("ps.rs.presets.hint")),
        };
        for (var g = 0; g < PresetGroups.Length; g++)
        {
            var kind = PresetGroups[g];
            var entries = new List<HudElement>();
            foreach (var e in PresetCatalog.All)
                if (e.Kind == kind) entries.Add(PresetRow(e));
            var index = g;
            items.Add(new ConditionalElement(() => PresetSourceIndex() == index, new ColumnElement(entries.ToArray(), Gap: 8f)));
        }
        items.Add(new RowElement(new HudElement[]
        {
            new CellElement(NoteLine(Loc("ps.rs.presets.import")), Weight: 1f),
            Pill(PillKind.Neutral, "ps.cap.openFolder", () => OpenFolderSafe(ReShadePresetFolder), 80f),
        }, Gap: RowGap));
        return SectionPanelWithTrailing("ps.rs.download", null,
            new HudElement[] { HelpButton(new HelpKeys("ps.rs.download", "ps.help.rs.presets", SecReShade)) }, items.ToArray());
    }

    /// <summary>The first open 1.x group fold (none open = Photo Studio's own).</summary>
    private int PresetSourceIndex()
    {
        for (var i = 0; i < PresetGroups.Length; i++)
            if (_settings.PresetGroupOpen(PresetGroups[i])) return i;
        return 0;
    }

    private void SetPresetSource(int index)
    {
        for (var i = 0; i < PresetGroups.Length; i++) _settings.SetPresetGroupOpen(PresetGroups[i], i == index);
    }

    private HudElement PresetRow(PresetEntry e)
    {
        var text = new List<HudElement>
        {
            new TextElement(() => e.Name, Color: () => TextColor(), NoWrap: true) { Ellipsize = true },
            NoteLine(() => PresetTexts(e).Sub),
        };
        if (e.Kind == PresetKind.Community && e.Partial)   // full coverage needs no badge; a partial one is worth a glance
            text.Add(new RowElement(new HudElement[] { TagChipText(() => PresetTexts(e).Cov, AccentColor) }, Gap: 0.01f));
        if (e.Kind == PresetKind.LinkOnly)
        {
            var shown = DisplayUrl(e.PageUrl);   // the address, as text (Copy link puts the full one on the clipboard)
            text.Add(NoteLine(() => shown));
        }
        else text.Add(new ConditionalElement(() => _rsPresets.Status(e) == PresetStatus.Failed, NoteLine(() => PresetFailedText(e), CautionColor)));
        return new RowElement(new HudElement[]
        {
            new CellElement(new ColumnElement(text.ToArray(), Gap: 1f), Weight: 1f),
            new CellElement(PresetAction(e), Width: PackStatusWidth),
            HelpButton(new HelpKeys("ps.rs.download", "ps.help.rs.presets", SecReShade, "ps.rs.download") { Body = PresetHelpFn(e) }),
        }, Gap: RowGap);
    }

    private Func<string> PresetHelpFn(PresetEntry e)
    {
        var cache = new Cached<(PresetStatus, string, string)>(_ => PresetHelp(e));
        return () => cache.Get((_rsPresets.Status(e), _rsPresets.Error(e) ?? string.Empty, T("ps.help.rs.presets")));
    }

    internal static string DisplayUrl(string url) =>
        url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? url.Substring(8) : url;

    private HudElement PresetAction(PresetEntry e)
    {
        if (e.Kind == PresetKind.LinkOnly)
            return Pill(PillKind.Neutral, () => ShowsCopied(e) ? T("ps.rs.preset.copied") : T("ps.rs.preset.copyLink"), () => CopyPresetLink(e));
        return new ConditionalElement(() => PresetButtonKey(e) is not null,
            Pill(PillKind.Neutral, () => T(PresetButtonKey(e) ?? "ps.rs.pack.download"), () => _rsPresets.Request(e)),
            new TextElement(() => PresetStatusText(e), Color: () => MutedColor(), Align: TextAlign.Center, NoWrap: true));
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
