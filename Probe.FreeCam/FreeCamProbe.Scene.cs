using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Cinemachine;
using Panda.ZGame;
using UnityEngine;

namespace Stellar.PhotoStudio.FreeCamProbe;

/// <summary>
/// Scene access helpers, gated per docs/il2cpp-probing-safety.md: entities are re-fetched by id through the game's
/// null-returning lookups each time (never cached across frames), models are skipped when <c>IsDestroying</c>, and
/// every step aborts on a scene change (<see cref="Aborted"/>).
/// </summary>
public sealed partial class FreeCamProbe
{
    internal sealed record Char(long CharId, bool IsSelf, float Dist);

    private static Camera? MainCam() => CameraManager.Instance?.MainCamera ?? Camera.main;

    private static ZEntity? SelfEntity()
    {
        var em = ZEntityMgr.Instance;
        if (em == null) return null;
        var e = em.GetEntity(em.PlayerUuid);
        return e == null || e.IsDestroying ? null : e;
    }

    private static ZModel? LiveModel(ZEntity? e)
    {
        if (e == null || e.IsDestroying) return null;
        var m = e.Model;
        return m == null || m.IsDestroying ? null : m;
    }

    /// <summary>Re-fetches a character entity by char id (null when culled / gone — the game's own null return).</summary>
    private static ZEntity? CharEntity(long charId, bool isSelf)
    {
        if (isSelf) return SelfEntity();
        var e = ZEntityMgr.Instance?.GetCharEntity(charId);
        return e == null || e.IsDestroying ? null : e;
    }

    /// <summary>Characters (players) within <paramref name="maxDist"/> m of the local player, nearest first.</summary>
    private List<Char> NearbyChars(float maxDist, int cap, bool includeSelf)
    {
        var list = new List<Char>();
        var em = ZEntityMgr.Instance;
        var self = SelfEntity();
        var selfModel = LiveModel(self);
        if (em == null || selfModel == null) return list;
        var origin = selfModel.GetAttrGoPosition();
        if (includeSelf) list.Add(new Char(0, true, 0f));
        var ids = em.CharIdList;
        if (ids == null) return list;
        for (var i = 0; i < ids.Count; i++)
        {
            var cid = ids[i];
            var e = CharEntity(cid, false);
            if (e == null || e.Uuid == self!.Uuid) continue;
            var m = LiveModel(e);
            if (m == null) continue;
            var d = Vector3.Distance(origin, m.GetAttrGoPosition());
            if (d <= maxDist) list.Add(new Char(cid, false, d));
        }
        return list.OrderBy(c => c.Dist).Take(cap).ToList();
    }

    private static string CamPose(Camera? cam) =>
        cam == null ? "cam=null" : $"camPos={V(cam.transform.position)} camRot={V(cam.transform.rotation.eulerAngles)} fov={cam.fieldOfView:F2}";

    private static string VcamName(ICinemachineCamera? c)
    {
        if (c == null) return "null";
        try
        {
            var b = c.TryCast<CinemachineVirtualCameraBase>();
            return b != null ? $"{b.Name}(prio={b.Priority},{b.GetIl2CppType().Name})" : "non-vcam-base";
        }
        catch (Exception ex) { return $"name-err:{ex.GetType().Name}"; }
    }

    private IEnumerator StepEnv()
    {
        Log($"screen={Screen.width}x{Screen.height} gfx={SystemInfo.graphicsDeviceName} api={SystemInfo.graphicsDeviceVersion} quality={QualitySettings.GetQualityLevel()}");
        var cam = MainCam();
        Log($"MainCamera={(cam == null ? "null" : cam.name)} {CamPose(cam)} near={cam?.nearClipPlane:F2} far={cam?.farClipPlane:F0}");
        Try("camera manager", () =>
        {
            var cm = CameraManager.Instance;
            Log($"CameraManager CurrentState={cm.CurrentState} resetFov_={cm.resetFov_} resetFovSpeed_={cm.resetFovSpeed_:F2} defaultFov_={cm.defaultFov_:F2} GetDefaultFov()={cm.GetDefaultFov():F2} IsCameraState={CameraFrameCtrl.IsCameraState}");
        });
        var self = SelfEntity();
        var m = LiveModel(self);
        Log($"self uuid={self?.Uuid} model={(m == null ? "null" : "ok")} pos={(m == null ? "-" : V(m.GetAttrGoPosition()))} chest={(m == null ? "-" : V(m.GetChestPosition()))} head={(m == null ? "-" : V(m.GetHeadPosition()))}");
        var near = NearbyChars(30f, 50, includeSelf: false);
        Log($"nearby players within 30 m: {near.Count} [{string.Join(",", near.Take(12).Select(c => $"{c.CharId}@{c.Dist:F1}m"))}]");
        yield break;
    }
}
