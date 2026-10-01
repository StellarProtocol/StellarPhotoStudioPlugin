using System;
using System.Collections;
using Panda;
using Panda.ZGame;
using UnityEngine;

namespace Stellar.PhotoStudio.FreeCamProbe;

/// <summary>
/// Run 4 pose suite per target: (a) action play → pause at 40 % → scrub to 75 % → scrub back → resume, using the photo
/// panel's own calls (self: the no-model <c>PlayAction(id, syncServer:false, …)</c> / <c>SetActionPersistTime(t)</c> that
/// <c>ExpressionSinglePlay</c> / <c>FreezeFrameCtrl</c> use; others: the <c>ZModel</c> overloads); (e) yaw +90° (self:
/// <c>LuaAsyncBridge.SetEntityRotation</c>, others: <c>SetAttrGoRotation(model)</c> — camerasys_team_edit_tpl
/// setModelRotation); look/face in Run4Look.cs; (f) reset to normal. Readbacks: <c>GetLuaAttrActionInfo*</c>, attr and
/// visual yaw; pixel pairs against an idle noise pair.
/// </summary>
public sealed partial class FreeCamProbe
{
    private IEnumerator PoseSuite(PoseTarget t)
    {
        var epoch = _sceneEpoch;
        if (ModelOf(t) == null) { Log($"R4 {t.Tag}: no model"); yield break; }
        Mark($"suite start {t.Tag}");
        Log($"R4 ==== target {t.Tag} self={t.IsSelf} clone={t.Clone != null} kind[{Safe(() => ModelKind(ModelOf(t)!))}]");
        try
        {
            yield return Aim(t, false);
            var bodyRegion = _r4Region;
            var idle0 = Cap($"{Tag(t)}_idle0");
            yield return Wait(0.5f);
            var idle1 = Cap($"{Tag(t)}_idle1");
            Log($"R4 {t.Tag} idle noise {DiffText(idle0, idle1)} actionInfo[{ActionInfo(ModelOf(t))}]");
            if (Aborted(epoch)) yield break;
            yield return PoseAction(t, idle1, epoch);
            if (Aborted(epoch)) yield break;
            yield return PoseRotate(t, epoch);
            if (Aborted(epoch)) yield break;
            yield return PoseHead(t, epoch);
            if (Aborted(epoch)) yield break;
            yield return PoseEyes(t, epoch);
            if (Aborted(epoch)) yield break;
            yield return PoseFace(t, epoch);
            if (Aborted(epoch)) yield break;
            // (f) reset to normal: every override released above; compare against the start.
            yield return Aim(t, false);
            _r4Region = bodyRegion;
            yield return Wait(0.8f);
            var end = Cap($"{Tag(t)}_end");
            Log($"R4 {t.Tag} (f) after full reset vs idle1 {DiffText(idle1, end)} (idle noise above) actionInfo[{ActionInfo(ModelOf(t))}] look[{Look(ModelOf(t))}]");
        }
        finally
        {
            Release($"r4.action.{t.Tag}");
            Release($"r4.rot.{t.Tag}");
            Release($"r4.look.{t.Tag}");
            Release($"r4.emote.{t.Tag}");
            Release("cam.vcam");
            _r4Vcam = null;
        }
    }

    private static string ActionInfo(ZModel? m)
    {
        if (m == null) return "gone";
        return Safe(() => $"id={m.GetLuaAttrActionInfoActionId():F0} total={m.GetLuaAttrActionInfoTotalTime():F2} passed={m.GetLuaAttrActionInfoPassedTime():F2}");
    }

    private string SelfState(PoseTarget t) => t.IsSelf ? $" actorState={ActorState()}" : "";

    private void Play(PoseTarget t, ZModel m, int id)
    {
        var a = Anim()!;
        if (t.IsSelf) a.PlayAction(id, false, 0f, -1f, false, 0f, false, true, 0, 0, false);
        else a.PlayAction(m, id, false, 0f, -1f, true, 0f, false, true, 0, 0, false, false, false);
    }

    private void Persist(PoseTarget t, ZModel m, float time)
    {
        if (t.IsSelf) Anim()!.SetActionPersistTime(time);
        else Anim()!.SetActionPersistTime(m, time);
    }

    private void ResetActionOf(PoseTarget t)
    {
        var m = ModelOf(t);
        var a = Anim();
        if (a == null) return;
        if (t.IsSelf) { a.SetActionPersistTime(-1f); a.ResetAction(); }
        else if (m != null) { a.SetActionPersistTime(m, -1f); a.ResetAction(m, false); }
    }

    /// <summary>(a) play → pause → scrub → scrub back → resume; sends counted over the whole window.</summary>
    private IEnumerator PoseAction(PoseTarget t, Shot? idle, int epoch)
    {
        var m = ModelOf(t);
        if (m == null || Anim() == null) yield break;
        var id = _r4ActionId;
        var mark = SendMark();
        Arm($"r4.action.{t.Tag}", () => ResetActionOf(t));
        try { Play(t, m, id); }
        catch (Exception ex) { Log($"R4 {t.Tag} (a) PlayAction threw {ex.GetType().Name}: {ex.Message}"); yield break; }
        Log($"R4 {t.Tag} (a) play {id}: sends after call {SendsSince(mark)}{SelfState(t)} actionInfo[{ActionInfo(m)}]");
        yield return Wait(0.6f);
        var p0 = Cap($"{Tag(t)}_a_play0");
        yield return Wait(0.3f);
        var p1 = Cap($"{Tag(t)}_a_play1");
        m = ModelOf(t);
        Log($"R4 {t.Tag} (a) SENDS play+0.9s: {SendsSince(mark)}");
        var ph = SendMark();
        Log($"R4 {t.Tag} (a) playing: vs idle {DiffText(idle, p0)}; motion {DiffText(p0, p1)}{SelfState(t)} actionInfo[{ActionInfo(m)}]");
        if (m == null || Aborted(epoch)) yield break;

        var total = m.GetLuaAttrActionInfoTotalTime();
        if (total <= 0.1f) total = _r4ActionTotal;
        var pauseAt = 0.40f * total;
        Persist(t, m, pauseAt);
        yield return Wait(0.3f);
        var f0 = Cap($"{Tag(t)}_a_pause0");
        yield return Wait(1.0f);
        var f1 = Cap($"{Tag(t)}_a_pause1");
        Log($"R4 {t.Tag} (a) paused SetActionPersistTime({pauseAt:F2}) total={total:F2}: hold {DiffText(f0, f1)}; vs playing {DiffText(p1, f0)}{SelfState(t)} actionInfo[{ActionInfo(ModelOf(t))}] SENDS pause {SendsSince(ph)}");
        ph = SendMark();

        m = ModelOf(t);
        if (m == null || Aborted(epoch)) yield break;
        Persist(t, m, 0.75f * total);
        yield return Wait(0.3f);
        var s0 = Cap($"{Tag(t)}_a_scrub75a");
        yield return Wait(1.0f);
        var s1 = Cap($"{Tag(t)}_a_scrub75b");
        Log($"R4 {t.Tag} (a) scrub to {0.75f * total:F2}: vs pause {DiffText(f1, s0)}; hold {DiffText(s0, s1)} actionInfo[{ActionInfo(ModelOf(t))}] SENDS scrub {SendsSince(ph)}");
        ph = SendMark();

        m = ModelOf(t);
        if (m == null || Aborted(epoch)) yield break;
        Persist(t, m, pauseAt);
        yield return Wait(0.4f);
        var b0 = Cap($"{Tag(t)}_a_scrubback");
        Log($"R4 {t.Tag} (a) scrub back to {pauseAt:F2}: vs first pause {DiffText(f1, b0)} (≈ noise ⇒ deterministic scrub) actionInfo[{ActionInfo(ModelOf(t))}]");

        m = ModelOf(t);
        if (m == null || Aborted(epoch)) yield break;
        Persist(t, m, -1f);   // the panel's "play" from a paused point = FreezeFrameCtrl(-1)
        yield return Wait(0.3f);
        var r0 = Cap($"{Tag(t)}_a_resume0");
        yield return Wait(0.3f);
        var r1 = Cap($"{Tag(t)}_a_resume1");
        Log($"R4 {t.Tag} (a) resume SetActionPersistTime(-1): motion {DiffText(r0, r1)}{SelfState(t)} actionInfo[{ActionInfo(ModelOf(t))}] SENDS scrubback+resume {SendsSince(ph)}");
        yield return Wait(Mathf.Clamp(total * 0.6f + 0.8f, 1f, 8f));
        Log($"R4 {t.Tag} (a) after natural end{SelfState(t)} actionInfo[{ActionInfo(ModelOf(t))}]");
        ph = SendMark();
        Release($"r4.action.{t.Tag}");
        yield return Wait(0.6f);
        Log($"R4 {t.Tag} (a) SENDS reset (SetActionPersistTime(-1)+ResetAction): {SendsSince(ph)}");
        var rs0 = Cap($"{Tag(t)}_a_reset0");
        yield return Wait(8f);
        var rs8 = Cap($"{Tag(t)}_a_reset8s");
        Log($"R4 {t.Tag} (a) after reset: vs idle {DiffText(idle, rs0)}; +8s vs idle {DiffText(idle, rs8)} actionInfo[{ActionInfo(ModelOf(t))}]{SelfState(t)}");
        Log($"R4 {t.Tag} (a) SENDS window play..reset: {SendsSince(mark)}{SelfState(t)}");
    }

    private static float Yaw(Quaternion q) => q.eulerAngles.y;

    private string YawText(ZModel? m) =>
        m == null ? "gone" : Safe(() => $"attrYaw={Yaw(EntityAttrExtensions.GetAttrGoRotation(m)):F1} visYaw={Yaw(m.ModelGoComp.Rotation):F1}");

    private void SetYaw(PoseTarget t, ZModel m, float yaw)
    {
        var q = Quaternion.Euler(0f, yaw, 0f);
        if (t.IsSelf) { var e = SelfEntity(); if (e != null) LuaAsyncBridge.SetEntityRotation(e, q); }
        else EntityAttrExtensions.SetAttrGoRotation(m, ref q);
    }

    /// <summary>(e) yaw +90 then readback over 2 s (is it rewritten?); restore the read yaw.</summary>
    private IEnumerator PoseRotate(PoseTarget t, int epoch)
    {
        var m = ModelOf(t);
        if (m == null) yield break;
        yield return Aim(t, false);
        var before = Cap($"{Tag(t)}_e_before");
        var pre = Yaw(EntityAttrExtensions.GetAttrGoRotation(m));
        var mark = SendMark();
        Arm($"r4.rot.{t.Tag}", () => { var mm = ModelOf(t); if (mm != null) SetYaw(t, mm, pre); });
        try { SetYaw(t, m, pre + 90f); }
        catch (Exception ex) { Log($"R4 {t.Tag} (e) rotate threw {ex.GetType().Name}: {ex.Message}"); yield break; }
        yield return Frames(3);
        Log($"R4 {t.Tag} (e) yaw {pre:F1} -> {pre + 90f:F1}: +3f {YawText(ModelOf(t))}");
        yield return Wait(0.5f);
        var after = Cap($"{Tag(t)}_e_after");
        Log($"R4 {t.Tag} (e) +0.5s {YawText(ModelOf(t))} visual {DiffText(before, after)}");
        yield return Wait(1.5f);
        Log($"R4 {t.Tag} (e) +2s {YawText(ModelOf(t))} sends {SendsSince(mark)}");
        if (Aborted(epoch)) yield break;
        Release($"r4.rot.{t.Tag}");
        yield return Wait(0.5f);
        var restored = Cap($"{Tag(t)}_e_restored");
        Log($"R4 {t.Tag} (e) restored {YawText(ModelOf(t))} vs before {DiffText(before, restored)} SENDS window {SendsSince(mark)}");
    }

    /// <summary>(f) for a clone: the panel's Refresh = UpdateMemberListData → RecyclePhotoModel + CloneModelForPhoto again.</summary>
    private IEnumerator RefreshClone(PoseTarget clone, PoseTarget source)
    {
        var mark = SendMark();
        Release($"r4.clone.{clone.Tag}");   // recycles + unhides
        yield return Wait(0.3f);
        var again = MakeClone(source, clone.Tag + "-refresh");
        if (again == null) yield break;
        yield return Wait(1.0f);
        yield return Aim(again, false);
        var shot = Cap($"{Tag(again)}_refreshed");
        Log($"R4 (f) refresh {again.Tag}: actionInfo[{ActionInfo(ModelOf(again))}] look[{Look(ModelOf(again))}] lum={(shot == null ? -1 : MeanLum(shot)):F2}");
        Release($"r4.clone.{again.Tag}");
        Release("cam.vcam");
        _r4Vcam = null;
        yield return Wait(0.5f);
        Log($"R4 (f) refresh SENDS window {SendsSince(mark)}; source visible again model={(LiveModel(EntByUuid(source.Uuid)) != null)}");
    }
}
