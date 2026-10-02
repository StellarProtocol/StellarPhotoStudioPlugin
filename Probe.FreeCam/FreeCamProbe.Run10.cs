using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cinemachine;
using Il2CppInterop.Runtime;
using Panda.ZGame;
using Stellar.Abstractions.Domain;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Stellar.PhotoStudio.FreeCamProbe;

/// <summary>
/// Run 10 (2026-10-02): VERIFY the framework's time-pause freeze (framework feat/posing time-pause implementation) through
/// Photo Studio's REAL panel — every action is a click on the panel's own uGUI Button (<c>Button.OnPointerClick</c> with a
/// left-button PointerEventData; no synthetic OS input, nothing reads the desktop). Per mode (town, field): open the Camera
/// tab, ❄ Freeze (the Scene group), measure the pause (scaled dt, timeScale, framework ticks, labels refreshing, effects,
/// remote movers held), enter Photo Studio's free camera (brain must cut, not blend), Capture from the Capture tab, exit the
/// free camera while frozen (the world must stay paused), move the framework free camera while paused, hold ~60 s, then
/// ❄ Unfreeze (timeScale restored, the brain's blend restored). Probe-only; never merged.
/// </summary>
public sealed partial class FreeCamProbe
{
    private static string R10Mode => Environment.GetEnvironmentVariable("STELLAR_R10_MODE") is { Length: > 0 } m ? m : "both";
    private const float R10PauseSeconds = 60f;

    private int _r10FwTicks, _r10Frames, _r10DtPos, _r10ScaleOff, _r10Chat, _r10Combat;
    private bool _r10Measure;
    private readonly List<string> _r10Verdicts = new();

    private IEnumerator StepSetup10()
    {
        Log($"R10 mode={R10Mode} self={_selfUuid} census {R7Census()}");
        R9TimeState("r10 boot");
        Log($"R10 LUA net-wrap: {Lua(R9NetWrapLua)}");
        Log($"R10 LUA net: {Lua(R9NetReadLua)}");
        yield break;
    }

    private void R10Arm()
    {
        _services.Framework.Update += R10OnFw;
        _services.Chat.MessageReceived += R10OnChat;
        _services.CombatEvents.CombatEventOccurred += R10OnCombat;
        ProbeTicks.FrameTick = R10Frame;
        Arm("r10.counters", () =>
        {
            _services.Framework.Update -= R10OnFw;
            _services.Chat.MessageReceived -= R10OnChat;
            _services.CombatEvents.CombatEventOccurred -= R10OnCombat;
            ProbeTicks.FrameTick = null;
        });
    }

    private void R10OnFw(float dt) => _r10FwTicks++;
    private void R10OnChat(ChatMessage m) => _r10Chat++;
    private void R10OnCombat(CombatEvent e) => _r10Combat++;

    private void R10Frame()
    {
        if (!_r10Measure) return;
        _r10Frames++;
        if (Time.deltaTime > 0f) _r10DtPos++;
        if (Time.timeScale != 0f) _r10ScaleOff++;
    }

    private IEnumerator StepTown10()
    {
        if (R10Mode is not ("both" or "town")) yield break;
        R10Arm();
        yield return R10Sequence("town");
        Release("r10.counters");
    }

    private IEnumerator StepField10()
    {
        if (R10Mode is not ("both" or "field")) yield break;
        yield return R9GoField();
        var ep0 = _sceneEpoch;
        var tw = Time.realtimeSinceStartup;
        while (ep0 == _sceneEpoch && Time.realtimeSinceStartup - tw < 20f) yield return Wait(0.5f);
        yield return Wait(3f);
        Log($"R10 field census {R7Census()} scene={Lua("return tostring(Z.StageMgr.GetCurrentSceneId())")}");
        R10Arm();
        yield return R10Sequence("field");
        Release("r10.counters");
        yield return R8GoHome("r10 field return");
    }

    private IEnumerator StepEnd10()
    {
        Log($"R10 LUA net final: {Lua(R9NetReadLua)}");
        Log($"R10 SENDS total {SendsTotal()}");
        R9TimeState("r10 end");
        Log($"R10 VERDICTS {string.Join(" | ", _r10Verdicts)}");
        yield break;
    }

    // ---- the sequence --------------------------------------------------------------------------------------------

    private IEnumerator R10Sequence(string label)
    {
        var epoch = _sceneEpoch;
        var ok = new List<string>();
        void Check(string what, bool pass, string detail = "")
        {
            ok.Add($"{what}={(pass ? "PASS" : "FAIL")}");
            Log($"R10 {label} CHECK {what} {(pass ? "PASS" : "FAIL")} {detail}".TrimEnd());
        }

        // 1. the panel's Camera tab, the Scene group open
        Check("panel-open", R10Click("Camera"), "(Camera tab)");
        yield return Wait(1.2f);
        if (R10FindButton("❄ Freeze") == null) { R10ClickText("Scene"); yield return Wait(1.2f); }
        var preScale = Time.timeScale;
        var preBlend = R10BlendStyle();

        // 2. ❄ Freeze
        var fw0 = _r10FwTicks;
        _r10Frames = _r10DtPos = _r10ScaleOff = 0;
        Check("click-freeze", R10Click("❄ Freeze"));
        _r10Measure = true;
        var tFreeze = Time.realtimeSinceStartup;
        yield return Wait(1.5f);
        Check("paused", _services.SceneFreeze.IsFrozen && Time.timeScale == 0f,
            $"fwFrozen={_services.SceneFreeze.IsFrozen} timeScale={Time.timeScale:F3} pre={preScale:F3} holdsPositions={_services.SceneFreeze.HoldsPositions}");
        Check("ui-refresh-while-paused", R10FindButton("❄ Unfreeze") != null, $"fwTicks+{_r10FwTicks - fw0} in 1.5 s");
        Check("blend-cut-while-paused", R10BlendStyle() == "Cut", $"pre={preBlend} now={R10BlendStyle()}");
        var e0 = R9Ents(60f);    // sampled after the pause took hold
        var fx0 = R9Fx();

        // 2b. a write to the clock while paused (through the same native setter the game calls) is held at 0 and kept
        Time.timeScale = 0.5f;
        yield return null;
        Check("hook-holds-write", Time.timeScale == 0f, $"after writing 0.5 timeScale={Time.timeScale:F3}");

        // 3. Photo Studio's free camera while paused: the brain cuts to it
        Check("click-enter-freecam", R10Click("Free camera"));
        yield return Wait(1.2f);
        Check("freecam-on-cut", _services.CameraOverride.IsOverridden && R10ActiveVcam() == "StellarFreeCamera" && !R10Blending(),
            $"overridden={_services.CameraOverride.IsOverridden} active={R10ActiveVcam()} blending={R10Blending()} exitLabel={R10FindButton("Exit free camera") != null}");

        // 4. the tabs switch while paused (the panel's content is rebuilt on the framework tick), then the footer Capture
        Check("click-look-tab", R10Click("Look"));
        yield return Wait(1.2f);
        Check("look-tab-shown-while-paused", R10FindButton("Reset all") != null && R10FindButton("Exit free camera") == null);
        var shots0 = R10Shots();
        var capBtn = R10CaptureButton();
        Check("click-capture", capBtn != null && R10Press(capBtn, "Capture (footer)"));
        var tc = Time.realtimeSinceStartup;
        string? newShot = null;
        while (Time.realtimeSinceStartup - tc < 15f && newShot == null) { yield return Wait(0.5f); newShot = R10Shots().Except(shots0).FirstOrDefault(); }
        Check("capture-while-paused", newShot != null && new FileInfo(newShot).Length > 0,
            $"file={newShot} size={(newShot != null ? new FileInfo(newShot).Length : 0)} in {Time.realtimeSinceStartup - tc:F1}s timeScale={Time.timeScale:F3}");

        // 5. exit the free camera while frozen: the world stays paused
        Check("click-camera-tab", R10Click("Camera"));
        yield return Wait(1.2f);
        Check("click-exit-freecam", R10Click("Exit free camera"));
        yield return Wait(1.2f);
        Check("exit-keeps-pause", !_services.CameraOverride.IsOverridden && _services.SceneFreeze.IsFrozen && Time.timeScale == 0f && !R10Blending(),
            $"overridden={_services.CameraOverride.IsOverridden} fwFrozen={_services.SceneFreeze.IsFrozen} timeScale={Time.timeScale:F3} active={R10ActiveVcam()} blending={R10Blending()}");

        // 6. the framework free camera moves while paused (acquire cuts, +1 m pose followed)
        if (R9AcquireCam($"r10 {label}"))
        {
            yield return Wait(0.3f);
            var cam = MainCam();
            var p0 = cam != null ? cam.transform.position : Vector3.zero;
            yield return R9FollowCheck($"r10 {label} paused");
            var moved = cam != null ? Vector3.Distance(cam.transform.position, p0) : 0f;
            Check("freecam-moves-while-paused", moved > 0.9f, $"moved={moved:F3}m {R9CamCounters()} active={R10ActiveVcam()}");
            R9ReleaseCam();
            yield return Wait(0.5f);
        }

        // 7. hold the rest of the pause, sampling
        while (Time.realtimeSinceStartup - tFreeze < R10PauseSeconds && !Aborted(epoch))
        {
            yield return Wait(10f);
            Log($"R10 {label} HOLD +{Time.realtimeSinceStartup - tFreeze:F0}s timeScale={Time.timeScale:F3} frames={_r10Frames} dt>0={_r10DtPos} " +
                $"scaleOff={_r10ScaleOff} fwTicks={_r10FwTicks - fw0} chat={_r10Chat} combat={_r10Combat} net[{Lua(R9NetReadLua)}]");
        }
        var paused = Time.realtimeSinceStartup - tFreeze;
        var e1 = R9Ents(60f);
        var fx1 = R9Fx();
        Log($"R10 {label} ENTITIES over the pause: {R9EntDelta(e0, e1)}");
        Log($"R10 {label} EFFECTS over the pause: {R9FxDelta(fx0, fx1)}");
        Check("world-paused", _r10DtPos == 0 && _r10ScaleOff == 0 && _r10Frames > 100,
            $"paused={paused:F0}s frames={_r10Frames} framesWithDt>0={_r10DtPos} framesTimeScale!=0={_r10ScaleOff}");
        var tickRate = (_r10FwTicks - fw0) / Math.Max(1f, paused);
        Check("framework-ticks-while-paused", tickRate > 5f, $"fwTicks={_r10FwTicks - fw0} ({tickRate:F1}/s)");
        Check("still-in-world", !Aborted(epoch), $"net[{Lua(R9NetReadLua)}]");

        // 8. ❄ Unfreeze (the Scene group): the clock and the blend come back
        _r10Measure = false;
        Check("click-unfreeze", R10Click("❄ Unfreeze"));
        yield return Wait(1.5f);
        Check("resumed-at-held-wish", !_services.SceneFreeze.IsFrozen && Math.Abs(Time.timeScale - 0.5f) < 0.001f,
            $"fwFrozen={_services.SceneFreeze.IsFrozen} timeScale={Time.timeScale:F3} (held wish 0.5) pre={preScale:F3} freezeLabel={R10FindButton("❄ Freeze") != null}");
        Time.timeScale = preScale;   // the probe's own wish undone
        Log($"R10 {label} timeScale reset to {Time.timeScale:F3}");
        Check("blend-restored", R10BlendStyle() == preBlend, $"pre={preBlend} now={R10BlendStyle()}");
        var fails = ok.Count(s => s.EndsWith("FAIL"));
        _r10Verdicts.Add($"{label}: {ok.Count - fails}/{ok.Count} pass{(fails > 0 ? " FAILS " + string.Join(",", ok.Where(s => s.EndsWith("FAIL"))) : "")}");
        Log($"R10 {label} SUMMARY {string.Join(" ", ok)}");
    }

    // ---- uGUI: find a panel button by its label, click it as the input module would ----------------------------

    private IEnumerable<Button> R10FindButtons(string label)
    {
        var arr = UnityEngine.Object.FindObjectsOfType(Il2CppType.Of<Button>());
        foreach (var o in arr)
        {
            var b = o.TryCast<Button>();
            if (b == null || !b.isActiveAndEnabled || !b.IsInteractable()) continue;
            foreach (var t in b.GetComponentsInChildren<Text>())
                if (t != null && t.text != null && t.text.Trim() == label) { yield return b; break; }
        }
    }

    private Button? R10FindButton(string label)
    {
        try { return R10FindButtons(label).FirstOrDefault(); }
        catch (Exception ex) { Log($"R10 find '{label}' FAILED {ex.GetType().Name}: {ex.Message}"); return null; }
    }

    private bool R10Click(string label)
    {
        var b = R10FindButton(label);
        if (b == null) { Log($"R10 CLICK '{label}': no active button with that label"); return false; }
        return R10Press(b, label);
    }

    private bool R10Press(Button b, string label = "")
    {
        try
        {
            var ev = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };
            b.OnPointerClick(ev);
            Log($"R10 CLICK '{label}' ok (go={b.gameObject.name}) frame={Time.frameCount} timeScale={Time.timeScale:F3}");
            return true;
        }
        catch (Exception ex) { Log($"R10 CLICK '{label}' FAILED {ex.GetType().Name}: {ex.Message}"); return false; }
    }

    /// <summary>The footer Capture button — not the Capture TAB (its row also holds the Look tab).</summary>
    private Button? R10CaptureButton()
    {
        foreach (var b in R10FindButtons("Capture"))
        {
            var row = b.transform.parent?.parent;
            var isTab = row != null && row.GetComponentsInChildren<Text>().Any(t => t != null && t.text == "Look");
            if (!isTab) return b;
        }
        return null;
    }

    /// <summary>A SelectableElement row (fold header): the button whose label row holds <paramref name="text"/>.</summary>
    private void R10ClickText(string text)
    {
        try
        {
            var arr = UnityEngine.Object.FindObjectsOfType(Il2CppType.Of<Button>());
            foreach (var o in arr)
            {
                var b = o.TryCast<Button>();
                if (b == null || !b.isActiveAndEnabled) continue;
                if (b.GetComponentsInChildren<Text>().Any(t => t != null && t.text == text)) { R10Press(b, text); return; }
            }
            Log($"R10 CLICK-TEXT '{text}': none");
        }
        catch (Exception ex) { Log($"R10 CLICK-TEXT '{text}' FAILED {ex.Message}"); }
    }

    // ---- camera brain ------------------------------------------------------------------------------------------

    private static string R10BlendStyle()
    {
        try { return CameraManager.Instance?.Brain?.m_DefaultBlend.m_Style.ToString() ?? "none"; }
        catch { return "err"; }
    }

    private static string R10ActiveVcam()
    {
        try
        {
            var v = CameraManager.Instance?.Brain?.ActiveVirtualCamera;
            return v == null ? "none" : v.Name;
        }
        catch { return "err"; }
    }

    private static bool R10Blending()
    {
        try { return CameraManager.Instance?.Brain?.IsBlending ?? false; }
        catch { return false; }
    }

    private static string[] R10Shots()
    {
        var dir = Path.Combine(BepInEx.Paths.GameRootPath, "stellar", "screenshots");
        try { return Directory.Exists(dir) ? Directory.GetFiles(dir, "*.*", SearchOption.TopDirectoryOnly) : Array.Empty<string>(); }
        catch { return Array.Empty<string>(); }
    }
}
