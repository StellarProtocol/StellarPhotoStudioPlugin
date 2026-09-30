using System;
using System.IO;
using Stellar.Abstractions.Domain;
using Stellar.Abstractions.Services;

namespace Stellar.PhotoStudio;

// Capture tab: output (scale, format, JPG quality, folder, file name), hide toggles, game-photo-mode option.
public sealed partial class Plugin
{
    private bool _editingFolder;
    private string _folderDraft = "";

    private HudElement BuildCaptureTab() => new ColumnElement(new HudElement[]
    {
        new TextElement(() => T("ps.cap.output"), Emphasis: true),
        LabeledRow(() => T("ps.cap.resolution"),
            ScaleButton(1), ScaleButton(2), ScaleButton(4), new SpacerElement(),
            new TextElement(ResolutionText, Color: Muted, Align: TextAlign.Right)),
        LabeledRow(() => T("ps.cap.format"),
            FormatButton(CaptureFormat.Png, "PNG"), FormatButton(CaptureFormat.Jpg, "JPG")),
        new ConditionalElement(() => _settings.Format == CaptureFormat.Jpg, SliderRow(
            () => T("ps.cap.jpgQuality"),
            new SliderElement(() => _settings.JpgQuality, v => _settings.SetJpgQuality((int)MathF.Round(v)), 1f, 100f),
            () => _settings.JpgQuality.ToString(System.Globalization.CultureInfo.InvariantCulture),
            () => _settings.SetJpgQuality(92))),
        LabeledRow(() => T("ps.cap.folder"), new CellElement(new TextElement(FolderDisplay, NoWrap: true), Weight: 1f)),
        new ConditionalElement(() => _folderFellBack && !string.IsNullOrWhiteSpace(_settings.Folder), new RowElement(new HudElement[]
        {
            new SpacerElement(Width: LabelW),
            new TextElement(() => T("ps.cap.folderFallback"), Color: () => _services.Theme.Colors.Warning),
        }, Gap: 6f)),
        FolderButtons(),
        new ConditionalElement(() => _editingFolder, FolderEditor()),
        LabeledRow(() => T("ps.cap.fileName"), new TextElement(NextFileName, Color: Muted, NoWrap: true)),
        new SpacerElement(Height: 6f),
        new SeparatorElement(),
        new RowElement(new HudElement[]
        {
            new TextElement(() => T("ps.cap.hide"), Emphasis: true),
            new SpacerElement(),
            new ButtonElement(() => T("ps.cap.showAll"), OnClick: ShowAllLayers, Width: 88f),
        }, Gap: 6f),
        HideToggle("hide.hud", VisibilityLayers.GameHud, "ps.hide.hud"),
        HideToggle("hide.overlay", VisibilityLayers.StellarOverlay, "ps.hide.overlay"),
        HideToggle("hide.names", VisibilityLayers.Nameplates, "ps.hide.names"),
        HideToggle("hide.others", VisibilityLayers.OtherPlayers, "ps.hide.others"),
        new RowElement(new HudElement[]
        {
            new SpacerElement(Width: 22f),
            new CellElement(KeepPartyToggle(), Weight: 1f),
        }),
        new SpacerElement(Height: 6f),
        new SeparatorElement(),
        new TextElement(() => T("ps.cap.gamePhoto"), Emphasis: true),
        HelpToggle("docked.auto", () => _settings.DockedAuto, _settings.SetDockedAuto,
            new HelpText(() => T("ps.cap.dockedAuto"), () => T("ps.help.dockedAuto"))),
    }, Gap: 6f);

    private HudElement ScaleButton(int s) => new ButtonElement(() => $"{s}×",
        OnClick: () => _settings.SetScale(s), Active: () => _settings.Scale == s, Width: 48f);

    private HudElement FormatButton(CaptureFormat f, string label) => new ButtonElement(() => label,
        OnClick: () => _settings.SetFormat(f), Active: () => _settings.Format == f, Width: 60f);

    private HudElement FolderButtons() => new RowElement(new HudElement[]
    {
        new SpacerElement(Width: LabelW),
        new ButtonElement(() => T("ps.cap.change"), OnClick: BeginFolderEdit, Width: 80f),
        new ButtonElement(() => T("ps.cap.useDefault"), OnClick: () => { _settings.SetFolder(""); _editingFolder = false; }, Width: 96f),
        new ButtonElement(() => T("ps.cap.openFolder"), OnClick: OpenScreenshotFolder, Width: 96f),
    }, Gap: 6f);

    private HudElement FolderEditor() => new ColumnElement(new HudElement[]
    {
        new RowElement(new HudElement[]
        {
            new SpacerElement(Width: LabelW),
            new InputElement(() => _folderDraft, CommitFolder, Width: 220f, OnChange: s => _folderDraft = s),
            new ButtonElement(() => T("ps.ok"), OnClick: () => CommitFolder(_folderDraft), Width: 48f),
            new ButtonElement(() => T("ps.cancel"), OnClick: () => _editingFolder = false, Width: 72f),
        }, Gap: 6f),
        new TextElement(FolderHint, Color: Muted),
    }, Gap: 4f);

    private string FolderDisplay()
    {
        if (string.IsNullOrWhiteSpace(_settings.Folder)) return T("ps.cap.folderDefault");
        return _settings.Folder;
    }

    private string FolderHint() => FolderOpener.IsRunningUnderWine ? T("ps.cap.folderHint") + T("ps.cap.folderHintWine") : T("ps.cap.folderHint");

    private string NextFileName() => $"BPSR_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.{(_settings.Format == CaptureFormat.Jpg ? "jpg" : "png")}";

    private void BeginFolderEdit()
    {
        _folderDraft = _settings.Folder;
        _editingFolder = true;
    }

    private void CommitFolder(string path)
    {
        _settings.SetFolder(path);
        _editingFolder = false;
        EffectiveFolder(out _folderFellBack);
    }

    private void OpenScreenshotFolder() => OpenFolderSafe(EffectiveFolder(out _folderFellBack));

    private void ShowAllLayers()
    {
        _overlayHidden = false;
        _settings.SetHides(VisibilityLayers.None);
        ApplyLiveHides();
    }

    private HudElement HideToggle(string key, VisibilityLayers layer, string labelKey) => HelpToggle(key,
        get: () => LayerAvailable(layer) && IsHidden(layer),
        set: on => SetHidden(layer, on),
        text: new HelpText(() => T(labelKey), () => HideHelp(key, layer)),
        enabled: () => LayerAvailable(layer));

    private string HideHelp(string key, VisibilityLayers layer)
    {
        if (!LayerAvailable(layer)) return T("ps.help.layerUnavailable");
        // The overlay hide hides this panel too; name the live panel hotkey that brings it back.
        return layer == VisibilityLayers.StellarOverlay
            ? _loc.TFormat("ps.help." + key, BindingText("photostudio.panel"))
            : T("ps.help." + key);
    }

    private HudElement KeepPartyToggle() => HelpToggle("hide.party",
        get: () => IsHidden(VisibilityLayers.KeepParty),
        set: on => SetHidden(VisibilityLayers.KeepParty, on),
        text: new HelpText(() => T("ps.hide.party"), () => T("ps.help.hide.party")),
        enabled: () => IsHidden(VisibilityLayers.OtherPlayers) && LayerAvailable(VisibilityLayers.KeepParty));
}
