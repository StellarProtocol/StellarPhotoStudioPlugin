using System;
using System.Collections;
using System.Linq;
using Panda.ZAnim;
using Panda.ZGame;
using UnityEngine;

namespace Stellar.PhotoStudio.FreeCamProbe;

/// <summary>Run 12 (2026-10-02): the controlled experiment behind the animation-request gate. While the framework pause holds,
/// the probe itself issues the request the game's movement logic issues — <c>ECSAnimController.PlayBaseState(ERun, fade 0)</c>
/// — on (a) the local player (never gated) and (b) the nearest remote character / NPC on screen (tracked by the freeze), and
/// diffs a full-resolution region capture before and 5 frames after. (a) shows a request alone re-poses a model with the
/// clock stopped (the driver); (b) shows the gate holds it (STELLAR_FREEZE_ANIM_GATE=0 for the A/B). Each model is put back
/// with an EIdle request. Probe-only.</summary>
public sealed partial class FreeCamProbe
{
    private IEnumerator StepRequest12()
    {
        var token = _services.SceneFreeze.Freeze();
        Arm("r12.freeze", () => token.Dispose());
        yield return Wait(1f);
        Log($"R12 FREEZE on timeScale={Time.timeScale:F3} input[{R11Input()}]");
        if (SelfEntity() is { } self) yield return R12Request("self", self.Uuid);
        var cam = MainCam();
        var target = AllEntities(18f).Where(x => x.Kind is "CharEnt" or "NpcEnt" && cam != null && LiveModel(EntByUuid(x.Uuid)) is { } m &&
                                                  cam.WorldToViewportPoint(m.GetChestPosition()) is var v && v.z > 0 && v.x is > 0.1f and < 0.9f && v.y is > 0.1f and < 0.9f)
                                     .OrderBy(x => x.Dist).FirstOrDefault();
        if (target != null) yield return R12Request($"remote-{target.Kind}", target.Uuid);
        else Log("R12 no remote character / NPC on screen within 18 m");
        Release("r12.freeze");
        Log($"R12 FREEZE off timeScale={Time.timeScale:F3}");
    }

    private IEnumerator R12Request(string label, long uuid)
    {
        var m = LiveModel(EntByUuid(uuid));
        var cam = MainCam();
        var ctl = m?.AnimComp?.TryCast<ECSAnimComp>()?.controller_;
        if (m == null || cam == null || ctl == null) { Log($"R12 {label} no model/camera/controller"); yield break; }
        var region = RegionAround(cam, m.GetChestPosition(), 0.06f, 0.14f);
        var a = Capture($"R12_{label}_before", region);
        yield return Frames(3);
        var a2 = Capture($"R12_{label}_before2", region);   // the noise floor: two shots with nothing requested
        var before = R11State(m).State;
        ctl.PlayBaseState(EAnimBase.ERun, -1f, new Vector2(0f, 1f), -1f, 0f, false, false);
        yield return Frames(5);
        var b = Capture($"R12_{label}_after_run", region);
        var after = R11State(m).State;
        Log($"R12 {label} {uuid} timeScale={Time.timeScale:F3} noise={Diff(a, a2).ChangedPct:F1}% request PlayBaseState(ERun, fade 0): {DiffText(a2, b)}");
        Log($"R12 {label} state before [{before}] after [{after}]");
        ctl.PlayBaseState(EAnimBase.EIdle, -1f, Vector2.zero, -1f, 0f, false, false);
        yield return Frames(5);
        var c = Capture($"R12_{label}_after_idle", region);
        Log($"R12 {label} back to EIdle: {DiffText(a2, c)}");
    }
}
