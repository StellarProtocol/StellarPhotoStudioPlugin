using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Stellar.Abstractions.Domain;

namespace Stellar.PhotoStudio;

// Unattended hide-list + FOV smoke for the TEST client only (STELLAR_PHOTOSTUDIO_HIDE_SELFTEST=1, read once at load; inert
// otherwise). 15 s after entering the world: a baseline photo, then each 1.7.0 hide layer ALONE — hide, wait, photo, show,
// wait — so the framework's diagnostics line ("[PhotoVis] holds after entity write target=[…]") names the hold counter each
// camera type drives; then all four player groups (= the master switch), then a restored photo; then the free camera at
// FOV 20 / 90 / reset. In-process camera renders only (no desktop capture); files go to <screenshots>/selftest-hide.
public sealed partial class Plugin
{
    private const string HideTestEnvVar = "STELLAR_PHOTOSTUDIO_HIDE_SELFTEST";
    private bool _hideTestOn;
    private int _hideTestStep;
    private readonly Stopwatch _hideTestClock = new();
    private double _hideTestDueAt = 15d;
    private Task<CaptureResult>? _hideTestPending;
    private string _hideTestLabel = "";
    private IDisposable? _hideTestToken;
    private int _hideTestFailures;

    private static readonly (string Name, VisibilityLayers Layer)[] HideTestLayers =
    {
        ("me", VisibilityLayers.SelfCharacter), ("spiritEcho", VisibilityLayers.OwnSpiritEcho),
        ("adventurers", VisibilityLayers.Strangers), ("npcs", VisibilityLayers.NonPlayers), ("enemies", VisibilityLayers.Enemies),
        ("weapons", VisibilityLayers.Weapons), ("friends", VisibilityLayers.Friends), ("party", VisibilityLayers.Party),
        ("guild", VisibilityLayers.Guild), ("collectibles", VisibilityLayers.Collectibles),
        ("otherSpiritEchoes", VisibilityLayers.OtherSpiritEchoes), ("allGroups", VisibilityLayerSets.PlayerGroups),
    };

    private void ArmHideSelfTest()
    {
        _hideTestOn = Environment.GetEnvironmentVariable(HideTestEnvVar) == "1";
        if (_hideTestOn) _services.Log.Info("[PhotoStudio] hide selftest armed");
    }

    private void TickHideSelfTest()
    {
        if (!_hideTestOn || _hideTestStep < 0) return;
        if (!InWorld()) { _hideTestClock.Reset(); return; }
        if (!_hideTestClock.IsRunning) _hideTestClock.Start();
        if (_hideTestPending is { } pending)
        {
            if (!pending.IsCompleted) return;
            _hideTestPending = null;
            var r = pending.IsCompletedSuccessfully ? pending.Result : CaptureResult.Fail("task faulted");
            if (!r.Success) _hideTestFailures++;
            _services.Log.Info($"[PhotoStudio] hide selftest capture {_hideTestLabel} ok={r.Success} path={r.Path} err={r.Error}");
            Due(1d);
            return;
        }
        if (_hideTestClock.Elapsed.TotalSeconds < _hideTestDueAt) return;
        StepHideSelfTest();
    }

    private void Due(double seconds) => _hideTestDueAt = _hideTestClock.Elapsed.TotalSeconds + seconds;

    // Steps: 0 baseline; then per layer 3 steps (hide, capture, show); then restored capture; then the FOV round.
    private void StepHideSelfTest()
    {
        var s = _hideTestStep++;
        if (s == 0) { HideTestCapture("baseline"); return; }
        var i = (s - 1) / 3;
        if (i < HideTestLayers.Length) { HideTestLayerStep(HideTestLayers[i], (s - 1) % 3); return; }
        var fovStep = s - 1 - HideTestLayers.Length * 3;
        if (fovStep == 0) { HideTestCapture("restored"); return; }
        if (!HideTestFovStep(fovStep - 1)) FinishHideSelfTest();
    }

    private void HideTestLayerStep((string Name, VisibilityLayers Layer) t, int phase)
    {
        var request = HideLayers.ToRequest(t.Layer);
        switch (phase)
        {
            case 0:
                _hideTestToken = _services.SceneVisibility.Hide(request);
                _services.Log.Info($"[PhotoStudio] hide selftest step={t.Name} hide request={request} hidden={_services.SceneVisibility.Hidden} ok={(_services.SceneVisibility.Hidden & request) == request}");
                if ((_services.SceneVisibility.Hidden & request) != request) _hideTestFailures++;
                Due(2d);
                return;
            case 1:
                HideTestCapture(t.Name);
                return;
            default:
                _hideTestToken?.Dispose();
                _hideTestToken = null;
                var restored = (_services.SceneVisibility.Hidden & VisibilityLayerSets.World) == VisibilityLayers.None;
                if (!restored) _hideTestFailures++;
                _services.Log.Info($"[PhotoStudio] hide selftest step={t.Name} show hidden={_services.SceneVisibility.Hidden} restored={restored}");
                Due(2d);
                return;
        }
    }

    /// <summary>FOV round: enter the free camera, FOV 20 → photo, FOV 90 → photo, reset → check = entry FOV, exit.</summary>
    private bool HideTestFovStep(int k)
    {
        switch (k)
        {
            case 0:
                _services.Log.Info($"[PhotoStudio] hide selftest fov enter={_freeCam.Enter()} entryFov={_freeCam.EntryFov:0.0}");
                Due(2d);
                return true;
            case 1: _freeCam.SetFov(20f); Due(1d); return true;
            case 2: HideTestCapture($"fov{_freeCam.Fov:0}"); return true;
            case 3: _freeCam.SetFov(250f); Due(1d); return true;   // clamps to 100
            case 4: HideTestCapture($"fov{_freeCam.Fov:0}"); return true;
            case 5:
                _freeCam.ResetFov();
                var ok = Math.Abs(_freeCam.Fov - _freeCam.EntryFov) < 0.01f;
                if (!ok) _hideTestFailures++;
                _services.Log.Info($"[PhotoStudio] hide selftest fov reset fov={_freeCam.Fov:0.0} entry={_freeCam.EntryFov:0.0} ok={ok}");
                _freeCam.Exit();
                ResetScene();
                Due(1d);
                return true;
            default: return false;
        }
    }

    private void HideTestCapture(string label)
    {
        _hideTestLabel = $"step={label} hidden={_services.SceneVisibility.Hidden} freecam={_freeCam.Active} fov={_freeCam.Fov:0}";
        _hideTestPending = _services.ScreenCapture.CaptureAsync(new CaptureRequest
        {
            Scale = 1,
            Directory = Path.Combine(_screenshotFolder, "selftest-hide"),
            FileStem = $"hide_{label}_{DateTime.Now:HHmmss}",
            ApplyReShade = false,
        });
    }

    private void FinishHideSelfTest()
    {
        _hideTestToken?.Dispose();
        _hideTestToken = null;
        _services.Log.Info($"[PhotoStudio] hide selftest DONE layers={HideTestLayers.Length} failures={_hideTestFailures} hidden={_services.SceneVisibility.Hidden}");
        _hideTestStep = -1;
    }
}
