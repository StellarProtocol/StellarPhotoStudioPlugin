using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Stellar.Abstractions.Domain;
namespace Stellar.PhotoStudio;

/// <summary>User-facing capture options; <see cref="CaptureController"/> turns these into a framework
/// <see cref="CaptureRequest"/> (timestamped file name, resolved folder).</summary>
/// <param name="Aspect">The photo shape (<see cref="PhotoShapes.Aspect"/>); null = the window's shape.</param>
internal sealed record CaptureSettings(int Scale, CaptureFormat Format, int JpgQuality, string? Folder, VisibilityLayers Hide,
    CaptureAspect? Aspect = null);

/// <summary>ReShade at the shutter: its preset file, its version (dxgi.dll product version; null when unreadable), whether
/// its effects went into the photo (ready + on, and no "not ready" note), and the capture's notes verbatim.</summary>
internal sealed record ReShadeShot(string? Preset, string? Version, bool Applied, IReadOnlyList<string> Notes);

/// <summary>Everything the sidecar records besides the result itself. <paramref name="ReShade"/> null = ReShade not installed.</summary>
internal sealed record SidecarInfo(string Preset, string Map, int Scale, LookSettings? Look, ReShadeShot? ReShade);

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

    public static string SidecarJson(CaptureResult r, SidecarInfo i) =>
        JsonSerializer.Serialize(new
        {
            file = Path.GetFileName(r.Path),
            width = r.Width,
            height = r.Height,
            preset = i.Preset,
            map = i.Map,
            scale = i.Scale,
            dofFocus = i.Look?.Dof?.FocusDistance,
            // Additive key (rules § 6): readers that predate it ignore it.
            reshade = i.ReShade is { } rs ? new { preset = rs.Preset, version = rs.Version, applied = rs.Applied, notes = rs.Notes } : null,
        });
}
