using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Panda.ZGame;
using UnityEngine;

namespace Stellar.PhotoStudio.FreeCamProbe;

/// <summary>
/// § 9 item 3b — animation freeze, four candidate paths, each judged by the pixel diff of the local player's region
/// between two captures 1 s apart (frozen ⇒ diff near 0; the idle baseline pair says what "not frozen" looks like):
/// P1 <c>model.AnimComp.Speed = 0</c>; P2 <c>SetAttrBattleFrameSpeed(entity, 0)</c> + <c>SetAttrAnimSpeedDirty</c>
/// (the attr <c>GetAttrAnimSpeed</c> derives from); P3 <c>CameraFrameCtrl.SetEModelAnimTimeSwitch()</c> (toggle,
/// CallerCount 0); P4 Unity <c>Animator.speed = 0</c> on the animators nearest each character. Each path is released
/// in its finally and the snap-back readback is logged.
/// </summary>
public sealed partial class FreeCamProbe
{
    private IEnumerator StepFreezeAnimation()
    {
        var epoch = _sceneEpoch;
        var chars = NearbyChars(30f, 20, includeSelf: true);
        LogAnimSpeeds("initial", chars);
        if (SelfRegion() is not { } region) { Log("ANIM no self region"); yield break; }
        var b0 = Capture("3b_base_t0", region);
        yield return Wait(1f);
        var b1 = Capture("3b_base_t1", region);
        Log($"ANIM baseline (not frozen) {DiffText(b0, b1)}");

        yield return AnimPath("P1_AnimComp.Speed", chars, region, epoch, SetAnimCompSpeed, "anim.p1");
        yield return AnimPath("P2_BattleFrameSpeed", chars, region, epoch, SetBattleFrameSpeed, "anim.p2");
        yield return AnimPathToggle(region, epoch);
        yield return AnimPath("P4_Animator.speed", chars, region, epoch, SetAnimatorSpeed, "anim.p4");
        LogAnimSpeeds("final", chars);
    }

    /// <summary>Applies <paramref name="apply"/> (returns a restore action), checks for per-frame overwrites, captures a pair.</summary>
    private IEnumerator AnimPath(string name, List<Char> chars, RectInt region, int epoch, Func<List<Char>, Action?> apply, string key)
    {
        if (Aborted(epoch)) yield break;
        Action? restore = null;
        try { restore = apply(chars); }
        catch (Exception ex) { Log($"ANIM {name} apply FAILED {ex.GetType().Name}: {ex.Message}"); }
        if (restore == null) { Log($"ANIM {name}: nothing applied"); yield break; }
        Arm(key, restore);
        try
        {
            var reads = new List<string>();
            for (var i = 0; i < 10; i++) { yield return null; if (i % 3 == 0) reads.Add(SelfAnimRead()); }
            Log($"ANIM {name} applied; self readback over 10 frames [{string.Join(" ; ", reads)}]");
            var f0 = Capture($"3b_{name}_t0", region);
            yield return Wait(1f);
            var f1 = Capture($"3b_{name}_t1", region);
            Log($"ANIM {name} frozen {DiffText(f0, f1)} | after1s {SelfAnimRead()}");
        }
        finally { Release(key); }
        yield return Frames(3);
        Log($"ANIM {name} snap-back readback {SelfAnimRead()}");
    }

    private IEnumerator AnimPathToggle(RectInt region, int epoch)
    {
        if (Aborted(epoch)) yield break;
        var cf = CameraFrameCtrl.Instance;
        if (cf == null) { Log("ANIM P3 CameraFrameCtrl.Instance null"); yield break; }
        if (!Try("ANIM P3 SetEModelAnimTimeSwitch #1", () => cf.SetEModelAnimTimeSwitch())) yield break;
        Arm("anim.p3", () => CameraFrameCtrl.Instance?.SetEModelAnimTimeSwitch());   // toggle back (semantics unknown)
        try
        {
            yield return Frames(5);
            Log($"ANIM P3_SetEModelAnimTimeSwitch called once; self {SelfAnimRead()}");
            var f0 = Capture("3b_P3_switch_t0", region);
            yield return Wait(1f);
            var f1 = Capture("3b_P3_switch_t1", region);
            Log($"ANIM P3_SetEModelAnimTimeSwitch {DiffText(f0, f1)}");
        }
        finally { Release("anim.p3"); }
        yield return Frames(3);
        Log($"ANIM P3 after second call (toggle back) self {SelfAnimRead()}");
    }

    private Action? SetAnimCompSpeed(List<Char> chars)
    {
        var saved = new List<(Char C, float Speed)>();
        foreach (var c in chars)
        {
            var ac = LiveModel(CharEntity(c.CharId, c.IsSelf))?.AnimComp;
            if (ac == null) continue;
            saved.Add((c, ac.Speed));
            ac.Speed = 0f;
        }
        Log($"ANIM P1 set AnimComp.Speed=0 on {saved.Count} models (saved [{string.Join(",", saved.Take(6).Select(s => s.Speed.ToString("F2")))}])");
        return saved.Count == 0 ? null : () =>
        {
            foreach (var (c, sp) in saved) { var ac = LiveModel(CharEntity(c.CharId, c.IsSelf))?.AnimComp; if (ac != null) ac.Speed = sp; }
        };
    }

    private Action? SetBattleFrameSpeed(List<Char> chars)
    {
        var saved = new List<(Char C, float V)>();
        foreach (var c in chars)
        {
            var e = CharEntity(c.CharId, c.IsSelf);
            if (e == null) continue;
            saved.Add((c, EntityAttrExtensions.GetAttrBattleFrameSpeed(e)));
            EntityAttrExtensions.SetAttrBattleFrameSpeed(e, 0f);
            EntityAttrExtensions.SetAttrAnimSpeedDirty(e, true);
            EntityAttrExtensions.tryCalculateAnimSpeed(e);
        }
        Log($"ANIM P2 set BattleFrameSpeed=0 + AnimSpeedDirty + tryCalculateAnimSpeed on {saved.Count} (saved [{string.Join(",", saved.Take(6).Select(s => s.V.ToString("F2")))}])");
        return saved.Count == 0 ? null : () =>
        {
            foreach (var (c, v) in saved)
            {
                var e = CharEntity(c.CharId, c.IsSelf);
                if (e == null) continue;
                EntityAttrExtensions.SetAttrBattleFrameSpeed(e, v);
                EntityAttrExtensions.SetAttrAnimSpeedDirty(e, true);
                EntityAttrExtensions.tryCalculateAnimSpeed(e);
            }
        };
    }

    private Action? SetAnimatorSpeed(List<Char> chars)
    {
        var all = UnityEngine.Object.FindObjectsOfType<Animator>();
        var saved = new List<(Animator A, float Speed)>();
        foreach (var c in chars)
        {
            var m = LiveModel(CharEntity(c.CharId, c.IsSelf));
            if (m == null) continue;
            var p = m.GetAttrGoPosition();
            foreach (var a in all)
            {
                if (a == null || !a.isActiveAndEnabled) continue;
                if ((a.transform.position - p).sqrMagnitude > 1.44f) continue;   // within 1.2 m of the model root
                saved.Add((a, a.speed));
                a.speed = 0f;
            }
        }
        Log($"ANIM P4 Animator.speed=0 on {saved.Count} animators near {chars.Count} chars (scene animators={all.Length})");
        return saved.Count == 0 ? null : () => { foreach (var (a, sp) in saved) if (a != null) a.speed = sp; };
    }

    private string SelfAnimRead()
    {
        try
        {
            var e = SelfEntity();
            var m = LiveModel(e);
            if (e == null || m == null) return "self gone";
            var near = UnityEngine.Object.FindObjectsOfType<Animator>()
                .Where(a => a != null && (a.transform.position - m.GetAttrGoPosition()).sqrMagnitude <= 1.44f)
                .Select(a => a.speed.ToString("F2")).Take(4);
            return $"animSpeed(ent)={EntityAttrExtensions.GetAttrAnimSpeed(e):F2} animSpeed(model)={EntityAttrExtensions.GetAttrAnimSpeed(m):F2} " +
                   $"AnimComp.Speed={m.AnimComp?.Speed:F2} battleFrame={EntityAttrExtensions.GetAttrBattleFrameSpeed(e):F2} animators=[{string.Join(",", near)}]";
        }
        catch (Exception ex) { return $"read err {ex.GetType().Name}: {ex.Message}"; }
    }

    private void LogAnimSpeeds(string when, List<Char> chars)
    {
        var parts = new List<string>();
        foreach (var c in chars.Take(10))
        {
            var e = CharEntity(c.CharId, c.IsSelf);
            var m = LiveModel(e);
            if (e == null || m == null) { parts.Add($"{(c.IsSelf ? "self" : c.CharId.ToString())}:gone"); continue; }
            parts.Add($"{(c.IsSelf ? "self" : c.CharId.ToString())}@{c.Dist:F0}m:anim={EntityAttrExtensions.GetAttrAnimSpeed(e):F2}/comp={m.AnimComp?.Speed:F2}/bf={EntityAttrExtensions.GetAttrBattleFrameSpeed(e):F2}");
        }
        Log($"ANIM speeds {when} n={chars.Count} [{string.Join(" ", parts)}]");
    }
}
