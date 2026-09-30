using System;
using System.IO;
using Stellar.Abstractions.Domain;
using Stellar.Abstractions.Services;

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
        new ColumnElement(new HudElement[]
        {
            new RowElement(new HudElement[]
            {
                new TextElement(() => _toastTitle, Emphasis: true),
                new CellElement(new TextElement(() => _toastFile, Color: Muted, NoWrap: true), Weight: 1f),
                new ButtonElement(() => T("ps.cap.openFolder"), OnClick: () => OpenFolderSafe(_toastDir), Width: 104f),
                new ButtonElement(() => "✕", OnClick: () => { _toastLeft = 0f; _toastWin.SetVisible(false); }, Width: 28f),
            }, Gap: 6f),
            new TextElement(() => _toastDetail, Color: Muted),
            new ConditionalElement(() => _toastWarning.Length > 0,
                new TextElement(() => _toastWarning, Color: () => _services.Theme.Colors.Warning)),
        }, Gap: 4f)));

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
        if (_settings.Scale == 4 && r.Width > 0 && r.Width < _services.Framework.ScreenWidth * 4)
            warning = T("ps.toast.retried2x");
        _toastWarning = warning;
        ShowFileToast(T("ps.toast.saved"), r.Path ?? "", $"{r.Width} × {r.Height} · {FormatName()}{size}");
    }

    private void ShowFileToast(string title, string path, string detail)
    {
        _toastTitle = title;
        _toastFile = Path.GetFileName(path);
        _toastDir = Path.GetDirectoryName(path) ?? _screenshotFolder;
        _toastDetail = detail;
        _toastLeft = ToastSeconds;
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
