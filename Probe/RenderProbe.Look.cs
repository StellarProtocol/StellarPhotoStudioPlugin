using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Bokura.Rendering;
using Il2CppInterop.Runtime;
using Panda.ZGame;
using Stellar.Abstractions.Services;
using UnityEngine;
using UnityEngine.Rendering;

namespace Stellar.PhotoStudioProbe;

public sealed partial class RenderProbe
{
    private Volume? _volume;
    private VolumeProfile? _profile;

    private List<(string, Func<IEnumerator>)> BuildSteps() => new()
    {
        ("env", StepEnv),
        ("baseline", () => Snap("00_baseline")),
        ("grey", StepGrey),
        ("dof_ZDofVolume", StepDofZ),
        ("dof_ZUnityDepthOfField", StepDofUnity),
        ("dof_variants", StepDofVariants),
        ("dof_game_api", StepDofGameApi),
        ("vignette_ZUnityVignetteVolume", StepVignetteUnity),
        ("vignette_ZVignetteVolume", StepVignetteZ),
        ("bloom_intensity", () => StepBloom(pc: false)),
        ("bloom_intensity_PC", () => StepBloom(pc: true)),
        ("bloom_threshold0_variants", StepBloomVariants),
        ("visibility_enumerate", StepEnumerate),
        ("hud_hide_ZUiRoot.SetUIInvisible", StepHudHide),
        ("nameplate_HudMgr.SetHudSwitch", StepNameplates),
        ("others_CameraFrameCtrl.SetEntityShow", StepOtherPlayers),
        ("others_CutsceneManager.SetHidingFlags", StepCutsceneHiding),
        ("capture_ScreenCapture", StepScreenCapture),
        ("capture_CameraRender", StepCameraRender),
        ("hold_grey_for_external_scrot", StepHoldGrey),
    };

    private IEnumerator StepEnv()
    {
        Log($"screen={Screen.width}x{Screen.height} fullScreen={Screen.fullScreen} quality={QualitySettings.GetQualityLevel()} " +
            $"({QualitySettings.names[QualitySettings.GetQualityLevel()]}) gfx={SystemInfo.graphicsDeviceName} api={SystemInfo.graphicsDeviceVersion}");
        try { Log($"QualityGradeSetting.QualityGrade={Panda.Utility.Quality.QualityGradeSetting.QualityGrade}"); } catch (Exception ex) { Log($"QualityGrade FAILED {ex.Message}"); }
        foreach (var n in new[] { "UnityEngine.Rendering.Volume", "UnityEngine.Rendering.VolumeProfile",
                     "Bokura.Rendering.ZColorAdjustmentVolume", "Bokura.Rendering.ZDofVolume", "Bokura.Rendering.ZUnityDepthOfField",
                     "Bokura.Rendering.ZUnityVignetteVolume", "Bokura.Rendering.ZVignetteVolume", "Bokura.Rendering.ZBloomVolume",
                     "Panda.Hud.HudMgr", "Panda.ZUi.ZUiRoot", "Panda.ZGame.CameraFrameCtrl", "Panda.ZGame.Timeline.CutsceneManager" })
            Log($"FindType {n} -> {(StellarInterop.FindType(n)?.Assembly.GetName().Name ?? "NULL")}");
        var cm = CameraManager.Instance;
        var cam = cm?.MainCamera;
        Log($"CameraManager.MainCamera={(cam == null ? "null" : cam.name)} Camera.main={(Camera.main == null ? "null" : Camera.main.name)} " +
            $"UsePostProcess={cm?.UsePostProcess}");
        var gv = cm?.CurVolume;
        if (gv != null)
            Log($"game CurVolume go='{gv.gameObject.name}' layer={gv.gameObject.layer} isGlobal={gv.isGlobal} priority={gv.priority} weight={gv.weight} " +
                $"profile='{(gv.sharedProfile == null ? "null" : gv.sharedProfile.name)}' comps={DescribeProfile(gv.sharedProfile)}");
        foreach (var v in UnityEngine.Object.FindObjectsOfType<Volume>())
            Log($"scene Volume '{v.gameObject.name}' layer={v.gameObject.layer} global={v.isGlobal} prio={v.priority} w={v.weight} enabled={v.enabled}");
        yield break;
    }

    private static string DescribeProfile(VolumeProfile? p)
    {
        if (p == null) return "-";
        var names = new List<string>();
        foreach (var c in p.components) names.Add($"{c.GetIl2CppType().Name}({(c.active ? "on" : "off")})");
        return string.Join(",", names);
    }

    private void EnsureVolume()
    {
        if (_volume != null) return;
        var go = new GameObject("StellarPhotoLook");
        UnityEngine.Object.DontDestroyOnLoad(go);
        var gv = CameraManager.Instance?.CurVolume;
        if (gv != null) go.layer = gv.gameObject.layer;   // match the game's volume layer (camera volume mask)
        var comp = go.AddComponent(Il2CppType.Of<Volume>());
        _volume = comp.Cast<Volume>();
        _volume.isGlobal = true;
        _volume.priority = 10000f;
        _volume.weight = 1f;
        _profile = ScriptableObject.CreateInstance(Il2CppType.Of<VolumeProfile>()).Cast<VolumeProfile>();
        _volume.sharedProfile = _profile;
        Log($"our Volume created layer={go.layer} isGlobal={_volume.isGlobal} priority={_volume.priority} weight={_volume.weight}");
    }

    private T AddComp<T>() where T : VolumeComponent
    {
        EnsureVolume();
        var t = Il2CppType.Of<T>();
        if (_profile!.Has(t)) _profile.Remove(t);
        var ret = _profile.Add(t, false);
        var typed = (T)Activator.CreateInstance(typeof(T), ret.Pointer)!;   // brief's wrap path
        typed.active = true;
        return typed;
    }

    private void RemoveComp<T>() where T : VolumeComponent
    {
        if (_profile == null) return;
        var t = Il2CppType.Of<T>();
        if (_profile.Has(t)) _profile.Remove(t);
    }

    private static void Set<T>(VolumeParameter<T> p, T v) { p.value = v; p.overrideState = true; }

    private IEnumerator StepGrey()
    {
        var ca = AddComp<ZColorAdjustmentVolume>();
        Set(ca.saturation, -100f);
        Log($"ZColorAdjustmentVolume saturation={ca.saturation.value} override={ca.saturation.overrideState}; profile={DescribeProfile(_profile)}");
        yield return Snap("01_grey");
        RemoveComp<ZColorAdjustmentVolume>();
        yield return Snap("01b_grey_removed");
    }

    private IEnumerator StepDofZ()
    {
        var d = AddComp<ZDofVolume>();
        Set(d.Enabled, true);
        Set(d.FocusDistance, 2f);
        Set(d.Aperture, 1.4f);
        Set(d.FocalLength, 85f);
        Log($"ZDofVolume Enabled={d.Enabled.value} FocusDistance={d.FocusDistance.value} Aperture={d.Aperture.value} FocalLength={d.FocalLength.value} " +
            $"blurType={d.blurType.value} blurRadius={d.blurRadius.value}");
        yield return Snap("02a_dof_ZDofVolume");
        RemoveComp<ZDofVolume>();
    }

    private IEnumerator StepDofUnity()
    {
        var d = AddComp<ZUnityDepthOfField>();
        Set(d.mode, DepthOfFieldMode.Bokeh);
        Set(d.focusDistance, 2f);
        Set(d.aperture, 1.4f);
        Set(d.focalLength, 85f);
        Log($"ZUnityDepthOfField mode={d.mode.value} focusDistance={d.focusDistance.value} aperture={d.aperture.value} focalLength={d.focalLength.value}");
        yield return Snap("02b_dof_ZUnityDepthOfField");
        RemoveComp<ZUnityDepthOfField>();
    }

    private IEnumerator StepVignetteUnity()
    {
        DumpProps(typeof(ZUnityVignetteVolume));
        var v = AddComp<ZUnityVignetteVolume>();
        Set(v.intensity, 1f);
        Set(v.smoothness, 1f);
        Set(v.color, Color.black);
        Log($"ZUnityVignetteVolume type={v.vignetteType.value} intensity={v.intensity.value} smoothness={v.smoothness.value} color={v.color.value}");
        yield return Snap("03a_vignette_ZUnityVignetteVolume");
        RemoveComp<ZUnityVignetteVolume>();
    }

    private IEnumerator StepVignetteZ()
    {
        DumpProps(typeof(ZVignetteVolume));
        var v = AddComp<ZVignetteVolume>();
        Set(v.Enabled, true);
        foreach (var c in new[] { v.topFrontColor, v.topBackColor, v.bottomFrontColor, v.bottomBackColor, v.screenRimColor }) Set(c, Color.black);
        foreach (var f in new[] { v.topWrap, v.bottomWrap, v.centerSmooth, v.centerRange, v.leftScreenRimRange, v.rightScreenRimRange })
            Set(f, f.max);
        Log($"ZVignetteVolume Enabled={v.Enabled.value} topWrap={v.topWrap.value} centerRange={v.centerRange.value} rim={v.leftScreenRimRange.value}");
        yield return Snap("03b_vignette_ZVignetteVolume");
        RemoveComp<ZVignetteVolume>();
    }

    private IEnumerator StepBloom(bool pc)
    {
        var b = AddComp<ZBloomVolume>();
        Set(b.Enabled, true);
        if (pc) Set(b.intensity_PC, 5f); else Set(b.intensity, 5f);
        Log($"ZBloomVolume Enabled={b.Enabled.value} intensity={b.intensity.value}(ov={b.intensity.overrideState}) " +
            $"intensity_PC={b.intensity_PC.value}(ov={b.intensity_PC.overrideState}) threshold={b.threshold.value} threshold_PC={b.threshold_PC.value}");
        yield return Snap(pc ? "04b_bloom_intensity_PC" : "04a_bloom_intensity");
        RemoveComp<ZBloomVolume>();
    }

    private IEnumerator StepHoldGrey()
    {
        var ca = AddComp<ZColorAdjustmentVolume>();
        Set(ca.saturation, -100f);
        Log("HOLD: grey look left ON for the runner's window-scoped scrot (external proof)");
        yield return Snap("99_hold_grey");
        yield return new WaitForEndOfFrame();
        var cam = CameraManager.Instance?.MainCamera ?? Camera.main;
        if (cam != null) RenderCameraOnce(cam, Screen.width, Screen.height, 101);   // n=101 -> probe_cam_101x.png = grey cam render
        Log("HOLD: CameraRender with grey look written as probe_cam_101x.png (does our Volume reach Camera.Render?)");
    }

    private void DumpProps(Type t)
    {
        var props = t.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly)
            .Select(p => $"{p.Name}:{p.PropertyType.Name}");
        Log($"props {t.Name}: {string.Join(", ", props)}");
    }
}
