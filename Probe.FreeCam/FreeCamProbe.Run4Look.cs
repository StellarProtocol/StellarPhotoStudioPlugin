using System;
using System.Collections;
using Panda.ZGame;
using UnityEngine;

namespace Stellar.PhotoStudio.FreeCamProbe;

/// <summary>
/// Run 4 look/face per target, all with the photo panel's own calls (camerasys_team_edit_tpl setHeadLookAt /
/// setEyesLookAt / setLockPos, camerasys_main_pc_view coordinateTransformation):
/// (d) "Face" = HEAD look-at: Lens = <c>SetLookAtIKParam(m,1)</c> + <c>HeadClose(false)</c> + <c>SetLookAtTransform(m, cam)</c>;
///     Free (joystick) = <c>SetLookAtPos(m, local, true)</c> with local = <c>LuaWorldPosToLocal(m, world)</c>, z forced 0.2;
///     Lock = <c>SetLookAtPos(m, LuaWorldPosToLocal(m, camPos), true)</c>, then the camera moves (head must stay).
/// (c) EYES: Lens = <c>EyeOpen(true)</c> + <c>SetLookAtTransform(m, cam, false, false)</c>; Free = <c>SetLookAtPos(m, local, false)</c>;
///     Lock = <c>SetLookAtPos(m, LuaWorldPosToLocal(m, camPos), false)</c>.
/// (b) facial expression (EmoteTable Type 2, FaceDataId by gender): <c>PlayEmote(model, faceId, false)</c> /
///     self <c>PlayEmote(faceId, false)</c>; cycle 1003 Angry → 1015 Startled; hold readback at +1/+3/+6 s; ResetEmote.
/// Release per target restores the snapshot (run 2 F recipe).
/// </summary>
public sealed partial class FreeCamProbe
{
    private void ArmLookRestore(PoseTarget t, LookSnap? pre)
    {
        Arm($"r4.look.{t.Tag}", () =>
        {
            var m = ModelOf(t);
            if (m == null) return;
            ZModelHelper.ResetLookAtIKParam(m);
            m.SetLuaAttrLookAtHeadClose(pre?.Head != true);
            m.SetLuaAttrLookAtEyeOpen(pre?.Eye == true);
            ZModelHelper.SetLookAtTransform(m, null);
            ZModelHelper.SetLookAtTransform(m, null, false, false);
        });
    }

    /// <summary>World point offset from the head in camera space, as the joystick drag produces.</summary>
    private static Vector3 HeadOffsetPoint(ZModel m, Camera cam, float right, float up) =>
        m.GetHeadPosition() + cam.transform.right * right + cam.transform.up * up;

    private static Vector3 JoystickLocal(ZModel m, Vector3 world)
    {
        var local = ZModelHelper.LuaWorldPosToLocal(m, world);
        local.z = 0.2f;   // coordinateTransformation forces z = 0.2
        return local;
    }

    private IEnumerator PoseHead(PoseTarget t, int epoch)
    {
        yield return Aim(t, true);
        var m = ModelOf(t);
        var cam = MainCam();
        if (m == null || cam == null) yield break;
        var pre = Look(m);
        Log($"R4 {t.Tag} (d) head pre {pre}");
        var mark = SendMark();
        ArmLookRestore(t, pre);
        var b = Cap($"{Tag(t)}_d_base");

        ZModelHelper.SetLookAtIKParam(m, 1);
        m.SetLuaAttrLookAtHeadClose(false);
        ZModelHelper.SetLookAtTransform(m, cam.transform);
        yield return Wait(1.2f);
        var lens = Cap($"{Tag(t)}_d_lens");
        Log($"R4 {t.Tag} (d) head LENS: {Look(ModelOf(t))} vs base {DiffText(b, lens)}");

        Shot? prev = lens;
        foreach (var (name, r, u) in new[] { ("right", 0.6f, 0f), ("left", -0.6f, 0f), ("up", 0f, 0.5f) })
        {
            m = ModelOf(t);
            if (m == null || Aborted(epoch)) yield break;
            var local = JoystickLocal(m, HeadOffsetPoint(m, cam, r, u));
            ZModelHelper.SetLookAtPos(m, local, true);
            yield return Wait(1.0f);
            var s = Cap($"{Tag(t)}_d_free_{name}");
            Log($"R4 {t.Tag} (d) head FREE {name} local={V(local)}: {Look(ModelOf(t))} vs prev {DiffText(prev, s)}");
            prev = s;
        }

        // Lock at the current camera position, then move the camera 1 m sideways: a locked head must not follow.
        m = ModelOf(t);
        if (m == null) yield break;
        var lockLocal = ZModelHelper.LuaWorldPosToLocal(m, cam.transform.position);
        ZModelHelper.SetLookAtPos(m, lockLocal, true);
        yield return Wait(1.0f);
        var locked = Cap($"{Tag(t)}_d_lock");
        var snapLocked = Look(ModelOf(t));
        if (_r4Vcam != null)
        {
            var tr = _r4Vcam.transform;
            tr.SetPositionAndRotation(tr.position + tr.right * 1.0f, tr.rotation);
        }
        yield return Wait(1.0f);
        var snapMoved = Look(ModelOf(t));
        Log($"R4 {t.Tag} (d) head LOCK local={V(lockLocal)}: {snapLocked} vs prev {DiffText(prev, locked)}; after cam +1 m right: lookAt changed=[{(snapLocked == null || snapMoved == null ? "n/a" : snapLocked.Diff(snapMoved))}]");
        Release($"r4.look.{t.Tag}");
        yield return Wait(1.0f);
        Log($"R4 {t.Tag} (d) head released: {Look(ModelOf(t))} equalsPre={(pre != null && Look(ModelOf(t)) is { } a && pre.Diff(a).Length == 0)} SENDS window {SendsSince(mark)}");
    }

    private IEnumerator PoseEyes(PoseTarget t, int epoch)
    {
        yield return Aim(t, true);
        var m = ModelOf(t);
        var cam = MainCam();
        if (m == null || cam == null) yield break;
        var pre = Look(m);
        var mark = SendMark();
        ArmLookRestore(t, pre);
        var b = Cap($"{Tag(t)}_c_base");

        m.SetLuaAttrLookAtEyeOpen(true);
        ZModelHelper.SetLookAtTransform(m, cam.transform, false, false);
        yield return Wait(1.0f);
        var lens = Cap($"{Tag(t)}_c_lens");
        Log($"R4 {t.Tag} (c) eyes LENS: {Look(ModelOf(t))} vs base {DiffText(b, lens)}");

        Shot? prev = lens;
        foreach (var (name, r) in new[] { ("right", 0.8f), ("left", -0.8f) })
        {
            m = ModelOf(t);
            if (m == null || Aborted(epoch)) yield break;
            var local = JoystickLocal(m, HeadOffsetPoint(m, cam, r, 0f));
            ZModelHelper.SetLookAtPos(m, local, false);
            yield return Wait(0.8f);
            var s = Cap($"{Tag(t)}_c_free_{name}");
            Log($"R4 {t.Tag} (c) eyes FREE {name} local={V(local)}: {Look(ModelOf(t))} vs prev {DiffText(prev, s)}");
            prev = s;
        }
        m = ModelOf(t);
        if (m == null) yield break;
        var lockLocal = ZModelHelper.LuaWorldPosToLocal(m, cam.transform.position);
        ZModelHelper.SetLookAtPos(m, lockLocal, false);
        yield return Wait(0.8f);
        var locked = Cap($"{Tag(t)}_c_lock");
        Log($"R4 {t.Tag} (c) eyes LOCK local={V(lockLocal)}: {Look(ModelOf(t))} vs prev {DiffText(prev, locked)} vs lens {DiffText(lens, locked)}");
        Release($"r4.look.{t.Tag}");
        yield return Wait(0.8f);
        Log($"R4 {t.Tag} (c) eyes released: {Look(ModelOf(t))} SENDS window {SendsSince(mark)}");
    }

    private int SelfGender() => int.TryParse(Lua("return tostring(Z.ContainerMgr.CharSerialize.charBase.gender)").Replace("ok ", ""), out var g) ? g : 1;

    private void PlayFace(PoseTarget t, ZModel m, int faceId)
    {
        var a = Anim()!;
        if (t.IsSelf) a.PlayEmote(faceId, false);
        else a.PlayEmote(m, faceId, false, 0f);
    }

    private void ResetFace(PoseTarget t)
    {
        var a = Anim();
        var m = ModelOf(t);
        if (a == null) return;
        if (t.IsSelf) a.ResetEmote();
        else if (m != null) a.ResetEmote(m);
    }

    /// <summary>(b) facial expressions: cycle two faces, then a hold readback. Others: both gender variants (gender unknown).</summary>
    private IEnumerator PoseFace(PoseTarget t, int epoch)
    {
        yield return Aim(t, true);
        var m = ModelOf(t);
        if (m == null) yield break;
        var mark = SendMark();
        Arm($"r4.emote.{t.Tag}", () => ResetFace(t));
        var b = Cap($"{Tag(t)}_b_base");
        var g = SelfGender();
        var faces = new[] { (1003, 303, 403), (1015, 315, 415) };
        Shot? prev = b;
        foreach (var (emote, male, female) in faces)
        {
            foreach (var faceId in t.IsSelf ? new[] { g == 1 ? male : female } : new[] { male, female })
            {
                m = ModelOf(t);
                if (m == null || Aborted(epoch)) yield break;
                try { PlayFace(t, m, faceId); }
                catch (Exception ex) { Log($"R4 {t.Tag} (b) PlayEmote({faceId}) threw {ex.GetType().Name}: {ex.Message}"); yield break; }
                yield return Wait(1.2f);
                var s = Cap($"{Tag(t)}_b_face{faceId}");
                Log($"R4 {t.Tag} (b) face emote {emote} faceId={faceId}: vs base {DiffText(b, s)} vs prev {DiffText(prev, s)}");
                prev = s;
            }
        }
        // Hold: replay the first face and read it back over 6 s (the panel treats an Emote as a 5 s clip).
        m = ModelOf(t);
        if (m == null) yield break;
        var holdId = t.IsSelf ? (g == 1 ? 303 : 403) : 303;
        PlayFace(t, m, holdId);
        foreach (var at in new[] { 1f, 3f, 6f })
        {
            yield return Wait(at == 1f ? 1f : at == 3f ? 2f : 3f);
            var s = Cap($"{Tag(t)}_b_hold{at:F0}s");
            Log($"R4 {t.Tag} (b) hold faceId={holdId} +{at:F0}s vs base {DiffText(b, s)}");
        }
        // "Lock" candidate: replay, then SetActionPersistTime(1.0) (the panel's pause call) — does the face outlive 5 s?
        m = ModelOf(t);
        if (m == null) yield break;
        PlayFace(t, m, holdId);
        yield return Wait(1f);
        Persist(t, m, 1.0f);
        foreach (var at in new[] { 6f, 9f })
        {
            yield return Wait(at == 6f ? 5f : 3f);
            var s = Cap($"{Tag(t)}_b_persist{at:F0}s");
            Log($"R4 {t.Tag} (b) face+SetActionPersistTime(1.0) +{at:F0}s vs base {DiffText(b, s)} actionInfo[{ActionInfo(ModelOf(t))}]");
        }
        m = ModelOf(t);
        if (m != null) Persist(t, m, -1f);
        Release($"r4.emote.{t.Tag}");
        yield return Wait(0.8f);
        var after = Cap($"{Tag(t)}_b_reset");
        Log($"R4 {t.Tag} (b) after ResetEmote vs base {DiffText(b, after)} SENDS window {SendsSince(mark)}");
    }
}
