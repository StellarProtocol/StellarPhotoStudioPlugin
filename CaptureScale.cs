using System;

namespace Stellar.PhotoStudio;

/// <summary>
/// The scale a capture will actually use: the framework lowers 4× → 2× → 1× until the image fits both its caps
/// (16384 px long side, 64 MP total — framework CaptureRequestValidator). Mirrored here so the panel shows the real
/// output size and the toast can tell "capped by size" from "ran out of memory".
/// </summary>
internal static class CaptureScale
{
    public const int MaxLongSide = 16384;
    public const long MaxPixels = 64_000_000;

    public static int Effective(int screenW, int screenH, int requested)
    {
        var scale = requested;
        var longSide = Math.Max(screenW, screenH);
        while (scale > 1 && (longSide * scale > MaxLongSide || (long)screenW * screenH * scale * scale > MaxPixels))
            scale /= 2;
        return scale;
    }
}
