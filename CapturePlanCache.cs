using Stellar.Abstractions.Domain;
using Stellar.Abstractions.Services;

namespace Stellar.PhotoStudio;

/// <summary>
/// The framework's capture plan for the current Shape + Scale on this window, kept until one of them (or the window
/// size) changes: the HUD size pill and the panel's size/status lines ask every refresh, and asking the framework
/// means a fresh <see cref="CaptureRequest"/> each time. The effective (window-shaped) scale comes from the same
/// GPU limit as the plan, so the status line can never disagree with the size it shows.
/// </summary>
internal sealed class CapturePlanCache
{
    private readonly IScreenCapture _capture;
    private (PhotoShape Shape, int Scale, int W, int H) _key;
    private bool _valid;
    private CaptureSize _size;
    private int _effectiveScale;

    public CapturePlanCache(IScreenCapture capture) => _capture = capture;

    /// <summary>The REAL output size (scale caps, shape and GPU limit) — <c>IScreenCapture.PlanSize</c>.</summary>
    public CaptureSize Size(PhotoShape shape, int scale, int screenW, int screenH)
    {
        Refresh(shape, scale, screenW, screenH);
        return _size;
    }

    /// <summary>The scale a window-shaped capture really uses (4× → 2× → 1×, GPU limit included).</summary>
    public int EffectiveScale(PhotoShape shape, int scale, int screenW, int screenH)
    {
        Refresh(shape, scale, screenW, screenH);
        return _effectiveScale;
    }

    private void Refresh(PhotoShape shape, int scale, int screenW, int screenH)
    {
        var key = (shape, scale, screenW, screenH);
        if (_valid && key == _key) return;
        _key = key;
        _valid = true;
        _size = _capture.PlanSize(new CaptureRequest { Scale = scale, Aspect = PhotoShapes.Aspect(shape) });
        _effectiveScale = CaptureScale.Effective(screenW, screenH, scale, _capture.MaxTextureSize);
    }
}
