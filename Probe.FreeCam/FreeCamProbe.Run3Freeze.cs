using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Panda.ZGame;
using UnityEngine;

namespace Stellar.PhotoStudio.FreeCamProbe;

/// <summary>
/// Run 3 freeze trials on NON-PLAYER entities. Recipe per entity (run 3a showed the attr path alone does not reach NPC /
/// pet animation): (1) attr path <c>SetAttrSkillStageTimeFactor(e,0)</c> + <c>SetAttrAnimSpeedDirty</c> +
/// <c>tryCalculateAnimSpeed</c> where the entity's storage supports it (a recalc failure restores the attr at once);
/// (2) two frames later, any entity whose drawn <c>AnimComp.Speed</c> is still &gt; 0 gets P1 <c>AnimComp.Speed = 0</c>
/// (prior read and restored), and the probe counts frames in which the game rewrote it. <c>R3_freeze_kinds</c>: per
/// class, a vcam frames the nearest one while EVERYTHING else (self included) is frozen, so the region's pixel motion
/// is the target's own: unfrozen pair, attr-only pair, attr+P1 pair. Every trial ends with a restore audit
/// (drawn speed per entity before vs after).
/// </summary>
public sealed partial class FreeCamProbe
{
    private readonly Dictionary<long, float> _r3Frozen = new();   // attr path: uuid -> prior SkillStageTimeFactor
    private readonly Dictionary<long, float> _r3P1 = new();       // P1 path: uuid -> prior AnimComp.Speed
    private readonly HashSet<long> _r3Fx = new();                  // effect uids frozen by FreezeAll (run 3c)
    private readonly Dictionary<string, (int Ok, int Fail, double Us, string? Err)> _r3ApplyStats = new();
    private readonly List<(long Uuid, string Hook, float T, string Kind)> _r3Caught = new();

    /// <summary>Attr path for one entity. True when the attr freeze is applied and recalculated.</summary>
    private bool FreezeEntity(long uuid, out string kind)
    {
        var e = EntByUuid(uuid);
        kind = e == null ? "gone" : KindOf(e);
        if (e == null || _r3Frozen.ContainsKey(uuid)) return false;
        var sw = Stopwatch.StartNew();
        float prior;
        try
        {
            prior = EntityAttrExtensions.GetAttrSkillStageTimeFactor(e);
            EntityAttrExtensions.SetAttrSkillStageTimeFactor(e, 0f);
        }
        catch (Exception ex) { Stat(kind + "(attr-get/set)", false, 0, ex.GetType().Name + ": " + Short(ex.Message)); return false; }
        try
        {
            EntityAttrExtensions.SetAttrAnimSpeedDirty(e, true);
            EntityAttrExtensions.tryCalculateAnimSpeed(e);
            _r3Frozen[uuid] = prior;
            Stat(kind, true, sw.Elapsed.TotalMilliseconds * 1000, null);
            return true;
        }
        catch (Exception ex)
        {
            try { EntityAttrExtensions.SetAttrSkillStageTimeFactor(e, prior); } catch { }
            Stat(kind + "(recalc)", false, 0, ex.GetType().Name + ": " + Short(ex.Message));
            return false;
        }
    }

    /// <summary>Stage 2: P1 for an entity whose drawn speed is still &gt; 0. Returns "p1", "attr-ok", "no-anim" or "err".</summary>
    private string EnsureDrawnFrozen(long uuid)
    {
        var e = EntByUuid(uuid);
        var ac = LiveModel(e)?.AnimComp;
        if (ac == null) return "no-anim";
        try
        {
            var s = ac.Speed;
            if (s <= 0.001f) return "attr-ok";
            if (!_r3P1.ContainsKey(uuid)) _r3P1[uuid] = s;
            ac.Speed = 0f;
            return "p1";
        }
        catch (Exception ex) { Stat(KindOf(e!) + "(p1)", false, 0, ex.GetType().Name + ": " + Short(ex.Message)); return "err"; }
    }

    private void Stat(string kind, bool ok, double us, string? err)
    {
        _r3ApplyStats.TryGetValue(kind, out var s);
        _r3ApplyStats[kind] = ok ? (s.Ok + 1, s.Fail, s.Us + us, s.Err) : (s.Ok, s.Fail + 1, s.Us, s.Err ?? err);
    }

    private string StatsText() => string.Join(" | ", _r3ApplyStats.Select(k =>
        $"{k.Key}: ok={k.Value.Ok} fail={k.Value.Fail} avg={(k.Value.Ok > 0 ? k.Value.Us / k.Value.Ok : 0):F1}us{(k.Value.Err == null ? "" : $" err=({k.Value.Err})")}"));

    private void RestoreFrozen()
    {
        int p1 = 0, restored = 0, gone = 0, residual = 0;
        foreach (var (uuid, speed) in _r3P1)
        {
            var ac = LiveModel(EntByUuid(uuid))?.AnimComp;
            if (ac == null) { gone++; continue; }
            try { ac.Speed = speed; p1++; } catch { residual++; }
        }
        foreach (var (uuid, prior) in _r3Frozen)
        {
            var e = EntByUuid(uuid);
            if (e == null) { gone++; continue; }
            try { EntityAttrExtensions.SetAttrSkillStageTimeFactor(e, prior); } catch { residual++; continue; }
            try { EntityAttrExtensions.SetAttrAnimSpeedDirty(e, true); EntityAttrExtensions.tryCalculateAnimSpeed(e); } catch { }
            restored++;
            try { if (Math.Abs(EntityAttrExtensions.GetAttrSkillStageTimeFactor(e) - prior) > 0.001f) residual++; } catch { residual++; }
        }
        var fem = Panda.ZEffect.ZEffectManager.IsCreated ? Panda.ZEffect.ZEffectManager.Instance : null;
        var fx = 0;
        if (fem != null) foreach (var uid in _r3Fx) { try { fem.SetEffectFreeze(uid, false); fx++; } catch { } }
        Log($"R3 restore: attr restored={restored}/{_r3Frozen.Count} p1 restored={p1}/{_r3P1.Count} effects unfrozen={fx}/{_r3Fx.Count} gone(left)={gone} residual={residual}");
        _r3Frozen.Clear();
        _r3P1.Clear();
        _r3Fx.Clear();
    }

    /// <summary>Drawn speed of every live entity with an anim comp (restore audit).</summary>
    private static Dictionary<long, float> DrawnSpeeds(bool includeSelf)
    {
        var d = new Dictionary<long, float>();
        var em = Mgr();
        if (em == null) return d;
        var self = em.PlayerUuid;
        foreach (var uuid in DictKeys(em.EntityDict))
        {
            if (uuid == self && !includeSelf) continue;
            try { var ac = LiveModel(EntByUuid(uuid))?.AnimComp; if (ac != null) d[uuid] = ac.Speed; } catch { }
        }
        return d;
    }

    private string AuditText(Dictionary<long, float> before)
    {
        var now = DrawnSpeeds(includeSelf: true);
        var bad = before.Where(kv => now.TryGetValue(kv.Key, out var s) && Math.Abs(s - kv.Value) > 0.01f)
            .Select(kv => $"{KindOf(EntByUuid(kv.Key)!)}:{kv.Key}:{kv.Value:F2}->{now[kv.Key]:F2}").ToList();
        return $"compared={before.Count(kv => now.ContainsKey(kv.Key))} changed={bad.Count} [{string.Join(" ", bad.Take(8))}]";
    }

    /// <summary>Freezes every live entity except <paramref name="exceptUuid"/> (self included when asked): attr pass, 2 frames, P1 pass.</summary>
    private IEnumerator FreezeAll(long exceptUuid, bool includeSelf, string tag)
    {
        _r3ApplyStats.Clear();
        var em = Mgr();
        if (em == null) yield break;
        var self = em.PlayerUuid;
        var uuids = DictKeys(em.EntityDict).Where(u => u != exceptUuid && (includeSelf || u != self)).ToList();
        var sw = Stopwatch.StartNew();
        var attr = uuids.Count(u => FreezeEntity(u, out _));
        var attrMs = sw.Elapsed.TotalMilliseconds;
        var fem = Panda.ZEffect.ZEffectManager.IsCreated ? Panda.ZEffect.ZEffectManager.Instance : null;
        if (fem != null) foreach (var uid in EffectKeys(fem, out _)) { try { fem.SetEffectFreeze(uid, true); _r3Fx.Add(uid); } catch { } }
        yield return Frames(2);
        sw.Restart();
        var res = uuids.Select(EnsureDrawnFrozen).ToList();
        var p1Ms = sw.Elapsed.TotalMilliseconds;
        Log($"R3 {tag} freeze-all: entities={uuids.Count} attr={attr} in {attrMs:F2} ms; after 2 frames p1={res.Count(r => r == "p1")} attr-ok={res.Count(r => r == "attr-ok")} " +
            $"no-anim={res.Count(r => r == "no-anim")} err={res.Count(r => r == "err")} in {p1Ms:F2} ms | p1 by kind [{P1KindsText()}] effects frozen={_r3Fx.Count} | [{StatsText()}]");
    }

    private string P1KindsText() => string.Join(" ", _r3P1.Keys.Select(u => KindOf(EntByUuid(u)!)).GroupBy(k => k).Select(g => $"{g.Key}={g.Count()}"));

    private IEnumerator StepFreezeKinds3()
    {
        var epoch = _sceneEpoch;
        ArmAppearHooks();   // re-armed per step (RunOne releases every armed hook after each step)
        var ents = AllEntities(R3Range).Where(x => x.Dist >= 0f && x.Kind != "CharEnt").ToList();
        if (!ents.Any(x => x.Kind == "MonsterEnt")) Log($"R3F no MonsterEnt within {R3Range:F0} m (town) — monsters judged statically");
        var targets = ents.GroupBy(x => x.Kind).Select(g => g.First()).Take(7).ToList();
        if (targets.Count == 0) { Log($"R3F no non-player entity with a model within {R3Range:F0} m"); yield break; }
        Log($"R3F targets: {string.Join(" ", targets.Select(t => $"{t.Kind}:{t.Uuid}@{t.Dist:F1}m"))}");
        foreach (var t in targets)
        {
            if (Aborted(epoch)) yield break;
            yield return KindTrial(t, epoch);
            yield return Wait(0.4f);
        }
    }

    /// <summary>Frames <paramref name="uuid"/>'s chest from <paramref name="dist"/> m on the side facing the game camera.</summary>
    private bool FrameEntity(long uuid, float dist)
    {
        var m = LiveModel(EntByUuid(uuid));
        var cam = MainCam();
        if (m == null || cam == null) return false;
        var chest = m.GetChestPosition();
        var dir = cam.transform.position - chest;
        dir.y = 0f;
        dir = dir.sqrMagnitude < 0.01f ? Vector3.forward : dir.normalized;
        var pos = chest + dir * dist + Vector3.up * 0.4f;
        return TryCreateVcam(pos, Quaternion.LookRotation(chest - pos), 45f, 100000) != null;
    }

    private RectInt? EntRegion(long uuid, float halfW = 0.08f, float halfH = 0.16f)
    {
        var m = LiveModel(EntByUuid(uuid));
        var cam = MainCam();
        if (m == null || cam == null) return null;
        var vp = cam.WorldToViewportPoint(m.GetChestPosition());
        if (vp.z <= 0f || vp.x < 0.05f || vp.x > 0.95f || vp.y < 0.05f || vp.y > 0.95f) return null;
        return RegionAround(cam, m.GetChestPosition(), halfW, halfH);
    }

    private IEnumerator Pair(string name, RectInt r, Action<float> result)
    {
        var a = Capture(name + "_t1", r);
        yield return Wait(0.6f);
        var b = Capture(name + "_t2", r);
        result(Diff(a, b).ChangedPct);
    }

    private IEnumerator KindTrial(Ent t, int epoch)
    {
        var m0 = LiveModel(EntByUuid(t.Uuid));
        if (m0 == null) { Log($"R3F {t.Kind} {t.Uuid} gone"); yield break; }
        Log($"R3F {t.Kind} {t.Uuid}@{t.Dist:F1}m model[{ModelKind(m0)}]");
        var before = DrawnSpeeds(includeSelf: true);
        var framed = FrameEntity(t.Uuid, 3f);
        Arm("r3.freeze", RestoreFrozen);
        try
        {
            yield return FreezeAll(t.Uuid, includeSelf: true, $"F-{t.Kind}-background");
            yield return Wait(0.8f);
            if (EntRegion(t.Uuid) is not { } r) { Log($"R3F {t.Kind} VERDICT n/a (not on screen, framed={framed})"); yield break; }
            float up = -1, ap = -1, pp = -1;
            yield return Pair($"R3F_{t.Kind}_U", r, v => up = v);
            var attrOk = FreezeEntity(t.Uuid, out _);
            StartHoldAll();
            yield return Frames(2);
            var readAttr = EntAnimRead(t.Uuid);
            yield return Pair($"R3F_{t.Kind}_A", r, v => ap = v);
            var p1 = EnsureDrawnFrozen(t.Uuid);
            var rewrites = 0;
            for (var i = 0; i < 30; i++) { yield return null; var ac = LiveModel(EntByUuid(t.Uuid))?.AnimComp; if (ac != null && ac.Speed > 0.001f) { rewrites++; if (p1 == "p1") ac.Speed = 0f; } }
            yield return Pair($"R3F_{t.Kind}_P", r, v => pp = v);
            Log($"R3F {t.Kind} VERDICT attr={Judge(up, ap)} attr+p1={Judge(up, pp)} | unfrozen={up:F1}% attrOnly={ap:F1}% attr+p1={pp:F1}% (everything else frozen incl. effects; A/P pairs with every visual position held) | attrApplied={attrOk} " +
                $"read@attr[{readAttr}] stage2={p1} gameRewroteSpeed={rewrites}/30 frames read@end[{EntAnimRead(t.Uuid)}]");
        }
        finally { Release("r3.holdall"); Release("r3.freeze"); Release("cam.vcam"); }
        yield return Frames(10);
        Log($"R3F {t.Kind} restore audit +10 frames: {AuditText(before)}");
        yield return Wait(1f);
        Log($"R3F {t.Kind} restore audit +1 s: {AuditText(before)}");
    }

    /// <summary>Holds the drawn position (ModelGoComp.Position, LateUpdate) of every non-self entity with a model.</summary>
    private void StartHoldAll()
    {
        var em = Mgr();
        if (em == null) return;
        var self = em.PlayerUuid;
        var held = new List<(long Uuid, Vector3 Pos)>();
        foreach (var u in DictKeys(em.EntityDict))
        {
            if (u == self) continue;
            var p = LiveModel(EntByUuid(u))?.ModelGoComp?.Position;
            if (p != null) held.Add((u, p.Value));
        }
        ProbeTicks.LateTick = () =>
        {
            foreach (var (u, pos) in held) { var g = LiveModel(EntByUuid(u))?.ModelGoComp; if (g != null) g.Position = pos; }
        };
        Arm("r3.holdall", () => ProbeTicks.LateTick = null);
    }

    private static string Judge(float unfrozen, float frozen) =>
        unfrozen < 0 || frozen < 0 ? "n/a" : unfrozen < 3f ? "NO-MOTION" : frozen <= Math.Max(3f, unfrozen * 0.35f) ? "FROZEN" : "MOVING";
}
