using Stellar.Abstractions.Domain;

namespace Stellar.PhotoStudio;

/// <summary>
/// The scale a window-shaped capture will actually use: the framework lowers 4× → 2× → 1× until the image fits its
/// caps (16384 px long side or the GPU's smaller texture limit, 64 MP total). The framework's own
/// <see cref="CaptureSizing.EffectiveScale"/>, so the panel's scale always agrees with <c>IScreenCapture.PlanSize</c>.
/// </summary>
internal static class CaptureScale
{
    public static int Effective(int screenW, int screenH, int requested, int maxTextureSize = CaptureSizing.MaxLongSide) =>
        CaptureSizing.EffectiveScale(screenW, screenH, requested, maxTextureSize);
}
