using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Cinemachine;
using HarmonyLib;
using Panda.ZAnim;
using Panda.ZGame;
using UnityEngine;

namespace Stellar.PhotoStudio.FreeCamProbe;

/// <summary>
/// Run 4 (owner 2026-10-01: "posing by person" — select a person in orbit mode and drive them like the game's photo
/// panel Adventurer tab: Rotate, Pose play/pause/scrub, Face (= head look-at) Default/Lens/Free + lock, Eyes
/// Default/Lens/Free + lock, head joystick, Refresh). Targets: self (live model), the nearest other player's LIVE model,
/// a photo CLONE of that player (<c>ZAnimActionPlayMgr.CloneModelForPhoto</c>, the game's own path for other members,
/// source entity hidden), and the nearest NPC (live model + clone attempt).
/// Server-sync check: Harmony prefixes on <c>ZCode.ZRpc.ZRpcImpl.SendMsg/SendBytes</c> (every outgoing Call/Notify,
/// framed or not) plus <c>ProxyCall/ProxyNotify</c> as an independent liveness counter, armed at plugin load so the login
/// traffic proves the hooks fire. Every sub-test logs the sends seen in its window; a 5 s idle window gives the noise.
/// </summary>
public sealed partial class FreeCamProbe
{
    internal sealed class PoseTarget
    {
        public string Tag = "";
        public bool IsSelf;
        public long Uuid;       // live entity uuid (source entity for a clone)
        public ZModel? Clone;   // set for clone targets
        public bool SourceHidden;
    }

    // ---- send counter (static: Harmony prefixes are static) -------------------------------------------------------
    private static readonly object _sendLock = new();
    private static readonly List<(long Ms, ulong Svc, uint Method, string Path)> _sends = new();
    private static readonly Stopwatch _sendClock = Stopwatch.StartNew();
    private static int _proxyHits;
    private Harmony? _sendHarmony;

    private void ArmSendCounter()
    {
        try
        {
            _sendHarmony = _services.Harmony.Create("freecamprobe.sends");
            var t = typeof(ZCode.ZRpc.ZRpcImpl);
            foreach (var m in t.GetMethods(BindingFlags.Instance | BindingFlags.Public))
            {
                string? prefix = m.Name switch
                {
                    "SendMsg" when m.GetParameters().Length == 6 => nameof(SendMsgPrefix),
                    "SendBytes" when m.GetParameters().Length == 7 => nameof(SendBytesPrefix),
                    "ProxyCall" or "ProxyNotify" => nameof(ProxyPrefix),
                    _ => null,
                };
                if (prefix == null) continue;
                _sendHarmony.Patch(m, prefix: new HarmonyMethod(typeof(FreeCamProbe).GetMethod(prefix, BindingFlags.NonPublic | BindingFlags.Static)));
                Log($"SENDS hook armed: prefix {m}");
            }
        }
        catch (Exception ex) { Log($"SENDS hook arm FAILED {ex.GetType().Name}: {ex.Message}"); }
    }

    private void DisarmSendCounter()
    {
        try { _sendHarmony?.UnpatchSelf(); } catch { }
        _sendHarmony = null;
    }

    private static void RecSend(ulong svc, uint method, string path)
    {
        lock (_sendLock) { if (_sends.Count < 200000) _sends.Add((_sendClock.ElapsedMilliseconds, svc, method, path)); }
    }

    private static void SendMsgPrefix(ulong uuid, uint methodId, bool framePack) { try { RecSend(uuid, methodId, framePack ? "msgF" : "msg"); } catch { } }
    private static void SendBytesPrefix(ulong uuid, uint methodId, bool framePack) { try { RecSend(uuid, methodId, framePack ? "bytesF" : "bytes"); } catch { } }
    private static void ProxyPrefix() { System.Threading.Interlocked.Increment(ref _proxyHits); }

    private static long SendMark() => _sendClock.ElapsedMilliseconds;

    private void Mark(string what) => Log($"R4 MARK {what} sendClock={_sendClock.ElapsedMilliseconds}");

    /// <summary>"n=… [svc:method(path)=count …]" for sends since <paramref name="mark"/>.</summary>
    private static string SendsSince(long mark)
    {
        List<(long Ms, ulong Svc, uint Method, string Path)> win;
        lock (_sendLock) win = _sends.Where(s => s.Ms >= mark).ToList();
        var secs = (_sendClock.ElapsedMilliseconds - mark) / 1000.0;
        var groups = win.GroupBy(s => $"{s.Svc}:{s.Method}({s.Path})").OrderByDescending(g => g.Count()).Select(g => $"{g.Key}={g.Count()}");
        return $"n={win.Count} in {secs:F1}s [{string.Join(" ", groups)}]";
    }

    private static string SendsTotal()
    {
        lock (_sendLock) return $"total={_sends.Count} proxyHits={_proxyHits} distinct=[{string.Join(" ", _sends.GroupBy(s => $"{s.Svc}:{s.Method}").OrderByDescending(g => g.Count()).Take(40).Select(g => $"{g.Key}={g.Count()}"))}]";
    }

    // ---- targets ------------------------------------------------------------------------------------------------
    private static ZAnimActionPlayMgr? Anim() => ZAnimActionPlayMgr.Instance;

    private ZModel? ModelOf(PoseTarget t)
    {
        if (t.Clone != null) return t.Clone.WasCollected || t.Clone.IsDestroying ? null : t.Clone;
        return t.IsSelf ? LiveModel(SelfEntity()) : LiveModel(EntByUuid(t.Uuid));
    }

    private PoseTarget? NearestOtherPlayer()
    {
        var c = NearbyChars(40f, 1, includeSelf: false).FirstOrDefault();
        var e = c == null ? null : CharEntity(c.CharId, false);
        return e == null ? null : new PoseTarget { Tag = $"other{c!.CharId}@{c.Dist:F1}m", Uuid = e.Uuid };
    }

    private PoseTarget? NearestNpc()
    {
        var n = AllEntities(25f).FirstOrDefault(x => x.Kind == "NpcEnt" && x.Dist >= 1.5f);
        return n == null ? null : new PoseTarget { Tag = $"npc{n.Uuid}@{n.Dist:F1}m", Uuid = n.Uuid };
    }

    /// <summary>Clones a live entity for photo posing exactly like camera_member_vm CreateModel, hiding the source.</summary>
    private PoseTarget? MakeClone(PoseTarget src, string tag)
    {
        var e = EntByUuid(src.Uuid);
        var mgr = Anim();
        if (e == null || mgr == null) { Log($"R4 clone {tag}: source gone or no anim mgr"); return null; }
        ZModel? clone = null;
        var sw = Stopwatch.StartNew();
        try { clone = mgr.CloneModelForPhoto(e); }
        catch (Exception ex) { Log($"R4 clone {tag}: CloneModelForPhoto threw {ex.GetType().Name}: {ex.Message}"); return null; }
        if (clone == null) { Log($"R4 clone {tag}: CloneModelForPhoto returned null"); return null; }
        Mark($"clone created {tag}");
        var t = new PoseTarget { Tag = tag, Uuid = src.Uuid, Clone = clone };
        var keep = clone;
        Arm($"r4.clone.{tag}", () => { try { Anim()?.RecyclePhotoModel(keep); } finally { UnhideSource(t); } });
        var srcModel = LiveModel(e);
        Log($"R4 clone {tag}: ok in {sw.Elapsed.TotalMilliseconds:F1} ms cloneUuid={Safe(() => clone.Uuid.ToString())} clonePos={Safe(() => V(clone.GetAttrGoPosition()))} " +
            $"srcPos={(srcModel == null ? "-" : V(srcModel.GetAttrGoPosition()))} cloneVisPos={Safe(() => V(clone.ModelGoComp.Position))} kind[{Safe(() => ModelKind(clone))}]");
        try
        {
            CameraFrameCtrl.Instance.SetTargetEntityVisible(e, false);
            t.SourceHidden = true;
            Mark($"source hidden {tag}");
            Log($"R4 clone {tag}: source hidden via CameraFrameCtrl.SetTargetEntityVisible(e,false)");
        }
        catch (Exception ex) { Log($"R4 clone {tag}: hide source FAILED {ex.GetType().Name}: {ex.Message}"); }
        return t;
    }

    private void UnhideSource(PoseTarget t)
    {
        if (!t.SourceHidden) return;
        var e = EntByUuid(t.Uuid);
        if (e != null) CameraFrameCtrl.Instance.SetTargetEntityVisible(e, true);
        Mark($"source unhidden {t.Tag}");
        t.SourceHidden = false;
    }

    private static string Safe(Func<string> f)
    {
        try { return f(); } catch (Exception ex) { return "ERR:" + ex.GetType().Name; }
    }

    // ---- camera + regions ---------------------------------------------------------------------------------------
    private CinemachineVirtualCamera? _r4Vcam;
    private RectInt _r4Region;

    /// <summary>Puts our vcam in front of the target (body or head close-up), waits for the blend, fixes the region.</summary>
    private IEnumerator Aim(PoseTarget t, bool head)
    {
        var m = ModelOf(t);
        if (m == null) yield break;
        var focus = head ? m.GetHeadPosition() : m.GetChestPosition();
        var fwd = EntityAttrExtensions.GetAttrGoRotation(m) * Vector3.forward;
        var pos = focus + fwd * (head ? 1.0f : 2.4f) + Vector3.up * (head ? 0.03f : 0.15f);
        var rot = Quaternion.LookRotation(focus - pos);
        if (_r4Vcam == null || !_releases.ContainsKey("cam.vcam"))
        {
            _r4Vcam = TryCreateVcam(pos, rot, 40f, 100000);
            if (_r4Vcam == null) yield break;
        }
        else _r4Vcam.transform.SetPositionAndRotation(pos, rot);
        yield return Wait(0.6f);
        var cam = MainCam();
        if (cam == null) yield break;
        _r4Region = head ? RegionAround(cam, focus, 0.09f, 0.13f) : RegionAround(cam, focus, 0.12f, 0.30f);
        Log($"R4 aim {t.Tag} {(head ? "head" : "body")} vcam={V(pos)} focus={V(focus)} region=({_r4Region.x},{_r4Region.y},{_r4Region.width}x{_r4Region.height})");
    }

    private Shot? Cap(string name) => Capture($"R4_{name}", _r4Region);

    // ---- steps --------------------------------------------------------------------------------------------------
    private PoseTarget? _r4Other;
    private int _r4ActionId;
    private float _r4ActionTotal;

    private IEnumerator StepSetup4()
    {
        Mark("setup start");
        Log($"R4 SENDS since boot: {SendsTotal()}");
        var mark = SendMark();
        yield return Wait(5f);
        Log($"R4 SENDS idle baseline: {SendsSince(mark)}");
        Log("R4 self gender (CharSerialize.charBase.gender): " + Lua("return tostring(Z.ContainerMgr.CharSerialize.charBase.gender)"));
        PickAction();
        _r4Other = NearestOtherPlayer();
        for (var i = 0; i < 15 && _r4Other == null; i++) { yield return Wait(2f); _r4Other = NearestOtherPlayer(); }   // bounded 90 s wait for a passer-by
        Log($"R4 nearest other player: {(_r4Other == null ? "none within 40 m after 30 s" : _r4Other.Tag)}; nearest npc: {NearestNpc()?.Tag ?? "none within 25 m"}");
    }

    /// <summary>Longest non-loop basic action (unlocked per CheckEmoteCondition) with 2.5–12 s total time.</summary>
    private void PickAction()
    {
        var mgr = Anim();
        var gender = int.TryParse(Lua("return tostring(Z.ContainerMgr.CharSerialize.charBase.gender)").Replace("ok ", ""), out var g) ? g : 1;
        var best = (Id: 9011, Total: 0f);
        foreach (var id in new[] { 9020, 9025, 9012, 9019, 9001, 9010, 9011 })
        {
            var info = mgr?.GetActionAnimInfoByActionId(id);
            var allowed = EmoteAllowed(id);
            var total = info == null ? -1f : info.GetTotalTime(gender);
            var loop = info?.IsLoop;
            Log($"R4 action candidate {id}: allowed={allowed} info={(info != null)} loop={loop} total={total:F2}s");
            if (allowed && info != null && loop == false && total is >= 2.5f and <= 12f && total > best.Total) best = (id, total);
        }
        _r4ActionId = best.Id;
        _r4ActionTotal = best.Total > 0 ? best.Total : 2.5f;
        Log($"R4 action chosen {_r4ActionId} total={_r4ActionTotal:F2}s");
    }

    private IEnumerator StepSelf4() => PoseSuite(new PoseTarget { Tag = "self", IsSelf = true, Uuid = SelfEntity()?.Uuid ?? 0 });

    /// <summary>Run 4c: the photo clone of the LOCAL player (a CharEnt storage, like any other player) — clone controls + Refresh.</summary>
    private IEnumerator StepSelfClone4()
    {
        var self = SelfEntity();
        if (self == null) { _currentOutcome = "skipped"; yield break; }
        var src = new PoseTarget { Tag = "selfsrc", IsSelf = true, Uuid = self.Uuid };
        var c = MakeClone(src, "selfclone");
        if (c == null) yield break;
        yield return Wait(1.0f);
        yield return PoseSuite(c);
        yield return RefreshClone(c, src);
    }

    private IEnumerator StepOtherLive4()
    {
        if (_r4Other == null || EntByUuid(_r4Other.Uuid) == null) { Log("R4 other-live: no other player"); _currentOutcome = "skipped"; yield break; }
        yield return PoseSuite(_r4Other);
    }

    private IEnumerator StepOtherClone4()
    {
        if (_r4Other == null || EntByUuid(_r4Other.Uuid) == null) { Log("R4 other-clone: no other player"); _currentOutcome = "skipped"; yield break; }
        var c = MakeClone(_r4Other, "clone-" + _r4Other.Tag);
        if (c == null) yield break;
        yield return Wait(1.0f);
        yield return PoseSuite(c);
        yield return RefreshClone(c, _r4Other);
    }

    private IEnumerator StepNpc4()
    {
        if (_r4Other != null && Environment.GetEnvironmentVariable("STELLAR_FREECAMPROBE_NPC") != "1") { Log("R4 npc: skipped (time budget — an other player was tested; NPC covered by run 4a)"); _currentOutcome = "skipped"; yield break; }
        var npc = NearestNpc();
        if (npc == null) { Log("R4 npc: none within 25 m"); _currentOutcome = "skipped"; yield break; }
        var playable = Lua($"local e = Z.EntityMgr:GetEntity({npc.Uuid}); if not e or not e.Model then return 'no model' end; " +
                           $"return tostring(Z.LuaBridge.GetPlayableActionIdByLua({_r4ActionId}, e.Model))");
        Log($"R4 npc {npc.Tag}: GetPlayableActionIdByLua({_r4ActionId}) -> {playable}");
        yield return PoseSuite(npc);
        var c = MakeClone(npc, "clone-" + npc.Tag);
        if (c == null) yield break;
        yield return Wait(1.0f);
        yield return Aim(c, false);
        var shot = Cap($"{Tag(c)}_present");
        Log($"R4 npc clone present: lum={(shot == null ? -1 : MeanLum(shot)):F2} actionInfo[{ActionInfo(ModelOf(c))}]");
        Release($"r4.clone.{c.Tag}");
    }

    private IEnumerator StepSummary4()
    {
        Log($"R4 SENDS since boot (end): {SendsTotal()}");
        List<(long Ms, ulong Svc, uint Method, string Path)> all;
        lock (_sendLock) all = _sends.Where(x => x.Method != 5).ToList();
        Log($"R4 SENDS timeline (non-ReqServerTime, sendClock ms): {string.Join(" ", all.Select(x => $"{x.Ms}:{x.Svc}:{x.Method}"))}");
        yield break;
    }

    private static string Tag(PoseTarget t) => t.Tag.Split('@')[0];
}
