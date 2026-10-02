using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Panda.ZAnim;
using Panda.ZGame;
using UnityEngine;

namespace Stellar.PhotoStudio.FreeCamProbe;

/// <summary>
/// Run 6 (2026-10-02): root cause of <c>CloneModelForPhoto</c> throwing NullReferenceException for some real players
/// (IL2CPP frame "TargetComp.checkDisAngleNeedCancel" under the cloneModel loaded-callback). Static analysis
/// (GameAssembly disassembly) shows the callback's !(state==8 &amp;&amp; actionId&gt;0) branch copies the source's
/// AnimRideTemplate info/template onto the copy through duplicated bodies of
/// <c>EntityAttrExtensions.SetAttrAnimRideTemplateInfo/SetAttrAnimRideTemplate</c> — the frame name is the nearest
/// registered symbol, not TargetComp. For every real player within 40 m over a ~5 min window this logs the source's
/// state (H1 TargetComp, H2 state/ride/combat/shapeshift/action, the AnimRideTemplate inputs), clones it with and
/// without our vcam (H3), captures the copy the game created even when the callback throws (postfix on
/// <c>ZModelManager.cloneModel</c>) to measure the leak, re-runs the two suspect setters on that copy, and recycles it.
/// </summary>
public sealed partial class FreeCamProbe
{
    private const float R6Window = 300f;
    private const float R6Range = 40f;
    private static bool _r6Capturing;
    private static readonly List<ZModel> _r6Created = new();
    private Harmony? _r6Harmony;
    private readonly Dictionary<long, (int Tries, float Last)> _r6Tried = new();
    private readonly List<string> _r6Rows = new();
    private static MethodInfo? _hasRide;
    private bool _r6FullLogged;

    private static void CloneModelPostfix(ZModel __result)
    {
        try { if (_r6Capturing && __result != null) _r6Created.Add(__result); } catch { }
    }

    private void ArmCloneHook()
    {
        try
        {
            _r6Harmony = _services.Harmony.Create("freecamprobe.r6");
            var t = typeof(ZModelManager).GetMethod("cloneModel", new[] { typeof(ZModel), typeof(EModelLod), typeof(bool), typeof(EModelCreator) });
            if (t == null) { Log("R6 HOOK ZModelManager.cloneModel(4) not found"); return; }
            _r6Harmony.Patch(t, postfix: new HarmonyMethod(typeof(FreeCamProbe).GetMethod(nameof(CloneModelPostfix), BindingFlags.NonPublic | BindingFlags.Static)));
            Log("R6 HOOK armed ZModelManager.cloneModel postfix");
        }
        catch (Exception ex) { Log($"R6 HOOK FAILED {ex.GetType().Name}: {ex.Message}"); }
        _hasRide = typeof(EntityAttrExtensions).GetMethod("hasAnimRideTemplate", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
        Log($"R6 hasAnimRideTemplate reflect={(_hasRide == null ? "MISSING" : "ok")}");
    }

    private void DisarmCloneHook()
    {
        try { _r6Harmony?.UnpatchSelf(); } catch { }
        _r6Harmony = null;
    }

    private static string HasRide(ZModel m) => Safe(() => _hasRide == null ? "n/a" : _hasRide.Invoke(null, new object[] { m })!.ToString()!);

    private static string Q(string? s) => s == null ? "null" : s.Length == 0 ? "''" : $"'{s}'";

    private static int ModelCount() { try { return ZModelManager.Instance.modelDict_.Count; } catch { return -1; } }

    /// <summary>Everything the clone callback reads from the source, plus the hypothesis fields.</summary>
    private string SourceState(ZEntity e, ZModel m)
    {
        var parts = new List<string>
        {
            "state=" + Safe(() => { var s = EntityAttrExtensions.GetAttrState(e); return $"{s}({(int)s})"; }),
            "luaState=" + Safe(() => e.GetLuaAttrState().ToString()),
            "actionId=" + Safe(() => EntityAttrExtensions.GetAttrActionInfoActionId(m).ToString()),
            "emote=" + Safe(() => EntityAttrExtensions.GetAttrEmoteInfoEmoteId(m).ToString()),
            "ride=" + Safe(() => $"{EntityAttrExtensions.GetAttrRideId(e)}/{EntityAttrExtensions.GetAttrRideUuid(e)}"),
            "rideStage=" + Safe(() => e.GetLuaRideStage().ToString()),
            "seated=" + Safe(() => EntityAttrExtensions.GetSeatedTargetUuid(e).ToString()),
            "combat=" + Safe(() => $"{EntityAttrExtensions.GetIsInCombat(e)}/lua{e.GetLuaIsInCombat()}"),
            "shapeshift=" + Safe(() => EntityAttrExtensions.GetIsInShapeShift(e).ToString()),
            "targetComp=" + Safe(() => e.GetComponent<TargetComp>() == null ? "none" : "present"),
            "modelType=" + Safe(() => m.ModelType.ToString()),
            "gender=" + Safe(() => m.ModelGender.ToString()),
            "host=" + Safe(() => m.Host == null ? "null" : m.Host.Uuid == e.Uuid ? "self" : m.Host.Uuid.ToString()),
            "vehicleModel=" + Safe(() => m.vehicleModel_.ToString()),
            "load=" + Safe(() => m.LoadStatus.ToString()),
            "hasRideTpl=" + HasRide(m),
            "rideTpl=" + Safe(() => Q(EntityAttrExtensions.GetAttrAnimRideTemplate(m))),
            "rideFade=" + Safe(() => Q(EntityAttrExtensions.GetAttrAnimRideTemplateFade(m))),
            "rideIK=" + Safe(() => EntityAttrExtensions.GetAttrAnimRideTemplateSwitchAnimDisableIK(m).ToString()),
        };
        return string.Join(" ", parts);
    }

    private string CopyState(ZModel c, ZModel? src) =>
        $"copy m{Safe(() => c.Uuid.ToString())} destroying={Safe(() => c.IsDestroying.ToString())} load={Safe(() => c.LoadStatus.ToString())} " +
        $"host={Safe(() => c.Host == null ? "null" : c.Host.Uuid.ToString())} modelType={Safe(() => c.ModelType.ToString())} vehicleModel={Safe(() => c.vehicleModel_.ToString())} " +
        $"inPhoto={Safe(() => EntityAttrExtensions.GetAttrInPhoto(c).ToString())} hasRideTpl={HasRide(c)} rideTpl={Safe(() => Q(EntityAttrExtensions.GetAttrAnimRideTemplate(c)))} " +
        $"pos={Safe(() => V(c.GetAttrGoPosition()))} srcPos={(src == null ? "-" : Safe(() => V(src.GetAttrGoPosition())))} visPos={Safe(() => V(c.ModelGoComp.Position))}";

    private IEnumerator StepSetup6()
    {
        Mark("R6 setup start");
        ArmCloneHook();
        Log($"R6 modelDict_={ModelCount()} nearby within {R6Range:F0} m: {NearbyChars(R6Range, 60, false).Count}");
        yield break;
    }

    private IEnumerator StepWindow6()
    {
        var epoch = _sceneEpoch;
        var start = Time.realtimeSinceStartup;
        var pairs = 0;
        while (Time.realtimeSinceStartup - start < R6Window && pairs < 60)
        {
            if (Aborted(epoch))
            {
                // A scene switch (house -> town etc.) is not the end of the window: wait for the world, settle, go on.
                Log($"R6 scene changed at +{Time.realtimeSinceStartup - start:F0}s; waiting for World");
                while (_services.ClientState.Phase != Stellar.Abstractions.Domain.GamePhase.World && Time.realtimeSinceStartup - start < R6Window) yield return Wait(1f);
                yield return Wait(8f);
                epoch = _sceneEpoch;
                Log($"R6 resumed at +{Time.realtimeSinceStartup - start:F0}s modelDict={ModelCount()} nearby={NearbyChars(R6Range, 60, false).Count}");
                continue;
            }
            var now = Time.realtimeSinceStartup;
            var pick = NearbyChars(R6Range, 60, false).FirstOrDefault(c =>
            {
                var e = CharEntity(c.CharId, false);
                if (e == null) return false;
                return !_r6Tried.TryGetValue(e.Uuid, out var t) || (t.Tries < 2 && now - t.Last > 60f);
            });
            if (pick == null) { yield return Wait(3f); continue; }
            var ent = CharEntity(pick.CharId, false);
            if (ent == null) { yield return null; continue; }
            var uuid = ent.Uuid;
            _r6Tried.TryGetValue(uuid, out var prev);
            _r6Tried[uuid] = (prev.Tries + 1, now);
            pairs++;
            Log($"R6 PAIR #{pairs} player char{pick.CharId} uuid={uuid} d={pick.Dist:F1}m try={prev.Tries + 1}");
            yield return Attempt(uuid, pick.CharId, "nocam");
            yield return Wait(0.5f);
            yield return AimVcamAt(uuid);
            yield return Attempt(uuid, pick.CharId, "vcam");
            Release("cam.vcam");
            yield return Wait(1f);
        }
        Log($"R6 window done pairs={pairs} players={_r6Tried.Count} elapsed={Time.realtimeSinceStartup - start:F0}s");
    }

    private IEnumerator AimVcamAt(long uuid)
    {
        var m = LiveModel(EntByUuid(uuid));
        if (m == null) yield break;
        var focus = m.GetChestPosition();
        var fwd = EntityAttrExtensions.GetAttrGoRotation(m) * Vector3.forward;
        var pos = focus + fwd * 3.0f + Vector3.up * 0.4f;
        if (TryCreateVcam(pos, Quaternion.LookRotation(focus - pos), 40f, 100000) == null) yield break;
        yield return Wait(0.7f);
    }

    private IEnumerator Attempt(long uuid, long charId, string mode)
    {
        var e = EntByUuid(uuid);
        var m = LiveModel(e);
        var mgr = Anim();
        if (e == null || m == null || mgr == null) { Log($"R6 {mode} uuid={uuid}: source gone"); yield break; }
        var src = SourceState(e, m);
        var before = ModelCount();
        _r6Created.Clear();
        _r6Capturing = true;
        ZModel? clone = null;
        string outcome;
        try { clone = mgr.CloneModelForPhoto(e); outcome = clone == null ? "null" : "ok"; }
        catch (Exception ex)
        {
            outcome = "THREW " + ex.GetType().Name + ":" + FirstLine(ex.Message);
            if (!_r6FullLogged) { _r6FullLogged = true; Log("R6 first throw full: " + ex.Message.Replace('\n', '|')); }
        }
        finally { _r6Capturing = false; }
        var created = _r6Created.ToList();
        var after = ModelCount();
        Log($"R6 {mode} uuid={uuid} char{charId} outcome={outcome} created={created.Count} modelDict {before}->{after} SRC {src}");
        var copy = clone ?? created.FirstOrDefault();
        if (copy == null) { _r6Rows.Add($"{uuid} {mode} {outcome} nocopy"); yield break; }
        Log($"R6 {mode} uuid={uuid} {CopyState(copy, m)}");
        if (clone == null)
        {
            // Re-run the two suspect setters (the callback's duplicated bodies) on the copy the game made.
            var ik = 0;
            try { ik = EntityAttrExtensions.GetAttrAnimRideTemplateSwitchAnimDisableIK(m); } catch { }
            var r1 = Safe(() => { EntityAttrExtensions.SetAttrAnimRideTemplateInfo(copy, ik); return "ok"; });
            var r2 = Safe(() => { EntityAttrExtensions.SetAttrAnimRideTemplate(copy, EntityAttrExtensions.GetAttrAnimRideTemplate(m), EntityAttrExtensions.GetAttrAnimRideTemplateFade(m)); return "ok"; });
            Log($"R6 {mode} uuid={uuid} REPRO SetAttrAnimRideTemplateInfo(copy,{ik})={r1} SetAttrAnimRideTemplate(copy,…)={r2}");
            yield return Wait(1f);
            Log($"R6 {mode} uuid={uuid} LEAK +1s {CopyState(copy, LiveModel(EntByUuid(uuid)))} modelDict={ModelCount()}");
            yield return LeakShot(uuid, copy, mode);
        }
        else
        {
            var r1 = Safe(() => { EntityAttrExtensions.SetAttrAnimRideTemplateInfo(copy, 0); return "ok"; });
            Log($"R6 {mode} uuid={uuid} CONTRAST SetAttrAnimRideTemplateInfo(okCopy,0)={r1}");
        }
        var rec = Safe(() => { mgr.RecyclePhotoModel(copy); return "ok"; });
        yield return Frames(3);
        Log($"R6 {mode} uuid={uuid} RECYCLE={rec} after: destroying={Safe(() => copy.IsDestroying.ToString())} load={Safe(() => copy.LoadStatus.ToString())} modelDict={ModelCount()}");
        _r6Rows.Add($"{uuid} {mode} {outcome} copy={(clone == null ? "leaked-captured" : "returned")}");
    }

    /// <summary>In-process render with the real player hidden: if the leaked copy is drawn, it shows where the player was.</summary>
    private IEnumerator LeakShot(long uuid, ZModel copy, string mode)
    {
        var e = EntByUuid(uuid);
        if (e == null) yield break;
        var hidden = false;
        try { CameraFrameCtrl.Instance.SetTargetEntityVisible(e, false); hidden = true; } catch (Exception ex) { Log($"R6 leakshot hide FAILED {ex.GetType().Name}"); }
        try
        {
            yield return Frames(2);
            var cam = MainCam();
            if (cam != null)
            {
                var region = RegionAround(cam, copy.GetChestPosition(), 0.12f, 0.30f);
                Capture($"R6_leak_{mode}_{uuid}", region);
            }
        }
        finally
        {
            if (hidden) { var e2 = EntByUuid(uuid); if (e2 != null) CameraFrameCtrl.Instance.SetTargetEntityVisible(e2, true); }
        }
    }

    private static string FirstLine(string s) { var i = s.IndexOf('\n'); return (i < 0 ? s : s[..i]).Trim(); }

    private IEnumerator StepSummary6()
    {
        DisarmCloneHook();
        Log($"R6 HOOK disarmed; rows={_r6Rows.Count}");
        foreach (var r in _r6Rows) Log("R6 ROW " + r);
        Log($"R6 modelDict_ end={ModelCount()}");
        yield break;
    }
}
