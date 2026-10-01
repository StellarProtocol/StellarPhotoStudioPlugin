using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Panda.ZGame;
using UnityEngine;

namespace Stellar.PhotoStudio.FreeCamProbe;

public sealed partial class FreeCamProbe
{
    /// <summary>The face-system / talk-view hold: <c>model:SetLuaAttrEmoteInfo(id, -1, true)</c> (lastInterval -1).</summary>
    private static void HoldFace(ZModel m, int faceId) => m.SetLuaAttrEmoteInfo(faceId, -1f, true, false, 0f, false, false);

    private void ClearFace(PoseTarget t)
    {
        var m = ModelOf(t);
        if (m != null) m.SetLuaAttrEmoteInfo(0, 0f, false, false, 0f, false, false);   // face_system_view: SetLuaAttrEmoteInfo(0)
        ResetFace(t);
    }

    private static string Stats(List<float> xs) =>
        xs.Count == 0 ? "n/a" : $"min={xs.Min():F1}% median={xs.OrderBy(x => x).ElementAt(xs.Count / 2):F1}% max={xs.Max():F1}%";

    /// <summary>Measures how long an expression lasts and which call holds it: H0 PlayEmote (the panel's call),
    /// H1 SetLuaAttrEmoteInfo(id,-1,true) for ≥ 22 s, switch, reset, H2 isFixed (only if H1 fails), H3 persist-time
    /// write, H4 re-play-before-expiry boundary.</summary>
    private IEnumerator FaceHold(PoseTarget t, int faceA, int faceB, bool full)
    {
        var epoch = _sceneEpoch;
        yield return Aim(t, true);
        var m = ModelOf(t);
        if (m == null) yield break;
        Arm($"r5.face.{t.Tag}", () => ClearFace(t));
        var mark = SendMark();
        var b0 = Cap($"R5_{Tag(t)}_f_base0");
        yield return Wait(2.0f);
        var b1 = Cap($"R5_{Tag(t)}_f_base1");
        Log($"R5 {t.Tag} face base noise (2 s apart, no face) {DiffText(b0, b1)} rb[{EmoRb(ModelOf(t))}]");

        // H0 — PlayEmote as the panel does
        var hm = SendMark();
        PlayFace(t, m, faceA);
        yield return Frames(2);
        Log($"R5 {t.Tag} H0 PlayEmote({faceA}) +2f rb[{EmoRb(ModelOf(t))}]");
        yield return Wait(1.2f);
        var h0a = Cap($"R5_{Tag(t)}_f_H0_1s");
        yield return Wait(5.3f);
        var h0b = Cap($"R5_{Tag(t)}_f_H0_6s");
        Log($"R5 {t.Tag} H0 +1.2s vs base {DiffText(b1, h0a)}; +6.5s vs base {DiffText(b1, h0b)}; +6.5s vs +1.2s {DiffText(h0a, h0b)} rb[{EmoRb(ModelOf(t))}] hooks: {EmoSince(hm)}");
        ClearFace(t);
        yield return Wait(1.5f);
        if (Aborted(epoch)) yield break;

        // H1 — SetLuaAttrEmoteInfo(id, -1, true) hold
        m = ModelOf(t);
        if (m == null) yield break;
        hm = SendMark();
        HoldFace(m, faceA);
        yield return Frames(2);
        Log($"R5 {t.Tag} H1 SetLuaAttrEmoteInfo({faceA},-1,true) +2f rb[{EmoRb(ModelOf(t))}]");
        yield return Wait(1.5f);
        var href = Cap($"R5_{Tag(t)}_f_H1_ref");
        var vsRef = new List<float>();
        var vsBase = new List<float>();
        Shot? last = href;
        for (var i = 1; i <= (full ? 11 : 5); i++)
        {
            yield return Wait(2f);
            if (Aborted(epoch)) yield break;
            var s = Cap($"R5_{Tag(t)}_f_H1_t{1.5f + 2f * i:F0}s");
            vsRef.Add(Diff(href, s).ChangedPct);
            vsBase.Add(Diff(b1, s).ChangedPct);
            last = s;
        }
        var held = Diff(b1, last).ChangedPct;
        var refVsBase = Diff(b1, href).ChangedPct;
        Log($"R5 {t.Tag} H1 hold over {1.5f + 2f * vsRef.Count:F1}s: vsRef[{Stats(vsRef)}] vsBase[{Stats(vsBase)}] ref-vs-base={refVsBase:F1}% end-vs-base={held:F1}% " +
            $"series vsRef=[{string.Join(",", vsRef.Select(x => x.ToString("F1")))}] rb[{EmoRb(ModelOf(t))}] hooks: {EmoSince(hm)} SENDS {SendsSince(hm)}");
        var h1Holds = held >= 0.6f * refVsBase;

        // switch A -> B (the hold must change when the user picks another face)
        m = ModelOf(t);
        if (m == null) yield break;
        hm = SendMark();
        HoldFace(m, faceB);
        yield return Wait(1.2f);
        var sw0 = Cap($"R5_{Tag(t)}_f_switchB_1s");
        yield return Wait(6.0f);
        var sw1 = Cap($"R5_{Tag(t)}_f_switchB_7s");
        Log($"R5 {t.Tag} switch {faceA}->{faceB}: vs held A {DiffText(last, sw0)}; vs base {DiffText(b1, sw0)}; +7s vs +1s {DiffText(sw0, sw1)}; +7s vs base {DiffText(b1, sw1)} rb[{EmoRb(ModelOf(t))}] hooks: {EmoSince(hm)}");

        // reset
        hm = SendMark();
        ClearFace(t);
        yield return Wait(1.2f);
        var rs = Cap($"R5_{Tag(t)}_f_reset");
        Log($"R5 {t.Tag} reset SetLuaAttrEmoteInfo(0)+ResetEmote: vs base {DiffText(b1, rs)} (base noise above) rb[{EmoRb(ModelOf(t))}] hooks: {EmoSince(hm)}");
        if (Aborted(epoch)) yield break;

        if (!h1Holds || full) yield return FaceFixedAndPersist(t, faceA, b1, !h1Holds);
        if (full) yield return FaceReplayBoundary(t, faceA, b1);
        Release($"r5.face.{t.Tag}");
        Log($"R5 {t.Tag} face suite verdict H1holds={h1Holds} SENDS whole face suite {SendsSince(mark)}");
    }

    private IEnumerator FaceFixedAndPersist(PoseTarget t, int face, Shot? b1, bool longFixed)
    {
        var m = ModelOf(t);
        if (m == null) yield break;
        var hm = SendMark();
        EntityAttrExtensions.SetAttrEmoteInfo(m, face, -1f, true, false, 0d, true, false, true);
        yield return Wait(1.5f);
        var r = Cap($"R5_{Tag(t)}_f_H2_ref");
        yield return Wait(longFixed ? 20f : 8f);
        var e = Cap($"R5_{Tag(t)}_f_H2_end");
        Log($"R5 {t.Tag} H2 SetAttrEmoteInfo(isFixed:true,lastInterval:-1) ref vs base {DiffText(b1, r)}; end(+{(longFixed ? 21.5 : 9.5):F1}s) vs base {DiffText(b1, e)}; end vs ref {DiffText(r, e)} rb[{EmoRb(ModelOf(t))}] hooks: {EmoSince(hm)}");
        ClearFace(t);
        yield return Wait(1.5f);

        m = ModelOf(t);
        if (m == null) yield break;
        hm = SendMark();
        PlayFace(t, m, face);
        yield return Frames(2);
        EntityAttrExtensions.SetAttrEmoteInfoPersistTime(m, 3600f);
        yield return Wait(1.2f);
        var p1 = Cap($"R5_{Tag(t)}_f_H3_1s");
        yield return Wait(7f);
        var p8 = Cap($"R5_{Tag(t)}_f_H3_8s");
        Log($"R5 {t.Tag} H3 PlayEmote+SetAttrEmoteInfoPersistTime(3600): +1.2s vs base {DiffText(b1, p1)}; +8.2s vs base {DiffText(b1, p8)} rb[{EmoRb(ModelOf(t))}] hooks: {EmoSince(hm)}");
        ClearFace(t);
        yield return Wait(1.5f);
    }

    /// <summary>The fallback design (re-play the face before its ~5 s expiry): is the boundary seamless?</summary>
    private IEnumerator FaceReplayBoundary(PoseTarget t, int face, Shot? b1)
    {
        var m = ModelOf(t);
        if (m == null) yield break;
        var hm = SendMark();
        PlayFace(t, m, face);
        yield return Wait(3.5f);
        var pre = Cap($"R5_{Tag(t)}_f_H4_pre");
        m = ModelOf(t);
        if (m == null) yield break;
        PlayFace(t, m, face);
        var parts = new List<string>();
        var prevFrame = 0;
        foreach (var f in new[] { 1, 3, 6, 12, 30 })
        {
            yield return Frames(f - prevFrame);
            prevFrame = f;
            var s = Cap($"R5_{Tag(t)}_f_H4_f{f}");
            parts.Add($"+{f}f {Diff(pre, s).ChangedPct:F1}%");
        }
        Log($"R5 {t.Tag} H4 re-PlayEmote at 3.5 s: vs just-before [{string.Join(" ", parts)}] pre vs base {DiffText(b1, pre)} hooks: {EmoSince(hm)}");

        // same boundary for the held path: a redundant re-apply of the SAME id while held
        m = ModelOf(t);
        if (m == null) yield break;
        HoldFace(m, face);
        yield return Wait(2.0f);
        var hpre = Cap($"R5_{Tag(t)}_f_H4b_pre");
        m = ModelOf(t);
        if (m == null) yield break;
        hm = SendMark();
        HoldFace(m, face);
        parts.Clear();
        prevFrame = 0;
        foreach (var f in new[] { 1, 3, 6, 12, 30 })
        {
            yield return Frames(f - prevFrame);
            prevFrame = f;
            var s = Cap($"R5_{Tag(t)}_f_H4b_f{f}");
            parts.Add($"+{f}f {Diff(hpre, s).ChangedPct:F1}%");
        }
        Log($"R5 {t.Tag} H4b re-SetLuaAttrEmoteInfo(same id) while held: vs just-before [{string.Join(" ", parts)}] hooks: {EmoSince(hm)}");
        ClearFace(t);
        yield return Wait(1.0f);
    }
}
