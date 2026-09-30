using System;
using System.Reflection;
using HarmonyLib;
using Panda.ZGame;
using Panda.ZGame.Timeline;
using Panda.ZInput;

namespace Stellar.PhotoStudioProbe;

/// <summary>
/// Armed photo-mode / cutscene hooks (need the owner at the keyboard). Harmony postfixes log every firing;
/// <see cref="PollStates"/> logs transitions of the state getters so a hook that never fires is still bracketed.
/// NOTE: CameraFrameCtrl.IsCameraState/CameraState and UILayerVisibilityRules.CutSceneHideUILayers are STATIC
/// FIELD accessors in the interop (no native get_/set_ method) — they cannot be Harmony-hooked, only read.
/// </summary>
public sealed partial class RenderProbe
{
    private static RenderProbe? _self;
    private float _pollAccum;
    private string _lastState = "";

    private void InstallHooks()
    {
        _self = this;
        var h = _services.Harmony.Create("probe");
        Hook(h, typeof(CameraManager), "EnterSelfPhoto");
        Hook(h, typeof(CameraManager), "ExitSelfPhoto");
        Hook(h, typeof(CameraFrameCtrl), "RegisterCameraActions");
        Hook(h, typeof(CameraFrameCtrl), "UnRegisterCameraActions");
        Hook(h, typeof(CameraFrameCtrl), "RecordCameraInitialParameters");
        Hook(h, typeof(CameraFrameCtrl), "ResetCameraInitialParameters");
        Hook(h, typeof(CameraFrameCtrl), "SetEntityShow");
        Hook(h, typeof(PlayerInputController), "set_IsSelfPhoto");
        Hook(h, typeof(CutsceneManager), "afterPlay");
        Hook(h, typeof(CutsceneManager), "afterStop");
        Hook(h, typeof(CutsceneManager), "Play");
        Hook(h, typeof(CutsceneManager), "StopCutscene");
        Hook(h, typeof(CutsceneManager), "SetHidingFlags");
        Hook(h, typeof(CameraStateSelfPhoto), "OnEnter");
        Hook(h, typeof(CameraStateSelfPhoto), "OnExit");
        Hook(h, typeof(CameraStateMachine), "onCameraStateChange");
        Hook(h, typeof(CameraFrameCtrl), "SetPhotoType");
        Hook(h, typeof(CameraFrameCtrl), "Init");
        Hook(h, typeof(CameraFrameCtrl), "UnInit");
    }

    private void Hook(Harmony h, Type t, string method)
    {
        try
        {
            var m = AccessTools.Method(t, method);
            if (m == null) { Log($"HOOK {t.Name}.{method}: method not found"); return; }
            h.Patch(m, postfix: new HarmonyMethod(typeof(RenderProbe).GetMethod(nameof(Postfix), BindingFlags.NonPublic | BindingFlags.Static)));
            Log($"HOOK {t.Name}.{method} armed");
        }
        catch (Exception ex) { Log($"HOOK {t.Name}.{method} FAILED {ex.GetType().Name}: {ex.Message}"); }
    }

    private static void Postfix(MethodBase __originalMethod, object[] __args)
    {
        try
        {
            var args = __args == null ? "" : string.Join(",", Array.ConvertAll(__args, a => a?.ToString() ?? "null"));
            _self?.Log($"FIRED {__originalMethod.DeclaringType?.Name}.{__originalMethod.Name}({args})");
        }
        catch { }
    }

    private void PollStates(float dt)
    {
        _pollAccum += dt;
        if (_pollAccum < 0.5f) return;
        _pollAccum = 0f;
        string s;
        try
        {
            var cut = CutsceneManager.IsCreated ? CutsceneManager.Instance : null;
            var pic = PlayerInputController.IsCreated ? PlayerInputController.Instance : null;
            s = $"IsCameraState={CameraFrameCtrl.IsCameraState} CameraState={CameraFrameCtrl.CameraState} " +
                $"IsSelfPhoto={pic?.IsSelfPhoto} InCutscene={cut?.InCutscene} CameraInCutscene={cut?.CameraInCutscene} " +
                $"CutsceneId={cut?.CurrentRunningCutsceneId}";
        }
        catch (Exception ex) { s = $"poll error {ex.GetType().Name}: {ex.Message}"; }
        if (s == _lastState) return;
        _lastState = s;
        Log($"STATE {s}");
    }
}
