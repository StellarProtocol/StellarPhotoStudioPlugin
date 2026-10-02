using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Panda.ZGame;
using Stellar.Abstractions.Domain;
using UnityEngine;

namespace Stellar.PhotoStudio.FreeCamProbe;

/// <summary>
/// Run 7 (2026-10-02): owner report — with the Photo Studio scene freeze on in combat, monsters freeze for a few
/// seconds and then move again (HUD: FROZEN + IN COMBAT, boss animating). Root-cause probe, evidence only.
/// Gets the TEST character into monster combat (the game's own map teleport <c>MapVM.AsyncUserTp</c> to the
/// highest unlocked field + the game's own auto-battle switch), freezes through the framework's REAL
/// <c>ISceneFreeze</c> (2.15.0 <c>GameFreezeBackend</c>) for three 10 s cycles, and every LateUpdate logs, for each
/// monster / summon / other player / self within 40 m, the attr skill-stage factor, attr anim speed, battle frame speed,
/// drawn <c>AnimComp.Speed</c>, skill id, action id, actor state, model identity and visual / logical position deltas
/// (one line per change). Run7Hooks.cs adds the setter hooks and context brackets that say WHO restored a value.
/// Hypotheses: H1 each new skill stage resets the factor / drawn speed; H2 despawn/respawn/model swap; H3 another
/// animation driver (timeline / root motion); H4 the freeze is lifted (<c>Changed(false)</c>).
/// </summary>
public sealed partial class FreeCamProbe
{
    private const float R7Range = 40f;
    private float _r7FreezeSeconds = 10f;   // run 8 sets 6 / 15 s per cycle
    private const int R7Cycles = 3;
    private const int R7MaxTracked = 30;

    // Highest-level fields first (level-60 test character: low fields die in one hit and never cast).
    private static readonly (int Scene, int Tp)[] R7Points =
    {
        (95, 50020), (95, 50945), (95, 50947), (95, 50948), (95, 50949), (95, 50731),
        (94, 50019), (94, 50941), (94, 50942), (94, 50943), (94, 50944), (94, 50946),
        (93, 50018), (93, 50925), (93, 50926), (93, 50927), (93, 50928), (93, 50929),
        (92, 50017), (91, 50016), (9, 50011), (9, 50012), (7, 50704), (7, 50707), (7, 50001),
    };

    private const int R7HomeScene = 8, R7HomeTp = 50801;   // Asterleeds — Town Gate Square

    private sealed class R7Row
    {
        public long Uuid;
        public string Kind = "";
        public string Fac = "", As = "", Bfs = "", Ds = "", Skill = "", Act = "", St = "";
        public IntPtr ModelPtr;
        public Vector3 Vis, Attr, Vis0;
        public bool Seen, Gone;
        public int Lines, FramesDs, FramesFac, FirstDsF = -1, FirstFacF = -1, SkillChanges, MovedFrames, LastMoveLogF = -999;
        public float MaxVisFromStart;
        public readonly List<string> Skills = new();
    }

    private readonly Dictionary<long, R7Row> _r7Rows = new();
    private int _r7Cycle;
    private int _r7FreezeChanges;
    private bool _r7AutoBattleSet;
    private string _r7AutoBattlePrev = "";

    private static bool R7Combat(ZEntity? e)
    {
        try { return e != null && EntityAttrExtensions.GetIsInCombat(e); } catch { return false; }
    }

    private List<Ent> R7Monsters(float range) => AllEntities(range).Where(x => x.Kind == "MonsterEnt" || x.LuaType == 1).ToList();

    private string R7Census()
    {
        var all = AllEntities(80f);
        var mons = all.Where(x => x.Kind == "MonsterEnt" || x.LuaType == 1).ToList();
        var acting = mons.Where(x => x.Dist <= R7Range).Count(x => Safe(() => EntityAttrExtensions.GetSkillId(EntByUuid(x.Uuid)!).ToString()) is var s && s != "0" && !s.StartsWith("ERR"));
        var me = SelfEntity();
        var em = Mgr();
        return $"monsters<=40m={mons.Count(x => x.Dist <= R7Range)} <=80m={mons.Count} actingNow={acting} " +
               $"nearest=[{string.Join(" ", mons.Take(4).Select(x => $"{x.Uuid}@{x.Dist:F0}m{(x.Summon ? "(summon)" : "")}{(R7Combat(EntByUuid(x.Uuid)) ? "(combat)" : "")}"))}] " +
               $"players<=40m={all.Count(x => x.Kind == "CharEnt" && x.Dist <= R7Range)} pets<=40m={all.Count(x => x.Kind == "PetEnt" && x.Dist <= R7Range)} " +
               $"MonsterDict={Safe(() => em!.MonsterDict.Count.ToString())} BossDict={Safe(() => em!.BossDict.Count.ToString())} " +
               $"selfCombat={R7Combat(me)} selfPos={Safe(() => V(LiveModel(me)!.GetAttrGoPosition()))} scene={_services.ClientState.Phase}";
    }

    // ---- steps --------------------------------------------------------------------------------------------------

    private IEnumerator StepSetup7()
    {
        Log($"R7 setup: self={_selfUuid} census {R7Census()}");
        try { R7StaticMap(); } catch (Exception ex) { Log($"R7 MAP FAILED {ex.GetType().Name}: {Short(ex.Message)}"); }
        _services.SceneFreeze.Changed += R7OnFreezeChanged;
        Arm("r7.changed", () => _services.SceneFreeze.Changed -= R7OnFreezeChanged);
        if (R7Monsters(80f).Count > 0) { Log("R7 monsters already within 80 m: no teleport"); yield break; }
        yield return R7TeleportToField(0);
    }

    private IEnumerator R7TeleportToField(int skip)
    {
        var ids = string.Join(",", R7Points.Select(p => p.Tp));
        var unlocked = Lua($"local vm=Z.VMMgr.GetVM('map'); local o={{}}; for _,id in ipairs({{{ids}}}) do " +
                           "if vm.CheckTransferPointUnlock(id) then o[#o+1]=tostring(id) end end; return table.concat(o,',')");
        Log($"R7 TP unlocked field points: {unlocked}");
        if (!unlocked.StartsWith("ok ")) yield break;
        var set = unlocked.Substring(3).Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToHashSet();
        var pick = R7Points.Where(p => set.Contains(p.Tp)).Skip(skip).FirstOrDefault();
        if (pick.Tp == 0) { Log("R7 TP no unlocked field point left"); yield break; }
        yield return R7Teleport(pick.Scene, pick.Tp, "field");
    }

    private IEnumerator R7Teleport(int scene, int tp, string why)
    {
        var epoch = _sceneEpoch;
        var r = Lua($"Z.VMMgr.GetVM('map').AsyncUserTp({scene},{tp}); return 'sent'");
        Log($"R7 TP {why} -> scene {scene} point {tp}: {r}");
        var until = Time.realtimeSinceStartup + 45f;
        while (epoch == _sceneEpoch && Time.realtimeSinceStartup < until) yield return Wait(0.5f);
        if (epoch == _sceneEpoch) { Log($"R7 TP {why}: no scene change within 45 s"); yield break; }
        while (_services.ClientState.Phase != GamePhase.World && Time.realtimeSinceStartup < until + 30f) yield return Wait(0.5f);
        yield return Wait(8f);   // settle (docs/il2cpp-probing-safety.md § 2)
        Log($"R7 TP {why} arrived phase={_services.ClientState.Phase} census {R7Census()}");
    }

    private IEnumerator StepCombat7()
    {
        var epoch = _sceneEpoch;
        yield return R7WalkToMonster(epoch);
        // Auto-battle first (the AI walks to targets itself); hop to the next field point only when nothing comes within
        // 40 m in 30 s and nothing is within 80 m either.
        R7AutoBattle(true);
        var start = Time.realtimeSinceStartup;
        var engaged = false;
        var hops = 0;
        var hopAt = start;
        while (Time.realtimeSinceStartup - start < 60f)
        {
            yield return Wait(5f);
            if (Aborted(epoch)) { yield return Wait(2f); epoch = _sceneEpoch; continue; }
            Log($"R7 wait-combat +{Time.realtimeSinceStartup - start:F0}s {R7Census()}");
            if (R7Combat(SelfEntity()) && R7Monsters(R7Range).Count > 0 && Time.realtimeSinceStartup - hopAt > 8f) { engaged = true; break; }
            if (hops < 2 && Time.realtimeSinceStartup - hopAt > 30f && R7Monsters(R7Range).Count == 0 && R7Monsters(80f).Count == 0)
            {
                hops++;
                Log($"R7 nothing within 80 m after 30 s; hop #{hops}");
                yield return R7TeleportToField(hops);
                epoch = _sceneEpoch;
                hopAt = Time.realtimeSinceStartup;
                yield return R7WalkToMonster(epoch);
                R7AutoBattle(true);
            }
        }
        Log($"R7 combat {(engaged ? "ENGAGED" : "NOT ENGAGED (freezing whatever is near anyway)")} after {Time.realtimeSinceStartup - start:F0}s");
        if (Aborted(epoch)) { Log("R7 aborted: scene changed while waiting"); yield break; }
        // Cycle 1 is pure observation (no hooks) so its data is banked even if a hook misbehaves (run 7c hung one frame
        // after the hooks went live); hooks arm for cycles 2 and 3.
        for (_r7Cycle = 1; _r7Cycle <= R7Cycles && !Aborted(epoch); _r7Cycle++)
        {
            if (_r7Cycle == 2) R7ArmHooks();
            Log($"R7C{_r7Cycle} hooks={(_r7Harmony != null ? "armed" : "off")}");
            yield return R7FreezeCycle(epoch);
            yield return Wait(6f);
        }
        R7LogHits("all cycles");
        Release("r7.hooks");
    }

    /// <summary>Run 7c: auto-battle does not walk to monsters 60+ m away. Uses the game's own map path-finding
    /// (<c>ZPathFindingMgr.SetPathFindingTarget(Point…)</c> + <c>StartPathFinding</c>, what the map's "path finding" button
    /// does) to the nearest monster, bounded 35 s, stopped within 12 m.</summary>
    private IEnumerator R7WalkToMonster(int epoch)
    {
        var target = R7Monsters(150f).FirstOrDefault();
        if (target == null) { Log("R7 WALK no monster within 150 m"); yield break; }
        if (target.Dist <= 15f) { Log($"R7 WALK nearest monster already at {target.Dist:F0} m"); yield break; }
        var pos = LiveModel(EntByUuid(target.Uuid))?.GetAttrGoPosition();
        if (pos == null) yield break;
        var p = pos.Value;
        string Go() => Lua($"local p=Vector3.New({p.x:F2},{p.y:F2},{p.z:F2}); local en=Z.ZPathFindingMgr:LuaCheckEnable(); " +
                           "Z.ZPathFindingMgr:SetPathFindingTarget(Z.GoalPosType.Point, Z.StageMgr.GetCurrentSceneId(), 0, p); " +
                           "Z.ZPathFindingMgr:StartPathFinding(); return 'enable='..tostring(en)..' stage='..tostring(Z.ZPathFindingMgr.CurStage)");
        Log($"R7 WALK to monster {target.Uuid} at {V(p)} d={target.Dist:F0}m: {Go()}");
        var start = Time.realtimeSinceStartup;
        var self0 = LiveModel(SelfEntity())?.GetAttrGoPosition() ?? Vector3.zero;
        var mounted = false;
        while (Time.realtimeSinceStartup - start < 35f && !Aborted(epoch))
        {
            yield return Wait(1f);
            var me = LiveModel(SelfEntity())?.GetAttrGoPosition() ?? Vector3.zero;
            var d = Vector3.Distance(me, p);
            if (d <= 12f || R7Monsters(15f).Count > 0) { Log($"R7 WALK arrived d={d:F1}m after {Time.realtimeSinceStartup - start:F0}s"); break; }
            if (!mounted && Time.realtimeSinceStartup - start > 4f && Vector3.Distance(me, self0) < 1f)
            {
                mounted = true;   // the VM mounts first when not riding (path_finding_vm.lua) — do the same, then start again
                Log($"R7 WALK not moving; mount: {Lua("Z.VMMgr.GetVM('gotofunc').GoToFunc(E.FunctionID.VehicleRide); return 'ok'")}");
                yield return Wait(3f);
                Log($"R7 WALK restart: {Go()}");
            }
        }
        Log($"R7 WALK stop: {Lua("Z.ZPathFindingMgr:StopPathFinding(false); return 'stopped'")} census {R7Census()}");
    }

    private IEnumerator StepReturn7()
    {
        R7AutoBattle(false);
        yield return Wait(1f);
        var unlocked = Lua($"return tostring(Z.VMMgr.GetVM('map').CheckTransferPointUnlock({R7HomeTp}))");
        if (unlocked != "ok true") { Log($"R7 home point not unlocked ({unlocked}); staying"); yield break; }
        yield return R7Teleport(R7HomeScene, R7HomeTp, "home");
    }

    private IEnumerator StepSummary7()
    {
        Log($"R7 summary: freezeChangedEvents={_r7FreezeChanges} rows={_r7Rows.Count}");
        R7LogHits("final");
        yield break;
    }

    private void R7AutoBattle(bool on)
    {
        if (on)
        {
            var r = Lua("local s=Z.VMMgr.GetVM('setting'); local prev=tostring(s.Get(E.SettingID.AutoBattle)); " +
                        "local can=tostring(Z.VMMgr.GetVM('profession'):CheckProfessionCanAutoBattle()); " +
                        "s.Set(E.SettingID.AutoBattle, true); Z.EntityMgr.PlayerEnt:SetLuaAttr(Z.LocalAttr.EAutoBattleSwitch, true); " +
                        "Z.EventMgr:Dispatch(Z.ConstValue.AutoBattleChange, true); " +
                        "return 'prev='..prev..' can='..can..' now='..tostring(Z.EntityMgr.PlayerEnt:GetLuaAttr(Z.LocalAttr.EAutoBattleSwitch).Value)");
            if (!_r7AutoBattleSet) _r7AutoBattlePrev = r.Contains("prev=true") ? "true" : "false";   // first call only
            _r7AutoBattleSet = true;
            Log($"R7 AUTOBATTLE on: {r}");
            Arm("r7.autobattle", () => R7AutoBattle(false));
            return;
        }
        if (!_r7AutoBattleSet) return;
        _r7AutoBattleSet = false;
        var off = Lua("Z.EntityMgr.PlayerEnt:SetLuaAttr(Z.LocalAttr.EAutoBattleSwitch, false); Panda.ZGame.ZBattleUtils.StopPlayerAIBattle(); " +
                      $"Z.VMMgr.GetVM('setting').Set(E.SettingID.AutoBattle, {_r7AutoBattlePrev}); Z.EventMgr:Dispatch(Z.ConstValue.AutoBattleChange, {_r7AutoBattlePrev}); " +
                      "return 'switch='..tostring(Z.EntityMgr.PlayerEnt:GetLuaAttr(Z.LocalAttr.EAutoBattleSwitch).Value)");
        Log($"R7 AUTOBATTLE off (setting restored to {_r7AutoBattlePrev}): {off}");
    }

    private void R7OnFreezeChanged(bool frozen)
    {
        _r7FreezeChanges++;
        Log($"R7 ISceneFreeze.Changed({frozen}) phase={_r7Phase} f{R7F()} isFrozen={_services.SceneFreeze.IsFrozen} holds={_services.SceneFreeze.HoldsPositions}");
    }

    // ---- one freeze cycle ---------------------------------------------------------------------------------------

    private IEnumerator R7FreezeCycle(int epoch)
    {
        var c = _r7Cycle;
        _r7Rows.Clear();
        lock (_r7Lock) _r7LogCap.Clear();
        R7RefreshTracked();
        Log($"R7C{c} tracked {_r7Rows.Count}: [{string.Join(" ", _r7Rows.Values.Select(r => $"{r.Kind}:{r.Uuid}"))}] census {R7Census()}");
        var subject = R7Subject();
        Shot? u1 = null, u2 = null;
        var region = subject == 0 ? null : EntRegion(subject, 0.10f, 0.20f);
        // Unfrozen baseline (3 s): hooks count, sampler on (lines tagged base).
        _r7Phase = "base";
        _r7Frame0 = Time.frameCount;
        ProbeTicks.LateTick = R7Sample;
        Arm("r7.sampler", () => { ProbeTicks.LateTick = null; _r7Phase = "off"; });
        Log($"R7C{c} base phase on (hooks live, sampler on) subject={subject} region={(region == null ? "none" : region.ToString())}");
        if (region is { } ur) { u1 = Capture($"R7C{c}_U_t1", ur); yield return Wait(0.6f); u2 = Capture($"R7C{c}_U_t2", ur); }
        yield return Wait(2.4f);
        // Freeze through the framework's real ISceneFreeze.
        foreach (var r in _r7Rows.Values) R7ResetStats(r);
        _r7Phase = "frozen";
        _r7Frame0 = Time.frameCount;
        IDisposable? token = null;
        try { token = _services.SceneFreeze.Freeze(); }
        catch (Exception ex) { Log($"R7C{c} Freeze() THREW {ex.GetType().Name}: {Short(ex.Message)}"); }
        Arm("r7.freeze", () => token?.Dispose());
        Log($"R7C{c} FREEZE applied f0 t={Time.realtimeSinceStartup:F2} isFrozen={_services.SceneFreeze.IsFrozen} holds={_services.SceneFreeze.HoldsPositions} subject={subject}");
        Shot? f1 = null, f2 = null, f3 = null, f4 = null;
        var t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < _r7FreezeSeconds && !Aborted(epoch))
        {
            var el = Time.realtimeSinceStartup - t0;
            var fr = subject == 0 ? null : EntRegion(subject, 0.10f, 0.20f);
            if (fr is { } r1)
            {
                if (f1 == null && el > 1.0f) f1 = Capture($"R7C{c}_F_t1", r1);
                else if (f1 != null && f2 == null && el > 1.6f) f2 = Capture($"R7C{c}_F_t2", r1);
                else if (f2 != null && f3 == null && el > 6.0f) f3 = Capture($"R7C{c}_F_t3", r1);
                else if (f3 != null && f4 == null && el > 6.6f) f4 = Capture($"R7C{c}_F_t4", r1);
            }
            if (Time.frameCount % 30 == 0) R7RefreshTracked();   // monsters that come into range mid-freeze
            if (_r8Inject && el > 1.0f) R8InjectNow();            // run 8: gate check (Run8.cs)
            yield return null;
        }
        Log($"R7C{c} frozen window done f{R7F()} isFrozen={_services.SceneFreeze.IsFrozen}; pixel subject={subject} " +
            $"unfrozen={Diff(u1, u2).ChangedPct:F1}% frozen+1.0/1.6s={Diff(f1, f2).ChangedPct:F1}% frozen+6.0/6.6s={Diff(f3, f4).ChangedPct:F1}%");
        R7LogCycleSummary(c);
        R7LogHits($"C{c} frozen");
        Release("r7.freeze");
        _r7Phase = "post";
        yield return Wait(1.5f);
        Release("r7.sampler");
        Log($"R7C{c} released; census {R7Census()}");
    }

    private long R7Subject()
    {
        // Nearest monster currently casting, else the nearest monster, else the nearest anything tracked.
        var mons = _r7Rows.Values.Where(r => r.Kind == "MonsterEnt").ToList();
        var casting = mons.FirstOrDefault(r => r.Skill != "0" && r.Skill != "" && !r.Skill.StartsWith("ERR"));
        return (casting ?? mons.FirstOrDefault() ?? _r7Rows.Values.FirstOrDefault(r => !r.Kind.EndsWith("(self)")))?.Uuid ?? 0;
    }

    private void R7RefreshTracked()
    {
        var near = AllEntities(80f).Where(x => x.Kind is "MonsterEnt" or "PetEnt" or "CharEnt" or "VehicleEnt" or "NpcEnt" || x.LuaType == 1)
            .OrderBy(x => x.Kind == "MonsterEnt" ? 0 : x.Kind == "PetEnt" ? 1 : x.Kind == "CharEnt" ? 2 : 3).ThenBy(x => x.Dist).ToList();
        foreach (var x in near)
        {
            if (_r7Rows.Count >= R7MaxTracked) break;
            if (_r7Rows.ContainsKey(x.Uuid)) continue;
            _r7Rows[x.Uuid] = new R7Row { Uuid = x.Uuid, Kind = x.Kind + (x.Summon ? "(summon)" : "") };
            lock (_r7Lock) _r7Tracked.Add(x.Uuid);
            if (_r7Phase == "frozen") Log($"R7C{_r7Cycle} f{R7F()} NEW-IN-RANGE {x.Kind}:{x.Uuid} d={x.Dist:F1}m (appeared or walked in mid-freeze)");
        }
        if (_selfUuid != 0 && !_r7Rows.ContainsKey(_selfUuid))
        {
            _r7Rows[_selfUuid] = new R7Row { Uuid = _selfUuid, Kind = "CharEnt(self)" };
            lock (_r7Lock) _r7Tracked.Add(_selfUuid);
        }
    }

    private static void R7ResetStats(R7Row r)
    {
        r.Lines = 0; r.FramesDs = 0; r.FramesFac = 0; r.FirstDsF = -1; r.FirstFacF = -1; r.SkillChanges = 0; r.MovedFrames = 0;
        r.MaxVisFromStart = 0f; r.Vis0 = r.Vis; r.Skills.Clear(); r.LastMoveLogF = -999;
    }

    /// <summary>LateUpdate: one read pass over the tracked rows; a line whenever a value changes (or every 15 frames while moving).</summary>
    private void R7Sample()
    {
        _r7Ctx.Clear();   // brackets are balanced inside a frame; a throw in a bracketed method must not leak context
        var f = R7F();
        foreach (var r in _r7Rows.Values)
        {
            if (r.Gone) continue;
            var e = EntByUuid(r.Uuid);
            var m = LiveModel(e);
            if (e == null || m == null)
            {
                r.Gone = true;
                Log($"R7S c{_r7Cycle} {_r7Phase} f{f} {r.Kind}:{r.Uuid} GONE (entity={(e == null ? "null" : "live")} model={(m == null ? "null" : "live")})");
                continue;
            }
            var fac = Safe(() => EntityAttrExtensions.GetAttrSkillStageTimeFactor(e).ToString("F2"));
            var asp = Safe(() => EntityAttrExtensions.GetAttrAnimSpeed(e).ToString("F2"));
            var bfs = Safe(() => EntityAttrExtensions.GetAttrBattleFrameSpeed(e).ToString("F2"));
            var ds = Safe(() => m.AnimComp?.Speed.ToString("F2") ?? "null");
            var sk = Safe(() => EntityAttrExtensions.GetSkillId(e).ToString());
            var act = Safe(() => EntityAttrExtensions.GetAttrActionInfoActionId(m).ToString());
            var st = Safe(() => ((int)EntityAttrExtensions.GetAttrState(e)).ToString());
            var ptr = m.Pointer;
            Vector3 vis = Vector3.zero, attr = Vector3.zero;
            try { vis = m.ModelGoComp?.Position ?? Vector3.zero; attr = m.GetAttrGoPosition(); } catch { }
            if (!r.Seen) { r.Seen = true; r.Vis = vis; r.Attr = attr; r.Vis0 = vis; }
            var dVis = Vector3.Distance(vis, r.Vis);
            var dAttr = Vector3.Distance(attr, r.Attr);
            var changed = fac != r.Fac || asp != r.As || bfs != r.Bfs || ds != r.Ds || sk != r.Skill || act != r.Act || st != r.St || ptr != r.ModelPtr;
            if (_r7Phase == "frozen")
            {
                if (ds != "0.00" && ds != "null" && !ds.StartsWith("ERR")) { r.FramesDs++; if (r.FirstDsF < 0) r.FirstDsF = f; }
                if (fac != "0.00" && !fac.StartsWith("ERR")) { r.FramesFac++; if (r.FirstFacF < 0) r.FirstFacF = f; }
                if (sk != r.Skill && r.Skill != "") { r.SkillChanges++; if (r.Skills.Count < 12) r.Skills.Add($"{sk}@f{f}"); }
                if (dVis > 0.01f) r.MovedFrames++;
                r.MaxVisFromStart = Math.Max(r.MaxVisFromStart, Vector3.Distance(vis, r.Vis0));
            }
            var moving = dVis > 0.01f && f - r.LastMoveLogF >= 15;
            if ((changed || moving) && r.Lines < 160)
            {
                r.Lines++;
                if (moving) r.LastMoveLogF = f;
                Log($"R7S c{_r7Cycle} {_r7Phase} f{f} {r.Kind}:{r.Uuid} fac={fac} animSpd={asp} bfs={bfs} drawn={ds} skill={sk} act={act} st={st}" +
                    $"{(ptr != r.ModelPtr && r.ModelPtr != IntPtr.Zero ? " MODEL-SWAP" : "")} dVis={dVis:F3} dAttr={dAttr:F3}{(changed ? "" : " (moving)")}");
            }
            r.Fac = fac; r.As = asp; r.Bfs = bfs; r.Ds = ds; r.Skill = sk; r.Act = act; r.St = st; r.ModelPtr = ptr; r.Vis = vis; r.Attr = attr;
        }
    }

    private void R7LogCycleSummary(int c)
    {
        var frames = R7F();
        foreach (var r in _r7Rows.Values.OrderBy(r => r.Kind))
            Log($"R7SUM c{c} {r.Kind}:{r.Uuid} frames={frames} gone={r.Gone} drawn>0 frames={r.FramesDs} first@f{r.FirstDsF} | fac>0 frames={r.FramesFac} first@f{r.FirstFacF} " +
                $"| skillChanges={r.SkillChanges} [{string.Join(" ", r.Skills)}] | visMovedFrames={r.MovedFrames} maxVisFromFreeze={r.MaxVisFromStart:F2}m | now fac={r.Fac} drawn={r.Ds} skill={r.Skill}");
    }
}
