using System;
using System.Collections.Generic;
using Stellar.Abstractions.Domain;
using Stellar.Abstractions.Services;
using Stellar.PhotoStudio.ReShade;

namespace Stellar.PhotoStudio;

// Look → ReShade → Presets (spec 2026-10-03 reshade § 12 V4; mockup v3 "In game — Presets"). Logic lives in
// ReShade/PresetCatalog.cs + PresetInstaller.cs. Row 1 is the shader-pack recipe that measured clean at 400 px in every
// locale (name + fixed 104 px status column + "?"); the "Photo Studio" / coverage pill opens line 2 (plan § Placement).
// The framework has no "open a web page" service, so a link-only row shows its address and a "Copy link" button.
public sealed partial class Plugin
{
    private readonly Dictionary<string, (string Sub, string Cov, string Help)> _presetText = new(StringComparer.Ordinal);
    private string? _copiedPresetId;

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
        foreach (var e in PresetCatalog.All)
            rows.Add(new ConditionalElement(() => _settings.PresetsOpen, Indent(PresetRow(e))));
        rows.Add(new ConditionalElement(() => _settings.PresetsOpen, Indent(new RowElement(new HudElement[]
        {
            new CellElement(new TextElement(() => T("ps.rs.presets.import"), Color: Muted), Weight: 1f),
            new CellElement(new ButtonElement(() => T("ps.cap.openFolder"), OnClick: () => OpenFolderSafe(ReShadePresetFolder)),
                Width: PackStatusWidth),   // fixed: an auto-width button clipped its label (sandbox, en/th)
        }, Gap: 6f))));
        return new ColumnElement(rows, Gap: 8f);
    }

    private HudElement PresetRow(PresetEntry e)
    {
        var lines = new List<HudElement>
        {
            new RowElement(new HudElement[]
            {
                new CellElement(new TextElement(() => e.Name, NoWrap: true), Weight: 1f),
                new CellElement(PresetAction(e), Width: PackStatusWidth),
                HelpDot("rs.preset." + e.Id, () => e.Name, () => PresetTexts(e).Help),
            }, Gap: 6f),
            PresetSubline(e),
        };
        if (e.Kind == PresetKind.LinkOnly) lines.Add(new TextElement(() => e.PageUrl, Color: Muted));   // the address, as text
        else lines.Add(new ConditionalElement(() => _rsPresets.Status(e) == PresetStatus.Failed,
            new TextElement(() => PresetFailedText(e), Color: () => _services.Theme.Colors.Warning)));
        return new ColumnElement(lines, Gap: 2f);
    }

    private HudElement PresetSubline(PresetEntry e)
    {
        var sub = new TextElement(() => PresetTexts(e).Sub, Color: Muted);
        if (e.Kind == PresetKind.LinkOnly) return sub;
        // Pill on its own line, then the text: beside the text the coverage pill overflowed at 400 px in every locale
        // (sandbox measure, plan Task 7 Step 5 fallback).
        return new ColumnElement(new HudElement[]
        {
            new RowElement(new HudElement[]
            {
                new PillElement(() => e.Kind == PresetKind.Own ? T("ps.rs.preset.ours") : PresetTexts(e).Cov, Color: () => PresetPillColor(e)),
                new SpacerElement(),
            }),
            sub,
        }, Gap: 2f);
    }

    private ColorRgba? PresetPillColor(PresetEntry e) => e.Kind == PresetKind.Own ? _services.Theme.Colors.Accent
        : e.Partial ? _services.Theme.Colors.Warning : _services.Theme.Colors.TextMuted;

    private HudElement PresetAction(PresetEntry e)
    {
        if (e.Kind == PresetKind.LinkOnly)
            return new ButtonElement(() => _copiedPresetId == e.Id ? T("ps.rs.preset.copied") : T("ps.rs.preset.copyLink"),
                OnClick: () => CopyPresetLink(e));
        return new ConditionalElement(() => PresetButtonKey(e) is not null,
            new ButtonElement(() => T(PresetButtonKey(e) ?? "ps.rs.pack.download"), OnClick: () => _rsPresets.Request(e)),
            new TextElement(() => PresetStatusText(e), Color: Muted, Align: TextAlign.Right, NoWrap: true));
    }

    // IPluginServices has no URL opener (Abstractions 2.17.0). The clipboard call is the IL2CPP-safe one CombatMeter uses.
    private void CopyPresetLink(PresetEntry e)
    {
        UnityEngine.GUIUtility.systemCopyBuffer = e.PageUrl;
        _copiedPresetId = e.Id;
    }

    private string? PresetButtonKey(PresetEntry e) => _rsPresets.Status(e) switch
    {
        PresetStatus.NotInstalled => "ps.rs.pack.download",
        PresetStatus.Failed => "ps.rs.pack.retry",
        _ => null,
    };

    private string PresetStatusText(PresetEntry e) => _rsPresets.Status(e) switch
    {
        PresetStatus.Installed => T("ps.rs.pack.installed"),
        PresetStatus.Queued => T("ps.rs.pack.queued"),
        PresetStatus.Downloading => T("ps.rs.preset.downloading"),
        _ => "",
    };

    private string PresetFailedText(PresetEntry e) => _rsPresets.FailedRequirement(e) is { } needs
        ? _loc.TFormat("ps.rs.pack.failedDep", needs)
        : _loc.TFormat("ps.rs.pack.failed", ReShadeView.Ellipsize(_rsPresets.Error(e) ?? "", 80));

    /// <summary>The row's localized lines, built once per locale (the panel polls them every refresh).</summary>
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

    private static string PresetPackNames(PresetEntry e)
    {
        var names = new List<string>();
        foreach (var p in PresetCatalog.PacksWithRequires(e)) names.Add(p.Name);
        return string.Join(", ", names);
    }
}
