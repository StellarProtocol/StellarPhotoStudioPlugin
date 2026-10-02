using Stellar.Abstractions.Domain;
using Stellar.Abstractions.Services;

namespace Stellar.PhotoStudio;

// Frame guide (portrait-capture spec § 5, approved mockup 2026-10-03-portrait-capture-mockup.html): a full-canvas window
// BEHIND every other window (ZOrder far below, so it never blocks a press — WindowInteractionTicker.FrontWindowBlocks only
// lets a FRONT window block) showing one drawn PNG: the area outside the shape dimmed, a border, thirds lines. It is UI,
// so the camera-render capture never contains it. Rebuilt only when the shape, the toggle or the canvas size changes.
public sealed partial class Plugin
{
    private const int GuideZOrder = -10000;
    private const int GuideDownscale = 2;   // the PNG is drawn at half the canvas size and stretched: dims need no detail

    private IWindowControl? _guideWin;
    private (PhotoShape Shape, int W, int H) _guideBuilt;

    private bool GuideWanted() => InWorld() && ShapeFrame.GuideVisible(_settings.Shape, _settings.ShowFrameGuide,
        _freeCam.Active, _panelWin.IsShown && _settings.Tab == StudioTabs.Capture);

    /// <summary>Called from OnUpdate: a few int compares; the window is only (re)built when its inputs change.</summary>
    private void TickFrameGuide()
    {
        if (!GuideWanted()) { HideGuide(); return; }
        var w = _services.Framework.CanvasWidth;
        var h = _services.Framework.CanvasHeight;
        if (w <= 0 || h <= 0) return;
        if (_guideWin is not null && _guideBuilt == (_settings.Shape, w, h)) return;
        HideGuide();
        _guideWin = RegisterFrameGuide(_settings.Shape, w, h);
        _guideBuilt = (_settings.Shape, w, h);
    }

    private void HideGuide()
    {
        if (_guideWin is null) return;
        _guideWin.Remove();
        _guideWin = null;
    }

    private IWindowControl RegisterFrameGuide(PhotoShape shape, int w, int h)
    {
        // The rectangle comes from the SCREEN's shape (pixels); the canvas has the same aspect, so it maps 1:1.
        var rect = ShapeFrame.GuideRect(shape, _services.Framework.ScreenWidth, _services.Framework.ScreenHeight);
        int pw = System.Math.Max(1, w / GuideDownscale), ph = System.Math.Max(1, h / GuideDownscale);
        var png = FrameGuidePainter.EncodePng(FrameGuidePainter.Paint(pw, ph, rect), pw, ph);
        var control = _services.Windows.Register(new WindowRegistration(
            new WindowSpec("photostudio.frameguide", T("ps.guide.title"), new WindowRect(0f, 0f, w, h),
                WindowCategory.HUD, WindowPanelStyle.Borderless)
            {
                Surface = SurfaceStyle.HudOverlay,
                ShowTitleBar = false, StartVisible = true, Draggable = false, ZOrder = GuideZOrder,
                Anchor = WindowAnchor.TopLeft, ShouldRender = GuideWanted,
            },
            new ImageElement(() => png, w, h)));
        control.SetRect(new WindowRect(0f, 0f, w, h));   // a saved position must never move it off the canvas origin
        return control;
    }
}
