using System;
using System.Globalization;
using Stellar.Abstractions.Domain;

namespace Stellar.PhotoStudio;

/// <summary>What a capture reports beyond "saved": why the image came out smaller than the setting asked for.</summary>
internal enum CaptureShortfall
{
    None,
    /// <summary>A window-shaped capture lowered its scale to fit the size limit (the existing "capped" toast).</summary>
    ScaleCapped,
    /// <summary>A shaped capture shrank both sides to fit the GPU / memory limit (no toast string yet — UI follow-up).</summary>
    ShapeCapped,
    /// <summary>The 4× grab failed and was retried at 2× (the existing "retried" toast).</summary>
    Retried2x,
}

/// <summary>
/// Pure, language-neutral helpers for the Shape row, the size labels and the frame guide (spec 2026-10-03 photo shapes
/// § Behaviour 2, 4, 5, 6). Sizes come from the framework's own calculator (<see cref="CaptureSizing"/>), so a label
/// is the real output size.
/// </summary>
internal static class ShapeFrame
{
    private const string Times = " × ";

    /// <summary>The photo's size for <paramref name="shape"/> on a <paramref name="screenW"/> × <paramref name="screenH"/>
    /// window at <paramref name="scale"/> (the live panel uses <c>IScreenCapture.PlanSize</c>, which adds the GPU limit).</summary>
    public static CaptureSize OutputSize(PhotoShape shape, int screenW, int screenH, int scale, int maxTextureSize = CaptureSizing.MaxLongSide) =>
        CaptureSizing.OutputSize(screenW, screenH, scale, PhotoShapes.Aspect(shape), maxTextureSize);

    /// <summary>"2160 × 3840" (the Resolution row).</summary>
    public static string SizeText(CaptureSize size) =>
        size.Width.ToString(CultureInfo.InvariantCulture) + Times + size.Height.ToString(CultureInfo.InvariantCulture);

    /// <summary>The line under Capture: "2× · PNG · 2160 × 3840 (9:16)"; Screen keeps today's "2× · PNG · 3840 × 2160".</summary>
    public static string StatusText(int scale, string formatName, CaptureSize size, PhotoShape shape)
    {
        var line = $"{scale.ToString(CultureInfo.InvariantCulture)}× · {formatName} · {SizeText(size)}";
        return shape == PhotoShape.Screen ? line : $"{line} ({PhotoShapes.RatioLabel(shape)})";
    }

    /// <summary>The guide's size label: "9:16 · 2160 × 3840"; empty for Screen (no guide).</summary>
    public static string GuideLabel(PhotoShape shape, CaptureSize size) =>
        shape == PhotoShape.Screen ? "" : $"{PhotoShapes.RatioLabel(shape)} · {SizeText(size)}";

    /// <summary>The guide rectangle, normalized 0..1 with the origin at the window's TOP-left: the largest rectangle of
    /// the shape centred on the screen (full height for portrait/square, full width for wide) — exactly what the photo
    /// frames. Screen = the whole window.</summary>
    public static NormalizedRect GuideRect(PhotoShape shape, int screenW, int screenH) =>
        CaptureSizing.GuideRect(screenW, screenH, PhotoShapes.Aspect(shape));

    /// <summary>The two vertical then two horizontal rule-of-thirds lines inside <paramref name="r"/>, normalized like it
    /// (x positions, then y positions).</summary>
    public static (float X1, float X2, float Y1, float Y2) Thirds(NormalizedRect r) =>
        (r.X + r.Width / 3f, r.X + r.Width * 2f / 3f, r.Y + r.Height / 3f, r.Y + r.Height * 2f / 3f);

    /// <summary>Spec § 5: shown while the free camera is on or the Capture tab is shown, when Shape ≠ Screen and the
    /// "Show frame guide" toggle is on.</summary>
    public static bool GuideVisible(PhotoShape shape, bool showFrameGuide, bool freeCamOn, bool captureTabShown) =>
        shape != PhotoShape.Screen && showFrameGuide && (freeCamOn || captureTabShown);

    /// <summary>Why <paramref name="written"/> is smaller than the setting asked for. The uncapped long side is always
    /// the window's long side × the requested scale (Screen and shapes alike); smaller than <paramref name="planned"/>
    /// means the out-of-memory 2× retry ran.</summary>
    public static CaptureShortfall Shortfall(PhotoShape shape, CaptureSize written, CaptureSize planned, int screenLongSide, int requestedScale)
    {
        if (written.IsEmpty) return CaptureShortfall.None;
        var writtenLong = Math.Max(written.Width, written.Height);
        if (writtenLong < Math.Max(planned.Width, planned.Height)) return CaptureShortfall.Retried2x;
        if (writtenLong >= (long)screenLongSide * requestedScale) return CaptureShortfall.None;
        return shape == PhotoShape.Screen ? CaptureShortfall.ScaleCapped : CaptureShortfall.ShapeCapped;
    }

    /// <summary>The scale a written image corresponds to (sidecar): its long side over the window's, at least 1.</summary>
    public static int CapturedScale(CaptureSize written, int screenW, int screenH, int fallback)
    {
        var screenLong = Math.Max(screenW, screenH);
        if (screenLong <= 0 || written.IsEmpty) return fallback;
        return Math.Max(1, (int)Math.Round((double)Math.Max(written.Width, written.Height) / screenLong));
    }
}
