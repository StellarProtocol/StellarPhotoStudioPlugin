using System;
using System.Collections;
using Bokura.Rendering;
using Panda.ZGame;
using UnityEngine;
using UnityEngine.Rendering;

namespace Stellar.PhotoStudioProbe;

/// <summary>Iteration-2 look variants: run 2 showed no DoF/bloom change with the brief's literal values.</summary>
public sealed partial class RenderProbe
{
    private void LogStack<T>(string tag, Func<T, string> describe) where T : VolumeComponent
    {
        try
        {
            var stack = VolumeManager.instance.stack;
            var c = stack.GetComponent<T>();
            Log($"STACK[{tag}] {typeof(T).Name}: {(c == null ? "null" : describe(c))}");
        }
        catch (Exception ex) { Log($"STACK[{tag}] {typeof(T).Name} FAILED {ex.GetType().Name}: {ex.Message}"); }
    }

    private static string DofDesc(ZDofVolume d) =>
        $"IsActive={d.IsActive()} Enabled={d.Enabled.value} blurType={d.blurType.value} focus={d.focus.value} aperture={d.aperture.value} " +
        $"blurRadius={d.blurRadius.value} blurIntensity={d.blurIntensity.value} FarStart={d.FarStart.value} FarEnd={d.FarEnd.value} " +
        $"FocusDistance={d.FocusDistance.value} Aperture={d.Aperture.value} FocalLength={d.FocalLength.value}";

    private IEnumerator StepDofVariants()
    {
        foreach (var bt in new[] { BlurType.Bokeh, BlurType.DepthBlur, BlurType.Gaussian })
        {
            var d = AddComp<ZDofVolume>();
            Set(d.Enabled, true);
            Set(d.blurType, bt);
            Set(d.FocusDistance, 2f); Set(d.Aperture, 1.4f); Set(d.FocalLength, 85f);
            Set(d.focus, 2f); Set(d.aperture, d.aperture.max); Set(d.blurRadius, d.blurRadius.max); Set(d.blurIntensity, d.blurIntensity.max);
            Set(d.FarStart, 3f); Set(d.FarEnd, 10f);
            Log($"ZDofVolume variant blurType={bt}: ours {DofDesc(d)}");
            yield return Wait(0.3f);
            LogStack<ZDofVolume>($"dof {bt}", DofDesc);
            yield return Snap($"02c_dof_ZDofVolume_{bt}");
            RemoveComp<ZDofVolume>();
        }
        var u = AddComp<ZUnityDepthOfField>();
        Set(u.mode, DepthOfFieldMode.Gaussian);
        Set(u.gaussianStart, 1f); Set(u.gaussianEnd, 4f); Set(u.gaussianMaxRadius, u.gaussianMaxRadius.max);
        Log($"ZUnityDepthOfField Gaussian IsActive={u.IsActive()} start={u.gaussianStart.value} end={u.gaussianEnd.value} maxR={u.gaussianMaxRadius.value}");
        yield return Wait(0.3f);
        LogStack<ZUnityDepthOfField>("unity dof gaussian", x => $"IsActive={x.IsActive()} mode={x.mode.value} maxR={x.gaussianMaxRadius.value}");
        yield return Snap("02d_dof_ZUnityDepthOfField_Gaussian");
        RemoveComp<ZUnityDepthOfField>();
    }

    /// <summary>The game's own photo-mode DoF path (CameraManager.SetDofValue drives its dof_ ZDofVolume).</summary>
    private IEnumerator StepDofGameApi()
    {
        var cm = CameraManager.Instance;
        cm.SetDofValue(true, EFrameCtrType.Focus, 2f);
        cm.SetDofValue(true, EFrameCtrType.Aperture, 1.4f);
        cm.SetDofValue(true, EFrameCtrType.BlurRadius, 10f);
        var d = cm.dof_;
        Log($"CameraManager.SetDofValue(true, Focus=2/Aperture=1.4/BlurRadius=10): game dof_ {(d == null ? "null" : "active=" + d.active + " " + DofDesc(d))}");
        yield return Wait(0.3f);
        LogStack<ZDofVolume>("game api", DofDesc);
        yield return Snap("02e_dof_game_SetDofValue");
        cm.SetDofValue(false, EFrameCtrType.Focus, 2f);
        cm.SetDofValue(false, EFrameCtrType.Aperture, 1.4f);
        cm.SetDofValue(false, EFrameCtrType.BlurRadius, 10f);
        Log($"CameraManager.SetDofValue(false, …) restored: game dof_ active={cm.dof_?.active}");
    }

    private static string BloomDesc(ZBloomVolume b) =>
        $"IsActive={b.IsActive()} Enabled={b.Enabled.value} i={b.intensity.value}/t={b.threshold.value} " +
        $"iPC={b.intensity_PC.value}/tPC={b.threshold_PC.value} iUE={b.intensity_UE.value}/tUE={b.threshold_UE.value}";

    private IEnumerator StepBloomVariants()
    {
        yield return Snap("04c_bloom_ref_none");
        foreach (var set in new[] { "plain", "PC", "UE", "PCgate", "UEgate" })
        {
            var b = AddComp<ZBloomVolume>();
            Set(b.Enabled, true);
            // *gate variants: keep plain intensity barely non-zero (IsActive keys on it) with an unreachable
            // threshold so only the named set can contribute.
            if (set.EndsWith("gate")) { Set(b.intensity, 0.01f); Set(b.threshold, 50f); }
            if (set == "plain") { Set(b.intensity, 5f); Set(b.threshold, 0f); }
            else if (set.StartsWith("PC")) { Set(b.intensity_PC, 5f); Set(b.threshold_PC, 0f); }
            else if (set.StartsWith("UE")) { Set(b.intensity_UE, b.intensity_UE.max); Set(b.threshold_UE, 0f); }
            Log($"ZBloomVolume variant {set}: ours {BloomDesc(b)}");
            yield return Wait(0.3f);
            LogStack<ZBloomVolume>($"bloom {set}", BloomDesc);
            yield return Snap($"04c_bloom_threshold0_{set}");
            RemoveComp<ZBloomVolume>();
        }
    }
}
