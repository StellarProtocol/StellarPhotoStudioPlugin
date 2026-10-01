using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Panda.ZGame;
using UnityEngine;

namespace Stellar.PhotoStudio.FreeCamProbe;

/// <summary>Run 5 (2026-10-02): facial-expression HOLD, NPC reversible posing (Partner generate path), opportunistic
/// other-player clone. Owner decisions: other players = photo clone; a chosen expression must hold until changed;
/// NPCs included.</summary>
public sealed partial class FreeCamProbe
{
    private static readonly object _emoLock = new();
    private static readonly List<(long Ms, string What)> _emoEvents = new();
    private Harmony? _emoHarmony;
    private PoseTarget? _r5Other;

    private static void RecEmo(string what)
    {
        lock (_emoLock) { if (_emoEvents.Count < 20000) _emoEvents.Add((_sendClock.ElapsedMilliseconds, what)); }
    }

    private static string ModelTag(ZModel? m)
    {
        try { return m == null ? "null" : $"m{m.Uuid}"; } catch { return "m?"; }
    }

    // Passive observers of the expression lifecycle: what PlayEmote writes, when the game ends a face clip.
    private static void SetAttrEmoteInfoPrefix(ZModel self, int emoteId, float lastInterval, bool isBlinkEyeTimeOffset, bool fromShowInfo,
        double startTime, bool notifyNow, bool dialogMode, bool isFixed)
    {
        try { RecEmo($"SetAttrEmoteInfo({ModelTag(self)} id={emoteId} lastInterval={lastInterval:F2} blinkOff={isBlinkEyeTimeOffset} fromShow={fromShowInfo} start={startTime:F2} notifyNow={notifyNow} dialog={dialogMode} fixed={isFixed})"); } catch { }
    }

    private static void SetAttrEmotePersistPrefix(ZModel self, float persistTime)
    {
        try { RecEmo($"SetAttrEmoteInfoPersistTime({ModelTag(self)} {persistTime:F2})"); } catch { }
    }

    private static void ClipEndPrefix(ZModel model)
    {
        try { RecEmo($"clipEnd({ModelTag(model)} emote={EntityAttrExtensions.GetAttrEmoteInfoEmoteId(model)} persist={EntityAttrExtensions.GetAttrEmoteInfoPersistTime(model):F2})"); } catch { }
    }

    private static void OnEmoteChangedPrefix(ZModel model)
    {
        try { RecEmo($"onEmoteChanged({ModelTag(model)} emote={EntityAttrExtensions.GetAttrEmoteInfoEmoteId(model)})"); } catch { }
    }

    private void ArmEmoteHooks()
    {
        try
        {
            _emoHarmony = _services.Harmony.Create("freecamprobe.emote");
            var ext = typeof(EntityAttrExtensions);
            var sys = typeof(ModelActionEmoteSystem);
            PatchOne(ext.GetMethods(BindingFlags.Public | BindingFlags.Static).FirstOrDefault(m => m.Name == "SetAttrEmoteInfo" && m.GetParameters().Length == 9), nameof(SetAttrEmoteInfoPrefix));
            PatchOne(ext.GetMethod("SetAttrEmoteInfoPersistTime", BindingFlags.Public | BindingFlags.Static), nameof(SetAttrEmotePersistPrefix));
            PatchOne(sys.GetMethod("clipEnd", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance), nameof(ClipEndPrefix));
            PatchOne(sys.GetMethod("onEmoteChanged", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance), nameof(OnEmoteChangedPrefix));
        }
        catch (Exception ex) { Log($"R5 EMOHOOK arm FAILED {ex.GetType().Name}: {ex.Message}"); }
    }

    private void PatchOne(MethodInfo? target, string prefix)
    {
        if (target == null) { Log($"R5 EMOHOOK target for {prefix} not found"); return; }
        try
        {
            _emoHarmony!.Patch(target, prefix: new HarmonyMethod(typeof(FreeCamProbe).GetMethod(prefix, BindingFlags.NonPublic | BindingFlags.Static)));
            Log($"R5 EMOHOOK armed {target.DeclaringType?.Name}.{target.Name}");
        }
        catch (Exception ex) { Log($"R5 EMOHOOK {target.Name} FAILED {ex.GetType().Name}: {ex.Message}"); }
    }

    private void DisarmEmoteHooks()
    {
        try { _emoHarmony?.UnpatchSelf(); } catch { }
        _emoHarmony = null;
    }

    private static string EmoSince(long mark)
    {
        List<(long Ms, string What)> win;
        lock (_emoLock) win = _emoEvents.Where(e => e.Ms >= mark).ToList();
        return win.Count == 0 ? "none" : string.Join(" ; ", win.Take(30).Select(e => $"+{e.Ms - mark}ms {e.What}")) + (win.Count > 30 ? $" ;… (+{win.Count - 30})" : "");
    }

    private static string EmoRb(ZModel? m)
    {
        if (m == null) return "gone";
        return Safe(() => $"emote={EntityAttrExtensions.GetAttrEmoteInfoEmoteId(m)} persist={EntityAttrExtensions.GetAttrEmoteInfoPersistTime(m):F2} fixed={EntityAttrExtensions.GetAttrEmoteInfoInFixedEmote(m)}");
    }

    private IEnumerator StepSetup5()
    {
        Mark("R5 setup start");
        ArmEmoteHooks();   // stays armed across steps (RunOne's ReleaseAll unpatched it in run 5a); disarmed in R5_end / Dispose
        Log($"R5 SENDS since boot: {SendsTotal()}");
        var mark = SendMark();
        yield return Wait(5f);
        Log($"R5 SENDS idle baseline: {SendsSince(mark)}");
        Log($"R5 EMOHOOK idle 5 s: {EmoSince(mark)}");
        Log("R5 self gender (CharSerialize.charBase.gender): " + Lua("return tostring(Z.ContainerMgr.CharSerialize.charBase.gender)"));
        PickAction();
        yield return FindStillOther(20);
        Log($"R5 nearest npc: {NearestNpc()?.Tag ?? "none within 25 m"}");
    }

    /// <summary>Other player within 15 m whose position moved less than 0.3 m over 1 s (bounded wait).</summary>
    private IEnumerator FindStillOther(int maxSeconds)
    {
        for (var i = 0; i < maxSeconds && _r5Other == null; i++)
        {
            var c = NearbyChars(15f, 1, includeSelf: false).FirstOrDefault();
            var e = c == null ? null : CharEntity(c.CharId, false);
            var m = LiveModel(e);
            if (m == null) { yield return Wait(1f); continue; }
            var p0 = m.GetAttrGoPosition();
            yield return Wait(1f);
            m = LiveModel(e);
            if (m == null) continue;
            var moved = Vector3.Distance(p0, m.GetAttrGoPosition());
            Log($"R5 other candidate char{c!.CharId}@{c.Dist:F1}m moved {moved:F2} m in 1 s");
            if (moved < 0.3f) _r5Other = new PoseTarget { Tag = $"other{c.CharId}@{c.Dist:F1}m", Uuid = e!.Uuid };
        }
        Log($"R5 still other player within 15 m: {(_r5Other == null ? $"none after {maxSeconds} s" : _r5Other.Tag)}");
    }

    private IEnumerator StepEnd5()
    {
        DisarmEmoteHooks();
        Log("R5 EMOHOOK disarmed");
        yield break;
    }

    private IEnumerator StepFaceSelf5()
    {
        var g = SelfGender();
        yield return FaceHold(new PoseTarget { Tag = "self", IsSelf = true, Uuid = SelfEntity()?.Uuid ?? 0 }, g == 1 ? 303 : 403, g == 1 ? 315 : 415, full: true);
    }

    private IEnumerator StepFaceSelfClone5()
    {
        var self = SelfEntity();
        if (self == null) { _currentOutcome = "skipped"; yield break; }
        var c = MakeClone(new PoseTarget { Tag = "selfsrc", IsSelf = true, Uuid = self.Uuid }, "selfclone");
        if (c == null) yield break;
        yield return Wait(1.0f);
        var g = SelfGender();
        yield return FaceHold(c, g == 1 ? 303 : 403, g == 1 ? 315 : 415, full: true);
        Release($"r4.clone.{c.Tag}");
    }

    private IEnumerator StepOtherClone5()
    {
        if (_r5Other == null || EntByUuid(_r5Other.Uuid) == null) yield return FindStillOther(25);
        if (_r5Other == null || EntByUuid(_r5Other.Uuid) == null) { Log("R5 other-clone: no still other player within 15 m"); _currentOutcome = "skipped"; yield break; }
        var c = MakeClone(_r5Other, "clone-" + _r5Other.Tag);
        if (c == null) yield break;
        yield return Wait(1.0f);
        yield return Aim(c, false);
        var idle = Cap($"{Tag(c)}_idle");
        yield return PoseAction(c, idle, _sceneEpoch);
        yield return FaceHold(c, 303, 403, full: false);
        yield return RefreshClone(c, _r5Other);
    }
}
