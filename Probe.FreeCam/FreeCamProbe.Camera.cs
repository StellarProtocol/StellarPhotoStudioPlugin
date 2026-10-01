using System;
using System.Collections;
using System.Collections.Generic;
using Cinemachine;
using Il2CppInterop.Runtime;
using Panda.ZGame;
using UnityEngine;

namespace Stellar.PhotoStudio.FreeCamProbe;

/// <summary>
/// § 9 item 1 — approach 1: a runtime-created top-priority <c>CinemachineVirtualCamera</c> takes the brain. Logs the
/// live vcam list, the brain before/after, a 3 s orbit with per-sample Main Camera vs vcam deltas, lens FOV 30/80 and
/// Dutch ±20 (does <c>CameraManager.UpdateResetFov</c> fight?), then the blend back to the game vcam on release.
/// </summary>
public sealed partial class FreeCamProbe
{
    private bool _vcamFailed;

    private IEnumerator StepCameraTakeover()
    {
        var epoch = _sceneEpoch;
        var cm = CameraManager.Instance;
        var brain = cm?.Brain;
        var cam = MainCam();
        var model = LiveModel(SelfEntity());
        if (brain == null || cam == null || model == null) { Log($"CAM prerequisites missing brain={brain != null} cam={cam != null} model={model != null}"); _currentOutcome = "skipped"; yield break; }
        LogBrain("before", brain);
        var maxPrio = LogVcams();
        Log($"CAM main before: {CamPose(cam)}");
        var vcam = TryCreateVcam(cam.transform.position, cam.transform.rotation, cam.fieldOfView, maxPrio + 1000);
        if (vcam == null) { _vcamFailed = true; _currentOutcome = "vcam-failed (auto runs 1b fallback next)"; yield break; }
        try
        {
            yield return SampleBrain("blend-in", brain, vcam, 1.5f, epoch);
            LogBrain("after takeover", brain);
            yield return Orbit(vcam, model, 3f, epoch);
            foreach (var (fov, dutch) in new[] { (30f, 0f), (80f, 0f), (55f, 20f), (55f, -20f) })
            {
                if (Aborted(epoch)) yield break;
                yield return LensStep(vcam, fov, dutch);
            }
            LogBrain("before release", brain);
        }
        finally { Release("cam.vcam"); }
        yield return SampleBrain("blend-out", brain, null, 2f, epoch);
        LogBrain("after release", brain);
        Log($"CAM main after release: {CamPose(MainCam())}");
    }

    private void LogBrain(string when, CinemachineBrain brain)
    {
        Try($"brain {when}", () =>
        {
            var blend = brain.ActiveBlend;
            var def = brain.m_DefaultBlend;
            Log($"BRAIN {when}: enabled={brain.enabled} update={brain.m_UpdateMethod} blendUpdate={brain.m_BlendUpdateMethod} " +
                $"defaultBlend={def.m_Style}/{def.m_Time:F2}s active={VcamName(brain.ActiveVirtualCamera)} isBlending={brain.IsBlending} " +
                $"blend={(blend == null ? "none" : $"{blend.Description} t={blend.TimeInBlend:F2}/{blend.Duration:F2}")}");
        });
    }

    /// <summary>Logs every registered vcam with priority/liveness; returns the highest priority seen.</summary>
    private int LogVcams()
    {
        var max = 0;
        Try("vcam list", () =>
        {
            var core = CinemachineCore.Instance;
            var n = core.VirtualCameraCount;
            var parts = new List<string>();
            for (var i = 0; i < n; i++)
            {
                var vc = core.GetVirtualCamera(i);
                if (vc == null) continue;
                max = Math.Max(max, vc.Priority);
                string live;
                try { live = core.IsLive(vc.Cast<ICinemachineCamera>()).ToString(); } catch (Exception ex) { live = "err:" + ex.GetType().Name; }
                parts.Add($"{vc.Name}:{vc.GetIl2CppType().Name}:prio={vc.Priority}:enabled={vc.isActiveAndEnabled}:live={live}");
            }
            Log($"VCAMS count={n} maxPrio={max} [{string.Join(" | ", parts)}]");
        });
        return max;
    }

    /// <summary>Creates our vcam via IL2CPP AddComponent; null (with the reason logged) on failure. Armed as "cam.vcam".</summary>
    private CinemachineVirtualCamera? TryCreateVcam(Vector3 pos, Quaternion rot, float fov, int priority)
    {
        GameObject? go = null;
        try
        {
            go = new GameObject("StellarFreeCamProbeVcam");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.transform.SetPositionAndRotation(pos, rot);
            var comp = go.AddComponent(Il2CppType.Of<CinemachineVirtualCamera>());
            if (comp == null) throw new InvalidOperationException("AddComponent returned null");
            var vcam = comp.Cast<CinemachineVirtualCamera>();
            var cam = MainCam();
            var lens = vcam.m_Lens;
            lens.FieldOfView = fov;
            lens.Dutch = 0f;
            if (cam != null) { lens.NearClipPlane = cam.nearClipPlane; lens.FarClipPlane = cam.farClipPlane; }
            vcam.m_Lens = lens;
            vcam.Priority = priority;
            var keep = go;
            Arm("cam.vcam", () => UnityEngine.Object.Destroy(keep));
            Log($"VCAM created: AddComponent ok type={vcam.GetIl2CppType().FullName} prio={vcam.Priority} lensFov={vcam.m_Lens.FieldOfView:F1} near={vcam.m_Lens.NearClipPlane:F2} far={vcam.m_Lens.FarClipPlane:F0} follow={(vcam.Follow == null ? "null" : "set")} lookAt={(vcam.LookAt == null ? "null" : "set")}");
            return vcam;
        }
        catch (Exception ex)
        {
            Log($"VCAM create FAILED {ex.GetType().Name}: {ex.Message}");
            if (go != null) UnityEngine.Object.Destroy(go);
            return null;
        }
    }

    /// <summary>Per-frame brain sampling: active vcam, blend progress, Main Camera distance to our vcam.</summary>
    private IEnumerator SampleBrain(string label, CinemachineBrain brain, CinemachineVirtualCamera? ours, float seconds, int epoch)
    {
        var start = Time.realtimeSinceStartup;
        var frames = 0;
        var lastActive = "";
        float settledAt = -1f;
        float maxBlend = 0f;
        while (Time.realtimeSinceStartup - start < seconds && !Aborted(epoch))
        {
            frames++;
            var active = VcamName(brain.ActiveVirtualCamera);
            var blend = brain.ActiveBlend;
            if (blend != null) maxBlend = Math.Max(maxBlend, blend.Duration);
            if (active != lastActive) { Log($"BLEND {label} f={frames} t={Time.realtimeSinceStartup - start:F2}s active -> {active}"); lastActive = active; }
            if (settledAt < 0f && !brain.IsBlending && frames > 1) settledAt = Time.realtimeSinceStartup - start;
            if (frames % 10 == 1)
            {
                var cam = MainCam();
                var d = ours != null && cam != null ? Vector3.Distance(cam.transform.position, ours.transform.position) : -1f;
                Log($"BLEND {label} f={frames} t={Time.realtimeSinceStartup - start:F2}s isBlending={brain.IsBlending} " +
                    $"blend={(blend == null ? "none" : $"{blend.TimeInBlend:F2}/{blend.Duration:F2}")} camToVcam={d:F3}m {CamPose(cam)}");
            }
            yield return null;
        }
        Log($"BLEND {label} summary frames={frames} maxBlendDuration={maxBlend:F2}s firstNotBlendingAt={settledAt:F2}s final active={lastActive}");
    }

    private IEnumerator Orbit(CinemachineVirtualCamera vcam, ZModel model, float seconds, int epoch)
    {
        var center = model.GetChestPosition();
        var cam = MainCam()!;
        var offset = cam.transform.position - center;
        var start = Time.realtimeSinceStartup;
        int frames = 0, lagFrames = 0;
        float maxPos = 0f, maxAng = 0f, sumPos = 0f;
        while (Time.realtimeSinceStartup - start < seconds && !Aborted(epoch))
        {
            // Measure what the brain produced last frame against the pose we set last frame, then set the next pose.
            var dPos = Vector3.Distance(cam.transform.position, vcam.transform.position);
            var dAng = Quaternion.Angle(cam.transform.rotation, vcam.transform.rotation);
            if (frames > 2) { maxPos = Math.Max(maxPos, dPos); maxAng = Math.Max(maxAng, dAng); sumPos += dPos; if (dPos > 0.01f) lagFrames++; }
            var a = (Time.realtimeSinceStartup - start) / seconds * 60f;   // a small 60° arc
            var pos = center + Quaternion.Euler(0f, a, 0f) * offset;
            vcam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(center - pos));
            if (frames % 15 == 0) Log($"ORBIT f={frames} arc={a:F1}deg vcam={V(pos)} dPos={dPos:F3}m dAng={dAng:F2}deg {CamPose(cam)}");
            frames++;
            yield return null;
        }
        Log($"ORBIT summary frames={frames} radius={offset.magnitude:F2}m meanDPos={(frames > 3 ? sumPos / (frames - 3) : 0):F4}m maxDPos={maxPos:F4}m maxDAng={maxAng:F3}deg framesOff>1cm={lagFrames}");
    }

    private IEnumerator LensStep(CinemachineVirtualCamera vcam, float fov, float dutch)
    {
        var lens = vcam.m_Lens;
        lens.FieldOfView = fov;
        lens.Dutch = dutch;
        vcam.m_Lens = lens;
        var cam = MainCam()!;
        var fovs = new List<string>();
        for (var i = 0; i < 20; i++)
        {
            yield return null;
            if (i % 4 == 3) fovs.Add($"{cam.fieldOfView:F2}/roll{NormRoll(cam.transform.rotation.eulerAngles.z):F1}");
        }
        var cm = CameraManager.Instance;
        string game = "-";
        Try("game fov", () => game = $"resetFov_={cm.resetFov_} defaultFov_={cm.defaultFov_:F2} gameFreeLookFov={cm.GetDefaultCM()?.m_Lens.FieldOfView:F2}");
        Log($"LENS set fov={fov} dutch={dutch} -> mainCam fov/roll over 20 frames [{string.Join(" ", fovs)}] lensNow={vcam.m_Lens.FieldOfView:F2}/{vcam.m_Lens.Dutch:F1} {game}");
        Capture($"1_lens_fov{fov:F0}_dutch{dutch:F0}", new RectInt(0, 0, 4, 4));
    }

    private static float NormRoll(float z) => z > 180f ? z - 360f : z;
}
