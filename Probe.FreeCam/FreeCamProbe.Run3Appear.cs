using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Panda.ZGame;
using UnityEngine;

namespace Stellar.PhotoStudio.FreeCamProbe;

/// <summary>Run 3 — position hold on non-player entities, and freezing entities that appear during a freeze.</summary>
public sealed partial class FreeCamProbe
{
    private const float AppearWindowSeconds = 120f;

    private sealed class Held3
    {
        public Ent E = null!;
        public Vector3 Pos;
        public Vector3 Prev;
        public float Moved;
        public bool Gone;
    }

    private IEnumerator StepHoldKinds3()
    {
        var epoch = _sceneEpoch;
        ArmAppearHooks();   // re-armed per step (RunOne releases every armed hook after each step)
        var held = AllEntities(R3Range).Where(x => x.Dist >= 0f && x.Kind != "CharEnt")
            .Select(x => new Held3 { E = x, Pos = LiveModel(EntByUuid(x.Uuid))!.GetAttrGoPosition() }).ToList();
        if (held.Count == 0) { Log("R3H no non-player entity with a model in range"); yield break; }
        foreach (var h in held) h.Prev = h.Pos;
        var until = Time.realtimeSinceStartup + 3f;
        while (Time.realtimeSinceStartup < until && !Aborted(epoch))
        {
            foreach (var h in held.Where(h => !h.Gone))
            {
                var m = LiveModel(EntByUuid(h.E.Uuid));
                if (m == null) { h.Gone = true; continue; }
                var p = m.GetAttrGoPosition();
                h.Moved += Vector3.Distance(p, h.Prev);
                h.Prev = p;
            }
            yield return null;
        }
        var movers = held.Where(h => !h.Gone && h.Moved > 0.3f).OrderByDescending(h => h.Moved).ToList();
        Log($"R3H pre-observe 3 s: n={held.Count} movers={movers.Count} [{string.Join(" ", movers.Take(8).Select(h => $"{h.E.Kind}:{h.E.Uuid}:{h.Moved:F2}m"))}]");
        var subject = movers.FirstOrDefault();
        var framed = subject != null && FrameEntity(subject.E.Uuid, 6f);
        try
        {
            if (framed) yield return Wait(1.0f);
            Shot? u1 = null, u2 = null;
            if (subject != null && EntRegion(subject.E.Uuid, 0.14f, 0.22f) is { } ur)
            {
                u1 = Capture("R3H_U_t1", ur);
                yield return Wait(0.6f);
                u2 = Capture("R3H_U_t2", ur);
            }
            yield return HoldRun3(held, subject, epoch, Diff(u1, u2).ChangedPct);
        }
        finally { Release("r3.hold"); Release("r3.freeze"); Release("cam.vcam"); }
    }

    private IEnumerator HoldRun3(List<Held3> held, Held3? subject, int epoch, float unfrozenPct)
    {
        Arm("r3.freeze", RestoreFrozen);
        yield return FreezeAll(0, includeSelf: false, "H");
        foreach (var h in held)
        {
            var m = h.Gone ? null : LiveModel(EntByUuid(h.E.Uuid));
            var v = m?.ModelGoComp?.Position;
            if (v == null) h.Gone = true; else h.Pos = v.Value;
        }
        double cost = 0, costMax = 0; long calls = 0; int frames = 0, errs = 0; string? firstErr = null;
        ProbeTicks.LateTick = () =>
        {
            if (Aborted(epoch)) return;
            var sw = Stopwatch.StartNew();
            foreach (var h in held)
            {
                if (h.Gone) continue;
                var m = LiveModel(EntByUuid(h.E.Uuid));
                if (m == null) { h.Gone = true; continue; }
                try { var g = m.ModelGoComp; if (g != null) g.Position = h.Pos; calls++; }
                catch (Exception ex) { errs++; firstErr ??= ex.GetType().Name + ": " + ex.Message; }
            }
            var ms = sw.Elapsed.TotalMilliseconds;
            cost += ms; costMax = Math.Max(costMax, ms); frames++;
        };
        Arm("r3.hold", () => ProbeTicks.LateTick = null);
        int samples = 0, off = 0, attrOff = 0; float maxOff = 0f, attrMax = 0f;
        Shot? f1 = null, f2 = null;
        RectInt? fr = subject == null ? null : EntRegion(subject.E.Uuid, 0.14f, 0.22f);
        var start = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - start < 2f && !Aborted(epoch))
        {
            yield return EndOfFrame();
            foreach (var h in held.Where(h => !h.Gone))
            {
                var m = LiveModel(EntByUuid(h.E.Uuid));
                if (m == null) { h.Gone = true; continue; }
                var d = Vector3.Distance(m.ModelGoComp?.Position ?? m.GetAttrGoPosition(), h.Pos);
                samples++;
                if (d > 0.05f) { off++; maxOff = Math.Max(maxOff, d); }
                var a = Vector3.Distance(m.GetAttrGoPosition(), h.Pos);
                if (a > 0.05f) { attrOff++; attrMax = Math.Max(attrMax, a); }
            }
            var el = Time.realtimeSinceStartup - start;
            if (fr is { } r && f1 == null && el > 0.4f) f1 = Capture("R3H_F_t1", r);
            else if (fr is { } r2 && f1 != null && f2 == null && el > 1.0f) f2 = Capture("R3H_F_t2", r2);
        }
        Release("r3.hold");
        var kinds = string.Join(" ", held.GroupBy(h => h.E.Kind).Select(g => $"{g.Key}={g.Count()}"));
        Log($"R3H W2 ModelGoComp.Position n={held.Count(h => !h.Gone)} [{kinds}] frames={frames} avg={(frames > 0 ? cost / frames : 0):F4}ms/frame max={costMax:F4}ms " +
            $"perEnt={(calls > 0 ? cost * 1000 / calls : 0):F2}us x50={(calls > 0 ? cost / calls * 50 : 0):F3}ms errors={errs}{(firstErr == null ? "" : $" ({Short(firstErr)})")} | " +
            $"END-OF-FRAME visual off-hold {off}/{samples} max={maxOff:F3}m | attr off-hold {attrOff}/{samples} max={attrMax:F3}m | " +
            $"subject={(subject == null ? "none" : $"{subject.E.Kind}:{subject.E.Uuid}")} unfrozen changed={unfrozenPct:F1}% frozen+held changed={Diff(f1, f2).ChangedPct:F1}%");
        Release("r3.freeze");
        yield return Frames(3);
        var gaps = held.Where(h => !h.Gone).Select(h => LiveModel(EntByUuid(h.E.Uuid))).Where(m => m != null)
            .Select(m => Vector3.Distance(m!.ModelGoComp?.Position ?? m.GetAttrGoPosition(), m.GetAttrGoPosition())).ToList();
        Log($"R3H released: visual-vs-attr gap 3 frames later n={gaps.Count} max={(gaps.Count > 0 ? gaps.Max() : 0):F3}m");
        foreach (var wait in new[] { 1f, 2f })
        {
            yield return Wait(wait);
            var g2 = held.Where(h => !h.Gone).Select(h => LiveModel(EntByUuid(h.E.Uuid))).Where(m => m != null)
                .Select(m => Vector3.Distance(m!.ModelGoComp?.Position ?? m.GetAttrGoPosition(), m.GetAttrGoPosition())).ToList();
            Log($"R3H released: visual-vs-attr gap +{(wait == 1f ? 1 : 3)} s n={g2.Count} max={(g2.Count > 0 ? g2.Max() : 0):F3}m");
        }
    }

    /// <summary>Called from the appear-hook postfix while armed: attr-freeze the new entity, queue it for stage 2 + verify.</summary>
    internal void FreezeAppeared(long uuid, string hook)
    {
        var em = Mgr();
        if (em == null || uuid == em.PlayerUuid || _r3Caught.Any(c => c.Uuid == uuid)) return;
        FreezeEntity(uuid, out var kind);   // attr where supported; stage 2 (P1) runs from the verify loop
        _r3Caught.Add((uuid, hook, Time.realtimeSinceStartup, kind));
    }

    private IEnumerator StepAppear3()
    {
        var epoch = _sceneEpoch;
        ArmAppearHooks();   // re-armed per step (RunOne releases every armed hook after each step)
        _r3Caught.Clear();
        var before = DrawnSpeeds(includeSelf: true);
        Arm("r3.freeze", RestoreFrozen);
        try
        {
            yield return FreezeAll(0, includeSelf: false, "A");
            var ev0 = _apEvents.Count;
            _apMode = 1;
            var start = Time.realtimeSinceStartup;
            var verdicts = new Dictionary<long, string>();
            var zeroSince = new Dictionary<long, float>();
            while (Time.realtimeSinceStartup - start < AppearWindowSeconds && !Aborted(epoch)
                   && verdicts.Count(v => v.Value.StartsWith("FROZEN")) < 8)
            {
                VerifyCaughtTick(verdicts, zeroSince);
                yield return Wait(0.1f);
            }
            _apMode = 0;
            var drain = Time.realtimeSinceStartup + 4f;
            while (Time.realtimeSinceStartup < drain && verdicts.Count < _r3Caught.Count) { VerifyCaughtTick(verdicts, zeroSince); yield return Wait(0.1f); }
            foreach (var c in _r3Caught.Take(20))
                Log($"R3A caught {c.Kind} uuid={c.Uuid} via {c.Hook}: {(verdicts.TryGetValue(c.Uuid, out var v) ? v : "pending")} read[{EntAnimRead(c.Uuid)}]");
            var evs = _apEvents.Skip(ev0).ToList();
            Log($"R3A window={Time.realtimeSinceStartup - start:F1}s appearEvents={evs.Count} [{string.Join(" ", evs.GroupBy(e => e.Hook).Select(g => $"{g.Key}={g.Count()}(modelAtHook={g.Count(x => x.ModelAtHook)})"))}] " +
                $"caught={_r3Caught.Count} verdicts[{string.Join(" ", verdicts.Values.GroupBy(x => x.Split('(')[0]).Select(g => $"{g.Key}={g.Count()}"))}] p1 by kind [{P1KindsText()}]");
        }
        finally { _apMode = 0; Release("r3.freeze"); }
        yield return Frames(10);
        Log($"R3A restore audit +10 frames: {AuditText(before)}");
        Log($"R3A appear hooks total: {ApHitsText()}");
    }

    private readonly Dictionary<long, int> _r3Reapplied = new();

    private static string SkillStageNow(long uuid)
    {
        try { var e = EntByUuid(uuid); return e == null ? "gone" : EntityAttrExtensions.GetAttrSkillStageTimeFactor(e).ToString("F2"); } catch { return "err"; }
    }

    /// <summary>Re-writes the attr freeze (idempotent; keeps the prior recorded at first freeze) — run 3b saw two players
    /// whose drawn speed came back to 1.00 after an AddEntity-time freeze.</summary>
    private void ReapplyAttr(long uuid)
    {
        var e = EntByUuid(uuid);
        if (e == null) return;
        try
        {
            if (!_r3Frozen.ContainsKey(uuid)) { FreezeEntity(uuid, out _); }
            else
            {
                EntityAttrExtensions.SetAttrSkillStageTimeFactor(e, 0f);
                EntityAttrExtensions.SetAttrAnimSpeedDirty(e, true);
                EntityAttrExtensions.tryCalculateAnimSpeed(e);
            }
            _r3Reapplied[uuid] = _r3Reapplied.TryGetValue(uuid, out var n) ? n + 1 : 1;
        }
        catch { }
    }

    /// <summary>One verify pass: stage-2 P1 for caught entities whose model is live; FROZEN once drawn speed stays 0 for 0.6 s.</summary>
    private void VerifyCaughtTick(Dictionary<long, string> verdicts, Dictionary<long, float> zeroSince)
    {
        var now = Time.realtimeSinceStartup;
        foreach (var c in _r3Caught.Where(c => !verdicts.ContainsKey(c.Uuid)))
        {
            var e = EntByUuid(c.Uuid);
            if (e == null) { verdicts[c.Uuid] = "left"; continue; }
            var ac = LiveModel(e)?.AnimComp;
            if (ac == null) { if (now - c.T > 5f) verdicts[c.Uuid] = "no-model-5s"; continue; }
            if (now - c.T < 0.05f) continue;   // let the attr recalc land (≈ 2 frames)
            float speed;
            try { speed = ac.Speed; } catch (Exception ex) { verdicts[c.Uuid] = "ERR:" + ex.GetType().Name; continue; }
            if (speed > 0.001f)
            {
                zeroSince.Remove(c.Uuid);
                ReapplyAttr(c.Uuid);
                EnsureDrawnFrozen(c.Uuid);
                if (now - c.T > 4f) verdicts[c.Uuid] = $"MOVING(speed={speed:F2} reapplied={(_r3Reapplied.TryGetValue(c.Uuid, out var rb) ? rb : 0)} skillStageNow={SkillStageNow(c.Uuid)})";
                continue;
            }
            if (!zeroSince.ContainsKey(c.Uuid)) zeroSince[c.Uuid] = now;
            else if (now - zeroSince[c.Uuid] >= 0.6f)
                verdicts[c.Uuid] = $"FROZEN(after {zeroSince[c.Uuid] - c.T:F2}s via {(_r3P1.ContainsKey(c.Uuid) ? "attr+p1" : _r3Frozen.ContainsKey(c.Uuid) ? "attr" : "?")} reapplied={(_r3Reapplied.TryGetValue(c.Uuid, out var ra) ? ra : 0)} skillStageNow={SkillStageNow(c.Uuid)})";
        }
    }
}
