using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Bokura.Rendering;
using Panda.ZGame;
using UnityEngine;
using UnityEngine.Rendering;

namespace Stellar.PhotoStudio.FreeCamProbe;

/// <summary>
/// Run 2 / D — the game's own character-light paths (run 1: a generic URP point light changes nothing — character
/// shading reads the game's GlobalCharacterLight shader globals). Front vcam on the player, then:
/// D1 <c>CameraManager.UpdateCharacterLight(true, i, range, warm)</c> → drives <c>CameraManager.characterLight_</c>
/// (a <c>CharacterLightVolume</c>: Enable / Intensity / Range / LightColor);
/// D2 <c>Bokura.Rendering.CharacterLight.Instance.SetLight(...)</c> (the MonoBehaviour behind it, if present);
/// D3 <c>CameraManager.UpdateFixedLight(true, 1, FixedLightData)</c> → the cutscene "fixed light" over
/// <c>weatherParamsVolume_</c> (ZBlueProtocolWeatherParamsVolume creature* params), seeded from the CURRENT values with
/// a warm, 3× key light. Each path: snapshot volume params before, capture before/with, call the game's own "off",
/// diff the snapshot, and if anything differs write the snapshot back (overrideState + value) and say so.
/// </summary>
public sealed partial class FreeCamProbe
{
    private sealed class PSnap
    {
        public string Name = "";
        public VolumeParameter P = null!;
        public bool Ov;
        public string Val = "";
        public Action<VolumeParameter>? RestoreValue;
    }

    private static readonly Color Warm = new(1f, 0.55f, 0.25f, 1f);

    private IEnumerator StepLights2()
    {
        var epoch = _sceneEpoch;
        var front = FrontCamera();
        try
        {
            if (front != null) yield return Wait(1.2f);
            var cm = CameraManager.Instance;
            if (cm == null || SelfRegion() is not { } region) { Log("D prerequisites missing"); yield break; }
            Log($"D characterLight_={(cm.characterLight_ == null ? "null" : $"active={cm.characterLight_.active}")} weatherParamsVolume_={(cm.weatherParamsVolume_ == null ? "null" : $"active={cm.weatherParamsVolume_.active}")} " +
                $"CharacterLight.Instance={CharLightText()}");
            var baseShot = Capture("D_before", region);
            yield return LightTrial("D1_UpdateCharacterLight_i3", region, baseShot, epoch, CharLightParams, () => CameraManager.Instance.UpdateCharacterLight(true, 3f, 4f, Warm), RestoreCharLight);
            yield return LightTrial("D1_UpdateCharacterLight_i12", region, baseShot, epoch, CharLightParams, () => CameraManager.Instance.UpdateCharacterLight(true, 12f, 6f, Warm), RestoreCharLight);
            yield return LightTrial("D1b_UpdateCharacterLight+active+Enable", region, baseShot, epoch, CharLightParams, ApplyCharLightForced, RestoreCharLightForced);
            yield return CharLightMonoTrial(region, baseShot, epoch);
            yield return LightTrial("D3_UpdateFixedLight", region, baseShot, epoch, WeatherParams, ApplyFixedLight, () => CameraManager.Instance.UpdateFixedLight(false, 0f, _fixedData));
            var after = Capture("D_after_all", region);
            Log($"D after all restores: lum before={(baseShot == null ? -1 : MeanLum(baseShot)):F2} after={(after == null ? -1 : MeanLum(after)):F2} {DiffText(baseShot, after)} CharacterLight.Instance={CharLightText()}");
        }
        finally { Release("cam.vcam"); }
    }

    private FixedLightData? _fixedData;

    /// <summary>Off path for D1: the game's own call with the PRIOR volume values (prior Enable / Intensity / Range / Color).</summary>
    private void RestoreCharLight()
    {
        var v = CameraManager.Instance?.characterLight_;
        if (v == null || _charLightPrior == null) return;
        var (en, i, r, c) = _charLightPrior.Value;
        CameraManager.Instance!.UpdateCharacterLight(en, i, r, c);
    }

    private (bool, float, float, Color)? _charLightPrior;
    private bool _clActivePrior;

    /// <summary>Run 2 showed UpdateCharacterLight(true, …) writes Intensity/Range/Color but never Enable, and the
    /// CharacterLightVolume component is inactive — so this variant also forces <c>active</c> + <c>Enable</c>.</summary>
    private void ApplyCharLightForced()
    {
        var cm = CameraManager.Instance;
        var v = cm.characterLight_;
        _clActivePrior = v != null && v.active;
        cm.UpdateCharacterLight(true, 8f, 6f, Warm);
        if (v == null) return;
        v.active = true;
        v.Enable.overrideState = true;
        v.Enable.value = true;
        Log($"D1b forced characterLight_.active=true Enable=true (prior active={_clActivePrior})");
    }

    private void RestoreCharLightForced()
    {
        RestoreCharLight();
        var v = CameraManager.Instance?.characterLight_;
        if (v != null) v.active = _clActivePrior;
    }

    private IEnumerator LightTrial(string name, RectInt region, Shot? baseShot, int epoch, Func<List<PSnap>> snap, Action apply, Action gameOff)
    {
        if (Aborted(epoch)) yield break;
        var pre = Snapshot(snap);
        if (name.StartsWith("D1"))
        {
            var v = CameraManager.Instance?.characterLight_;
            _charLightPrior = v == null ? null : (v.Enable.value, v.Intensity.value, v.Range.value, v.LightColor.value);
        }
        Log($"{name} pre [{Text(pre)}]");
        var key = "light2." + name;
        Arm(key, () => { try { gameOff(); } catch { } RestoreSnapshot(pre, name); });
        try
        {
            try { apply(); }
            catch (Exception ex) { Log($"{name} apply FAILED {ex.GetType().Name}: {Short(ex.Message)}"); yield break; }
            yield return Frames(4);
            var with = Capture($"{name}_on", region);
            var mid = Snapshot(snap);
            Log($"{name} ON: lum before={(baseShot == null ? -1 : MeanLum(baseShot)):F2} with={(with == null ? -1 : MeanLum(with)):F2} " +
                $"delta={(baseShot == null || with == null ? 0 : MeanLum(with) - MeanLum(baseShot)):F2} {DiffText(baseShot, with)} changedParams[{Changed(pre, mid)}] CharacterLight={CharLightText()}");
        }
        finally
        {
            // Game's own "off" first; measure whether it alone restored the snapshot.
            _releases.Remove(key);
            try { gameOff(); Log($"{name} game-off call ok"); } catch (Exception ex) { Log($"{name} game-off FAILED {ex.GetType().Name}: {Short(ex.Message)}"); }
        }
        yield return Frames(3);
        var post = Snapshot(snap);
        var diff = Changed(pre, post);
        Log($"{name} after game-off: restoredExactly={diff.Length == 0} residual[{diff}]");
        if (diff.Length > 0)
        {
            RestoreSnapshot(pre, name);
            yield return Frames(2);
            var post2 = Snapshot(snap);
            Log($"{name} after snapshot write-back: restoredExactly={Changed(pre, post2).Length == 0} residual[{Changed(pre, post2)}]");
        }
        var back = Capture($"{name}_off", region);
        Log($"{name} OFF: lum={(back == null ? -1 : MeanLum(back)):F2} {DiffText(baseShot, back)}");
    }

    private IEnumerator CharLightMonoTrial(RectInt region, Shot? baseShot, int epoch)
    {
        if (Aborted(epoch)) yield break;
        CharacterLight? cl = null;
        try { cl = CharacterLight.Instance; } catch (Exception ex) { Log($"D2 CharacterLight.Instance FAILED {ex.GetType().Name}"); }
        if (cl == null) { Log("D2 CharacterLight.Instance null — n/a"); yield break; }
        var pl = cl.PointLight;
        var prior = (cl.LightState, pl == null ? 0f : pl.intensity, pl == null ? 0f : pl.range, pl == null ? Color.white : pl.color);
        var priorPos = pl == null ? Vector3.zero : pl.transform.position;
        Log($"D2 prior {CharLightText()}");
        Arm("light2.mono", () => CharacterLight.Instance?.SetLight(prior.Item1, prior.Item2, prior.Item3, prior.Item4));
        try
        {
            cl.SetLight(true, 6f, 6f, Warm);
            yield return Frames(4);
            var with = Capture("D2_CharacterLight.SetLight_on", region);
            Log($"D2 ON {CharLightText()} lum delta={(baseShot == null || with == null ? 0 : MeanLum(with) - MeanLum(baseShot)):F2} {DiffText(baseShot, with)}");
        }
        finally { Release("light2.mono"); }
        yield return Frames(3);
        // D2b: ForceEnable(true) first (SetLight alone left LightState=False / light disabled in run 2).
        var priorEnabled = pl != null && pl.enabled;
        Arm("light2.mono2", () => { var c = CharacterLight.Instance; if (c == null) return; c.SetLight(prior.Item1, prior.Item2, prior.Item3, prior.Item4); c.ForceEnable(priorEnabled); });
        try
        {
            cl.ForceEnable(true);
            cl.SetLight(true, 6f, 6f, Warm);
            yield return Frames(4);
            var with2 = Capture("D2b_ForceEnable+SetLight_on", region);
            Log($"D2b ON {CharLightText()} lum delta={(baseShot == null || with2 == null ? 0 : MeanLum(with2) - MeanLum(baseShot)):F2} {DiffText(baseShot, with2)}");
        }
        finally { Release("light2.mono2"); }
        yield return Frames(3);
        var plNow = CharacterLight.Instance?.PointLight;
        Log($"D2 restored {CharLightText()} equalsPrior={(CharacterLight.Instance?.LightState == prior.Item1 && (plNow == null || (Math.Abs(plNow.intensity - prior.Item2) < 1e-4f && Math.Abs(plNow.range - prior.Item3) < 1e-4f && plNow.color == prior.Item4)))} posMoved={(plNow == null ? 0 : Vector3.Distance(plNow.transform.position, priorPos)):F3}m");
    }

    private void ApplyFixedLight()
    {
        var w = CameraManager.Instance.weatherParamsVolume_;
        var share = new FixedLightDataShare();
        if (w != null)
        {
            var ang = w.creatureCameraLightAngle.value;
            share.UseCameraLightAngle = true;
            share.CameraLightParm = new Vector3(ang.x, ang.y, ang.z);
            share.CameraToWorld = w.cameraLight2World.value;
            share.CameraLightColor = Warm;
            share.CreatureSunlightColorIntensity = Math.Max(0.5f, w.creatureSunlightColorIntensity.value) * 3f;
            share.CreatureSunlight_LowLightIntensity = w.creatureSunlight_LowLightIntensity.value;
            share.CreatureSunlight_ToonIntensityScale = w.creatureSunlight_ToonIntensityScale.value;
            share.creatureSunlight_ToonSaturationScale = w.creatureSunlight_ToonSaturationScale.value;
            share.AmbientColor = w.ambientSkyColor.value;
            share.creatureSkylightColorIntensity = w.creatureSkylightColorIntensity.value;
            share.creatureSkyight_LowLightIntensity = w.creatureSkyight_LowLightIntensity.value;
            share.creatureSkylight_ToonIntensityScale = w.creatureSkylight_ToonIntensityScale.value;
            share.creatureSkylight_ToonSaturationScale = w.creatureSkylight_ToonSaturationScale.value;
        }
        share.LightCurve = AnimationCurve.Constant(0f, 1f, 1f);
        _fixedData = new FixedLightData { Share = share };
        Log($"D3 FixedLightData seeded from current weather: angle={V(share.CameraLightParm)} cam2world={share.CameraToWorld} sun={share.CreatureSunlightColorIntensity:F2} color=warm");
        CameraManager.Instance.UpdateFixedLight(true, 1f, _fixedData);
    }

    private List<PSnap> CharLightParams()
    {
        var v = CameraManager.Instance?.characterLight_;
        if (v == null) return new List<PSnap>();
        return new List<PSnap> { P("Enable", v.Enable), P("Intensity", v.Intensity), P("Range", v.Range), P("LightColor", v.LightColor) };
    }

    private List<PSnap> WeatherParams()
    {
        var w = CameraManager.Instance?.weatherParamsVolume_;
        if (w == null) return new List<PSnap>();
        return new List<PSnap>
        {
            P("camLightAngle", w.creatureCameraLightAngle), P("cam2World", w.cameraLight2World), P("sunI", w.creatureSunlightColorIntensity),
            P("sunColor", w.creatureSunlightColor), P("sunLow", w.creatureSunlight_LowLightIntensity), P("sunToonI", w.creatureSunlight_ToonIntensityScale),
            P("sunToonS", w.creatureSunlight_ToonSaturationScale), P("skyI", w.creatureSkylightColorIntensity), P("skyColor", w.creatureSkylightColor),
            P("skyLow", w.creatureSkyight_LowLightIntensity), P("skyToonI", w.creatureSkylight_ToonIntensityScale), P("skyToonS", w.creatureSkylight_ToonSaturationScale),
            P("pointI", w.creaturePointlightColorIntensity), P("ambColor", w.ambientSkyColor), P("ambI", w.ambientSkyIntensity),
        };
    }

    /// <summary>Typed snapshot of one VolumeParameter: overrideState + value (+ a write-back closure).</summary>
    private static PSnap P(string name, VolumeParameter? p)
    {
        var s = new PSnap { Name = name, P = p! };
        if (p == null) { s.Val = "null"; return s; }
        s.Ov = p.overrideState;
        if (p.TryCast<BoolParameter>() is { } b) { var v = b.value; s.Val = v.ToString(); s.RestoreValue = q => q.Cast<BoolParameter>().value = v; }
        else if (p.TryCast<FloatParameter>() is { } f) { var v = f.value; s.Val = v.ToString("F4"); s.RestoreValue = q => q.Cast<FloatParameter>().value = v; }
        else if (p.TryCast<MinFloatParameter>() is { } mf) { var v = mf.value; s.Val = v.ToString("F4"); s.RestoreValue = q => q.Cast<MinFloatParameter>().value = v; }
        else if (p.TryCast<ColorParameter>() is { } c) { var v = c.value; s.Val = $"{v.r:F3},{v.g:F3},{v.b:F3},{v.a:F3}"; s.RestoreValue = q => q.Cast<ColorParameter>().value = v; }
        else if (p.TryCast<Vector4Parameter>() is { } v4) { var v = v4.value; s.Val = $"{v.x:F3},{v.y:F3},{v.z:F3},{v.w:F3}"; s.RestoreValue = q => q.Cast<Vector4Parameter>().value = v; }
        else s.Val = "type:" + p.GetIl2CppType().Name;
        return s;
    }

    private List<PSnap> Snapshot(Func<List<PSnap>> f)
    {
        try { return f(); } catch (Exception ex) { Log($"D snapshot FAILED {ex.GetType().Name}: {Short(ex.Message)}"); return new List<PSnap>(); }
    }

    private static string Text(List<PSnap> s) => string.Join(" ", s.Select(x => $"{x.Name}={(x.Ov ? "" : "~")}{x.Val}"));

    private static string Changed(List<PSnap> a, List<PSnap> b)
    {
        var parts = new List<string>();
        foreach (var x in a)
        {
            var y = b.FirstOrDefault(q => q.Name == x.Name);
            if (y == null) { parts.Add($"{x.Name}:missing"); continue; }
            if (x.Ov != y.Ov || x.Val != y.Val) parts.Add($"{x.Name}:{(x.Ov ? "" : "~")}{x.Val}->{(y.Ov ? "" : "~")}{y.Val}");
        }
        return string.Join(" ", parts);
    }

    private void RestoreSnapshot(List<PSnap> pre, string tag)
    {
        var n = 0;
        foreach (var s in pre)
        {
            if (s.P == null) continue;
            try { s.RestoreValue?.Invoke(s.P); s.P.overrideState = s.Ov; n++; }
            catch (Exception ex) { Log($"{tag} write-back {s.Name} FAILED {ex.GetType().Name}"); }
        }
        Log($"{tag} snapshot write-back: {n}/{pre.Count} params");
    }

    private static string CharLightText()
    {
        try
        {
            var cl = CharacterLight.Instance;
            if (cl == null) return "null";
            var pl = cl.PointLight;
            return $"state={cl.LightState} enabled={cl.isActiveAndEnabled} light={(pl == null ? "null" : $"i={pl.intensity:F2} r={pl.range:F2} c=({pl.color.r:F2},{pl.color.g:F2},{pl.color.b:F2}) en={pl.enabled} pos={V(pl.transform.position)} parent={(pl.transform.parent == null ? "none" : pl.transform.parent.name)}")}";
        }
        catch (Exception ex) { return $"err {ex.GetType().Name}"; }
    }
}
