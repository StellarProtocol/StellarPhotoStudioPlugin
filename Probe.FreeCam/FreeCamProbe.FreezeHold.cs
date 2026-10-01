using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Panda.ZGame;
using WriteNotify = Panda.ZGame.Pure.WriteNotify;
using UnityEngine;

namespace Stellar.PhotoStudio.FreeCamProbe;

/// <summary>
/// § 9 item 3c — position hold. Phase OBSERVE (2 s): each LateUpdate, how often / how far the game rewrites every
/// nearby character's model position (<c>GetAttrGoPosition</c>) and its animator root transform. Phase HOLD (2 s):
/// re-assert the frozen positions via <c>ZModel.SetAttrGoPosition(ref pos, WriteNotify.Now)</c> in LateUpdate,
/// Stopwatch the per-frame cost for N characters, and check at the next Update whether the game moved them again and
/// whether the visible transform followed. The local player is EXCLUDED from writes (its model feeds the controller;
/// a stationary self adds no information). Release logs the snap-back distance.
/// </summary>
public sealed partial class FreeCamProbe
{
    private sealed class Held
    {
        public Char C = null!;
        public Vector3 Pos;
        public Vector3 Last;
        public Transform? Root;
        public int Rewrites;
        public int SeenAtUpdate;   // change observed between the previous LateUpdate and this Update
        public int SeenAtLate;     // change observed between this Update and this LateUpdate
        public float MaxDrift;
        public bool Gone;
    }

    private IEnumerator StepPositionHold()
    {
        var epoch = _sceneEpoch;
        var chars = NearbyChars(30f, 50, includeSelf: false);
        if (chars.Count == 0) { Log("HOLD no other players within 30 m — measuring cost on self as a read-only proxy only"); }
        var held = Snapshot(chars);
        Log($"HOLD snapshot n={held.Count} rootsMatched={held.Count(h => h.Root != null)}");

        // OBSERVE — no writes.
        int frames = 0;
        ProbeTicks.FrameTick = () => { if (!Aborted(epoch)) Observe(held, late: false); };
        ProbeTicks.LateTick = () => { if (!Aborted(epoch)) { frames++; Observe(held, late: true); } };
        Arm("hold.observe", () => { ProbeTicks.LateTick = null; ProbeTicks.FrameTick = null; });
        try { yield return Wait(2f); }
        finally { Release("hold.observe"); }
        var moving = held.Count(h => h.Rewrites > 0);
        Log($"HOLD observe frames={frames} movingChars={moving}/{held.Count} totalRewriteFrames={held.Sum(h => h.Rewrites)} " +
            $"phase: changedBeforeOurUpdate={held.Sum(h => h.SeenAtUpdate)} changedBetweenUpdateAndLateUpdate={held.Sum(h => h.SeenAtLate)} " +
            $"maxDrift={(held.Count > 0 ? held.Max(h => h.MaxDrift) : 0):F3}m gone={held.Count(h => h.Gone)} " +
            $"[{string.Join(" ", held.Take(10).Select(h => $"{h.C.CharId}:rw{h.Rewrites}/d{h.MaxDrift:F2}"))}]");
        if (Aborted(epoch)) yield break;

        // HOLD — re-assert each LateUpdate, measure cost + next-frame rewrite.
        held = Snapshot(chars);
        double costTotal = 0, costMax = 0;
        long calls = 0;
        int holdFrames = 0, rewrittenAtUpdate = 0, rootOff = 0;
        float maxGameMove = 0f, maxRootOff = 0f;
        ProbeTicks.LateTick = () =>
        {
            if (Aborted(epoch)) return;
            var sw = Stopwatch.StartNew();
            foreach (var h in held)
            {
                var m = h.Gone ? null : LiveModel(CharEntity(h.C.CharId, false));
                if (m == null) { h.Gone = true; continue; }
                var p = h.Pos;
                EntityAttrExtensions.SetAttrGoPosition(m, ref p, WriteNotify.Now);
                calls++;
            }
            var ms = sw.Elapsed.TotalMilliseconds;
            costTotal += ms; costMax = Math.Max(costMax, ms); holdFrames++;
        };
        ProbeTicks.FrameTick = () =>
        {
            if (Aborted(epoch) || holdFrames == 0) return;
            foreach (var h in held)
            {
                if (h.Gone) continue;
                var m = LiveModel(CharEntity(h.C.CharId, false));
                if (m == null) { h.Gone = true; continue; }
                var d = Vector3.Distance(m.GetAttrGoPosition(), h.Pos);
                if (d > 0.005f) { rewrittenAtUpdate++; maxGameMove = Math.Max(maxGameMove, d); }
                if (h.Root != null)
                {
                    var r = Vector3.Distance(h.Root.position, h.Pos);
                    if (r > 0.05f) { rootOff++; maxRootOff = Math.Max(maxRootOff, r); }
                }
            }
        };
        Arm("hold.ticks", () => { ProbeTicks.LateTick = null; ProbeTicks.FrameTick = null; });
        try
        {
            yield return Wait(2f);
        }
        finally { Release("hold.ticks"); }
        var n = Math.Max(1, held.Count);
        Log($"HOLD reassert frames={holdFrames} n={held.Count} cost avg={(holdFrames > 0 ? costTotal / holdFrames : 0):F4}ms max={costMax:F4}ms " +
            $"perCall={(calls > 0 ? costTotal * 1000 / calls : 0):F2}us extrapolated50={(calls > 0 ? costTotal / calls * 50 : 0):F4}ms/frame " +
            $"gameRewroteBeforeNextUpdate={rewrittenAtUpdate} maxGameMove={maxGameMove:F3}m rootOffHeld={rootOff} maxRootOff={maxRootOff:F3}m gone={held.Count(h => h.Gone)} (n={n})");

        // Snap-back: after release the game's next writes should return each model to its real position.
        yield return Frames(3);
        var snaps = held.Where(h => !h.Gone).Select(h =>
        {
            var m = LiveModel(CharEntity(h.C.CharId, false));
            return m == null ? -1f : Vector3.Distance(m.GetAttrGoPosition(), h.Pos);
        }).Where(d => d >= 0f).ToList();
        Log($"HOLD released: snap-back distances (real - held) n={snaps.Count} max={(snaps.Count > 0 ? snaps.Max() : 0):F3}m mean={(snaps.Count > 0 ? snaps.Average() : 0):F3}m");
        yield return CostProxy(epoch);
    }

    private List<Held> Snapshot(List<Char> chars)
    {
        var animators = UnityEngine.Object.FindObjectsOfType<Animator>();
        var list = new List<Held>();
        foreach (var c in chars)
        {
            var m = LiveModel(CharEntity(c.CharId, false));
            if (m == null) continue;
            var p = m.GetAttrGoPosition();
            Transform? root = null;
            var best = 1.44f;
            foreach (var a in animators)
            {
                if (a == null) continue;
                var d = (a.transform.position - p).sqrMagnitude;
                if (d < best) { best = d; root = a.transform; }
            }
            list.Add(new Held { C = c, Pos = p, Last = p, Root = root });
        }
        return list;
    }

    private void Observe(List<Held> held, bool late)
    {
        foreach (var h in held)
        {
            if (h.Gone) continue;
            var m = LiveModel(CharEntity(h.C.CharId, false));
            if (m == null) { h.Gone = true; continue; }
            var p = m.GetAttrGoPosition();
            if ((p - h.Last).sqrMagnitude > 1e-6f)
            {
                h.Rewrites++;
                if (late) h.SeenAtLate++; else h.SeenAtUpdate++;
            }
            h.MaxDrift = Math.Max(h.MaxDrift, Vector3.Distance(p, h.Pos));
            h.Last = p;
        }
    }

    /// <summary>Read-only cost proxy for 50 characters when the scene has few: 50 GetAttrGoPosition reads on self per frame.</summary>
    private IEnumerator CostProxy(int epoch)
    {
        double total = 0;
        var frames = 0;
        ProbeTicks.LateTick = () =>
        {
            var m = LiveModel(SelfEntity());
            if (m == null || Aborted(epoch)) return;
            var sw = Stopwatch.StartNew();
            for (var i = 0; i < 50; i++) { var p = m.GetAttrGoPosition(); _ = p; }
            total += sw.Elapsed.TotalMilliseconds;
            frames++;
        };
        Arm("hold.proxy", () => ProbeTicks.LateTick = null);
        try { yield return Frames(60); }
        finally { Release("hold.proxy"); }
        Log($"HOLD cost proxy: 50x GetAttrGoPosition(self) per frame avg={(frames > 0 ? total / frames : 0):F4}ms over {frames} frames (budget 0.3 ms for the hold)");
    }
}
