using System;
using System.Collections;
using System.Collections.Generic;
using Bokura.Rendering;
using Il2CppInterop.Runtime;
using Panda.ZGame;
using UnityEngine;

namespace Stellar.PhotoStudio.FreeCamProbe;

/// <summary>
/// Run 13d: 13c showed the character's response to a cluster lamp barely depends on where the lamp is (back, spot,
/// 1.3 m vs 2.9 m, i10 vs i40 all ~+20..25 L, and the screen-left half is always brighter whichever side the lamp is
/// on). 13d tests whether the creature point-light term is positional at all: range edges (in/out of range), a lamp
/// above the head and one at the feet, and whether the paused residual decays (TAA history) or persists.
/// </summary>
public sealed partial class FreeCamProbe
{
    private IEnumerator StepTrials13d()
    {
        var epoch = _sceneEpoch;
        var front = FrontCamera();
        try
        {
            yield return Wait(2f);
            var model = LiveModel(SelfEntity());
            var cam = MainCam();
            if (model == null || cam == null || front == null) { Log("R13d prerequisites missing"); yield break; }
            Log($"R13d stack pointI at start={R13StackPointI()}");
            var chest = model.GetChestPosition();
            var head = model.GetHeadPosition();
            var feet = model.GetAttrGoPosition();
            var toCam = (cam.transform.position - chest).normalized;
            var right = Vector3.Cross(Vector3.up, toCam).normalized;
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
            Vector3 Side(float d) => chest + right * d + Vector3.up * 0.3f;   // to the character's side, away from the camera axis
            R13Pause(true);
            yield return Frames(3);
            _r13Base = CaptureMany("R13d_base", _r13Regions);
            yield return Frames(3);
            Log("R13d NOISE " + R13Cmp(_r13Base, CaptureMany("R13d_noise", _r13Regions)));
            var cases = new (string Name, Vector3 Pos, float Range)[]
            {
                ("D_side1m_r6", Side(1f), 6f),
                ("D_side5m_r6", Side(5f), 6f),
                ("D_side8m_r6_outOfRange", Side(8f), 6f),
                ("D_side1m_r2", Side(1f), 2f),
                ("D_side3m_r2_outOfRange", Side(3f), 2f),
                ("D_side1m_r6_otherSide", chest - right * 1f + Vector3.up * 0.3f, 6f),
                ("D_above_head1.5m_r6", head + Vector3.up * 1.5f, 6f),
                ("D_feet_front_r6", feet + toCam * 0.6f + Vector3.up * 0.05f, 6f),
            };
            foreach (var (name, pos, range) in cases)
            {
                Log($"R13d case {name} pos={V(pos)} dChest={Vector3.Distance(pos, chest):F2}m range={range} viewportX={cam.WorldToViewportPoint(pos).x:F2}");
                yield return R13Trial(name, R13WithPointI(2f, R13LampsRange(new[] { (pos, R13Warm, 40f, range) })), epoch);
            }
            var end0 = CaptureMany("R13d_end_paused", _r13Regions);
            Log("R13d END paused vs base " + R13Cmp(_r13Base, end0));
            yield return Frames(60);
            var end1 = CaptureMany("R13d_end_paused_60f", _r13Regions);
            Log("R13d END paused+60 frames vs base " + R13Cmp(_r13Base, end1));
            for (var k = 0; k < 8; k++) { CaptureMany($"R13d_settle{k}", _r13Regions); yield return null; }
            var end2 = CaptureMany("R13d_end_paused_8renders", _r13Regions);
            Log("R13d END paused+8 extra renders vs base " + R13Cmp(_r13Base, end2));
        }
        finally
        {
            R13Pause(false);
            Release("cam.vcam");
        }
    }

    private Func<(Action, Func<string>)?> R13LampsRange((Vector3 Pos, Color C, float I, float Range)[] lamps) => () =>
    {
        var gos = new List<GameObject>();
        foreach (var (pos, c, i, range) in lamps)
        {
            var go = new GameObject("StellarR13dLamp");
            go.SetActive(false);
            go.transform.position = pos;
            var l = go.AddComponent(Il2CppType.Of<Light>()).Cast<Light>();
            l.type = LightType.Point; l.color = c; l.intensity = i; l.range = range; l.shadows = LightShadows.None;
            var ml = go.AddComponent(Il2CppType.Of<MultiLight>()).Cast<MultiLight>();
            ml.lightLayer = MultiLightLayer.Everything; ml.type = MultiLight.LightType.Common; ml.maxDistance = 128f;
            ml.falloffExponent = 4f; ml.specularScale = 1f; ml.IsReflection = true;
            go.SetActive(true);
            gos.Add(go);
        }
        return (() => { foreach (var g in gos) UnityEngine.Object.Destroy(g); }, () => $"mlDict={MultiLightManager.Instance?.lightDict_?.Count}");
    };
}
