using System;
using System.Collections;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace Stellar.PhotoStudio.FreeCamProbe;

/// <summary>
/// Coroutine host + per-frame callbacks: a bare injected MonoBehaviour (plugins cannot reach the framework's
/// StellarTicker, and <c>IFramework.Update</c> may be rate-limited, so frame-accurate measurements use these).
/// Kept member-minimal (only the ctor + Unity messages) so ClassInjector has nothing unsupported to map.
/// </summary>
public sealed class ProbeRunner : MonoBehaviour
{
    public ProbeRunner(IntPtr ptr) : base(ptr) { }

    public void Update() => ProbeTicks.InvokeFrame();

    public void LateUpdate() => ProbeTicks.InvokeLate();
}

/// <summary>Managed side of <see cref="ProbeRunner"/>: tick hooks, creation and coroutine start.</summary>
internal static class ProbeTicks
{
    /// <summary>Runs in Unity Update (order vs the game's own Update is unspecified).</summary>
    internal static Action? FrameTick;
    /// <summary>Runs in Unity LateUpdate (order vs the game's own LateUpdate is unspecified).</summary>
    internal static Action? LateTick;
    private static Action<string>? _log;

    internal static ProbeRunner Create(Action<string> log)
    {
        _log = log;
        try { ClassInjector.RegisterTypeInIl2Cpp<ProbeRunner>(); } catch (Exception ex) { log($"inject: {ex.Message}"); }
        var go = new GameObject("StellarFreeCamProbeRunner");
        UnityEngine.Object.DontDestroyOnLoad(go);
        var r = go.AddComponent<ProbeRunner>();
        log("coroutine host = injected MonoBehaviour ProbeRunner on a DontDestroyOnLoad GameObject");
        return r;
    }

    internal static void Run(ProbeRunner runner, IEnumerator routine) => runner.StartCoroutine(routine.WrapToIl2Cpp());

    internal static void InvokeFrame()
    {
        var t = FrameTick;
        if (t == null) return;
        try { t(); } catch (Exception ex) { FrameTick = null; _log?.Invoke($"FrameTick threw, unhooked: {ex.GetType().Name}: {ex.Message}"); }
    }

    internal static void InvokeLate()
    {
        var t = LateTick;
        if (t == null) return;
        try { t(); } catch (Exception ex) { LateTick = null; _log?.Invoke($"LateTick threw, unhooked: {ex.GetType().Name}: {ex.Message}"); }
    }
}
