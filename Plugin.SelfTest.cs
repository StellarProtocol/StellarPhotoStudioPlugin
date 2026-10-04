using System;
using System.Numerics;
using Stellar.Abstractions.Domain;
using Stellar.PhotoStudio.FreeCam;

namespace Stellar.PhotoStudio;

// Unattended free-camera smoke for the TEST client only (STELLAR_PHOTOSTUDIO_FREECAM_SELFTEST=1, read once at load;
// inert otherwise). 15 s after entering the world: enter → scripted orbit → emote → freeze → capture → unfreeze → fly
// toward the leash → freeze again → exit (logs scene-kept: the freeze and entry hides stay) → Reset scene, one log line
// per step. Every image is an in-process camera render (no desktop capture).
public sealed partial class Plugin
{
    private const string SelfTestEnvVar = "STELLAR_PHOTOSTUDIO_FREECAM_SELFTEST";
    private const float SelfTestDelay = 15f;
    private bool _selfTestOn;
    private int _selfTestStep;
    private float _selfTestClock;
    private float _selfTestDueAt;

    /// <summary>The capture, posing and shapes self-tests (each inert unless its env var is set).</summary>
    private void ArmSelfTests()
    {
        ArmSelfTest();
        ArmPosingSelfTest();      // Plugin.SelfTest.Posing.cs
        ArmShapeSelfTest();       // Plugin.SelfTest.Shapes.cs
    }

    private void ArmSelfTest()
    {
        _selfTestOn = Environment.GetEnvironmentVariable(SelfTestEnvVar) == "1";
        if (_selfTestOn) _services.Log.Info("[PhotoStudio] freecam selftest armed");
    }

    private void TickSelfTest(float dt)
    {
        if (!_selfTestOn || _selfTestStep < 0) return;
        if (!InWorld()) { _selfTestClock = 0f; return; }
        _selfTestClock += dt;
        if (_selfTestClock < SelfTestDelay || _selfTestClock < _selfTestDueAt) return;
        RunSelfTestStep();
    }

    private void RunSelfTestStep()
    {
        switch (_selfTestStep++)
        {
            case 0: SelfTestLog("enter", _freeCam.Enter()); Due(1f); break;
            case 1: _freeCam.ScriptedIntent = new CamIntent { Looking = true, LookX = 4f }; SelfTestLog("orbit", _freeCam.Active); Due(2f); break;
            case 2: _freeCam.ScriptedIntent = null; PlaySelfTestEmote(); Due(0.6f); break;
            case 3: _freeCam.ToggleFreeze(); SelfTestLog("freeze", _freeCam.Frozen, $"holdsPositions={_services.SceneFreeze.HoldsPositions}"); Due(1f); break;
            case 4: CaptureNow(); SelfTestLog("capture-requested", true); Due(5f); break;
            case 5: _freeCam.ToggleFreeze(); SelfTestLog("unfreeze", !_freeCam.Frozen); Due(0.5f); break;
            case 6: _freeCam.ToggleMode(); _freeCam.ScriptedIntent = new CamIntent { Move = new Vector3(0f, 0f, 1f), Shift = true }; SelfTestLog("fly", _freeCam.Mode == FreeCamMode.Fly); Due(8f); break;
            default: FinishSelfTest(); break;
        }
    }

    private void FinishSelfTest()
    {
        _freeCam.ScriptedIntent = null;
        SelfTestLog("leash", _freeCam.Distance <= _fcSettings.Leash + 0.01f, $"distance={_freeCam.Distance:F2} leash={_fcSettings.Leash:F0}");
        if (!_scene.Frozen) _freeCam.ToggleFreeze();   // set a scene, so the exit below proves it stays
        _freeCam.Exit();
        LogSceneKept(SelfTestLog);
        ResetScene();   // scene-stays: leaving the free camera keeps the scene; Reset scene ends it
        SelfTestLog("exit", SceneCleanAfterReset());
        _services.Log.Info("[PhotoStudio] freecam selftest DONE");
        _selfTestStep = -1;
    }

    private void PlaySelfTestEmote()
    {
        var unlocked = _services.Emotes.Unlocked;
        var id = unlocked.Count > 0 ? unlocked[0].Id : 9011;   // 9011 = Wave (probe-proven)
        var result = _services.Emotes.PlayAsync(id).Result;
        SelfTestLog("emote", result == EmoteResult.Played, $"id={id} result={result} unlocked={unlocked.Count}");
    }

    /// <summary>Called from OnCaptureResult: the capture taken while frozen.</summary>
    private void SelfTestCaptured(CaptureResult r)
    {
        if (_selfTestOn && _selfTestStep >= 0) SelfTestLog("capture", r.Success, $"path={r.Path} size={r.Width}x{r.Height}");
    }

    private void Due(float seconds) => _selfTestDueAt = _selfTestClock + seconds;

    /// <summary>Right after Exit(), before Reset scene (review minor): the in-game smoke's proof that leaving the free camera
    /// kept the scene — still frozen in the framework, or people still posed, with the entry hides kept by the scene.</summary>
    private void LogSceneKept(Action<string, bool, string> log)
    {
        var kept = !_freeCam.Active && _scene.KeepsCamera && (!_scene.Frozen || _services.SceneFreeze.IsFrozen);
        log("scene-kept", kept, $"frozen={_scene.Frozen} fwFrozen={_services.SceneFreeze.IsFrozen} posed={_scene.PosedCount} " +
                                $"entryHidesKept={_scene.HoldsEntryHide} layers={_scene.EntryHideLayers}");
    }

    /// <summary>After Reset scene: nothing of the camera or the scene is left (entry hides released too).</summary>
    private bool SceneCleanAfterReset() =>
        !_freeCam.Active && !_scene.IsSet && !_scene.HoldsEntryHide && !_services.CameraOverride.IsOverridden &&
        !_services.InputShield.IsShielded && !_services.SceneFreeze.IsFrozen;

    private void SelfTestLog(string step, bool ok, string detail = "") =>
        _services.Log.Info($"[PhotoStudio] freecam selftest step={step} ok={ok} {detail}".TrimEnd());
}
