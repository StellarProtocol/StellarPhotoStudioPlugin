using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using Panda.ZGame;
using UnityEngine;

namespace Stellar.PhotoStudioProbe;

public sealed partial class RenderProbe
{
    /// <summary>Settle, then grab the game's own backbuffer at 1x at end of frame (step evidence).</summary>
    private IEnumerator Snap(string name)
    {
        yield return Wait(1.5f);
        yield return new WaitForEndOfFrame();
        var tex = ScreenCapture.CaptureScreenshotAsTexture(1);
        var png = ImageConversion.EncodeToPNG(tex);
        var path = Path.Combine(_outDir, $"probe_{name}.png");
        File.WriteAllBytes(path, png);
        Log($"SNAP {name} {tex.width}x{tex.height} -> {path}");
        UnityEngine.Object.Destroy(tex);
    }

    private IEnumerator StepScreenCapture()
    {
        foreach (var n in new[] { 1, 2, 4 })
        {
            yield return Wait(0.5f);
            yield return new WaitForEndOfFrame();
            var sw = Stopwatch.StartNew();
            var tex = ScreenCapture.CaptureScreenshotAsTexture(n);
            var capMs = sw.Elapsed.TotalMilliseconds;
            var png = ImageConversion.EncodeToPNG(tex);
            var encMs = sw.Elapsed.TotalMilliseconds - capMs;
            var path = Path.Combine(_outDir, $"probe_{n}x.png");
            File.WriteAllBytes(path, png);
            Log($"CAPTURE ScreenCapture superSize={n} -> {tex.width}x{tex.height} capture={capMs:F1}ms encode={encMs:F1}ms png={png.Length}B {path}");
            UnityEngine.Object.Destroy(tex);
        }
    }

    private IEnumerator StepCameraRender()
    {
        var cam = CameraManager.Instance?.MainCamera ?? Camera.main;
        if (cam == null) { Log("CAPTURE CameraRender: no camera"); yield break; }
        var w = Screen.width;
        var h = Screen.height;
        foreach (var n in new[] { 1, 2, 4 })
        {
            yield return Wait(0.5f);
            yield return new WaitForEndOfFrame();
            RenderCameraOnce(cam, w * n, h * n, n);
        }
    }

    private void RenderCameraOnce(Camera cam, int w, int h, int n)
    {
        var sw = Stopwatch.StartNew();
        var rt = new RenderTexture(w, h, 24);
        var prevTarget = cam.targetTexture;
        var prevActive = RenderTexture.active;
        Texture2D? tex = null;
        try
        {
            cam.targetTexture = rt;
            cam.Render();
            var renderMs = sw.Elapsed.TotalMilliseconds;
            RenderTexture.active = rt;
            tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            var readMs = sw.Elapsed.TotalMilliseconds - renderMs;
            var png = ImageConversion.EncodeToPNG(tex);
            var path = Path.Combine(_outDir, $"probe_cam_{n}x.png");
            File.WriteAllBytes(path, png);
            Log($"CAPTURE CameraRender n={n} -> {w}x{h} render={renderMs:F1}ms readback={readMs:F1}ms total={sw.Elapsed.TotalMilliseconds:F1}ms " +
                $"meanLum={MeanLum(tex):F3} png={png.Length}B {path}");
        }
        catch (Exception ex) { Log($"CAPTURE CameraRender n={n} FAILED {ex.GetType().Name}: {ex.Message}"); }
        finally
        {
            cam.targetTexture = prevTarget;
            RenderTexture.active = prevActive;
            rt.Release();
            UnityEngine.Object.Destroy(rt);
            if (tex != null) UnityEngine.Object.Destroy(tex);
        }
    }

    private static float MeanLum(Texture2D t)
    {
        float sum = 0f;
        var k = 0;
        for (var y = 1; y < 16; y++)
        for (var x = 1; x < 16; x++)
        {
            var c = t.GetPixel(t.width * x / 16, t.height * y / 16);
            sum += 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;
            k++;
        }
        return sum / k;
    }
}
