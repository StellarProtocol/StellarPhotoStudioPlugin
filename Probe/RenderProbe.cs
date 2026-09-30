using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Il2CppInterop.Runtime.Injection;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using Stellar.Abstractions.Domain;
using Stellar.Abstractions.Plugins;
using Stellar.Abstractions.Services;
using UnityEngine;

namespace Stellar.PhotoStudioProbe;

/// <summary>
/// THROWAWAY recon probe for the Photo Studio plan (Task 0). On the first world entry it auto-runs every
/// experiment once (look volumes, capture, visibility), logging each result with the <c>[PhotoProbe]</c>
/// prefix and writing a 1x game-backbuffer PNG per step to <c>game_mini/stellar/screenshots/</c>. F9 re-runs
/// the step list one step per press (owner stepping). Photo-mode and cutscene hooks stay armed for the owner.
/// </summary>
public sealed partial class RenderProbe : IStellarPlugin
{
    public string Name => "PhotoStudioProbe";

    private readonly IPluginServices _services;
    private readonly IHotkeyAction _stepAction;
    private readonly string _outDir;
    private readonly string _logPath;
    private readonly List<(string Name, Func<IEnumerator> Run)> _steps;
    private ProbeRunner? _runner;
    private bool _autoRan;
    private bool _busy;
    private int _manualIndex;
    private float _worldSeconds;

    public RenderProbe(IPluginServices services)
    {
        _services = services;
        _outDir = Path.Combine(BepInEx.Paths.GameRootPath, "stellar", "screenshots");
        Directory.CreateDirectory(_outDir);
        _logPath = Path.Combine(_outDir, "photoprobe.log");
        Log($"loaded; out={_outDir}");
        _steps = BuildSteps();
        InstallHooks();
        _stepAction = _services.Hotkeys.DeclareAction(
            new HotkeyAction(Id: "photostudioprobe.step", Description: "PhotoProbe: run next experiment step",
                SuggestedDefault: new KeyBinding(StellarKeyCode.F9)),
            callback: OnF9);
        _services.Framework.Update += OnUpdate;
    }

    public void Dispose()
    {
        _services.Framework.Update -= OnUpdate;
        _stepAction.Dispose();
    }

    internal void Log(string msg)
    {
        var line = $"[PhotoProbe] {msg}";
        _services.Log.Info(line);
        try { File.AppendAllText(_logPath, $"{DateTime.UtcNow:HH:mm:ss.fff} {line}\n"); } catch { }
    }

    private void OnUpdate(float dt)
    {
        PollStates(dt);
        if (_autoRan || _busy) return;
        if (_services.ClientState.Phase != GamePhase.World) { _worldSeconds = 0f; return; }
        _worldSeconds += dt;
        if (_worldSeconds < 15f) return;   // let AutoNav dismiss the newbie popup and the world settle
        _autoRan = true;
        Start(RunAll());
    }

    private void OnF9()
    {
        if (_busy) { Log("F9 ignored: a step is running"); return; }
        var i = _manualIndex % _steps.Count;
        _manualIndex++;
        Log($"F9 -> step {i} '{_steps[i].Name}'");
        Start(RunOne(i));
    }

    private IEnumerator RunAll()
    {
        Log("SEQUENCE START");
        for (var i = 0; i < _steps.Count; i++)
        {
            Log($"--- step {i} '{_steps[i].Name}'");
            yield return Guard(_steps[i].Run());
        }
        Log("SEQUENCE DONE");
    }

    private IEnumerator RunOne(int i)
    {
        yield return Guard(_steps[i].Run());
    }

    /// <summary>Runs a step; an exception inside it is logged and the sequence continues.</summary>
    private IEnumerator Guard(IEnumerator inner)
    {
        while (true)
        {
            object? cur;
            try
            {
                if (!inner.MoveNext()) yield break;
                cur = inner.Current;
            }
            catch (Exception ex) { Log($"STEP EXCEPTION {ex.GetType().Name}: {ex.Message}"); yield break; }
            yield return cur;
        }
    }

    private void Start(IEnumerator routine)
    {
        if (_runner == null)
        {
            try { ClassInjector.RegisterTypeInIl2Cpp<ProbeRunner>(); } catch (Exception ex) { Log($"inject: {ex.Message}"); }
            var go = new GameObject("StellarPhotoProbeRunner");
            UnityEngine.Object.DontDestroyOnLoad(go);
            _runner = go.AddComponent<ProbeRunner>();
            Log("coroutine host = own injected MonoBehaviour ProbeRunner (ClassInjector) on DontDestroyOnLoad GameObject");
        }
        _busy = true;
        _runner.StartCoroutine(Flatten(routine).WrapToIl2Cpp());
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
            if (!moved) { stack.Pop(); continue; }
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
}

/// <summary>Coroutine host: a bare injected MonoBehaviour (plugins cannot reach the framework's StellarTicker).</summary>
public sealed class ProbeRunner : MonoBehaviour
{
    public ProbeRunner(IntPtr ptr) : base(ptr) { }
}
