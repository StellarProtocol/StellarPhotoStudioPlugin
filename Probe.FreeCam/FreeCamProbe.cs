using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Stellar.Abstractions.Domain;
using Stellar.Abstractions.Plugins;
using Stellar.Abstractions.Services;
using UnityEngine;

namespace Stellar.PhotoStudio.FreeCamProbe;

/// <summary>
/// THROWAWAY recon probe for Photo Studio sub-project 2 (free camera / GPose), spec § 9 items 1-6.
/// With <c>STELLAR_FREECAMPROBE_AUTO=1</c> it runs every step once, 15 s after the first World phase; F8 runs the next
/// step manually (F6 is a framework-hotkey "ping" used to prove hotkeys still fire under the input shield). Every
/// measured fact is one <c>[FreeCamProbe]</c> log line (BepInEx log + <c>stellar/screenshots/freecamprobe/freecamprobe.log</c>).
/// Every override is released in a <c>finally</c> and again by <see cref="ReleaseAll"/> on scene change / dispose.
/// </summary>
public sealed partial class FreeCamProbe : IStellarPlugin
{
    public string Name => "FreeCamProbe";

    private const string Prefix = "[FreeCamProbe]";
    private const float AutoDelaySeconds = 15f;

    private readonly IPluginServices _services;
    private readonly string _outDir;
    private readonly string _logPath;
    private readonly bool _auto;
    private readonly List<(string Name, Func<IEnumerator> Run, bool InAuto)> _steps;
    private readonly IHotkeyAction _stepAction;
    private readonly IHotkeyAction _pingAction;
    private readonly Dictionary<string, Action> _releases = new();
    private readonly List<(string Step, double Ms, string Outcome)> _timings = new();
    private readonly Action<string?> _onScene;

    private ProbeRunner? _runner;
    private bool _autoRan;
    private bool _busy;
    private int _manualIndex;
    private float _worldSeconds;
    private int _sceneEpoch;
    private int _pingCount;
    private string _currentOutcome = "";

    public FreeCamProbe(IPluginServices services)
    {
        _services = services;
        _outDir = Path.Combine(BepInEx.Paths.GameRootPath, "stellar", "screenshots", "freecamprobe");
        Directory.CreateDirectory(_outDir);
        _logPath = Path.Combine(_outDir, "freecamprobe.log");
        _auto = Environment.GetEnvironmentVariable("STELLAR_FREECAMPROBE_AUTO") == "1";
        Log($"loaded; auto={_auto} out={_outDir}");
        _steps = BuildSteps();
        _stepAction = _services.Hotkeys.DeclareAction(
            new HotkeyAction("freecamprobe.step", "FreeCamProbe: run next step", new KeyBinding(StellarKeyCode.F8)), OnStepKey);
        _pingAction = _services.Hotkeys.DeclareAction(
            new HotkeyAction("freecamprobe.ping", "FreeCamProbe: hotkey ping (shield test)", new KeyBinding(StellarKeyCode.F6)), OnPing);
        Log("hotkeys declared: freecamprobe.step=F8 freecamprobe.ping=F6");
        _onScene = OnSceneChanged;
        _services.ClientState.SceneChanged += _onScene;
        _services.Framework.Update += OnUpdate;
        ArmCombatWatch();   // FreeCamProbe.Combat.cs — framework attr stream + game combat-setter hooks
    }

    public void Dispose()
    {
        _services.Framework.Update -= OnUpdate;
        _services.ClientState.SceneChanged -= _onScene;
        DisarmCombatWatch();
        ReleaseAll("dispose");
        ProbeTicks.FrameTick = null;
        ProbeTicks.LateTick = null;
        _stepAction.Dispose();
        _pingAction.Dispose();
        if (_runner != null) UnityEngine.Object.Destroy(_runner.gameObject);
    }

    // Run 3 (2026-10-01): run-1 and run-2 steps stay available on F8 but are off in auto except env + the cheap combat
    // read; the R3 steps (non-player entities: enumeration, anim freeze, position hold, appear-during-freeze) run in auto.
    private List<(string, Func<IEnumerator>, bool)> BuildSteps() => new()
    {
        ("env", StepEnv, true),
        ("1_camera_takeover_vcam", StepCameraTakeover, false),
        ("1b_camera_fallback_brain_off", StepCameraFallback, false),
        ("2_input_shield", StepInputShield, false),
        ("3a_freeze_effects", StepFreezeEffects, false),
        ("3b_freeze_animation", StepFreezeAnimation, false),
        ("3c_freeze_position_hold", StepPositionHold, false),
        ("4_emote", StepEmote, false),
        ("5_combat_flag", StepCombatFlag, true),
        ("6a_point_light", StepPointLight, false),
        ("6b_head_look_at", StepLookAt, false),
        ("A_anim_freeze_isolated", StepAnimFreeze2, false),
        ("B_effect_freeze_visual", StepEffects2, false),
        ("C_hold_write_cost", StepHold2, false),
        ("D_character_lights", StepLights2, false),
        ("F_lookat_restore", StepLookRestore2, false),
        ("E_input_mask_source", StepInputSource2, false),
        ("R3_entities", StepEntities3, true),
        ("R3_freeze_kinds", StepFreezeKinds3, true),
        ("R3_hold_kinds", StepHoldKinds3, true),
        ("R3_appear_freeze", StepAppear3, true),
        ("R3_entities_end", StepEntities3, true),
    };

    internal void Log(string msg)
    {
        var line = $"{Prefix} {msg}";
        _services.Log.Info(line);
        try { File.AppendAllText(_logPath, $"{DateTime.UtcNow:HH:mm:ss.fff} {line}\n"); } catch { }
    }

    private void OnUpdate(float dt)
    {
        if (_services.ClientState.Phase == GamePhase.World)
            Try("self uuid", () => _selfUuid = Panda.ZGame.ZEntityMgr.IsCreated ? Panda.ZGame.ZEntityMgr.Instance.PlayerUuid : 0);
        if (!_auto || _autoRan || _busy) return;
        if (_services.ClientState.Phase != GamePhase.World) { _worldSeconds = 0f; return; }
        _worldSeconds += dt;
        if (_worldSeconds < AutoDelaySeconds) return;   // AutoNav dismisses the newbie popup; the world settles
        _autoRan = true;
        Start(RunAll());
    }

    private void OnSceneChanged(string? scene)
    {
        _sceneEpoch++;
        if (_releases.Count > 0) ReleaseAll($"scene change -> {scene}");
    }

    private void OnStepKey()
    {
        if (_busy) { Log("F8 ignored: a step is running"); return; }
        var i = _manualIndex % _steps.Count;
        _manualIndex++;
        Log($"F8 -> step {i} '{_steps[i].Name}'");
        Start(RunOne(i));
    }

    private void OnPing()
    {
        _pingCount++;
        Log($"HOTKEY ping #{_pingCount} fired (shieldHeld={_releases.ContainsKey("shield.ignore")})");
    }

    private IEnumerator RunAll()
    {
        _mainThreadId = Environment.CurrentManagedThreadId;
        Log("SEQUENCE START");
        var total = Stopwatch.StartNew();
        for (var i = 0; i < _steps.Count; i++)
        {
            if (!_steps[i].InAuto && !(i == 2 && _vcamFailed)) { Log($"--- step {i} '{_steps[i].Name}' skipped in auto"); continue; }
            yield return RunOne(i);
        }
        Log("TIMING summary: " + string.Join(" | ", _timings.ConvertAll(t => $"{t.Step}={t.Ms:F0}ms({t.Outcome})")));
        Log($"TIMING total={total.Elapsed.TotalSeconds:F1}s steps={_timings.Count} pendingReleases={_releases.Count}");
        LogCombatSummary();
        ReleaseAll("sequence end");
        Log("DONE");
    }

    private IEnumerator RunOne(int i)
    {
        var name = _steps[i].Name;
        Log($"--- step {i} '{name}'");
        _currentOutcome = "ok";
        var epoch = _sceneEpoch;
        var sw = Stopwatch.StartNew();
        yield return Guard(_steps[i].Run(), name);
        if (epoch != _sceneEpoch) _currentOutcome = "scene-changed";
        ReleaseAll($"after step {name}");   // belt and braces: a step's finally already released its own
        _timings.Add((name, sw.Elapsed.TotalMilliseconds, _currentOutcome));
        Log($"step '{name}' finished in {sw.Elapsed.TotalMilliseconds:F0} ms outcome={_currentOutcome}");
    }

    /// <summary>Runs a step; an exception inside it is logged and the sequence continues.</summary>
    private IEnumerator Guard(IEnumerator inner, string name)
    {
        try
        {
            while (true)
            {
                object? cur;
                try
                {
                    if (!inner.MoveNext()) yield break;
                    cur = inner.Current;
                }
                catch (Exception ex)
                {
                    Log($"STEP EXCEPTION in '{name}' {ex.GetType().Name}: {ex.Message}");
                    _currentOutcome = "exception";
                    yield break;
                }
                yield return cur;
            }
        }
        finally { (inner as IDisposable)?.Dispose(); }   // runs the step's own finally blocks on abort
    }

    /// <summary>Registers a release action (run by <see cref="Release"/>, <see cref="ReleaseAll"/>, scene change, dispose).</summary>
    internal void Arm(string key, Action release) => _releases[key] = release;

    internal void Release(string key)
    {
        if (!_releases.TryGetValue(key, out var a)) return;
        _releases.Remove(key);
        try { a(); Log($"RELEASE {key} ok"); }
        catch (Exception ex) { Log($"RELEASE {key} FAILED {ex.GetType().Name}: {ex.Message}"); }
    }

    private void ReleaseAll(string why)
    {
        if (_releases.Count == 0) return;
        Log($"RELEASE ALL ({why}): {string.Join(",", _releases.Keys)}");
        foreach (var k in new List<string>(_releases.Keys)) Release(k);
    }

    /// <summary>True when the step must stop (scene changed since <paramref name="epoch"/>).</summary>
    internal bool Aborted(int epoch) => epoch != _sceneEpoch || _services.ClientState.Phase != GamePhase.World;

    private void Start(IEnumerator routine)
    {
        _runner ??= ProbeTicks.Create(Log);
        _busy = true;
        ProbeTicks.Run(_runner, Flatten(routine));
    }

    /// <summary>Flattens nested managed IEnumerators so only null / Unity yield instructions reach Unity.</summary>
    private IEnumerator Flatten(IEnumerator root)
    {
        var stack = new Stack<IEnumerator>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var top = stack.Peek();
            bool moved;
            try { moved = top.MoveNext(); }
            catch (Exception ex) { Log($"ROUTINE EXCEPTION {ex.GetType().Name}: {ex.Message}"); moved = false; }
            if (!moved) { (stack.Pop() as IDisposable)?.Dispose(); continue; }
            if (top.Current is IEnumerator nested) { stack.Push(nested); continue; }
            yield return top.Current;
        }
        _busy = false;
    }

    /// <summary>Real-time wait expressed as null-yields (no managed CustomYieldInstruction crossing).</summary>
    internal static IEnumerator Wait(float seconds)
    {
        var until = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < until) yield return null;
    }

    internal static IEnumerator Frames(int n)
    {
        for (var i = 0; i < n; i++) yield return null;
    }

    private bool Try(string what, Action a)
    {
        try { a(); return true; }
        catch (Exception ex) { Log($"{what} FAILED {ex.GetType().Name}: {ex.Message}"); return false; }
    }

    private static string V(Vector3 v) => $"({v.x:F2},{v.y:F2},{v.z:F2})";
}
