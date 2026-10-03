using Stellar.Abstractions.Domain;

namespace Stellar.PhotoStudio.FreeCam;

/// <summary>Whether a screen point (pixels, origin top-left) lies inside a window rect given in canvas units.</summary>
internal static class UiHitTest
{
    public static bool Contains(WindowRect canvasRect, float x, float y, float pixelsPerCanvasUnit)
    {
        if (canvasRect.Width <= 0f || canvasRect.Height <= 0f || pixelsPerCanvasUnit <= 0f) return false;
        var cx = x / pixelsPerCanvasUnit;
        var cy = y / pixelsPerCanvasUnit;
        return cx >= canvasRect.X && cx <= canvasRect.Right && cy >= canvasRect.Y && cy <= canvasRect.Y + canvasRect.Height;
    }
}
