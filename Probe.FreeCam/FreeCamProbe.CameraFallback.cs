using System;
using System.Collections;
using Panda.ZGame;
using UnityEngine;

namespace Stellar.PhotoStudio.FreeCamProbe;

/// <summary>
/// § 9 item 1 fallback — approach 3: <c>CinemachineBrain.enabled = false</c>, drive the Main Camera transform/FOV
/// directly, and measure whether anything overwrites them before the next frame. Auto runs it only when the vcam
/// could not be created; F8 can run it any time.
/// </summary>
public sealed partial class FreeCamProbe
{
    private IEnumerator StepCameraFallback()
    {
        var epoch = _sceneEpoch;
        var brain = CameraManager.Instance?.Brain;
        var cam = MainCam();
        var model = LiveModel(SelfEntity());
        if (brain == null || cam == null || model == null) { Log("FALLBACK prerequisites missing"); yield break; }
        var t = cam.transform;
        var origPos = t.position;
        var origRot = t.rotation;
        var origFov = cam.fieldOfView;
        var wasEnabled = brain.enabled;
        brain.enabled = false;
        Arm("cam.brainOff", () =>
        {
            var c = MainCam();
            if (c != null) { c.transform.SetPositionAndRotation(origPos, origRot); c.fieldOfView = origFov; }
            var b = CameraManager.Instance?.Brain;
            if (b != null) b.enabled = wasEnabled;
        });
        Log($"FALLBACK brain.enabled {wasEnabled} -> {brain.enabled}; {CamPose(cam)}");
        var center = model.GetChestPosition();
        var offset = origPos - center;
        int frames = 0, posOverwritten = 0, fovOverwritten = 0;
        float maxPosDelta = 0f, maxFovDelta = 0f;
        Vector3 setPos = origPos;
        var setFov = 50f;
        var lateOverwrites = 0;
        ProbeTicks.LateTick = () =>
        {
            // LateUpdate-time check: did something overwrite our Update-time write within this frame?
            var c = MainCam();
            if (c != null && (Vector3.Distance(c.transform.position, setPos) > 0.001f || Math.Abs(c.fieldOfView - setFov) > 0.01f)) lateOverwrites++;
        };
        try
        {
            var start = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - start < 2f && !Aborted(epoch))
            {
                // Read what survived since our last write (next-frame overwrite check), then write again.
                if (frames > 0)
                {
                    var dp = Vector3.Distance(t.position, setPos);
                    var df = Math.Abs(cam.fieldOfView - setFov);
                    if (dp > 0.001f) posOverwritten++;
                    if (df > 0.01f) fovOverwritten++;
                    maxPosDelta = Math.Max(maxPosDelta, dp);
                    maxFovDelta = Math.Max(maxFovDelta, df);
                }
                var a = (Time.realtimeSinceStartup - start) / 2f * 40f;
                setPos = center + Quaternion.Euler(0f, a, 0f) * offset;
                setFov = 50f + 20f * Mathf.Sin(a * Mathf.Deg2Rad * 4f);
                t.SetPositionAndRotation(setPos, Quaternion.LookRotation(center - setPos));
                cam.fieldOfView = setFov;
                if (frames % 20 == 0) Log($"FALLBACK f={frames} set={V(setPos)} fov={setFov:F2} now {CamPose(cam)}");
                frames++;
                yield return null;
            }
            Capture("1b_fallback_brain_off", new RectInt(0, 0, 4, 4));
        }
        finally
        {
            ProbeTicks.LateTick = null;
            Release("cam.brainOff");
        }
        Log($"FALLBACK summary frames={frames} posOverwrittenNextFrame={posOverwritten} fovOverwrittenNextFrame={fovOverwritten} " +
            $"maxPosDelta={maxPosDelta:F4}m maxFovDelta={maxFovDelta:F3} lateUpdateOverwrites={lateOverwrites}");
        yield return Frames(3);
        Log($"FALLBACK after re-enable: brain.enabled={brain.enabled} {CamPose(MainCam())} (orig pos={V(origPos)} fov={origFov:F2})");
    }
}
