using System;
using System.Collections;
using Cinemachine;
using Il2CppInterop.Runtime;
using Panda.ZGame;
using UnityEngine;

namespace Stellar.PhotoStudio.FreeCamProbe;

/// <summary>
/// § 9 item 6 — (a) one extra URP point light 1.5 m in front of the local player's chest (towards the camera): capture
/// before / with / after and log the character-region luminance delta; (b) head look-at via the game's own photo-mode
/// recipe (<c>camerasys_vm.lua SetHeadLookAt</c>): <c>ZModelHelper.SetLookAtIKParam(model, 1)</c> +
/// <c>model.SetLuaAttrLookAtHeadClose(false)</c> + <c>ZModelHelper.SetLookAtTransform(model, target)</c>, released by
/// <c>ResetLookAtIKParam</c> + <c>SetLuaAttrLookAtHeadClose(true)</c> + <c>SetLookAtTransform(model, null)</c>; then an
/// arbitrary point via <c>ZModelHelper.SetLookAtPos(model, LuaWorldPosToLocal(point), true)</c>. When a vcam can be
/// created, the camera is first placed 2.5 m in front of the player so the face is visible.
/// </summary>
public sealed partial class FreeCamProbe
{
    private IEnumerator StepPointLight()
    {
        var epoch = _sceneEpoch;
        var front = FrontCamera();
        try
        {
            if (front != null) yield return Wait(1.2f);   // blend to the front view
            var model = LiveModel(SelfEntity());
            var cam = MainCam();
            if (model == null || cam == null || SelfRegion() is not { } region) { Log("LIGHT prerequisites missing"); yield break; }
            var chest = model.GetChestPosition();
            var toCam = (cam.transform.position - chest).normalized;
            var before = Capture("6a_light_before", region);
            foreach (var (intensity, range) in new[] { (6f, 5f), (30f, 6f) })
            {
                if (Aborted(epoch)) yield break;
                var go = new GameObject("StellarFreeCamProbeLight");
                Arm("light.go", () => UnityEngine.Object.Destroy(go));
                try
                {
                    go.transform.position = chest + toCam * 1.5f + Vector3.up * 0.2f;
                    var light = go.AddComponent(Il2CppType.Of<Light>()).Cast<Light>();
                    light.type = LightType.Point;
                    light.color = new Color(1f, 0.55f, 0.25f);   // warm orange: easy to see in the PNG
                    light.intensity = intensity;
                    light.range = range;
                    light.shadows = LightShadows.None;
                    Log($"LIGHT added Point at {V(go.transform.position)} intensity={light.intensity} range={light.range} renderMode={light.renderMode} cullingMask=0x{light.cullingMask:X} layer={go.layer} enabled={light.isActiveAndEnabled}");
                    yield return Frames(3);
                    var with = Capture($"6a_light_on_i{intensity:F0}", region);
                    Log($"LIGHT i={intensity} lum before={(before == null ? -1 : MeanLum(before)):F2} with={(with == null ? -1 : MeanLum(with)):F2} " +
                        $"delta={(before == null || with == null ? 0 : MeanLum(with) - MeanLum(before)):F2} {DiffText(before, with)}");
                }
                finally { Release("light.go"); }
                yield return Frames(3);
            }
            var after = Capture("6a_light_after", region);
            Log($"LIGHT destroyed: lum after={(after == null ? -1 : MeanLum(after)):F2} {DiffText(before, after)}");
        }
        finally { Release("cam.vcam"); }
    }

    private IEnumerator StepLookAt()
    {
        var epoch = _sceneEpoch;
        var front = FrontCamera();
        try
        {
            if (front != null) yield return Wait(1.2f);
            var model = LiveModel(SelfEntity());
            var cam = MainCam();
            if (model == null || cam == null) { Log("LOOKAT prerequisites missing"); yield break; }
            var region = RegionAround(cam, model.GetHeadPosition(), 0.06f, 0.08f);
            var before = Capture("6b_lookat_before", region);
            Log("LOOKAT state before: " + LookState(model));
            Arm("lookat", () =>
            {
                var m = LiveModel(SelfEntity());
                if (m == null) return;
                ZModelHelper.ResetLookAtIKParam(m);
                m.SetLuaAttrLookAtHeadClose(true);
                ZModelHelper.SetLookAtTransform(m, null);
            });
            try
            {
                // (i) look at the camera — the game's own recipe with the Main Camera transform.
                ZModelHelper.SetLookAtIKParam(model, 1);
                model.SetLuaAttrLookAtHeadClose(false);
                ZModelHelper.SetLookAtTransform(model, cam.transform);
                yield return Wait(1.5f);
                if (Aborted(epoch)) yield break;
                var atCam = Capture("6b_lookat_camera", region);
                Log($"LOOKAT camera: {LookState(model)} {DiffText(before, atCam)}");

                // (ii) an arbitrary world point: 2 m to the player's right at head height.
                var head = model.GetHeadPosition();
                var right = Vector3.Cross(Vector3.up, (cam.transform.position - head).normalized).normalized;
                var point = head + right * 2f;
                ZModelHelper.SetLookAtTransform(model, null);
                var local = ZModelHelper.LuaWorldPosToLocal(model, point);
                ZModelHelper.SetLookAtPos(model, local, true);
                yield return Wait(1.5f);
                var atPoint = Capture("6b_lookat_point", region);
                Log($"LOOKAT point world={V(point)} local={V(local)}: {LookState(model)} {DiffText(before, atPoint)} {DiffText(atCam, atPoint)}");
            }
            finally { Release("lookat"); }
            yield return Wait(1.5f);
            var released = Capture("6b_lookat_released", region);
            Log($"LOOKAT released: {LookState(model)} {DiffText(before, released)}");
        }
        finally { Release("cam.vcam"); }
    }

    /// <summary>Places our vcam 2.5 m in front of the player looking at the chest (null when no vcam can be made).</summary>
    private CinemachineVirtualCamera? FrontCamera()
    {
        var model = LiveModel(SelfEntity());
        var cam = MainCam();
        if (model == null || cam == null || _vcamFailed) return null;
        var chest = model.GetChestPosition();
        var fwd = EntityAttrExtensions.GetAttrGoRotation(model) * Vector3.forward;
        var pos = chest + fwd * 2.5f + Vector3.up * 0.15f;
        var v = TryCreateVcam(pos, Quaternion.LookRotation(chest - pos), 40f, 100000);
        Log($"FRONTCAM {(v == null ? "unavailable — using the game camera" : $"vcam at {V(pos)} looking at chest {V(chest)}")}");
        return v;
    }

    private static string LookState(ZModel m)
    {
        try
        {
            var la = m.AnimLookAtComp;
            var ik = m.AnimIKComp?.TryCast<AnimGenericIKComp>();
            return $"lookAt[enable={la?.lookEnable()} head={la?.headLookEnable()} eye={la?.eyeLookEnable()} disabled={la?.lookAtDisabled_} " +
                   $"trans={(la?.lookAtTransMain_ == null ? "null" : la.lookAtTransMain_.name)} pos={(la == null ? "-" : V(la.lookAtPosMain_))} rate={la?.lookAtWeightRate_:F2}] " +
                   $"headIK[{(ik == null ? "n/a" : $"active={ik.HeadIKActive} weight={ik.HeadIKWeight:F2}")}]";
        }
        catch (Exception ex) { return $"lookState err {ex.GetType().Name}: {ex.Message}"; }
    }
}
