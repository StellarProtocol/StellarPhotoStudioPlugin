using System;
using System.Diagnostics;
using System.IO;
using UnityEngine;

namespace Stellar.PhotoStudio.FreeCamProbe;

/// <summary>
/// In-process evidence capture (the render-recon probe's CameraRender path: MainCamera → RenderTexture → ReadPixels
/// → PNG under <c>stellar/screenshots/freecamprobe/</c>). Never reads the screen/desktop. A <see cref="Shot"/> keeps a
/// managed copy of one screen region so two shots can be compared (pixel diff / luminance).
/// </summary>
public sealed partial class FreeCamProbe
{
    internal sealed class Shot
    {
        public string Name = "";
        public RectInt Region;
        public Color32[] Px = Array.Empty<Color32>();
    }

    /// <summary>Region of ±<paramref name="halfW"/> × ±<paramref name="halfH"/> (screen fractions) around a world point.</summary>
    private static RectInt RegionAround(Camera cam, Vector3 world, float halfW = 0.08f, float halfH = 0.16f)
    {
        var vp = cam.WorldToViewportPoint(world);
        if (vp.z <= 0f) vp = new Vector3(0.5f, 0.5f, 1f);   // behind camera: fall back to the centre
        var w = Screen.width;
        var h = Screen.height;
        var x0 = Mathf.Clamp((int)((vp.x - halfW) * w), 0, w - 2);
        var y0 = Mathf.Clamp((int)((vp.y - halfH) * h), 0, h - 2);
        var x1 = Mathf.Clamp((int)((vp.x + halfW) * w), x0 + 1, w - 1);
        var y1 = Mathf.Clamp((int)((vp.y + halfH) * h), y0 + 1, h - 1);
        return new RectInt(x0, y0, x1 - x0, y1 - y0);
    }

    /// <summary>Renders the main camera once and keeps <paramref name="region"/>; writes a PNG of the full frame.</summary>
    private Shot? Capture(string name, RectInt region) => CaptureMany(name, region)?[0];

    /// <summary>One render, several kept regions (index-aligned with <paramref name="regions"/>); null on failure.</summary>
    private Shot[]? CaptureMany(string name, params RectInt[] regions)
    {
        var cam = MainCam();
        if (cam == null) { Log($"CAPTURE {name}: no camera"); return null; }
        var w = Screen.width;
        var h = Screen.height;
        var sw = Stopwatch.StartNew();
        var rt = new RenderTexture(w, h, 24);
        var prevTarget = cam.targetTexture;
        var prevActive = RenderTexture.active;
        Texture2D? tex = null;
        try
        {
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            var src = tex.GetPixels32();
            var shots = new Shot[regions.Length];
            for (var i = 0; i < regions.Length; i++)
                shots[i] = new Shot { Name = regions.Length == 1 ? name : $"{name}#{i}", Region = regions[i], Px = CopyRegion(src, w, regions[i]) };
            var path = Path.Combine(_outDir, $"fcp_{name}.png");
            File.WriteAllBytes(path, ImageConversion.EncodeToPNG(tex));
            var parts = new System.Text.StringBuilder();
            foreach (var s in shots) parts.Append($" region=({s.Region.x},{s.Region.y},{s.Region.width}x{s.Region.height}) lum={MeanLum(s):F2}");
            Log($"CAPTURE {name} {w}x{h}{parts} ms={sw.Elapsed.TotalMilliseconds:F0} -> {path}");
            return shots;
        }
        catch (Exception ex) { Log($"CAPTURE {name} FAILED {ex.GetType().Name}: {ex.Message}"); return null; }
        finally
        {
            cam.targetTexture = prevTarget;
            RenderTexture.active = prevActive;
            rt.Release();
            UnityEngine.Object.Destroy(rt);
            if (tex != null) UnityEngine.Object.Destroy(tex);
        }
    }

    private static Color32[] CopyRegion(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Color32> src, int w, RectInt r)
    {
        // src = whole frame, row-major from the bottom-left
        var dst = new Color32[r.width * r.height];
        for (var y = 0; y < r.height; y++)
        {
            var row = (r.y + y) * w + r.x;
            for (var x = 0; x < r.width; x++) dst[y * r.width + x] = src[row + x];
        }
        return dst;
    }

    private static float MeanLum(Shot s)
    {
        if (s.Px.Length == 0) return 0f;
        double sum = 0;
        foreach (var c in s.Px) sum += 0.299 * c.r + 0.587 * c.g + 0.114 * c.b;
        return (float)(sum / s.Px.Length);
    }

    /// <summary>Mean absolute RGB difference (0-255) and the share of pixels whose max channel delta exceeds 12.</summary>
    private static (float Mean, float ChangedPct) Diff(Shot? a, Shot? b)
    {
        if (a == null || b == null || a.Px.Length != b.Px.Length || a.Px.Length == 0) return (-1f, -1f);
        double sum = 0;
        var changed = 0;
        for (var i = 0; i < a.Px.Length; i++)
        {
            var p = a.Px[i];
            var q = b.Px[i];
            int dr = Math.Abs(p.r - q.r), dg = Math.Abs(p.g - q.g), db = Math.Abs(p.b - q.b);
            sum += (dr + dg + db) / 3.0;
            if (Math.Max(dr, Math.Max(dg, db)) > 12) changed++;
        }
        return ((float)(sum / a.Px.Length), 100f * changed / a.Px.Length);
    }

    private string DiffText(Shot? a, Shot? b)
    {
        var (mean, pct) = Diff(a, b);
        return $"diff({a?.Name}->{b?.Name}) meanAbs={mean:F2} changed>{12}={pct:F1}%";
    }
}
