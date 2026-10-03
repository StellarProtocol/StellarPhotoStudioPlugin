using System;
using System.Collections;
using System.Collections.Generic;
using Bokura.Rendering;
using Il2CppInterop.Runtime;
using Panda.ZGame;
using UnityEngine;
using UnityEngine.Rendering;

namespace Stellar.PhotoStudio.FreeCamProbe;

/// <summary>
/// Run 13b: the MultiLight (cluster) lamp is the only per-lamp path that reached the character in 13a (scene-config
/// clone i=39.6: char R +11.3). 13b sweeps its intensity, the weather volume's creature point-light multiplier
/// (creaturePointlightColorIntensity), two lamps of different colour on either side (left/right half regions), a back
/// lamp and a spot lamp; then unpauses and re-measures the residual seen in 13a.
/// </summary>
public sealed partial class FreeCamProbe
{
    private IEnumerator StepTrials13b()
    {
        var epoch = _sceneEpoch;
        var front = FrontCamera();
        try
        {
            yield return Wait(2f);
            var model = LiveModel(SelfEntity());
            var cam = MainCam();
            if (model == null || cam == null || front == null) { Log("R13b prerequisites missing"); yield break; }
            R13Globals("13b start");
            var chest = model.GetChestPosition();
            var feet = model.GetAttrGoPosition();
            var toCam = (cam.transform.position - chest).normalized;
            var right = Vector3.Cross(Vector3.up, toCam).normalized;
            _r13Lamp = chest + toCam * 1.0f + right * 0.7f + Vector3.up * 0.3f;
            var lampL = chest + toCam * 1.0f - right * 0.7f + Vector3.up * 0.3f;
            var lampBack = chest - toCam * 0.8f + Vector3.up * 0.5f;
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
            Log($"R13b lampR={V(_r13Lamp)} lampL={V(lampL)} back={V(lampBack)} chest={V(chest)}; regions: char ground sky charLeft(screen) charRight(screen)");
            R13Pause(true);
            yield return Frames(3);
            _r13Base = CaptureMany("R13b_base", _r13Regions);
            yield return Frames(3);
            Log("R13b NOISE " + R13Cmp(_r13Base, CaptureMany("R13b_noise", _r13Regions)));

            foreach (var i in new[] { 40f, 150f, 600f })
                yield return R13Trial($"M_i{i:F0}", R13Lamps(new[] { (_r13Lamp, R13Warm, i, false) }), epoch);
            foreach (var p in new[] { 5f, 20f })
                yield return R13Trial($"M_i40+pointI{p:F0}", R13WithPointI(p, R13Lamps(new[] { (_r13Lamp, R13Warm, 40f, false) })), epoch);
            yield return R13Trial("M_pointI20_nolamp", R13WithPointI(20f, () => (() => { }, () => "no lamp")), epoch);
            var blue = new Color(0.2f, 0.45f, 1f, 1f);
            yield return R13Trial("M_two_warmR_blueL_i150", R13Lamps(new[] { (_r13Lamp, R13Warm, 150f, false), (lampL, blue, 150f, false) }), epoch);
            yield return R13Trial("M_back_i150", R13Lamps(new[] { (lampBack, R13Warm, 150f, false) }), epoch);
            yield return R13Trial("M_spot_i150", R13Lamps(new[] { (_r13Lamp, R13Warm, 150f, true) }), epoch);

            var endPaused = CaptureMany("R13b_end_paused", _r13Regions);
            Log("R13b END paused vs base " + R13Cmp(_r13Base, endPaused));
            R13Pause(false);
            yield return Wait(3f);
            var endLive = CaptureMany("R13b_end_live3s", _r13Regions);
            Log("R13b END live+3s vs base " + R13Cmp(_r13Base, endLive) + $" mlDict={MultiLightManager.Instance?.lightDict_?.Count}");
            R13Globals("13b end");
        }
        finally
        {
            R13Pause(false);
            Release("cam.vcam");
        }
    }

    /// <summary>N MultiLight lamps (point or spot aimed at the chest), all registered before the capture.</summary>
    private Func<(Action, Func<string>)?> R13Lamps((Vector3 Pos, Color C, float I, bool Spot)[] lamps) => () =>
    {
        var chest = LiveModel(SelfEntity())!.GetChestPosition();
        var gos = new List<GameObject>();
        var mls = new List<MultiLight>();
        var before = MultiLightManager.Instance?.lightDict_?.Count ?? -1;
        foreach (var (pos, c, i, spot) in lamps)
        {
            var go = new GameObject("StellarR13bLamp");
            go.SetActive(false);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.LookRotation(chest - pos);
            var l = go.AddComponent(Il2CppType.Of<Light>()).Cast<Light>();
            l.type = spot ? LightType.Spot : LightType.Point;
            if (spot) { l.spotAngle = 50f; l.innerSpotAngle = 25f; }
            l.color = c; l.intensity = i; l.range = 6f; l.shadows = LightShadows.None;
            var ml = go.AddComponent(Il2CppType.Of<MultiLight>()).Cast<MultiLight>();
            ml.lightLayer = MultiLightLayer.Everything;
            ml.type = MultiLight.LightType.Common;
            ml.maxDistance = 128f;
            ml.falloffExponent = 4f;
            ml.specularScale = 1f;
            ml.IsReflection = true;
            go.SetActive(true);
            gos.Add(go); mls.Add(ml);
        }
        Log($"R13b lamps x{lamps.Length} mlDict {before}->{MultiLightManager.Instance?.lightDict_?.Count}");
        return (() => { foreach (var g in gos) UnityEngine.Object.Destroy(g); },
            () => $"mlDict={MultiLightManager.Instance?.lightDict_?.Count} idx=[{string.Join(",", mls.ConvertAll(m => m.InfoIndex))}] g_MultiLightCount={Shader.GetGlobalInt(MultiLightManager.g_MultiLightCount)}");
    };

    /// <summary>Wraps a trial with the weather volume's creature point-light multiplier overridden (everything else on
    /// that component temporarily not overriding), then restores every override flag, the value and the active flag.</summary>
    private Func<(Action, Func<string>)?> R13WithPointI(float value, Func<(Action, Func<string>)?> inner) => () =>
    {
        var w = CameraManager.Instance?.weatherParamsVolume_;
        if (w == null) return null;
        var plist = w.parameterList;
        var ov = new List<(VolumeParameter P, bool O)>();
        for (var k = 0; k < plist.Count; k++) { var p = plist[k]; ov.Add((p, p.overrideState)); p.overrideState = false; }
        var prevVal = w.creaturePointlightColorIntensity.value;
        var prevActive = w.active;
        var stackBefore = R13StackPointI();
        w.creaturePointlightColorIntensity.overrideState = true;
        w.creaturePointlightColorIntensity.value = value;
        w.active = true;
        var h = inner();
        return (() =>
        {
            h?.Item1();
            w.creaturePointlightColorIntensity.value = prevVal;
            foreach (var (p, o) in ov) p.overrideState = o;
            w.active = prevActive;
            Log($"R13b pointI restored value={w.creaturePointlightColorIntensity.value:F3} active={w.active} overrides={ov.Count}");
        }, () => $"pointI stack {stackBefore} -> {R13StackPointI()} (set {value}) {h?.Item2()}");
    };

    private static string R13StackPointI()
    {
        try
        {
            var s = VolumeManager.instance?.stack;
            var c = s?.GetComponent(Il2CppType.Of<ZBlueProtocolWeatherParamsVolume>())?.TryCast<ZBlueProtocolWeatherParamsVolume>();
            return c == null ? "n/a" : $"{c.creaturePointlightColorIntensity.value:F2}";
        }
        catch (Exception ex) { return "err " + ex.GetType().Name; }
    }
}
