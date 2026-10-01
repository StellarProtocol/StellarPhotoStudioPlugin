using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Panda.ZGame;
using UnityEngine;
using WriteNotify = Panda.ZGame.Pure.WriteNotify;

namespace Stellar.PhotoStudio.FreeCamProbe;

/// <summary>
/// Run 2 / C — position-hold write cost per character, three write paths, each held 1.5 s from LateUpdate:
/// W1 <c>SetAttrGoPosition(model, ref pos, Now)</c> (run 1: 21.9 µs/char); W2 the model's visual
/// <c>ModelGoComp.Position</c> setter (works for ECS and GameObject models); W3 <c>UGo.transform.position</c>
/// (GameObject models only). "Did the game rewrite it before render" is read at END OF FRAME (WaitForEndOfFrame —
/// after every LateUpdate and the render): the visual position (<c>ModelGoComp.Position</c> / transform) vs the held
/// point, plus an end-of-frame t1/t2 capture of the most-moving held character. The local player is never written.
/// </summary>
public sealed partial class FreeCamProbe
{
    private sealed class Held2
    {
        public Char C = null!;
        public Vector3 Pos;
        public Vector3 Prev;
        public float Moved;
        public bool Gone;
    }

    private IEnumerator StepHold2()
    {
        var epoch = _sceneEpoch;
        var chars = NearbyChars(30f, 50, includeSelf: false);
        LogModelKinds("C", chars);
        if (chars.Count == 0)
        {
            Log("C no other players within 30 m — SELF proxy (ECS model like every char): W1 same-position (cost only), W2/W3 visual offset 0.3 m (does the game rewrite the visual before render?)");
            yield return HoldSelfProxy(epoch);
            yield break;
        }

        // Pre-observe 1 s: who moves (attr position), to pick the capture subject.
        var held = Snap2(chars);
        var until = Time.realtimeSinceStartup + 1f;
        while (Time.realtimeSinceStartup < until && !Aborted(epoch))
        {
            foreach (var h in held)
            {
                var m = h.Gone ? null : LiveModel(CharEntity(h.C.CharId, false));
                if (m == null) { h.Gone = true; continue; }
                var p = m.GetAttrGoPosition();
                h.Moved += Vector3.Distance(p, h.Prev);
                h.Prev = p;
            }
            yield return null;
        }
        var subject = held.Where(h => !h.Gone).OrderByDescending(h => h.Moved).FirstOrDefault();
        Log($"C pre-observe 1 s: {string.Join(" ", held.Take(10).Select(h => $"{h.C.CharId}:moved{h.Moved:F2}m"))} subject={subject?.C.CharId}@moved{subject?.Moved:F2}m");

        foreach (var w in new[] { "W1_SetAttrGoPosition", "W2_ModelGoComp.Position", "W3_transform.position" })
        {
            if (Aborted(epoch)) yield break;
            yield return HoldVariant(w, chars, subject?.C, epoch);
            yield return Wait(0.5f);
        }
    }

    private List<Held2> Snap2(List<Char> chars)
    {
        var list = new List<Held2>();
        foreach (var c in chars)
        {
            var m = LiveModel(CharEntity(c.CharId, false));
            if (m == null) continue;
            var p = m.GetAttrGoPosition();
            list.Add(new Held2 { C = c, Pos = p, Prev = p });
        }
        return list;
    }

    private static Vector3? VisualPos(ZModel m, bool transformOnly)
    {
        if (transformOnly) { var go = ModelGo(m); return go == null ? null : go.transform.position; }
        return m.ModelGoComp?.Position;
    }

    private IEnumerator HoldVariant(string w, List<Char> chars, Char? subject, int epoch)
    {
        var held = Snap2(chars);
        // Hold the VISUAL position the model shows now (W2/W3) or the attr position (W1).
        foreach (var h in held)
        {
            var m = LiveModel(CharEntity(h.C.CharId, false));
            if (m == null) { h.Gone = true; continue; }
            var v = w.StartsWith("W1") ? (Vector3?)m.GetAttrGoPosition() : VisualPos(m, w.StartsWith("W3"));
            if (v == null) h.Gone = true; else h.Pos = v.Value;
        }
        var writable = held.Count(h => !h.Gone);
        if (writable == 0) { Log($"C {w}: no writable models (e.g. no GameObject-backed model for W3) — n/a"); yield break; }

        double cost = 0, costMax = 0;
        long calls = 0;
        int frames = 0, writeErrors = 0;
        string? firstErr = null;
        ProbeTicks.LateTick = () =>
        {
            if (Aborted(epoch)) return;
            var sw = Stopwatch.StartNew();
            foreach (var h in held)
            {
                if (h.Gone) continue;
                var m = LiveModel(CharEntity(h.C.CharId, false));
                if (m == null) { h.Gone = true; continue; }
                try
                {
                    var p = h.Pos;
                    if (w.StartsWith("W1")) EntityAttrExtensions.SetAttrGoPosition(m, ref p, WriteNotify.Now);
                    else if (w.StartsWith("W2")) { var g = m.ModelGoComp; if (g != null) g.Position = p; }
                    else { var go = ModelGo(m); if (go != null) go.transform.position = p; }
                    calls++;
                }
                catch (Exception ex) { writeErrors++; firstErr ??= ex.GetType().Name + ": " + ex.Message; }
            }
            var ms = sw.Elapsed.TotalMilliseconds;
            cost += ms; costMax = Math.Max(costMax, ms); frames++;
        };
        Arm("hold2.tick", () => ProbeTicks.LateTick = null);
        int eofSamples = 0, eofOff = 0, attrOff = 0;
        float eofMaxOff = 0f, attrMaxOff = 0f;
        Shot[]? t1 = null, t2 = null;
        var heldSubject = subject == null ? null : held.FirstOrDefault(h => h.C.CharId == subject.CharId && !h.Gone);
        try
        {
            var start = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - start < 1.5f && !Aborted(epoch))
            {
                yield return EndOfFrame();
                foreach (var h in held)
                {
                    if (h.Gone) continue;
                    var m = LiveModel(CharEntity(h.C.CharId, false));
                    if (m == null) { h.Gone = true; continue; }
                    var vis = VisualPos(m, w.StartsWith("W3")) ?? m.GetAttrGoPosition();
                    var d = Vector3.Distance(vis, h.Pos);
                    eofSamples++;
                    if (d > 0.05f) { eofOff++; eofMaxOff = Math.Max(eofMaxOff, d); }
                    var a = Vector3.Distance(m.GetAttrGoPosition(), h.Pos);
                    if (a > 0.05f) { attrOff++; attrMaxOff = Math.Max(attrMaxOff, a); }
                }
                var el = Time.realtimeSinceStartup - start;
                if (heldSubject != null && t1 == null && el > 0.3f && CharRegion(heldSubject.C) is { } r1) t1 = CaptureMany($"C_{w}_t1", r1);
                else if (heldSubject != null && t1 != null && t2 == null && el > 1.0f) t2 = CaptureMany($"C_{w}_t2", t1[0].Region);
            }
        }
        finally { Release("hold2.tick"); }
        var (dm, dp) = Diff(t1?[0], t2?[0]);
        Log($"C {w} frames={frames} n={writable} cost avg={(frames > 0 ? cost / frames : 0):F4}ms/frame max={costMax:F4}ms perChar={(calls > 0 ? cost * 1000 / calls : 0):F2}us " +
            $"x50={(calls > 0 ? cost / calls * 50 : 0):F3}ms writeErrors={writeErrors}{(firstErr == null ? "" : $" ({Short(firstErr)})")} | END-OF-FRAME visual off-hold {eofOff}/{eofSamples} max={eofMaxOff:F3}m | " +
            $"attr off-hold {attrOff}/{eofSamples} max={attrMaxOff:F3}m | subject={(heldSubject?.C.CharId.ToString() ?? "none")} capture t1->t2 meanAbs={dm:F2} changed={dp:F1}%");
        yield return Frames(3);
        var snaps = held.Where(h => !h.Gone).Select(h =>
        {
            var m = LiveModel(CharEntity(h.C.CharId, false));
            return m == null ? -1f : Vector3.Distance(VisualPos(m, false) ?? m.GetAttrGoPosition(), m.GetAttrGoPosition());
        }).Where(d => d >= 0f).ToList();
        Log($"C {w} released: visual-vs-attr gap 3 frames later n={snaps.Count} max={(snaps.Count > 0 ? snaps.Max() : 0):F3}m");
    }

    /// <summary>Self proxy when nobody is around: W1 writes the attr position to its CURRENT value (cost only, no move);
    /// W2/W3 hold the VISUAL 0.3 m to the side (visual only; the attr — what the controller/server use — is untouched).</summary>
    private IEnumerator HoldSelfProxy(int epoch)
    {
        foreach (var w in new[] { "W1_SetAttrGoPosition(same-pos)", "W2_ModelGoComp.Position(+0.3m)", "W3_transform.position(+0.3m)" })
        {
            if (Aborted(epoch)) yield break;
            var m0 = LiveModel(SelfEntity());
            var cam = MainCam();
            if (m0 == null || cam == null) yield break;
            var isW1 = w.StartsWith("W1");
            var isW3 = w.StartsWith("W3");
            if (isW3 && ModelGo(m0) == null) { Log($"C self {w}: ECS model has no GameObject — n/a"); continue; }
            var vis0 = (isW1 ? m0.GetAttrGoPosition() : VisualPos(m0, isW3)) ?? m0.GetAttrGoPosition();
            var right = Vector3.Cross(Vector3.up, (cam.transform.position - vis0).normalized).normalized;
            var target = isW1 ? vis0 : vis0 + right * 0.3f;
            var region = SelfRegion(0.12f, 0.18f);
            yield return EndOfFrame();
            var refShot = region is { } r0 ? CaptureMany($"C_self_{w.Substring(0, 2)}_unheld", r0) : null;
            double cost = 0; long calls = 0; int frames = 0, errs = 0; string? firstErr = null;
            ProbeTicks.LateTick = () =>
            {
                if (Aborted(epoch)) return;
                var m = LiveModel(SelfEntity());
                if (m == null) return;
                var sw = Stopwatch.StartNew();
                try
                {
                    var p = target;
                    if (isW1) EntityAttrExtensions.SetAttrGoPosition(m, ref p, WriteNotify.Now);
                    else if (!isW3) { var g = m.ModelGoComp; if (g != null) g.Position = p; }
                    else { var go = ModelGo(m); if (go != null) go.transform.position = p; }
                    calls++;
                }
                catch (Exception ex) { errs++; firstErr ??= ex.GetType().Name + ": " + ex.Message; }
                cost += sw.Elapsed.TotalMilliseconds; frames++;
            };
            Arm("hold2.self", () => ProbeTicks.LateTick = null);
            int samples = 0, off = 0; float maxOff = 0f, attrMove = 0f;
            Shot[]? heldShot = null;
            try
            {
                var start = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - start < 1.5f && !Aborted(epoch))
                {
                    yield return EndOfFrame();
                    var m = LiveModel(SelfEntity());
                    if (m == null) break;
                    var vis = (isW1 ? m.GetAttrGoPosition() : VisualPos(m, isW3)) ?? m.GetAttrGoPosition();
                    var d = Vector3.Distance(vis, target);
                    samples++;
                    if (d > 0.05f) { off++; maxOff = Math.Max(maxOff, d); }
                    attrMove = Math.Max(attrMove, Vector3.Distance(m.GetAttrGoPosition(), vis0));
                    if (heldShot == null && Time.realtimeSinceStartup - start > 0.6f && region is { } r1) heldShot = CaptureMany($"C_self_{w.Substring(0, 2)}_held", r1);
                }
            }
            finally { Release("hold2.self"); }
            Log($"C self {w} frames={frames} perCall={(calls > 0 ? cost * 1000 / calls : 0):F2}us x50={(calls > 0 ? cost / calls * 50 : 0):F3}ms errors={errs}{(firstErr == null ? "" : $" ({Short(firstErr)})")} " +
                $"| END-OF-FRAME visual off-target {off}/{samples} max={maxOff:F3}m | attr moved max={attrMove:F3}m | unheld->held capture {DiffText(refShot?[0], heldShot?[0])}");
            yield return Frames(3);
            var mm = LiveModel(SelfEntity());
            if (mm != null) Log($"C self {w} released: visual-vs-attr gap={Vector3.Distance(VisualPos(mm, false) ?? mm.GetAttrGoPosition(), mm.GetAttrGoPosition()):F3}m attr-vs-start={Vector3.Distance(mm.GetAttrGoPosition(), vis0):F3}m");
            yield return Wait(0.3f);
        }
    }
}
