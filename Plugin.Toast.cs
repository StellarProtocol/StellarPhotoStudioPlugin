using System;
using System.IO;
using Stellar.Abstractions.Domain;
using Stellar.Abstractions.Services;
using Stellar.PhotoStudio.ReShade;

namespace Stellar.PhotoStudio;

// The "Saved" toast (INotifications can't carry a button, so this is a small plugin window with Open folder)
// and the shutter flash (a borderless full-screen blink — capture renders the camera only, so it is never in
// the image).
public sealed partial class Plugin
{
    private string _toastTitle = "";
    private string _toastFile = "";
    private string _toastDetail = "";
    private string _toastWarning = "";
    private string _toastDir = "";

    private IWindowControl RegisterToastWindow() => _services.Windows.Register(new WindowRegistration(
        new WindowSpec(
            Id: "photostudio.toast",
            Title: T("ps.toast.saved"),
            DefaultRect: new WindowRect(0f, -170f, 460f, 0f),
            Category: WindowCategory.Tools,
            Style: WindowPanelStyle.GlassMenu)
        { ShowTitleBar = false, StartVisible = false, Anchor = WindowAnchor.Bottom, ShouldRender = InWorld },
        // Approved prototype § 4 "Saved toast": [Saved chip] [file name] … [Open folder] [✕], then the size line and any
        // warning. The chip carries the title (Saved / Exported), so it is one bold green wash either way.
        new ColumnElement(new HudElement[]
        {
            new RowElement(new HudElement[]
            {
                TagChipText(() => _toastTitle, SuccessColor, WindowBg),
                new CellElement(new TextElement(() => _toastFile, Color: () => TextColor(), NoWrap: true) { Ellipsize = true }, Weight: 1f),
                Pill(PillKind.Neutral, "ps.cap.openFolder", () => OpenFolderSafe(_toastDir), 80f, surface: WindowBg),
                IconButton("✕", HideToast, IconButtonKind.Bare, surface: WindowBg),
            }, Gap: 8f),
            NoteLine(() => _toastDetail),
            new ConditionalElement(() => _toastWarning.Length > 0, NoteLine(() => _toastWarning, CautionColor)),
        }, Gap: 6f)));

    private void HideToast()
    {
        _toastLeft = 0f;
        _toastWin.SetVisible(false);
        _dockedWin.MarkDirty();   // the strip's saved line goes too
    }

    private IWindowControl RegisterFlashWindow() => _services.Windows.Register(new WindowRegistration(
        new WindowSpec(
            Id: "photostudio.flash",
            Title: "",
            DefaultRect: new WindowRect(0f, 0f, Math.Max(1, _services.Framework.ScreenWidth), Math.Max(1, _services.Framework.ScreenHeight)),
            Category: WindowCategory.Tools,
            Style: WindowPanelStyle.Borderless)
        { ShowTitleBar = false, StartVisible = false, BackgroundOpacity = () => _flash, ShouldRender = InWorld },
        new SpacerElement()));

    private void ShowToast(CaptureResult r)
    {
        var size = r.Path is not null && File.Exists(r.Path) ? $" · {FormatBytes(new FileInfo(r.Path).Length)}" : "";
        var warning = _folderFellBack ? T("ps.toast.folderFallback") : "";
        var screenLong = Math.Max(_services.Framework.ScreenWidth, _services.Framework.ScreenHeight);
        // A 1× photo forced by screen-size-only ReShade effects is not a memory shortfall: its own note says why.
        var shortfall = AnyNote(r.Notes, n => n == ReShadeInfo.ScreenSizeOnlyNote) ? CaptureShortfall.None
            : ShapeFrame.Shortfall(_lastPlan.Shape, new CaptureSize(r.Width, r.Height), _lastPlan.Planned, screenLong, _lastPlan.RequestedScale);
        switch (shortfall)
        {
            case CaptureShortfall.ScaleCapped: warning = T("ps.toast.cappedSize"); break;
            case CaptureShortfall.Retried2x: warning = T("ps.toast.retried2x"); break;
            case CaptureShortfall.ShapeCapped: warning = T("ps.toast.shapeCapped"); break;
        }
        _toastWarning = ReShadeInfo.JoinWarnings(warning, r.Notes, T);   // R2: capture notes reach the player
        ShowFileToast(T("ps.toast.saved"), r.Path ?? "", $"{r.Width} × {r.Height} · {FormatName()}{size}");
    }

    private void ShowFileToast(string title, string path, string detail)
    {
        _toastTitle = title;
        _toastFile = Path.GetFileName(path);
        _toastDir = Path.GetDirectoryName(path) ?? _screenshotFolder;
        _toastDetail = detail;
        _toastLeft = ToastSeconds;
        if (_dockedShown) { _dockedWin.MarkDirty(); return; }   // shown inside the strip instead
        _toastWin.SetVisible(true);
        _toastWin.MarkDirty();
    }

    private void OpenFolderSafe(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            if (FolderOpener.Open(dir)) return;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
        _view.ShowError(_loc.TFormat("ps.err.openFolder", dir));
    }

    private static string FormatBytes(long bytes) => bytes >= 1024 * 1024
        ? F(bytes / (1024f * 1024f), "0.0") + " MB"
        : F(bytes / 1024f, "0") + " KB";
}
