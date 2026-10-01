using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Panda.ZEffect;
using UnityEngine;

namespace Stellar.PhotoStudio.FreeCamProbe;

/// <summary>
/// § 9 item 3a — effect freeze: <c>ZEffectManager.SetEffectFreeze(uid, true)</c> over every key of <c>EffectDict</c>,
/// hold 2 s (counting effects created meanwhile — those are NOT frozen by the one-shot walk), then unfreeze the same
/// uids. Also lists the manager's creation-path method names (the spec wants to freeze new effects at creation).
/// </summary>
public sealed partial class FreeCamProbe
{
    private IEnumerator StepFreezeEffects()
    {
        var epoch = _sceneEpoch;
        var em = ZEffectManager.IsCreated ? ZEffectManager.Instance : null;
        if (em == null) { Log("EFFECTS ZEffectManager not created"); yield break; }
        LogCreationPath();
        var keys = EffectKeys(em, out var how);
        Log($"EFFECTS EffectDict.Count={em.EffectDict?.Count} keysRead={keys.Count} via={how}");
        var region = SelfRegion();
        var before = region is { } r0 ? Capture("3a_effects_before", r0) : null;
        var sw = Stopwatch.StartNew();
        var frozen = new List<long>();
        foreach (var uid in keys)
        {
            try { em.SetEffectFreeze(uid, true); frozen.Add(uid); }
            catch (Exception ex) { Log($"EFFECTS freeze uid={uid} FAILED {ex.GetType().Name}: {ex.Message}"); }
        }
        var freezeMs = sw.Elapsed.TotalMilliseconds;
        Arm("freeze.effects", () =>
        {
            var m = ZEffectManager.IsCreated ? ZEffectManager.Instance : null;
            if (m == null) return;
            foreach (var uid in frozen) { try { m.SetEffectFreeze(uid, false); } catch { } }
        });
        Log($"EFFECTS froze {frozen.Count}/{keys.Count} in {freezeMs:F2} ms ({(keys.Count > 0 ? freezeMs * 1000 / keys.Count : 0):F1} us/effect)");
        try
        {
            var start = Time.realtimeSinceStartup;
            yield return Wait(1f);
            var mid = region is { } r1 ? Capture("3a_effects_frozen_t1", r1) : null;
            yield return Wait(1f);
            var late = region is { } r2 ? Capture("3a_effects_frozen_t2", r2) : null;
            var after = EffectKeys(em, out _);
            var created = after.Except(keys).Count();
            var gone = keys.Except(after).Count();
            Log($"EFFECTS after {Time.realtimeSinceStartup - start:F1}s frozen: count={after.Count} createdWhileFrozen={created} (not frozen) removedWhileFrozen={gone} {DiffText(mid, late)} {DiffText(before, mid)}");
        }
        finally
        {
            sw.Restart();
            Release("freeze.effects");
            Log($"EFFECTS unfroze {frozen.Count} in {sw.Elapsed.TotalMilliseconds:F2} ms");
        }
        if (Aborted(epoch)) yield break;
        yield return Wait(0.5f);
        if (region is { } r3) Log($"EFFECTS after release {DiffText(before, Capture("3a_effects_released", r3))}");
    }

    /// <summary>EffectDict keys via the KeyCollection enumerator, falling back to the entries_ array.</summary>
    private List<long> EffectKeys(ZEffectManager em, out string how)
    {
        var keys = new List<long>();
        how = "none";
        var dict = em.EffectDict;
        if (dict == null) return keys;
        try
        {
            var e = dict.Keys.GetEnumerator();
            while (e.MoveNext()) keys.Add(e.Current);
            how = "Keys.GetEnumerator";
            return keys;
        }
        catch (Exception ex) { Log($"EFFECTS key enumerator FAILED {ex.GetType().Name}: {ex.Message}"); keys.Clear(); }
        try
        {
            var entries = dict.entries_;
            var n = dict.count_;
            for (var i = 0; i < n && i < entries.Length; i++)
                if (entries[i].hashCode >= 0) keys.Add(entries[i].key);
            how = "entries_";
        }
        catch (Exception ex) { Log($"EFFECTS entries_ fallback FAILED {ex.GetType().Name}: {ex.Message}"); }
        return keys;
    }

    private void LogCreationPath()
    {
        var names = typeof(ZEffectManager).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(m => m.Name).Where(n => n.IndexOf("Create", StringComparison.OrdinalIgnoreCase) >= 0
                                            || n.IndexOf("Play", StringComparison.OrdinalIgnoreCase) >= 0
                                            || n.IndexOf("Add", StringComparison.OrdinalIgnoreCase) >= 0
                                            || n.IndexOf("Spawn", StringComparison.OrdinalIgnoreCase) >= 0
                                            || n.IndexOf("Freeze", StringComparison.OrdinalIgnoreCase) >= 0)
            .Distinct().OrderBy(n => n);
        Log($"EFFECTS creation-path candidates (ZEffectManager): {string.Join(" ", names)}");
    }

    /// <summary>Screen region around the local player's chest (null when no model / camera).</summary>
    private RectInt? SelfRegion(float halfW = 0.08f, float halfH = 0.16f)
    {
        var m = LiveModel(SelfEntity());
        var cam = MainCam();
        if (m == null || cam == null) return null;
        return RegionAround(cam, m.GetChestPosition(), halfW, halfH);
    }
}
