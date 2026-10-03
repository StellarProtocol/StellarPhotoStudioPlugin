using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Bokura.Rendering;
using Il2CppInterop.Runtime;
using Panda.ZGame;
using UnityEngine;
using UnityEngine.Rendering;

namespace Stellar.PhotoStudio.FreeCamProbe;

/// <summary>
/// Run 13 trials: each candidate lamp path is applied to the local player under a front vcam with the clock paused
/// (Time.timeScale = 0, so idle animation adds no noise), captured in-process, and reverted. Regions: the character's
/// chest, the ground in front of the feet (scene-light control), and the top band (global change control).
/// </summary>
public sealed partial class FreeCamProbe
{
    private static readonly Color R13Warm = new(1f, 0.45f, 0.15f, 1f);
    private const uint R13All = 0x3FFFFFFF;

    private RectInt[] _r13Regions = Array.Empty<RectInt>();
    private Shot[]? _r13Base;
    private Vector3 _r13Lamp;
    private float _r13PrevTs = 1f;

    private IEnumerator StepTrials13()
    {
        var epoch = _sceneEpoch;
        var front = FrontCamera();
        try
        {
            yield return Wait(2f);
            var model = LiveModel(SelfEntity());
            var cam = MainCam();
            if (model == null || cam == null || front == null) { Log("R13 trials prerequisites missing"); yield break; }
            var chest = model.GetChestPosition();
            var feet = model.GetAttrGoPosition();
            var toCam = (cam.transform.position - chest).normalized;
            var right = Vector3.Cross(Vector3.up, toCam).normalized;
            _r13Lamp = chest + toCam * 1.0f + right * 0.7f + Vector3.up * 0.3f;
            _r13Regions = new[]
            {
                RegionAround(cam, chest, 0.08f, 0.16f),
                RegionAround(cam, feet + toCam * 0.8f + right * 0.6f, 0.12f, 0.04f),
                new RectInt(0, (int)(Screen.height * 0.88f), Screen.width, (int)(Screen.height * 0.1f)),
            };
            Log($"R13 TRIALS lamp={V(_r13Lamp)} chest={V(chest)} lampToChest={Vector3.Distance(_r13Lamp, chest):F2}m cam={V(cam.transform.position)}");
            R13Pause(true);
            yield return Frames(3);
            _r13Base = CaptureMany("R13_base", _r13Regions);
            yield return Frames(3);
            var noise = CaptureMany("R13_noise", _r13Regions);
            Log("R13 NOISE " + R13Cmp(_r13Base, noise));

            yield return R13Trial("T1_unity_point", R13ApplyUnityLight(false, false), epoch);
            yield return R13Trial("T1b_unity_spot", R13ApplyUnityLight(false, true), epoch);
            yield return R13Trial("T2_point+MultiLight", R13ApplyUnityLight(true, false), epoch);
            yield return R13Trial("T2s_spot+MultiLight", R13ApplyUnityLight(true, true), epoch);
            yield return R13Trial("T2c_clone_scene_MultiLight", R13ApplySceneClone(), epoch);
            yield return R13Trial("T3_EffectLight", R13ApplyEffectLight(), epoch);
            yield return R13Trial("T4_GlobalParamsPass.UpdateCharacterLight", R13ApplyGpCharLight(), epoch);
            yield return R13Trial("T4b_CharacterLight_full_drive", R13ApplyCharLightFull(), epoch);
            yield return R13Trial("T5a_SetFixedLight_dirToLamp", R13ApplyFixedLight(0), epoch);
            yield return R13Trial("T5b_SetFixedLight_cam(0,0,-1,1)", R13ApplyFixedLight(1), epoch);
            yield return R13Trial("T5c_SetFixedLight_(1,0,0,1)", R13ApplyFixedLight(2), epoch);
            yield return R13Trial("T6a_FresnelEffect_warm", R13ApplyFresnel(), epoch);
            yield return R13Trial("T6b_CreatureRim_pass+width", R13ApplyCreatureRim(), epoch);
            yield return R13Trial("T7_ADDITIONAL_LIGHTS_kw+point", R13ApplyAddLightsKeyword(), epoch);

            var end = CaptureMany("R13_end", _r13Regions);
            Log("R13 END vs base " + R13Cmp(_r13Base, end));
            R13Globals("end");
        }
        finally
        {
            R13Pause(false);
            Release("cam.vcam");
        }
    }

    private void R13Pause(bool on)
    {
        if (on)
        {
            _r13PrevTs = Time.timeScale;
            Time.timeScale = 0f;
            Arm("r13.pause", () => Time.timeScale = _r13PrevTs <= 0f ? 1f : _r13PrevTs);
            Log($"R13 PAUSE on prev={_r13PrevTs:F2}");
        }
        else
        {
            Release("r13.pause");
            Log($"R13 PAUSE off now={Time.timeScale:F2}");
        }
    }

    /// <summary>A trial: apply (returns a revert action + a state reader, or null when not applicable), capture, revert.</summary>
    private IEnumerator R13Trial(string name, Func<(Action Revert, Func<string> State)?> apply, int epoch)
    {
        if (Aborted(epoch)) yield break;
        if (Time.timeScale != 0f) { Time.timeScale = 0f; Log($"R13 {name}: re-paused (game wrote timeScale)"); }
        (Action Revert, Func<string> State)? h = null;
        try { h = apply(); }
        catch (Exception ex) { Log($"R13 {name} APPLY FAILED {ex.GetType().Name}: {ex.Message}"); }
        if (h == null) { Log($"R13 {name} n/a"); yield break; }
        var key = "r13." + name;
        Arm(key, h.Value.Revert);
        yield return Frames(4);
        var on = CaptureMany($"R13_{name}_on", _r13Regions);
        string st;
        try { st = h.Value.State(); } catch (Exception ex) { st = $"state-err {ex.GetType().Name}: {ex.Message}"; }
        Log($"R13 RESULT {name} ON {R13Cmp(_r13Base, on)} | {st}");
        R13Globals(name + " on");
        Release(key);
        yield return Frames(4);
        var off = CaptureMany($"R13_{name}_off", _r13Regions);
        Log($"R13 RESULT {name} OFF {R13Cmp(_r13Base, off)}");
    }

    private static (float L, float R, float G, float B) R13Mean(Shot s)
    {
        if (s.Px.Length == 0) return (0, 0, 0, 0);
        double l = 0, r = 0, g = 0, b = 0;
        foreach (var c in s.Px) { l += 0.299 * c.r + 0.587 * c.g + 0.114 * c.b; r += c.r; g += c.g; b += c.b; }
        var n = s.Px.Length;
        return ((float)(l / n), (float)(r / n), (float)(g / n), (float)(b / n));
    }

    private string R13Cmp(Shot[]? a, Shot[]? b)
    {
        if (a == null || b == null) return "no-capture";
        var names = new[] { "char", "ground", "sky", "charL", "charR" };
        var parts = new List<string>();
        for (var i = 0; i < Math.Min(a.Length, b.Length); i++)
        {
            var x = R13Mean(a[i]);
            var y = R13Mean(b[i]);
            var (mean, pct) = Diff(a[i], b[i]);
            parts.Add($"{names[i]}: dL={y.L - x.L:+0.00;-0.00} dR={y.R - x.R:+0.0;-0.0} dG={y.G - x.G:+0.0;-0.0} dB={y.B - x.B:+0.0;-0.0} L={y.L:F1} ch>12={pct:F1}% mAbs={mean:F2}");
        }
        return string.Join(" | ", parts);
    }

    // ---------- T1/T2: Unity light, with and without the game's MultiLight component ----------

    private Func<(Action, Func<string>)?> R13ApplyUnityLight(bool multi, bool spot) => () =>
    {
        var go = new GameObject("StellarR13Lamp");
        go.SetActive(false);
        go.transform.position = _r13Lamp;
        var chest = LiveModel(SelfEntity())!.GetChestPosition();
        go.transform.rotation = Quaternion.LookRotation(chest - _r13Lamp);
        var l = go.AddComponent(Il2CppType.Of<Light>()).Cast<Light>();
        l.type = spot ? LightType.Spot : LightType.Point;
        if (spot) { l.spotAngle = 60f; l.innerSpotAngle = 30f; }
        l.color = R13Warm;
        l.intensity = 10f;
        l.range = 5f;
        l.shadows = LightShadows.None;
        l.cullingMask = -1;
        l.renderingLayerMask = -1;
        l.renderMode = LightRenderMode.ForcePixel;
        MultiLight? ml = null;
        if (multi)
        {
            ml = go.AddComponent(Il2CppType.Of<MultiLight>()).Cast<MultiLight>();
            ml.lightLayer = MultiLightLayer.Everything;
            ml.type = MultiLight.LightType.Common;
            ml.maxDistance = 60f;
        }
        var mm = MultiLightManager.Instance;
        var before = mm?.lightDict_?.Count ?? -1;
        go.SetActive(true);
        if (ml != null) { try { ml.MarkDirty(); } catch (Exception ex) { Log($"R13 MarkDirty FAILED {ex.GetType().Name}"); } }
        Log($"R13 lamp created multi={multi} spot={spot} mlDict {before}->{mm?.lightDict_?.Count ?? -1} light.en={l.isActiveAndEnabled}");
        return (() => UnityEngine.Object.Destroy(go), () =>
            $"mlDict={MultiLightManager.Instance?.lightDict_?.Count} ml={(ml == null ? "-" : $"idx={ml.InfoIndex} layer={ml.lightLayer} mask=0x{ml.cullingMask_:X} fall={ml.falloffExponent:F2} inv={ml.inverseSquared} spec={ml.specularScale:F2}")}");
    };

    /// <summary>T2c: a lamp with the exact Light + MultiLight settings of the nearest enabled scene MultiLight, moved to our spot.</summary>
    private Func<(Action, Func<string>)?> R13ApplySceneClone() => () =>
    {
        var all = UnityEngine.Object.FindObjectsOfType(Il2CppType.Of<MultiLight>());
        MultiLight? src = null;
        var best = float.MaxValue;
        var origin = LiveModel(SelfEntity())!.GetAttrGoPosition();
        for (var i = 0; i < all.Length; i++)
        {
            var m = all[i].Cast<MultiLight>();
            if (!m.isActiveAndEnabled || m.Light == null || m.Light.type == LightType.Directional) continue;
            var d = Vector3.Distance(origin, m.transform.position);
            if (d < best) { best = d; src = m; }
        }
        if (src == null) return null;
        var sl = src.Light;
        var go = new GameObject("StellarR13LampClone");
        go.SetActive(false);
        go.layer = src.gameObject.layer;
        go.transform.position = _r13Lamp;
        go.transform.rotation = Quaternion.LookRotation(LiveModel(SelfEntity())!.GetChestPosition() - _r13Lamp);
        var l = go.AddComponent(Il2CppType.Of<Light>()).Cast<Light>();
        l.type = sl.type; l.spotAngle = sl.spotAngle; l.innerSpotAngle = sl.innerSpotAngle;
        l.color = R13Warm; l.intensity = Math.Max(sl.intensity, 3f) * 3f; l.range = Math.Max(sl.range, 5f);
        l.cullingMask = sl.cullingMask; l.renderingLayerMask = sl.renderingLayerMask; l.renderMode = sl.renderMode; l.shadows = LightShadows.None;
        var ml = go.AddComponent(Il2CppType.Of<MultiLight>()).Cast<MultiLight>();
        ml.lightLayer = src.lightLayer; ml.type = src.type; ml.isGridLow = src.isGridLow; ml.maxDistance = src.maxDistance;
        ml.falloffExponent = src.falloffExponent; ml.specularScale = src.specularScale; ml.inverseSquared = src.inverseSquared; ml.IsReflection = src.IsReflection;
        go.SetActive(true);
        Log($"R13 clone of '{src.gameObject.name}' d={best:F1}m type={sl.type} i={l.intensity:F2} r={l.range:F1} mask=0x{l.cullingMask:X} rl=0x{l.renderingLayerMask:X} layer={go.layer} ML[layer={ml.lightLayer} type={ml.type} grid={ml.isGridLow} fall={ml.falloffExponent:F2} inv={ml.inverseSquared}]");
        return (() => UnityEngine.Object.Destroy(go), () => $"mlDict={MultiLightManager.Instance?.lightDict_?.Count} idx={ml.InfoIndex}");
    };

    // ---------- T3: the effect-light path (ZEffectLightPass) ----------

    private Func<(Action, Func<string>)?> R13ApplyEffectLight() => () =>
    {
        var go = new GameObject("StellarR13EffectLight");
        go.SetActive(false);
        go.transform.position = _r13Lamp;
        var el = go.AddComponent(Il2CppType.Of<EffectLight>()).Cast<EffectLight>();
        el.lightColor_ = new Color(R13Warm.r * 4f, R13Warm.g * 4f, R13Warm.b * 4f, 1f);
        el.lightRange_ = 5f;
        el.animiCuve = AnimationCurve.Constant(0f, 100f, 1f);
        el.camera_ = MainCam();
        go.SetActive(true);
        var ep = ZScriptableRendererPassSingleton<ZEffectLightPass>.Instance;
        return (() => UnityEngine.Object.Destroy(go), () =>
            $"effPass lightCount={ep?.lightCount} list={ep?.effectLights_?.Count} el.color={el.lightColor} el.range={el.lightRange:F2} aniIndex={el.aniIndex:F2}");
    };

    // ---------- T4: the character light (one global, written by ZGlobalParamsPass) ----------

    private Func<(Action, Func<string>)?> R13ApplyGpCharLight() => () =>
    {
        var gp = ZScriptableRendererPassSingleton<ZGlobalParamsPass>.Instance;
        if (gp == null) return null;
        var go = new GameObject("StellarR13CharLight");
        go.transform.position = _r13Lamp;
        var l = go.AddComponent(Il2CppType.Of<Light>()).Cast<Light>();
        l.type = LightType.Point; l.color = R13Warm; l.intensity = 10f; l.range = 5f; l.enabled = false;
        gp.UpdateCharacterLight(true, l);
        Arm("r13.gpcl.tick", () => { });
        ProbeTicks.LateTick = () => { try { gp.UpdateCharacterLight(true, l); } catch { } };
        return (() =>
        {
            ProbeTicks.LateTick = null;
            try { gp.UpdateCharacterLight(false, l); } catch { }
            _releases.Remove("r13.gpcl.tick");
            UnityEngine.Object.Destroy(go);
        }, () => { var g = gp.m_CharacterLight; return g == null ? "gcl null" : $"gcl enable={g.m_enable} pos=({g.m_position.x:F2},{g.m_position.y:F2},{g.m_position.z:F2}) i={g.m_intensity:F2} r={g.m_range:F2}"; });
    };

    private Func<(Action, Func<string>)?> R13ApplyCharLightFull() => () =>
    {
        var cm = CameraManager.Instance;
        var cl = CharacterLight.Instance;
        if (cm == null || cl == null) return null;
        var pre = Snapshot(CharLightParams);
        var v = cm.characterLight_;
        var vActive = v != null && v.active;
        var pl = cl.PointLight;
        var plEn = pl != null && pl.enabled;
        var plPos = pl == null ? Vector3.zero : pl.transform.position;
        var prior = (cl.LightState, pl == null ? 1f : pl.intensity, pl == null ? 1f : pl.range, pl == null ? Color.white : pl.color);
        cm.UpdateCharacterLight(true, 10f, 5f, R13Warm);
        if (v != null) { v.active = true; v.Enable.overrideState = true; v.Enable.value = true; }
        cl.ForceEnable(true);
        cl.SetLight(true, 10f, 5f, R13Warm);
        if (pl != null) { pl.transform.position = _r13Lamp; pl.enabled = true; }
        cl.UpdateVolume();
        return (() =>
        {
            try { cl.SetLight(prior.Item1, prior.Item2, prior.Item3, prior.Item4); cl.ForceEnable(plEn); } catch { }
            if (pl != null) { pl.transform.position = plPos; pl.enabled = plEn; }
            RestoreSnapshot(pre, "R13 T4b");
            var vv = CameraManager.Instance?.characterLight_;
            if (vv != null) vv.active = vActive;
            try { CharacterLight.Instance?.UpdateVolume(); } catch { }
        }, () => $"CL state={cl.LightState} pl.en={pl?.enabled} pl.pos={(pl == null ? "-" : V(pl.transform.position))} gcl.enable={ZScriptableRendererPassSingleton<ZGlobalParamsPass>.Instance?.m_CharacterLight?.m_enable}");
    };

    // ---------- T5-T7: per-character material paths (snapshot + exact write-back) ----------

    private sealed class R13MatSnap
    {
        public Material M = null!;
        public readonly Dictionary<int, (ShaderPropertyType T, Vector4 V)> Props = new();
        public string[] Kw = Array.Empty<string>();
        public readonly Dictionary<string, bool> Passes = new();
    }

    private List<R13MatSnap> R13SnapMats()
    {
        var list = new List<R13MatSnap>();
        foreach (var (_, m) in R13SelfMats())
        {
            var s = new R13MatSnap { M = m };
            var sh = m.shader;
            if (sh != null)
            {
                var pc = sh.GetPropertyCount();
                for (var i = 0; i < pc; i++)
                {
                    var t = sh.GetPropertyType(i);
                    var id = Shader.PropertyToID(sh.GetPropertyName(i));
                    if (t == ShaderPropertyType.Float || t == ShaderPropertyType.Range) s.Props[id] = (t, new Vector4(m.GetFloat(id), 0, 0, 0));
                    else if (t == ShaderPropertyType.Vector) s.Props[id] = (t, m.GetVector(id));
                    else if (t == ShaderPropertyType.Color) { var c = m.GetColor(id); s.Props[id] = (t, new Vector4(c.r, c.g, c.b, c.a)); }
                }
            }
            s.Kw = m.shaderKeywords?.ToArray() ?? Array.Empty<string>();
            for (var i = 0; i < m.passCount; i++) { var pn = m.GetPassName(i); s.Passes[pn] = m.GetShaderPassEnabled(pn); }
            list.Add(s);
        }
        return list;
    }

    private static Vector4 R13Read(Material m, int id, ShaderPropertyType t) =>
        t == ShaderPropertyType.Vector ? m.GetVector(id)
        : t == ShaderPropertyType.Color ? (Vector4)m.GetColor(id)
        : new Vector4(m.GetFloat(id), 0, 0, 0);

    /// <summary>Changed properties / keywords / passes vs the snapshot (names via the material's shader).</summary>
    private static string R13MatDiff(List<R13MatSnap> snaps)
    {
        var parts = new List<string>();
        foreach (var s in snaps)
        {
            var m = s.M;
            if (m == null) continue;
            var sh = m.shader;
            var names = new Dictionary<int, string>();
            if (sh != null) for (var i = 0; i < sh.GetPropertyCount(); i++) names[Shader.PropertyToID(sh.GetPropertyName(i))] = sh.GetPropertyName(i);
            foreach (var (id, (t, v)) in s.Props)
            {
                var now = R13Read(m, id, t);
                if ((now - v).sqrMagnitude > 1e-8f) parts.Add($"{m.name}:{(names.TryGetValue(id, out var n) ? n : id.ToString())} ({v.x:F2},{v.y:F2},{v.z:F2},{v.w:F2})->({now.x:F2},{now.y:F2},{now.z:F2},{now.w:F2})");
            }
            var kw = m.shaderKeywords?.ToArray() ?? Array.Empty<string>();
            foreach (var k in kw.Except(s.Kw)) parts.Add($"{m.name}:+kw {k}");
            foreach (var k in s.Kw.Except(kw)) parts.Add($"{m.name}:-kw {k}");
            foreach (var (pn, en) in s.Passes) if (m.GetShaderPassEnabled(pn) != en) parts.Add($"{m.name}:pass {pn} {en}->{!en}");
        }
        return parts.Count == 0 ? "no material change" : $"{parts.Count} changes: " + string.Join("; ", parts.Take(24));
    }

    private void R13RestoreMats(List<R13MatSnap> snaps, string tag)
    {
        var n = 0;
        foreach (var s in snaps)
        {
            var m = s.M;
            if (m == null) continue;
            foreach (var (id, (t, v)) in s.Props)
            {
                if ((R13Read(m, id, t) - v).sqrMagnitude <= 1e-8f) continue;
                if (t == ShaderPropertyType.Vector) m.SetVector(id, v);
                else if (t == ShaderPropertyType.Color) m.SetColor(id, new Color(v.x, v.y, v.z, v.w));
                else m.SetFloat(id, v.x);
                n++;
            }
            var kw = m.shaderKeywords?.ToArray() ?? Array.Empty<string>();
            foreach (var k in kw.Except(s.Kw)) { m.DisableKeyword(k); n++; }
            foreach (var k in s.Kw.Except(kw)) { m.EnableKeyword(k); n++; }
            foreach (var (pn, en) in s.Passes) if (m.GetShaderPassEnabled(pn) != en) { m.SetShaderPassEnabled(pn, en); n++; }
        }
        Log($"R13 {tag} material write-back: {n} writes; residual: {R13MatDiff(snaps)}");
    }

    private Func<(Action, Func<string>)?> R13MatTrial(string tag, Action<RenderCompBase> apply, Action<RenderCompBase>? gameOff = null) => () =>
    {
        var rc = LiveModel(SelfEntity())?.RenderComp?.TryCast<RenderCompBase>();
        if (rc == null) return null;
        var snaps = R13SnapMats();
        apply(rc);
        var diff = R13MatDiff(snaps);
        Log($"R13 {tag} applied: {diff}");
        return (() =>
        {
            if (gameOff != null) { try { gameOff(rc); Log($"R13 {tag} game-off: {R13MatDiff(snaps)}"); } catch (Exception ex) { Log($"R13 {tag} game-off FAILED {ex.GetType().Name}"); } }
            R13RestoreMats(snaps, tag);
        }, () => $"mats={snaps.Count}");
    };

    private Func<(Action, Func<string>)?> R13ApplyFixedLight(int variant) => R13MatTrial($"T5.{variant}", rc =>
    {
        var chest = LiveModel(SelfEntity())!.GetChestPosition();
        var dir = (_r13Lamp - chest).normalized;
        var v = variant switch { 0 => new Vector4(dir.x, dir.y, dir.z, 1f), 1 => new Vector4(0f, 0f, -1f, 1f), _ => new Vector4(1f, 0f, 0f, 1f) };
        var data = new ModelFixedLightData { LightParms = v };
        rc.SetFixedLight(ref data, R13All);
    });

    private Func<(Action, Func<string>)?> R13ApplyFresnel() => R13MatTrial("T6a", rc =>
        rc.SetFresnelEffect(1f, R13Warm, new Vector4(-0.7f, 1f, 1f, 1f), EModelRenderMask.All),
        rc => rc.SetFresnelEffect(0f, R13Warm, new Vector4(-0.7f, 1f, 1f, 1f), EModelRenderMask.All));

    private Func<(Action, Func<string>)?> R13ApplyCreatureRim() => R13MatTrial("T6b", rc =>
    {
        rc.SetCreatureRimPass(true, R13All);
        rc.SetCreatureRimWidth(3f, R13All);
        rc.SetFresnel(1f, 1f, R13All);
    });

    /// <summary>T7: the URP additional-lights keyword on the character materials + a generic point light.</summary>
    private Func<(Action, Func<string>)?> R13ApplyAddLightsKeyword() => () =>
    {
        var light = R13ApplyUnityLight(true, false)();
        var mat = R13MatTrial("T7", rc => { rc.SetKeyWord("_ADDITIONAL_LIGHTS", true, R13All); })();
        var globalPrior = Shader.IsKeywordEnabled("_ADDITIONAL_LIGHTS");
        Shader.EnableKeyword("_ADDITIONAL_LIGHTS");
        return (() =>
        {
            if (!globalPrior) Shader.DisableKeyword("_ADDITIONAL_LIGHTS");
            mat?.Item1();
            light?.Item1();
        }, () => $"globalKw={Shader.IsKeywordEnabled("_ADDITIONAL_LIGHTS")} (prior {globalPrior}) {mat?.Item2()} {light?.Item2()}");
    };
}
