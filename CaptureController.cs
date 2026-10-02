using System;
using System.IO;
using System.Text.Json;
using Stellar.Abstractions.Domain;
namespace Stellar.PhotoStudio;

/// <summary>User-facing capture options; <see cref="CaptureController"/> turns these into a framework
/// <see cref="CaptureRequest"/> (timestamped file name, resolved folder).</summary>
/// <param name="Aspect">The photo shape (<see cref="PhotoShapes.Aspect"/>); null = the window's shape.</param>
internal sealed record CaptureSettings(int Scale, CaptureFormat Format, int JpgQuality, string? Folder, VisibilityLayers Hide,
    CaptureAspect? Aspect = null);

/// <summary>Builds capture requests and the JSON sidecar written alongside a saved screenshot.</summary>
internal static class CaptureController
{
    public static CaptureRequest BuildRequest(CaptureSettings s, DateTime now, string defaultDir) => new()
    {
        Scale = s.Scale,
        Format = s.Format,
        JpgQuality = s.JpgQuality,
        Directory = string.IsNullOrWhiteSpace(s.Folder) ? defaultDir : s.Folder!,
        FileStem = "BPSR_" + now.ToString("yyyy-MM-dd_HH-mm-ss"),
        HideDuringCapture = s.Hide,
        Aspect = s.Aspect,
    };

    public static string SidecarJson(CaptureResult r, string presetName, string mapName, int scale, LookSettings? look) =>
        JsonSerializer.Serialize(new
        {
            file = Path.GetFileName(r.Path),
            width = r.Width,
            height = r.Height,
            preset = presetName,
            map = mapName,
            scale,
            dofFocus = look?.Dof?.FocusDistance,
        });
}
