using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using Bokura.Rendering;
using Panda.ZGame;
using UnityEngine;

namespace Stellar.PhotoStudio.FreeCamProbe;

/// <summary>
/// Run 13c: 13b found the gate — the weather volume's creaturePointlightColorIntensity read 0.00 on the stack, so the
/// character ignored every cluster lamp until it was overridden (pointI 5 + one i=40 lamp: char +46 L). 13c measures,
/// with pointI overridden, natural levels (pointI 1/2), two coloured lamps on either side, a back lamp, a spot, a far
/// lamp, and the frame cost of 8 lamps (unpaused).
/// </summary>
public sealed partial class FreeCamProbe
{
    private IEnumerator StepTrials13c()
    {
        var epoch = _sceneEpoch;
        var front = FrontCamera();
        try
        {
            yield return Wait(2f);
            var model = LiveModel(SelfEntity());
            var cam = MainCam();
            if (model == null || cam == null || front == null) { Log("R13c prerequisites missing"); yield break; }
            Log($"R13c stack pointI at start={R13StackPointI()} cmVolume pointI={CameraManager.Instance?.weatherParamsVolume_?.creaturePointlightColorIntensity?.value:F2}");
            var chest = model.GetChestPosition();
            var feet = model.GetAttrGoPosition();
            var toCam = (cam.transform.position - chest).normalized;
            var right = Vector3.Cross(Vector3.up, toCam).normalized;
            _r13Lamp = chest + toCam * 1.0f + right * 0.7f + Vector3.up * 0.3f;
            var lampL = chest + toCam * 1.0f - right * 0.7f + Vector3.up * 0.3f;
            var lampBack = chest - toCam * 0.8f + Vector3.up * 0.5f;
            var lampFar = chest + toCam * 2.0f + right * 2.0f + Vector3.up * 0.5f;
            var full = RegionAround(cam, chest, 0.08f, 0.16f);
            var half = full.width / 2;
            _r13Regions = new[]
            {
                full,
                RegionAround(cam, feet + toCam * 0.8f + right * 0.6f, 0.12f, 0.04f),
                new RectInt(0, (int)(Screen.height * 0.88f), Screen.width, (int)(Screen.height * 0.1f)),
                new RectInt(full.x, full.y, half, full.height),
                new RectInt(full.x + half, full.y, full.width - half, full.height),
            };
            var lampVp = cam.WorldToViewportPoint(_r13Lamp);
            Log($"R13c lampR={V(_r13Lamp)} (viewport x={lampVp.x:F2}) lampL={V(lampL)} back={V(lampBack)} far={V(lampFar)} dFar={Vector3.Distance(lampFar, chest):F2}m");
            R13Pause(true);
            yield return Frames(3);
            _r13Base = CaptureMany("R13c_base", _r13Regions);
            yield return Frames(3);
            Log("R13c NOISE " + R13Cmp(_r13Base, CaptureMany("R13c_noise", _r13Regions)));
            var blue = new Color(0.2f, 0.45f, 1f, 1f);
            var one = new[] { (_r13Lamp, R13Warm, 40f, false) };
            yield return R13Trial("C_i40_pointI1", R13WithPointI(1f, R13Lamps(one)), epoch);
            yield return R13Trial("C_i40_pointI2", R13WithPointI(2f, R13Lamps(one)), epoch);
            yield return R13Trial("C_i10_pointI2", R13WithPointI(2f, R13Lamps(new[] { (_r13Lamp, R13Warm, 10f, false) })), epoch);
            yield return R13Trial("C_warmOnly_R_pointI2", R13WithPointI(2f, R13Lamps(new[] { (_r13Lamp, R13Warm, 40f, false) })), epoch);
            yield return R13Trial("C_blueOnly_L_pointI2", R13WithPointI(2f, R13Lamps(new[] { (lampL, blue, 40f, false) })), epoch);
            yield return R13Trial("C_two_warmR_blueL_pointI2", R13WithPointI(2f, R13Lamps(new[] { (_r13Lamp, R13Warm, 40f, false), (lampL, blue, 40f, false) })), epoch);
            yield return R13Trial("C_back_pointI2", R13WithPointI(2f, R13Lamps(new[] { (lampBack, R13Warm, 40f, false) })), epoch);
            yield return R13Trial("C_spot_pointI2", R13WithPointI(2f, R13Lamps(new[] { (_r13Lamp, R13Warm, 40f, true) })), epoch);
            yield return R13Trial("C_far3m_pointI2", R13WithPointI(2f, R13Lamps(new[] { (lampFar, R13Warm, 40f, false) })), epoch);
            Log("R13c END paused vs base " + R13Cmp(_r13Base, CaptureMany("R13c_end_paused", _r13Regions)));
            R13Pause(false);
            yield return Wait(1f);

            // Frame cost (unpaused, camera static): 0 lamps vs 8 lamps (+pointI 2), 180 frames each, twice interleaved.
            for (var rep = 0; rep < 2; rep++)
            {
                yield return R13FrameCost($"cost rep{rep} 0 lamps", null);
                var ring = new List<(Vector3, Color, float, bool)>();
                for (var k = 0; k < 8; k++)
                {
                    var a = k * Mathf.PI / 4f;
                    ring.Add((chest + new Vector3(Mathf.Cos(a), 0.3f, Mathf.Sin(a)) * 1.2f, k % 2 == 0 ? R13Warm : blue, 40f, false));
                }
                yield return R13FrameCost($"cost rep{rep} 8 lamps+pointI2", R13WithPointI(2f, R13Lamps(ring.ToArray())));
            }
            R13Globals("13c end");
        }
        finally
        {
            R13Pause(false);
            Release("cam.vcam");
        }
    }

    private IEnumerator R13FrameCost(string tag, Func<(Action, Func<string>)?>? apply)
    {
        (Action, Func<string>)? h = null;
        if (apply != null) { try { h = apply(); } catch (Exception ex) { Log($"R13c {tag} apply FAILED {ex.Message}"); } }
        if (h != null) Arm("r13c.cost", h.Value.Item1);
        yield return Frames(20);
        var dts = new List<float>();
        var sw = Stopwatch.StartNew();
        for (var f = 0; f < 180; f++) { yield return null; dts.Add(Time.unscaledDeltaTime * 1000f); }
        dts.Sort();
        var avg = 0f; foreach (var d in dts) avg += d; avg /= dts.Count;
        Log($"R13c FRAMECOST {tag}: avg={avg:F2}ms p50={dts[dts.Count / 2]:F2} p95={dts[(int)(dts.Count * 0.95)]:F2} wall={sw.Elapsed.TotalMilliseconds / 180:F2}ms/f {(h == null ? "" : h.Value.Item2())}");
        if (h != null) Release("r13c.cost");
        yield return Frames(5);
    }
}
