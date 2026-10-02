using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using HarmonyLib;
using Il2CppInterop.Common;
using Panda.ZAnim;
using Panda.ZGame;
using UnityEngine;

namespace Stellar.PhotoStudio.FreeCamProbe;

/// <summary>Run 13 (2026-10-03): verify the time-pause review fixes (framework feat/posing 4dff1f6+). PROBE ONLY.
/// STELLAR_R13 = comma list of: vec2 (qa M-11: a by-value 8-byte Vector2 through the patched entry point, byte-for-byte),
/// shield (one EGm source: pause bits masked, camera bits open), stall (the watchdog's beat removed → a full release),
/// gone (the paused-frame driver destroyed → a full release), tp (teleport while frozen → full release on the leave
/// prefix), tpdog (teleport while frozen with the framework's own leave / SceneChanged release handlers removed → only the
/// watchdog outside the world can release). Framework internals are read and poked by reflection.</summary>
public sealed partial class FreeCamProbe
{
    private static string R13Modes => Environment.GetEnvironmentVariable("STELLAR_R13") is { Length: > 0 } m ? m : "";
    private static bool R13On(string mode) => R13Modes.Split(',').Contains(mode);
    private const BindingFlags R13Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void R13PlayBase(nint self, int state, float nt, long param, float toff, float fade, byte stay, byte frozen, nint mi);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate float R13Dot(long a, long b, nint mi);

    private static long R13Pack(Vector2 v) => (uint)BitConverter.SingleToInt32Bits(v.x) | ((long)BitConverter.SingleToInt32Bits(v.y) << 32);
    private static string R13Bits(Vector2 v) => $"{BitConverter.SingleToInt32Bits(v.x):X8}/{BitConverter.SingleToInt32Bits(v.y):X8}";

    // ---- framework internals ----

    private static object? R13Fw()
    {
        foreach (var info in BepInEx.Unity.IL2CPP.IL2CPPChainloader.Instance.Plugins.Values)
            if (info.Instance?.GetType().FullName == "Stellar.Host.BootstrapPlugin") return info.Instance;
        return null;
    }

    private static FieldInfo? R13Field(Type? t, string name)
    {
        for (; t != null; t = t.BaseType)
            if (t.GetField(name, R13Any) is { } f) return f;
        return null;
    }

    private static object? R13Get(object? o, string name) => o is null ? null : R13Field(o.GetType(), name)?.GetValue(o);

    private static void R13Set(object o, string name, object? value) => R13Field(o.GetType(), name)!.SetValue(o, value);

    private static object? R13Prop(object? o, string name) => o?.GetType().GetProperty(name, R13Any)?.GetValue(o);

    /// <summary>The freeze's whole state: service, clock, anim gate, hold, input block, the game's own mask.</summary>
    private string R13State()
    {
        var fw = R13Fw();
        var backend = R13Get(fw, "_freezeBackend");
        var gate = R13Get(backend, "_anim");
        var shield = R13Get(fw, "_inputShield");
        return $"isFrozen={_services.SceneFreeze.IsFrozen} timeScale={Time.timeScale:F3} backendFrozen={R13Get(backend, "_frozen")} " +
               $"gateArmed={R13Prop(gate, "Armed")} gateTracked={R13Prop(gate, "Tracked")} gateKept={R13Prop(gate, "Kept")} holding={R13Get(backend, "_holding")} " +
               $"pauseBlock={R13Prop(shield, "PauseBlocked")} camShield={R13Prop(shield, "IsShielded")} cam={_services.CameraOverride.IsOverridden} " +
               $"input[{R13Input()}]";
    }

    private string R13Input()
    {
        try
        {
            var m = ZIgnoreMgr.Instance;
            return $"Move={m.IsInputIgnore(EInputMask.Move)} Skill={m.IsInputIgnore(EInputMask.Skill)} Rotation={m.IsInputIgnore(EInputMask.Rotation)} " +
                   $"Zoom={m.IsInputIgnore(EInputMask.Zoom)} gmMove={m.IsInputIgnoreBySource(EInputMask.Move, EIgnoreMaskSource.EGm)} " +
                   $"payMove={m.IsInputIgnoreBySource(EInputMask.Move, EIgnoreMaskSource.EPayWebView)}";
        }
        catch (Exception ex) { return "err " + ex.GetType().Name; }
    }

    private bool R13Released() =>
        !_services.SceneFreeze.IsFrozen && Time.timeScale > 0f &&
        R13Prop(R13Get(R13Get(R13Fw(), "_freezeBackend"), "_anim"), "Armed") is false &&
        R13Get(R13Get(R13Fw(), "_freezeBackend"), "_holding") is false &&
        R13Prop(R13Get(R13Fw(), "_inputShield"), "PauseBlocked") is false;

    private IEnumerator R13AwaitRelease(string label, float timeout)
    {
        var t0 = Time.realtimeSinceStartup;
        while (_services.SceneFreeze.IsFrozen && Time.realtimeSinceStartup - t0 < timeout) yield return Wait(0.25f);
        yield return Frames(3);
        var ok = R13Released();
        Log($"R13 {label} RESULT {(ok ? "PASS" : "FAIL")} fullRelease={ok} after={Time.realtimeSinceStartup - t0:F1}s {R13State()}");
    }

    // ---- steps ----

    private IEnumerator StepRun13()
    {
        Log($"R13 modes={R13Modes} fw={(R13Fw() != null ? "found" : "MISSING")} census {R7Census()}");
        if (R13On("vec2")) yield return R13Vec2();
        if (R13On("shield")) yield return R13Shield();
        if (R13On("stall")) yield return R13Stall();
        if (R13On("gone")) yield return R13Gone();
        if (R13On("tp")) yield return R13Teleport(dogOnly: false);
        if (R13On("tpdog")) yield return R13Teleport(dogOnly: true);
        if (R13On("tppre")) yield return R13TeleportInFlight();
        if (R13On("tpleave")) yield return R13FreezeOnLeave();
        Log($"R13 END {R13State()}");
    }

    private IEnumerator R13Shield()
    {
        Log($"R13 shield before {R13State()}");
        var token = _services.SceneFreeze.Freeze();
        Arm("r13.freeze", () => token.Dispose());
        yield return Wait(1f);
        var paused = R13Input();
        Log($"R13 shield frozen {R13State()}");
        var cam = _services.CameraOverride.TryAcquire(out var control);
        yield return Wait(0.5f);
        Log($"R13 shield frozen+freecam acquired={cam} {R13State()}");
        control?.Dispose();
        yield return Wait(0.5f);
        var afterExit = R13Input();
        Log($"R13 shield frozen after freecam exit {R13State()}");
        Release("r13.freeze");
        yield return Wait(0.5f);
        var after = R13Input();
        var pass = paused.Contains("Move=True") && paused.Contains("Rotation=False") && paused.Contains("gmMove=True") && paused.Contains("payMove=False") &&
                   afterExit.Contains("Move=True") && afterExit.Contains("Rotation=False") && after.Contains("Move=False") && after.Contains("Rotation=False");
        Log($"R13 shield RESULT {(pass ? "PASS" : "FAIL")} paused[{paused}] afterExit[{afterExit}] after[{after}]");
    }

    private IEnumerator R13Stall()
    {
        var fw = R13Fw();
        if (fw == null) { Log("R13 stall: framework instance not found"); yield break; }
        var token = _services.SceneFreeze.Freeze();
        Arm("r13.freeze", () => token.Dispose());
        yield return Wait(2f);
        Log($"R13 stall frozen {R13State()}");
        var dog = R13Get(fw, "_pauseWatchdog");
        R13Set(fw, "_pauseWatchdog", null);   // the global-rate watchdog tick no longer beats; the paused-frame check still runs
        Arm("r13.dog", () => R13Set(fw, "_pauseWatchdog", dog));
        Log("R13 stall: watchdog beat removed (framework field _pauseWatchdog = null)");
        yield return R13AwaitRelease("stall", 30f);
        Release("r13.dog");
        Release("r13.freeze");
        Log($"R13 stall restored watchdog={R13Get(fw, "_pauseWatchdog") != null}");
        yield return R13Refreeze("after-stall");
    }

    private IEnumerator R13Gone()
    {
        var fw = R13Fw();
        if (fw == null) { Log("R13 gone: framework instance not found"); yield break; }
        var token = _services.SceneFreeze.Freeze();
        Arm("r13.freeze", () => token.Dispose());
        yield return Wait(2f);
        var driver = R13Get(R13Get(fw, "_tickHost"), "_paused") as UnityEngine.Object;
        Log($"R13 gone frozen driver={(driver != null ? driver.GetType().Name : "null")} {R13State()}");
        if (driver != null) UnityEngine.Object.Destroy(driver);
        yield return R13AwaitRelease("gone", 10f);
        Release("r13.freeze");
        yield return R13Refreeze("after-gone");
    }

    // A fresh freeze after a forced release must pause again and tick the framework (a new paused-frame driver).
    private IEnumerator R13Refreeze(string label)
    {
        var ticks0 = _r13Ticks;
        var token = _services.SceneFreeze.Freeze();
        Arm("r13.refreeze", () => token.Dispose());
        yield return Wait(2f);
        var driver = R13Get(R13Get(R13Fw(), "_tickHost"), "_paused") as Behaviour;
        var ticks = _r13Ticks - ticks0;
        var ok = _services.SceneFreeze.IsFrozen && Time.timeScale == 0f && ticks > 10 && driver != null && driver.enabled;
        Log($"R13 {label} refreeze RESULT {(ok ? "PASS" : "FAIL")} fwTicks+{ticks} in 2 s driverEnabled={driver?.enabled} {R13State()}");
        Release("r13.refreeze");
        yield return Wait(1f);
        Log($"R13 {label} refreeze released {R13State()}");
    }

    private int _r13Ticks;

    // A user teleport (map VM AsyncUserTp) does not progress while the clock is stopped (measured run 13a: no scene change in
    // 45 s, twice), so the zone change is requested through the bridge call it wraps (ClientReqSwitchSceneByTransfer: the
    // request goes straight to the server, whose scene switch arrives while paused).
    private IEnumerator R13Teleport(bool dogOnly)
    {
        var label = dogOnly ? "tpdog" : "tp";
        var fw = R13Fw();
        if (fw == null) { Log($"R13 {label}: framework instance not found"); yield break; }
        var home = Lua("return tostring(Z.StageMgr.GetCurrentSceneId())").Contains("ok 8");
        var (scene, tp) = home ? R13FieldPoint() : (R7HomeScene, R7HomeTp);
        if (tp == 0) { Log($"R13 {label}: no unlocked field point"); yield break; }
        var token = _services.SceneFreeze.Freeze();
        Arm("r13.freeze", () => token.Dispose());
        yield return Wait(2f);
        Log($"R13 {label} frozen home={home} {R13State()}");
        if (dogOnly) R13DetachReleases(fw);
        var epoch = _sceneEpoch;
        Log($"R13 {label} bridge -> scene {scene} point {tp}: {Lua($"Z.LuaBridge.ClientReqSwitchSceneByTransfer({scene}, {tp}); return 'sent'")}");
        yield return R13AwaitScene(epoch, label);
        if (dogOnly) Release("r13.handlers");
        var moved = epoch != _sceneEpoch;
        var ok = moved && R13Released();
        Log($"R13 {label} RESULT {(ok ? "PASS" : "FAIL")} sceneChanged={moved} watchdogReleases={R13Prop(R13Get(fw, "_pauseWatchdog"), "Releases")} {R13State()}");
        Release("r13.freeze");
        if (!moved)
        {
            yield return R13AwaitScene(epoch, label + "-after-unfreeze");   // the request was only held: it proceeds now
            if (epoch == _sceneEpoch) yield break;
        }
        while (_services.ClientState.Phase != Stellar.Abstractions.Domain.GamePhase.World) yield return Wait(0.5f);
        yield return Wait(8f);
        Log($"R13 {label} arrived {R13State()}");
    }

    // "Left the world while frozen must release" (qa I-1): a freeze that is still held when the game leaves the scene. The
    // freeze is pressed from the scene-LEAVE notification itself (SceneChanged(null), after the framework's own release ran)
    // with the framework's leave / SceneChanged release handlers detached, so only the watchdog — ticking outside the world
    // gate — can end it; the zone load must then complete normally.
    private IEnumerator R13FreezeOnLeave()
    {
        var fw = R13Fw();
        if (fw == null) { Log("R13 tpleave: framework instance not found"); yield break; }
        var home = Lua("return tostring(Z.StageMgr.GetCurrentSceneId())").Contains("ok 8");
        var (scene, tp) = home ? R13FieldPoint() : (R7HomeScene, R7HomeTp);
        if (tp == 0) { Log("R13 tpleave: no unlocked field point"); yield break; }
        IDisposable? token = null;
        float pressedAt = -1f, releasedAt = -1f;
        Action<string?> onScene = name =>
        {
            if (name != null || token != null) return;
            token = _services.SceneFreeze.Freeze();
            pressedAt = Time.realtimeSinceStartup;
            Log($"R13 tpleave FREEZE pressed on scene leave worldActive={_services.ClientState.IsWorldActive} {R13State()}");
        };
        _services.ClientState.SceneChanged += onScene;
        Arm("r13.leave", () => { _services.ClientState.SceneChanged -= onScene; token?.Dispose(); });
        R13DetachReleases(fw);
        var epoch = _sceneEpoch;
        Log($"R13 tpleave AsyncUserTp -> scene {scene} point {tp}: {Lua($"Z.VMMgr.GetVM('map').AsyncUserTp({scene},{tp}); return 'sent'")}");
        var t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < 60f)
        {
            if (pressedAt >= 0f && releasedAt < 0f && !_services.SceneFreeze.IsFrozen)
            {
                releasedAt = Time.realtimeSinceStartup;
                Log($"R13 tpleave freeze released +{releasedAt - pressedAt:F2}s after the press worldActive={_services.ClientState.IsWorldActive} " +
                    $"watchdogReleases={R13Prop(R13Get(fw, "_pauseWatchdog"), "Releases")} {R13State()}");
            }
            if (releasedAt >= 0f && _services.ClientState.Phase == Stellar.Abstractions.Domain.GamePhase.World && _services.ClientState.IsWorldActive) break;
            yield return null;
        }
        Release("r13.handlers");
        yield return Wait(8f);
        var ok = pressedAt >= 0f && releasedAt >= 0f && epoch != _sceneEpoch && R13Released() && _services.ClientState.IsWorldActive;
        Log($"R13 tpleave RESULT {(ok ? "PASS" : "FAIL")} pressed={pressedAt >= 0f} released={releasedAt >= 0f} sceneChanged={epoch != _sceneEpoch} " +
            $"inWorld={_services.ClientState.IsWorldActive} {R13State()}");
        Release("r13.leave");
    }

    // The other order: the user teleport is requested first and the freeze pressed right after, before the scene switch.
    private IEnumerator R13TeleportInFlight()
    {
        var home = Lua("return tostring(Z.StageMgr.GetCurrentSceneId())").Contains("ok 8");
        var (scene, tp) = home ? R13FieldPoint() : (R7HomeScene, R7HomeTp);
        if (tp == 0) { Log("R13 tppre: no unlocked field point"); yield break; }
        var epoch = _sceneEpoch;
        Log($"R13 tppre AsyncUserTp -> scene {scene} point {tp}: {Lua($"Z.VMMgr.GetVM('map').AsyncUserTp({scene},{tp}); return 'sent'")}");
        yield return Frames(2);
        var token = _services.SceneFreeze.Freeze();
        Arm("r13.freeze", () => token.Dispose());
        Log($"R13 tppre frozen in flight {R13State()}");
        yield return R13AwaitScene(epoch, "tppre");
        var moved = epoch != _sceneEpoch;
        var ok = moved && R13Released();
        Log($"R13 tppre RESULT {(ok ? "PASS" : "FAIL")} sceneChanged={moved} {R13State()}");
        Release("r13.freeze");
        if (!moved) yield break;
        while (_services.ClientState.Phase != Stellar.Abstractions.Domain.GamePhase.World) yield return Wait(0.5f);
        yield return Wait(8f);
        Log($"R13 tppre arrived {R13State()}");
    }

    private IEnumerator R13AwaitScene(int epoch, string label)
    {
        var t0 = Time.realtimeSinceStartup;
        var released = -1f;
        while (epoch == _sceneEpoch && Time.realtimeSinceStartup - t0 < 45f)
        {
            if (released < 0f && !_services.SceneFreeze.IsFrozen) { released = Time.realtimeSinceStartup - t0; Log($"R13 {label} freeze released at +{released:F1}s {R13State()}"); }
            yield return null;
        }
        Log($"R13 {label} scene {(epoch != _sceneEpoch ? "changed" : "NOT changed")} at +{Time.realtimeSinceStartup - t0:F1}s releasedAt={released:F1}");
    }

    private (int Scene, int Tp) R13FieldPoint()
    {
        var ids = string.Join(",", R7Points.Select(p => p.Tp));
        var unlocked = Lua($"local vm=Z.VMMgr.GetVM('map'); local o={{}}; for _,id in ipairs({{{ids}}}) do " +
                           "if vm.CheckTransferPointUnlock(id) then o[#o+1]=tostring(id) end end; return table.concat(o,',')");
        if (!unlocked.StartsWith("ok ")) return (0, 0);
        var set = unlocked.Substring(3).Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToHashSet();
        var pick = R7Points.FirstOrDefault(p => set.Contains(p.Tp));
        return (pick.Scene, pick.Tp);
    }

    // Removes the framework's own free-camera release handlers on the scene-leave prefix and SceneChanged (restored after):
    // with them gone only the watchdog's "paused outside the world" check can end the freeze.
    private void R13DetachReleases(object fw)
    {
        var removed = new List<(object Owner, string Field, Delegate Del)>();
        void Detach(object? owner, string field)
        {
            if (owner is null || R13Field(owner.GetType(), field) is not { } f || f.GetValue(owner) is not Delegate d) return;
            var keep = d.GetInvocationList().Where(x => !x.Method.Name.Contains("WireFreeCameraReleases")).ToArray();
            foreach (var x in d.GetInvocationList().Except(keep)) removed.Add((owner, field, x));
            f.SetValue(owner, keep.Length == 0 ? null : Delegate.Combine(keep));
        }
        Detach(R13Get(fw, "_sceneLeave"), "Leaving");
        Detach(R13Get(fw, "_clientState"), "SceneChanged");
        Log($"R13 tpdog detached {removed.Count} framework release handler(s): {string.Join(",", removed.Select(r => r.Field))}");
        Arm("r13.handlers", () =>
        {
            foreach (var (owner, field, del) in removed)
            {
                var f = R13Field(owner.GetType(), field)!;
                f.SetValue(owner, Delegate.Combine((Delegate?)f.GetValue(owner), del));
            }
            Log($"R13 tpdog restored {removed.Count} handler(s)");
        });
    }

    // ---- qa M-11: a by-value 8-byte Vector2 through a patched call, byte-for-byte ----

    private IEnumerator R13Vec2()
    {
        R13DotProbe();
        var token = _services.SceneFreeze.Freeze();
        Arm("r13.freeze", () => token.Dispose());
        yield return Wait(1f);
        try { R13PlayBaseProbe(); }
        catch (Exception ex) { Log($"R13 vec2 playBaseState FAILED {ex.GetType().Name}: {ex.Message}"); }
        Release("r13.freeze");
        yield return Wait(1f);
        Log($"R13 vec2 unfrozen {R13State()}");
    }

    // The production gate's typed prefix on playBaseState: the probe calls the game method's NATIVE entry (as a native caller:
    // the Vector2 in r9) on a tracked remote controller while armed; the prefix defers it and keeps its arguments — the kept
    // Vector2 must equal what was passed, bit for bit (and the replay re-issues it on unfreeze).
    private void R13PlayBaseProbe()
    {
        var gate = R13Get(R13Get(R13Fw(), "_freezeBackend"), "_anim");
        if (R13Get(gate, "_tracked") is not System.Collections.IDictionary tracked || tracked.Count == 0) { Log("R13 vec2: no tracked controller"); return; }
        var ctl = tracked.Keys.Cast<IntPtr>().First();
        var m = typeof(ECSAnimController).GetMethod("playBaseState", R13Any)!;
        var mi = (IntPtr)Il2CppInteropUtils.GetIl2CppMethodInfoPointerFieldForGeneratedMethod(m)!.GetValue(null)!;
        var fp = Marshal.ReadIntPtr(mi);
        var sent = new Vector2(1.25f, -3.5078125f);
        var kept0 = (int)R13Prop(gate, "Kept")!;
        Marshal.GetDelegateForFunctionPointer<R13PlayBase>(fp)(ctl, (int)EAnimBase.EIdle, 0.3125f, R13Pack(sent), -1f, 0.15f, 0, 0, mi);
        var kept = (System.Collections.IList)R13Get(gate, "_kept")!;
        if (kept.Count == 0) { Log($"R13 vec2 playBaseState: nothing kept (kept {kept0} -> 0)"); return; }
        var last = kept[kept.Count - 1]!;
        var args = (object?[])R13Prop(last, "Args")!;
        var got = args.Length > 2 && args[2] is Vector2 v ? v : new Vector2(float.NaN, float.NaN);
        var ok = R13Bits(got) == R13Bits(sent) && args[0] is EAnimBase.EIdle && args[1] is 0.3125f && args[4] is 0.15f;
        Log($"R13 vec2 playBaseState RESULT {(ok ? "PASS" : "FAIL")} ctl=0x{ctl.ToInt64():X} sent={R13Bits(sent)} kept={R13Bits(got)} " +
            $"args=[{string.Join(", ", args.Select(a => $"{a?.GetType().Name}:{a}"))}] keptCount {kept0}->{kept.Count}");
    }

    private static Vector2 s_r13Seen0, s_r13Seen1;
    private static int s_r13Hits;

    private static void R13DotPrefix(Vector2 __0, Vector2 __1) { s_r13Seen0 = __0; s_r13Seen1 = __1; s_r13Hits++; }

    // End to end through Il2CppInterop's native->managed trampoline: Vector2.Dot's native entry with (x, y) and a unit axis,
    // unpatched, then patched with a typed by-value prefix: same result bits, and the prefix saw the same bits.
    private void R13DotProbe()
    {
        var m = typeof(Vector2).GetMethod("Dot", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(Vector2), typeof(Vector2) }, null);
        var field = m == null ? null : Il2CppInteropUtils.GetIl2CppMethodInfoPointerFieldForGeneratedMethod(m);
        if (field?.GetValue(null) is not IntPtr mi || mi == IntPtr.Zero || Marshal.ReadIntPtr(mi) == IntPtr.Zero) { Log("R13 vec2 Dot: no native entry"); return; }
        var call = Marshal.GetDelegateForFunctionPointer<R13Dot>(Marshal.ReadIntPtr(mi));
        var a = new Vector2(1.25f, -3.5078125f);
        float X(Vector2 axis) => call(R13Pack(a), R13Pack(axis), mi);
        var before = (X(Vector2.right), X(Vector2.up));
        var h = new Harmony("stellar.freecamprobe.r13");
        try
        {
            h.Patch(m, prefix: new HarmonyMethod(typeof(FreeCamProbe).GetMethod(nameof(R13DotPrefix), BindingFlags.Static | BindingFlags.NonPublic)));
            s_r13Hits = 0;
            var after = (X(Vector2.right), X(Vector2.up));
            var ok = BitConverter.SingleToInt32Bits(after.Item1) == BitConverter.SingleToInt32Bits(before.Item1) &&
                     BitConverter.SingleToInt32Bits(after.Item2) == BitConverter.SingleToInt32Bits(before.Item2) &&
                     before.Item1 == a.x && before.Item2 == a.y && s_r13Hits == 2 && R13Bits(s_r13Seen0) == R13Bits(a) && R13Bits(s_r13Seen1) == R13Bits(Vector2.up);
            Log($"R13 vec2 Dot RESULT {(ok ? "PASS" : "FAIL")} unpatched=({before.Item1},{before.Item2}) patched=({after.Item1},{after.Item2}) " +
                $"prefixHits={s_r13Hits} seen0={R13Bits(s_r13Seen0)} want {R13Bits(a)} seen1={R13Bits(s_r13Seen1)}");
        }
        finally { h.UnpatchSelf(); }
    }
}
