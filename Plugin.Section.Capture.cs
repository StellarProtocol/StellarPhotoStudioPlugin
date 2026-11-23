using System;
using System.Globalization;
using System.IO;
using Stellar.Abstractions.Domain;
using Stellar.Abstractions.Services;

namespace Stellar.PhotoStudio;

// Capture section (approved prototype, section "Capture"): OUTPUT (resolution, shape, frame guide, format, JPG quality —
// shown RECESSED for PNG instead of hidden), QUALITY (the render-quality levers moved here from the 1.x Look tab; the
// logic is Plugin.Quality.cs), SAVE TO (folder, change / default / open, file name) and GAME PHOTO MODE. The 1.x hide
// toggles moved to the Hide section.
public sealed partial class Plugin
{
    private const string SecCapture = "ps.tab.capture";
    private bool _editingFolder;
    private string _folderDraft = "";

    // Fixed label boxes (house rule 10: size for the longest locale, never reflow). Shape: 305 px track, fits the
    // 309 px a SettingRow leaves at the 600 px window (sandbox maths: 475 inner − label 110 − "?" 24 − gaps 32).
    private static readonly float[] ScaleSegW = { 22f, 22f, 22f };
    private static readonly float[] ShapeSegW = { 48f, 27f, 27f, 27f, 27f, 27f };
    private static readonly float[] FormatSegW = { 32f, 32f };
    private static readonly float[] ModeSegW = { 32f, 72f, 48f };   // Off · Composing · Always (fil ellipsizes)
    private static readonly int[] Scales = { 1, 2, 4 };
    private static readonly PhotoShape[] Shapes =
    {
        PhotoShape.Screen, PhotoShape.Portrait9x16, PhotoShape.Portrait4x5, PhotoShape.Portrait2x3, PhotoShape.Square, PhotoShape.Wide21x9,
    };

    private HudElement BuildCaptureSection() => SectionColumn(
        LivePanel(UpperLoc("ps.cap.output"), ResolutionText, null, new HudElement[]
        {
            SettingRow("ps.cap.resolution", SegmentedText(new Func<string>[] { static () => "1×", static () => "2×", static () => "4×" },
                ScaleSegW, () => Array.IndexOf(Scales, _settings.Scale), i => _settings.SetScale(Scales[i]))),
            SettingRow("ps.cap.shape", SegmentedText(ShapeLabels(), ShapeSegW, () => Array.IndexOf(Shapes, _settings.Shape),
                i => _settings.SetShape(Shapes[i]))),
            SettingToggle("ps.cap.frameGuide", () => _settings.ShowFrameGuide, _settings.SetShowFrameGuide,
                new HelpKeys("ps.cap.frameGuide", "ps.help.frameGuide", SecCapture, "ps.cap.output")),
            SettingRow("ps.cap.format", SegmentedText(new Func<string>[] { static () => "PNG", static () => "JPG" }, FormatSegW,
                () => _settings.Format == CaptureFormat.Jpg ? 1 : 0, i => _settings.SetFormat(i == 1 ? CaptureFormat.Jpg : CaptureFormat.Png))),
            SettingSlider("ps.cap.jpgQuality",
                KitSlider(() => _settings.JpgQuality, v => _settings.SetJpgQuality((int)MathF.Round(v)), 1f, 100f,
                    () => _settings.Format == CaptureFormat.Jpg),
                JpgQualityText, () => _settings.SetJpgQuality(92), new SliderOpts(Step: 1f)),
            NoteLine(Loc("ps.cap.jpgNote")),
        }),
        SectionPanelWithTrailing("ps.cap.quality", "ps.cap.qualityHint",
            new HudElement[] { HelpButton(new HelpKeys("ps.cap.quality", "ps.help.q.title", SecCapture)) }, new HudElement[]
        {
            ModeRow("ps.q.ssaa", "ps.help.q.ssaa", () => _quality.Supersample, SetSupersample, () => _services.RenderQuality.Capabilities.RenderScale),
            ModeRow("ps.q.shadows", "ps.help.q.shadows", () => _quality.Shadows, SetShadows, () => _services.RenderQuality.Capabilities.Shadows),
            SettingToggle("ps.q.boost", () => _quality.BoostForCapture, SetBoostForCapture,
                new HelpKeys("ps.q.boost", "ps.help.q.boost", SecCapture, "ps.cap.quality"),
                enabled: () => _services.RenderQuality.Capabilities.Shadows),
            NoteLine(LiveQualityText),
        }),
        SectionPanel("ps.cap.saveTo", null, new HudElement[]
        {
            FolderTile(),
            new ConditionalElement(() => _folderFellBack && !string.IsNullOrWhiteSpace(_settings.Folder),
                NoteLine(Loc("ps.cap.folderFallback"), CautionColor)),
            new RowElement(new HudElement[]
            {
                Pill(PillKind.Neutral, "ps.cap.change", BeginFolderEdit, 72f),
                Pill(PillKind.Neutral, "ps.cap.useDefault", () => { _settings.SetFolder(""); _editingFolder = false; }, 80f,
                    enabled: () => !string.IsNullOrWhiteSpace(_settings.Folder)),
                Pill(PillKind.Neutral, "ps.cap.openFolder", OpenScreenshotFolder, 80f),
            }, Gap: 8f),
            new ConditionalElement(() => _editingFolder, FolderEditor()),
            NoteLine(FileNameLine),
        }),
        SectionPanel("ps.cap.gamePhoto", null, new HudElement[]
        {
            SettingToggle("ps.cap.dockedAuto", () => _settings.DockedAuto, _settings.SetDockedAuto,
                new HelpKeys("ps.cap.dockedAuto", "ps.help.dockedAuto", SecCapture, "ps.cap.gamePhoto")),
        }));

    private Func<string>[] ShapeLabels()
    {
        var labels = new Func<string>[Shapes.Length];
        labels[0] = Loc("ps.cap.shape.screen");
        for (var i = 1; i < Shapes.Length; i++)
        {
            var text = PhotoShapes.RatioLabel(Shapes[i]);   // "9:16" — the same in every language
            labels[i] = () => text;
        }
        return labels;
    }

    /// <summary>[label] [Off | Composing | Always] … [?] — recessed when the lever is unavailable on this client.</summary>
    private HudElement ModeRow(string labelKey, string helpKey, Func<QualityMode> get, Action<QualityMode> set, Func<bool> available)
        => SettingRow(labelKey,
            Segmented(new[] { "ps.q.off", "ps.q.composing", "ps.q.always" }, ModeSegW, () => (int)get(), i => set((QualityMode)i), available),
            new HelpKeys(labelKey, helpKey, SecCapture, "ps.cap.quality") { Body = () => T(available() ? helpKey : "ps.help.q.unavailable") },
            available);

    private Cached<int>? _jpgText;
    private string JpgQualityText() => (_jpgText ??= new Cached<int>(static q => q.ToString(CultureInfo.InvariantCulture))).Get(_settings.JpgQuality);

    // The folder in a raised tile (prototype: "Default · stellar\screenshots"), one line, ellipsized.
    private HudElement FolderTile() => new BubbleElement(new RowElement(new HudElement[]
        {
            new SpacerElement(0f, PillH),
            new CellElement(new TextElement(FolderDisplay, Color: () => string.IsNullOrWhiteSpace(_settings.Folder) ? MutedColor() : TextColor(),
                NoWrap: true) { Ellipsize = true }, Weight: 1f),
        }, Gap: 0.01f))
    {
        FillColorFn = () => Fade(TileColor()), CornerRadius = TileRadius, PaddingX = 8f, PaddingY = 0f,
    };

    private HudElement FolderEditor() => new ColumnElement(new HudElement[]
    {
        new RowElement(new HudElement[]
        {
            new CellElement(new InputElement(() => _folderDraft, CommitFolder, Width: 240f, OnChange: s => _folderDraft = s), Weight: 1f),
            Pill(PillKind.Primary, "ps.ok", () => CommitFolder(_folderDraft), 40f),
            Pill(PillKind.Neutral, "ps.cancel", () => _editingFolder = false, 56f),
        }, Gap: 8f),
        NoteLine(FolderHint),
    }, Gap: 6f);

    private string FolderDisplay() => string.IsNullOrWhiteSpace(_settings.Folder) ? T("ps.cap.folderDefault") : _settings.Folder;

    private Cached<(string, string, bool)>? _folderHint;
    private string FolderHint() => (_folderHint ??= new Cached<(string, string, bool)>(k => k.Item3 ? k.Item1 + k.Item2 : k.Item1))
        .Get((T("ps.cap.folderHint"), T("ps.cap.folderHintWine"), FolderOpener.IsRunningUnderWine));

    // "File name: BPSR_2026-11-23_14-05-09.png" — the clock ticks once a second, so the text is rebuilt once a second.
    private Cached<(long, CaptureFormat, string)>? _fileNameLine;

    private string FileNameLine()
    {
        var now = DateTime.Now;
        _fileNameLine ??= new Cached<(long, CaptureFormat, string)>(k => _loc.TFormat("ps.cap.fileNameLine",
            $"BPSR_{new DateTime(k.Item1 * TimeSpan.TicksPerSecond):yyyy-MM-dd_HH-mm-ss}.{(k.Item2 == CaptureFormat.Jpg ? "jpg" : "png")}"));
        return _fileNameLine.Get((now.Ticks / TimeSpan.TicksPerSecond, _settings.Format, T("ps.cap.fileNameLine")));
    }

    private void BeginFolderEdit()
    {
        _folderDraft = _settings.Folder;
        _editingFolder = true;
    }

    private void CommitFolder(string path)
    {
        path = path.Trim().Trim('"');
        if (path.Length > 0)
        {
            if (path.IndexOf('"') >= 0) { _view.ShowError(T("ps.cap.folderInvalid")); return; }
            try { path = Path.GetFullPath(path); }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                _view.ShowError(T("ps.cap.folderInvalid"));
                return;
            }
        }
        _settings.SetFolder(path);
        _editingFolder = false;
        EffectiveFolder(out _folderFellBack);
    }

    private void OpenScreenshotFolder() => OpenFolderSafe(EffectiveFolder(out _folderFellBack));
}
