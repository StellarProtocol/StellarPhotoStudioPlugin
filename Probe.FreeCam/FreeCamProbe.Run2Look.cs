using System;
using System.Collections;
using System.Collections.Generic;
using Panda.ZGame;
using UnityEngine;

namespace Stellar.PhotoStudio.FreeCamProbe;

/// <summary>
/// Run 2 / F — look-at snapshot / restore. Snapshot every observable look-at field (enable, head, eye, disabled,
/// main transform, main pos, weight rate, model uuid, cutscene flags), apply the photo-mode camera look-at, release
/// with the game's own recipe (camerasys_vm SetHeadLookAt(false): ResetLookAtIKParam + HeadClose(true) +
/// SetLookAtTransform(null)), compare, then apply only the corrective writes the diff calls for (HeadClose(!preHead),
/// EyeOpen(preEye), SetLookAtTransform(preTrans)) and compare again after 1 s.
/// </summary>
public sealed partial class FreeCamProbe
{
    private sealed record LookSnap(bool? Enable, bool? Head, bool? Eye, bool? Disabled, IntPtr Trans, string TransName, Vector3 Pos, float Rate, long ModelUuid)
    {
        public string Diff(LookSnap o)
        {
            var d = new List<string>();
            if (Enable != o.Enable) d.Add($"enable {Enable}->{o.Enable}");
            if (Head != o.Head) d.Add($"head {Head}->{o.Head}");
            if (Eye != o.Eye) d.Add($"eye {Eye}->{o.Eye}");
            if (Disabled != o.Disabled) d.Add($"disabled {Disabled}->{o.Disabled}");
            if (Trans != o.Trans) d.Add($"trans {TransName}->{o.TransName}");
            if ((Pos - o.Pos).sqrMagnitude > 1e-6f) d.Add($"pos {V(Pos)}->{V(o.Pos)}");
            if (Math.Abs(Rate - o.Rate) > 0.01f) d.Add($"rate {Rate:F2}->{o.Rate:F2}");
            if (ModelUuid != o.ModelUuid) d.Add($"modelUuid {ModelUuid}->{o.ModelUuid}");
            return string.Join(", ", d);
        }
    }

    private static LookSnap? Look(ZModel? m)
    {
        var la = m?.AnimLookAtComp;
        if (la == null) return null;
        var t = la.lookAtTransMain_;
        return new LookSnap(la.lookEnable(), la.headLookEnable(), la.eyeLookEnable(), la.lookAtDisabled_, t == null ? IntPtr.Zero : t.Pointer,
            t == null ? "null" : t.name, la.lookAtPosMain_, la.lookAtWeightRate_, la.lookAtModelUuid_);
    }

    private IEnumerator StepLookRestore2()
    {
        var epoch = _sceneEpoch;
        var front = FrontCamera();
        try
        {
            if (front != null) yield return Wait(1.5f);
            var model = LiveModel(SelfEntity());
            var cam = MainCam();
            if (model == null || cam == null || Look(model) is not { } pre) { Log("F prerequisites missing"); yield break; }
            var region = RegionAround(cam, model.GetHeadPosition(), 0.06f, 0.08f);
            var shotPre = Capture("F_pre", region);
            Log($"F pre {pre}");
            Arm("look2", () =>
            {
                var m = LiveModel(SelfEntity());
                if (m == null) return;
                ZModelHelper.ResetLookAtIKParam(m);
                m.SetLuaAttrLookAtHeadClose(pre.Head != true);
                ZModelHelper.SetLookAtTransform(m, null);
            });
            ZModelHelper.SetLookAtIKParam(model, 1);
            model.SetLuaAttrLookAtHeadClose(false);
            ZModelHelper.SetLookAtTransform(model, cam.transform);
            yield return Wait(1.5f);
            if (Aborted(epoch)) yield break;   // outer finally releases look2
            Log($"F applied (camera) {Look(model)}");
            _releases.Remove("look2");          // the measured release below replaces the safety release

            // Game recipe release.
            var m2 = LiveModel(SelfEntity());
            if (m2 == null) yield break;
            ZModelHelper.ResetLookAtIKParam(m2);
            m2.SetLuaAttrLookAtHeadClose(true);
            ZModelHelper.SetLookAtTransform(m2, null);
            yield return Wait(1f);
            var afterGame = Look(m2);
            Log($"F after game-recipe release: equalsPre={afterGame != null && pre.Diff(afterGame).Length == 0} diff[{(afterGame == null ? "gone" : pre.Diff(afterGame))}]");

            // Corrective writes from the snapshot.
            var fixes = new List<string>();
            if (afterGame != null && afterGame.Head != pre.Head && pre.Head != null) { m2.SetLuaAttrLookAtHeadClose(!pre.Head.Value); fixes.Add($"HeadClose({!pre.Head.Value})"); }
            if (afterGame != null && afterGame.Eye != pre.Eye && pre.Eye != null) { m2.SetLuaAttrLookAtEyeOpen(pre.Eye.Value); fixes.Add($"EyeOpen({pre.Eye.Value})"); }
            if (afterGame != null && afterGame.Enable != pre.Enable && pre.Enable != null) { m2.SetLuaAttrLookAtEnable(pre.Enable.Value); fixes.Add($"LookAtEnable({pre.Enable.Value})"); }
            yield return Wait(1f);
            var fixedSnap = Look(m2);
            var shotPost = Capture("F_restored", region);
            Log($"F after snapshot restore [{string.Join(",", fixes)}]: equalsPre={fixedSnap != null && pre.Diff(fixedSnap).Length == 0} diff[{(fixedSnap == null ? "gone" : pre.Diff(fixedSnap))}] " +
                $"head-region {DiffText(shotPre, shotPost)}");
        }
        finally { Release("look2"); Release("cam.vcam"); }
    }
}
