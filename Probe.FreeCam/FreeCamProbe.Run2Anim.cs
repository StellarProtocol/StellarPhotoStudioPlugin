using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Panda.ZGame;
using UnityEngine;

namespace Stellar.PhotoStudio.FreeCamProbe;

/// <summary>
/// Run 2 / A — animation freeze, each path ISOLATED (own try/catch, own release, own capture verdict). Motion source =
/// the Wave emote (9011): every trial plays it, applies its path 0.5 s in, then captures t1 / t2 (0.6 s apart) of the
/// player region and one nearby character's region in ONE render each. Baselines: a no-emote pair (render noise) and
/// an unfrozen emote pair (what "moving" looks like). No BattleFrameSpeed accessor anywhere (run 1:
/// MethodAccessException on <c>IPureComponentAccessor&lt;LocalAttrBattleFrameSpeedComponent&gt;</c>).
/// </summary>
public sealed partial class FreeCamProbe
{
    private float _animNoisePct = -1f;
    internal bool AnimP1Froze;

    private IEnumerator StepAnimFreeze2()
    {
        var epoch = _sceneEpoch;
        var chars = NearbyChars(30f, 10, includeSelf: true);
        LogModelKinds("A", chars);
        var self = chars.FirstOrDefault(c => c.IsSelf);
        if (self == null || CharRegion(self, 0.08f, 0.16f) is not { } selfR) { Log("A no self region"); yield break; }
        RectInt? otherR = null;
        foreach (var c in chars.Where(c => !c.IsSelf)) { otherR = CharRegion(c); if (otherR != null) { Log($"A other region char={c.CharId}@{c.Dist:F1}m"); break; } }
        if (otherR == null) Log("A no on-screen nearby character — other-region diff n/a");

        yield return AnimTrial("A0_static", selfR, otherR, epoch, false, null);
        yield return AnimTrial("A0_emote_unfrozen", selfR, otherR, epoch, true, null);
        yield return AnimTrial("A1_P1_AnimComp.Speed", selfR, otherR, epoch, true, () => ApplyAnimCompSpeed(chars));
        yield return AnimTrial("A3_P3_SetEModelAnimTimeSwitch", selfR, otherR, epoch, true, ApplyP3);
        yield return AnimTrial("A4_P4_Animator.speed", selfR, otherR, epoch, true, () => ApplyAnimatorSpeed(chars));
        yield return AnimTrial("A5a_SkillStageTimeFactor", selfR, otherR, epoch, true, () => ApplyEntityFactor(chars, skillStage: true));
        yield return AnimTrial("A5b_AnimResFactor", selfR, otherR, epoch, true, () => ApplyEntityFactor(chars, skillStage: false));
        Log($"A done; final self read [{SelfAnimRead2()}]");
    }

    private IEnumerator AnimTrial(string name, RectInt selfR, RectInt? otherR, int epoch, bool emote, Func<Action?>? apply)
    {
        if (Aborted(epoch)) yield break;
        if (emote) Log($"A {name} emote: {PlayEmote(EmoteId)}");
        yield return Wait(0.5f);
        var key = "anim2." + name;
        if (apply != null)
        {
            Action? restore = null;
            try { restore = apply(); }
            catch (Exception ex) { Log($"A {name} apply FAILED {ex.GetType().Name}: {ex.Message}"); }
            if (restore == null) { Log($"A {name} VERDICT n/a (nothing applied)"); yield return WaitEmoteEnd(epoch); yield break; }
            Arm(key, restore);
        }
        try
        {
            yield return Frames(2);
            var readAtApply = SelfAnimRead2();
            var regions = otherR is { } o ? new[] { selfR, o } : new[] { selfR };
            var t1 = CaptureMany($"A_{name}_t1", regions);
            yield return Wait(0.6f);
            var t2 = CaptureMany($"A_{name}_t2", regions);
            var (sm, sp) = Diff(t1?[0], t2?[0]);
            var (om, op) = otherR != null ? Diff(t1?[1], t2?[1]) : (-1f, -1f);
            if (name == "A0_static") _animNoisePct = sp;
            var v = apply == null ? (emote ? "baseline-moving" : "baseline-noise") : Verdict(sp, _animNoisePct);
            if (name.StartsWith("A1_")) AnimP1Froze = v == "FROZEN";
            Log($"A {name} VERDICT {v} self meanAbs={sm:F2} changed={sp:F1}% | other meanAbs={om:F2} changed={op:F1}% | noise={_animNoisePct:F1}% " +
                $"read@apply[{readAtApply}] read@t2[{SelfAnimRead2()}] actorState={ActorState()}");
        }
        finally { if (apply != null) Release(key); }
        if (apply != null)
        {
            yield return Frames(3);
            Log($"A {name} after release read[{SelfAnimRead2()}]");
        }
        yield return WaitEmoteEnd(epoch);
    }

    private Action? ApplyAnimCompSpeed(List<Char> chars)
    {
        var saved = new List<(Char C, float Speed)>();
        foreach (var c in chars)
        {
            var ac = LiveModel(CharEntity(c.CharId, c.IsSelf))?.AnimComp;
            if (ac == null) continue;
            saved.Add((c, ac.Speed));
            ac.Speed = 0f;
        }
        Log($"A P1 AnimComp.Speed=0 on {saved.Count} (saved [{string.Join(",", saved.Take(6).Select(s => s.Speed.ToString("F2")))}])");
        return saved.Count == 0 ? null : () =>
        {
            foreach (var (c, sp) in saved) { var ac = LiveModel(CharEntity(c.CharId, c.IsSelf))?.AnimComp; if (ac != null) ac.Speed = sp; }
        };
    }

    private Action? ApplyP3()
    {
        var cf = CameraFrameCtrl.Instance;
        if (cf == null) { Log("A P3 CameraFrameCtrl.Instance null"); return null; }
        var before = P3State();
        cf.SetEModelAnimTimeSwitch();
        Log($"A P3 SetEModelAnimTimeSwitch() #1: before[{before}] after[{P3State()}]");
        return () =>
        {
            CameraFrameCtrl.Instance?.SetEModelAnimTimeSwitch();
            var now = P3State();
            Log($"A P3 undo = second call: state[{now}] equalsBefore={now == before}");
        };
    }

    private string P3State()
    {
        try
        {
            var ac = LiveModel(SelfEntity())?.AnimComp;
            return $"timeScale={Time.timeScale:F2} IsCameraState={CameraFrameCtrl.IsCameraState} CameraState={CameraFrameCtrl.CameraState} " +
                   $"self.Speed={ac?.Speed:F2} self.speed_={ac?.speed_:F2} persist={ac?.persistTime_:F2}";
        }
        catch (Exception ex) { return $"p3 state err {ex.GetType().Name}: {ex.Message}"; }
    }

    private Action? ApplyAnimatorSpeed(List<Char> chars)
    {
        var saved = new List<(Animator A, float Speed)>();
        int ecs = 0, models = 0;
        foreach (var c in chars)
        {
            var m = LiveModel(CharEntity(c.CharId, c.IsSelf));
            if (m == null) continue;
            models++;
            var set = new HashSet<IntPtr>();
            var go = ModelGo(m);
            if (go == null) ecs++;
            else foreach (var a in go.GetComponentsInChildren<Animator>(true)) if (a != null && set.Add(a.Pointer)) { saved.Add((a, a.speed)); a.speed = 0f; }
            var direct = m.AnimComp?.TryCast<AnimComp>()?.Animator;
            if (direct != null && set.Add(direct.Pointer)) { saved.Add((direct, direct.speed)); direct.speed = 0f; }
        }
        Log($"A P4 Animator.speed=0 on {saved.Count} animators across {models} models (no GameObject/ECS models={ecs})");
        return saved.Count == 0 ? null : () => { foreach (var (a, sp) in saved) if (a != null) a.speed = sp; };
    }

    /// <summary>Per-entity anim-speed attrs that bypass the BattleFrameSpeed accessor: SkillStageTimeFactor (has a getter)
    /// or AnimResFactor (no getter — restored to 1.0, logged), each followed by AnimSpeedDirty + tryCalculateAnimSpeed.</summary>
    private Action? ApplyEntityFactor(List<Char> chars, bool skillStage)
    {
        var tag = skillStage ? "P5a SkillStageTimeFactor" : "P5b AnimResFactor";
        var saved = new List<(Char C, float V)>();
        foreach (var c in chars)
        {
            var e = CharEntity(c.CharId, c.IsSelf);
            if (e == null) continue;
            var prior = 1f;
            if (skillStage) prior = EntityAttrExtensions.GetAttrSkillStageTimeFactor(e);
            if (skillStage) EntityAttrExtensions.SetAttrSkillStageTimeFactor(e, 0f); else EntityAttrExtensions.SetAttrAnimResFactor(e, 0f);
            saved.Add((c, prior));
            Recalc(e, tag);
        }
        Log($"A {tag}=0 on {saved.Count} (saved [{string.Join(",", saved.Take(6).Select(s => s.V.ToString("F2")))}]{(skillStage ? "" : " — no getter, restore writes 1.00")})");
        return saved.Count == 0 ? null : () =>
        {
            foreach (var (c, v) in saved)
            {
                var e = CharEntity(c.CharId, c.IsSelf);
                if (e == null) continue;
                if (skillStage) EntityAttrExtensions.SetAttrSkillStageTimeFactor(e, v); else EntityAttrExtensions.SetAttrAnimResFactor(e, v);
                Recalc(e, tag);
            }
        };
    }

    private void Recalc(ZEntity e, string tag)
    {
        try { EntityAttrExtensions.SetAttrAnimSpeedDirty(e, true); }
        catch (Exception ex) { Log($"A {tag} SetAttrAnimSpeedDirty FAILED {ex.GetType().Name}: {Short(ex.Message)}"); }
        try { EntityAttrExtensions.tryCalculateAnimSpeed(e); }
        catch (Exception ex) { Log($"A {tag} tryCalculateAnimSpeed FAILED {ex.GetType().Name}: {Short(ex.Message)}"); }
    }

    private static string Short(string s) => s.Length > 160 ? s.Substring(0, 160) + "…" : s;

    /// <summary>Self anim-speed readout; every term isolated (the attr getters may hit the throwing accessor).</summary>
    private string SelfAnimRead2()
    {
        var e = SelfEntity();
        var m = LiveModel(e);
        if (e == null || m == null) return "self gone";
        string R(Func<string> f) { try { return f(); } catch (Exception ex) { return "ERR:" + ex.GetType().Name; } }
        return $"comp.Speed={R(() => m.AnimComp?.Speed.ToString("F2") ?? "null")} speed_={R(() => m.AnimComp?.speed_.ToString("F2") ?? "null")} " +
               $"attrAnimSpeed(ent)={R(() => EntityAttrExtensions.GetAttrAnimSpeed(e).ToString("F2"))} attrAnimSpeed(model)={R(() => EntityAttrExtensions.GetAttrAnimSpeed(m).ToString("F2"))} " +
               $"skillStage={R(() => EntityAttrExtensions.GetAttrSkillStageTimeFactor(e).ToString("F2"))}";
    }
}
