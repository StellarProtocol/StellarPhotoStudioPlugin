using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Stellar.Abstractions.Domain;

namespace Stellar.PhotoStudio;

// Unattended photo-shapes smoke for the TEST client only (STELLAR_PHOTOSTUDIO_SHAPES_SELFTEST=1, read once at load;
// inert otherwise). 15 s after entering the world: every shape (Screen, 9:16, 4:5, 2:3, 1:1, 21:9) at 1× and 2×, three
// rounds — free camera off, free camera on, free camera on + frozen (time pause) — each capture's planned size, written
// size and PNG header size on one log line, then the camera is released and the scene reset. Files go to
// <screenshots>/selftest-shapes. In-process camera renders only (no desktop capture). Timing uses real time, so the
// frozen round's paused clock does not stall it.
public sealed partial class Plugin
{
    private const string ShapeTestEnvVar = "STELLAR_PHOTOSTUDIO_SHAPES_SELFTEST";
    private const int ShapeTestPerRound = 12;   // 6 shapes × 2 scales
    private const int ShapeTestRounds = 3;
    private bool _shapeTestOn;
    private int _shapeTestIndex;
    private readonly Stopwatch _shapeTestClock = new();
    private double _shapeTestDueAt = 15d;
    private Task<CaptureResult>? _shapeTestPending;
    private CaptureSize _shapeTestPlan;
    private string _shapeTestLabel = "";
    private PhotoShape _shapeTestShape;
    private int _shapeTestScale;
    private int _shapeTestFailures;

    private void ArmShapeSelfTest()
    {
        _shapeTestOn = Environment.GetEnvironmentVariable(ShapeTestEnvVar) == "1";
        if (_shapeTestOn) _services.Log.Info("[PhotoStudio] shapes selftest armed");
    }

    private void TickShapeSelfTest()
    {
        if (!_shapeTestOn || _shapeTestIndex < 0) return;
        if (!InWorld()) { _shapeTestClock.Reset(); return; }
        if (!_shapeTestClock.IsRunning) _shapeTestClock.Start();
        if (_shapeTestPending is { } pending)
        {
            if (!pending.IsCompleted) return;
            _shapeTestPending = null;
            LogShapeResult(pending.IsCompletedSuccessfully ? pending.Result : CaptureResult.Fail("task faulted"));
            _shapeTestDueAt = _shapeTestClock.Elapsed.TotalSeconds + 1d;
            return;
        }
        if (_shapeTestClock.Elapsed.TotalSeconds < _shapeTestDueAt) return;
        StepShapeSelfTest();
    }

    private void StepShapeSelfTest()
    {
        if (_shapeTestIndex >= ShapeTestPerRound * ShapeTestRounds) { FinishShapeSelfTest(); return; }
        if (_shapeTestIndex == ShapeTestPerRound && !_freeCam.Active)
        {
            _services.Log.Info($"[PhotoStudio] shapes selftest round=freecam enter={_freeCam.Enter()}");
            _shapeTestDueAt = _shapeTestClock.Elapsed.TotalSeconds + 2d;
            return;
        }
        if (_shapeTestIndex == ShapeTestPerRound * 2 && !_freeCam.Frozen)
        {
            _freeCam.ToggleFreeze();
            _services.Log.Info($"[PhotoStudio] shapes selftest round=frozen frozen={_freeCam.Frozen} fwFrozen={_services.SceneFreeze.IsFrozen}");
            _shapeTestDueAt = _shapeTestClock.Elapsed.TotalSeconds + 2d;
            return;
        }
        StartShapeCapture(_shapeTestIndex++);
    }

    private void StartShapeCapture(int i)
    {
        var round = (i / ShapeTestPerRound) switch { 0 => "off", 1 => "freecam", _ => "frozen" };
        var shape = PhotoShapes.All[i % ShapeTestPerRound / 2];
        var scale = i % 2 == 0 ? 1 : 2;
        var request = new CaptureRequest
        {
            Scale = scale,
            Aspect = PhotoShapes.Aspect(shape),
            Directory = Path.Combine(_screenshotFolder, "selftest-shapes"),
            FileStem = $"shape_{round}_{PhotoShapes.Key(shape).Replace(':', 'x')}_{scale}x_{DateTime.Now:HHmmss}",
        };
        _shapeTestPlan = _services.ScreenCapture.PlanSize(request);
        (_shapeTestShape, _shapeTestScale) = (shape, scale);
        _shapeTestLabel = $"round={round} shape={PhotoShapes.Key(shape)} scale={scale} " +
                          $"window={_services.Framework.ScreenWidth}x{_services.Framework.ScreenHeight} freecam={_freeCam.Active} frozen={_services.SceneFreeze.IsFrozen}";
        _shapeTestPending = _services.ScreenCapture.CaptureAsync(request);
    }

    private void LogShapeResult(CaptureResult r)
    {
        var header = r.Path is not null ? PngHeaderSize(r.Path) : default;
        var expected = ShapeFrame.OutputSize(_shapeTestShape, _services.Framework.ScreenWidth, _services.Framework.ScreenHeight, _shapeTestScale);
        var ok = r.Success && header == _shapeTestPlan && header == expected && header == new CaptureSize(r.Width, r.Height);
        if (!ok) _shapeTestFailures++;
        _services.Log.Info($"[PhotoStudio] shapes selftest {_shapeTestLabel} ok={ok} planned={Fmt(_shapeTestPlan)} " +
                           $"calculator={Fmt(expected)} result={r.Width}x{r.Height} png={Fmt(header)} path={r.Path} err={r.Error}");
    }

    private void FinishShapeSelfTest()
    {
        if (_freeCam.Frozen) _freeCam.ToggleFreeze();
        _freeCam.Exit();
        ResetScene();
        _services.Log.Info($"[PhotoStudio] shapes selftest DONE captures={ShapeTestPerRound * ShapeTestRounds} failures={_shapeTestFailures}");
        _shapeTestIndex = -1;
    }

    /// <summary>Width/height from the PNG IHDR chunk (bytes 16..23, big-endian).</summary>
    private static CaptureSize PngHeaderSize(string path)
    {
        try
        {
            var b = new byte[24];
            using (var f = File.OpenRead(path))
                if (f.Read(b, 0, 24) != 24) return default;
            static int Be(byte[] a, int o) => (a[o] << 24) | (a[o + 1] << 16) | (a[o + 2] << 8) | a[o + 3];
            return new CaptureSize(Be(b, 16), Be(b, 20));
        }
        catch (IOException) { return default; }
    }

    private static string Fmt(CaptureSize s) => $"{s.Width}x{s.Height}";
}
