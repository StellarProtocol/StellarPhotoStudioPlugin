using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Bokura.Rendering;
using Il2CppInterop.Common.XrefScans;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.XrefScans;
using Panda.ZGame;
using UnityEngine;
using UnityEngine.Rendering;

namespace Stellar.PhotoStudio.FreeCamProbe;

/// <summary>
/// Run 13 (2026-10-03): "real lamps on characters" feasibility. Static census of the light paths the character shader
/// can see: shader keywords/properties of the local player's materials, the cluster (MultiLight), effect-light
/// (ZEffectLightPass) and character-light (GlobalCharacterLight via ZGlobalParamsPass) shader globals, the character
/// draw pass settings, and xref scans (string literals + callees + users) of the light APIs. Read-only.
/// </summary>
public sealed partial class FreeCamProbe
{
    private static readonly string[] R13Keywords =
    {
        "_ADDITIONAL_LIGHTS", "_ADDITIONAL_LIGHTS_VERTEX", "_ADDITIONAL_LIGHT_SHADOWS", "_FORWARD_PLUS", "_CLUSTERED_RENDERING",
        "_LIGHT_SWITCH", "_RENDERING_SPOT_LIGHT", "_MAIN_LIGHT_SHADOWS", "_SPECIFICEFFECT_ON", "_RESONANCE_ON",
    };

    private static readonly string[] R13LightWords =
        { "LIGHT", "RIM", "FRESNEL", "EMIS", "CLUSTER", "MULTI", "ADD", "CHAR", "FIXED", "POINT", "SPOT", "EFFECT", "CREATURE" };

    private IEnumerator StepInventory13()
    {
        Log($"R13 time={DateTime.UtcNow:O} scene={Lua("return tostring(Z.StageMgr.GetCurrentSceneId())")} quality={QualitySettings.GetQualityLevel()}");
        R13Globals("inventory");
        Try("R13 keywords", () =>
        {
            Log("R13 GLOBAL-KW " + string.Join(" ", R13Keywords.Select(k => $"{k}={Shader.IsKeywordEnabled(k)}")));
            var en = Shader.enabledGlobalKeywords;
            var names = new List<string>();
            if (en != null) for (var i = 0; i < en.Length; i++) names.Add(en[i].name);
            Log($"R13 GLOBAL-KW enabled({names.Count}): {string.Join(" ", names.OrderBy(n => n))}");
        });
        Try("R13 layers", () =>
        {
            var parts = new List<string>();
            for (var i = 0; i < 32; i++) { var n = LayerMask.LayerToName(i); if (!string.IsNullOrEmpty(n)) parts.Add($"{i}={n}"); }
            Log($"R13 LAYERS {string.Join(" ", parts)}");
        });
        Try("R13 managers", R13Managers);
        Try("R13 scene lights", R13SceneLights);
        Try("R13 draw pass", R13DrawPass);
        Try("R13 self materials", R13SelfMaterials);
        yield return null;
    }

    /// <summary>Run 13a hung the client inside the xref scan of ZEffectLightPass.OnSetup (an override); 13b scans
    /// only the non-override targets not yet covered, scan-only (no UsedBy), after the trials.</summary>
    private IEnumerator StepXref13()
    {
        Try("R13 xref", R13Xrefs);
        yield break;
    }

    /// <summary>The shader globals the light paths write (ids from the game's own static fields).</summary>
    private void R13Globals(string tag)
    {
        var parts = new List<string>();
        void F(string n, Func<int> id) { try { parts.Add($"{n}={Shader.GetGlobalFloat(id()):F3}/i{Shader.GetGlobalInt(id())}"); } catch (Exception ex) { parts.Add($"{n}=err:{ex.GetType().Name}"); } }
        void V4(string n, Func<int> id) { try { var v = Shader.GetGlobalVector(id()); parts.Add($"{n}=({v.x:F2},{v.y:F2},{v.z:F2},{v.w:F2})"); } catch (Exception ex) { parts.Add($"{n}=err:{ex.GetType().Name}"); } }
        void Arr(string n, Func<int> id)
        {
            try
            {
                var a = Shader.GetGlobalVectorArray(id());
                if (a == null) { parts.Add($"{n}=null"); return; }
                var nz = 0; var first = "";
                for (var i = 0; i < a.Length; i++) { var v = a[i]; if (v != Vector4.zero) { nz++; if (first.Length == 0) first = $"[{i}]({v.x:F2},{v.y:F2},{v.z:F2},{v.w:F2})"; } }
                parts.Add($"{n}=len{a.Length}/nz{nz}{first}");
            }
            catch (Exception ex) { parts.Add($"{n}=err:{ex.GetType().Name}"); }
        }
        V4("gCL.Enable", () => GlobalCharacterLight.g_CharacterLightEnable);
        V4("gCL.Color", () => GlobalCharacterLight.g_CharacterLightColor);
        V4("gCL.Dir", () => GlobalCharacterLight.g_CharacterLightDirection);
        V4("gCL.Atten", () => GlobalCharacterLight.g_CharacterLightAttenuation);
        F("gCL.EnableF", () => GlobalCharacterLight.g_CharacterLightEnable);
        F("effLight.Count", () => ZEffectLightPass.EffectLightCount);
        Arr("effLight.Pos", () => ZEffectLightPass.EffectLightPos);
        Arr("effLight.Color", () => ZEffectLightPass.EffectLightColor);
        F("ml.Count", () => MultiLightManager.g_MultiLightCount);
        F("ml.StaticCount", () => MultiLightManager.g_StaticLightCount);
        Arr("ml.AddColors", () => MultiLightManager.g_AddLightColors);
        Arr("ml.AddDirs", () => MultiLightManager.g_AddLightDirections);
        F("mlp.UseCluster", () => MultiLightPass.UseClusterLight);
        V4("mlp.ClusterInfo", () => MultiLightPass.ClusterLightInfo);
        F("fwd.AddCount", () => ZForwardLights.LightConstantBuffer._AdditionalLightsCount);
        V4("fwd.AddCountV", () => ZForwardLights.LightConstantBuffer._AdditionalLightsCount);
        Arr("fwd.AddColor", () => ZForwardLights.LightConstantBuffer._AdditionalLightsColor);
        V4("fwd.MainColor", () => ZForwardLights.LightConstantBuffer._MainLightColor);
        Log($"R13 GLOBALS {tag}: {string.Join(" ", parts)}");
    }

    private void R13Managers()
    {
        var mm = MultiLightManager.Instance;
        Log(mm == null ? "R13 MLM null" :
            $"R13 MLM curLayer={mm.CurLightLayer} lightDict={mm.lightDict_?.Count} additional={mm.additionalLightList_?.Count} statics={mm.staticLights_?.Count} " +
            $"sceneSize={mm.SceneSize:F1} useGridLow={mm.UseGridLow} maxZ={mm.MaxLightZDistance:F1} mobile={MultiLightManager.s_IsMobileApi} maxScreen={MultiLightManager.g_maxScreenLightCount}");
        Log($"R13 MLP useClusterCulling={MultiLightPass.UseClusterLightCulling} useGridLow={MultiLightPass.UseGridLow} active={ZScriptableRendererPassSingleton<MultiLightPass>.IsRuntimeActive}");
        var ep = ZScriptableRendererPassSingleton<ZEffectLightPass>.Instance;
        Log(ep == null ? "R13 EFFPASS null" : $"R13 EFFPASS active={ZScriptableRendererPassSingleton<ZEffectLightPass>.IsRuntimeActive} max={ZEffectLightPass.k_MaxCount} lightCount={ep.lightCount} list={ep.effectLights_?.Count}");
        var gp = ZScriptableRendererPassSingleton<ZGlobalParamsPass>.Instance;
        var gcl = gp?.m_CharacterLight;
        Log(gcl == null ? $"R13 GCL null (pass={(gp == null ? "null" : "ok")})" :
            $"R13 GCL enable={gcl.m_enable} pos=({gcl.m_position.x:F2},{gcl.m_position.y:F2},{gcl.m_position.z:F2}) i={gcl.m_intensity:F2} r={gcl.m_range:F2} c={gcl.m_color} mobile={GlobalCharacterLight.g_IsMobileApi}");
        var cl = CharacterLight.Instance;
        Log(cl == null ? "R13 CL null" : $"R13 CL state={cl.LightState} mask=0x{cl.cullingMask_:X} iface={cl.isInterfaceInvoke_} light={(cl.PointLight == null ? "null" : $"en={cl.PointLight.enabled} mask=0x{cl.PointLight.cullingMask:X} rl=0x{cl.PointLight.renderingLayerMask:X} layer={cl.PointLight.gameObject.layer}")}");
        var cm = CameraManager.Instance;
        var v = cm?.characterLight_;
        Log(v == null ? "R13 CLV null" : $"R13 CLV active={v.active} enable={v.Enable.overrideState}/{v.Enable.value} i={v.Intensity.value:F2} r={v.Range.value:F2} c={v.LightColor.value}");
        var stack = VolumeManager.instance?.stack;
        if (stack != null)
        {
            var sv = stack.GetComponent(Il2CppType.Of<CharacterLightVolume>())?.TryCast<CharacterLightVolume>();
            Log(sv == null ? "R13 STACK CLV null" : $"R13 STACK CLV enable={sv.Enable.value} i={sv.Intensity.value:F2} r={sv.Range.value:F2} c={sv.LightColor.value} isActive={sv.IsActive()}");
        }
        var cap = ZScriptableRendererPassSingleton<ZCompositionAfterLightingPass>.Instance;
        if (cap != null)
            Log($"R13 POSTRIM width={cap.RimLightWidth:F3} dist={cap.RimLightDistance:F3} thr={cap.RimLightThreshold:F3} scale={cap.RimLightScale:F3} i={cap.RimLightIntensity:F3} grad={cap.RimGradientWeight:F3} dir={cap.RimDirectionWeight:F3}");
    }

    private void R13SceneLights()
    {
        var lights = UnityEngine.Object.FindObjectsOfType(Il2CppType.Of<Light>());
        var self = LiveModel(SelfEntity());
        var origin = self?.GetAttrGoPosition() ?? Vector3.zero;
        var rows = new List<(float D, string S)>();
        var en = 0;
        for (var i = 0; i < lights.Length; i++)
        {
            var l = lights[i].Cast<Light>();
            if (l.isActiveAndEnabled) en++;
            var go = l.gameObject;
            var ml = go.GetComponent(Il2CppType.Of<MultiLight>())?.TryCast<MultiLight>();
            var d = Vector3.Distance(origin, l.transform.position);
            rows.Add((d, $"{go.name}:{l.type} en={l.isActiveAndEnabled} d={d:F1} i={l.intensity:F2} r={l.range:F1} c=({l.color.r:F2},{l.color.g:F2},{l.color.b:F2}) mask=0x{l.cullingMask:X} rl=0x{l.renderingLayerMask:X} layer={go.layer} mode={l.renderMode} " +
                         (ml == null ? "noML" : $"ML[layer={ml.lightLayer} type={ml.type} grid={ml.isGridLow} maxD={ml.maxDistance:F1} fall={ml.falloffExponent:F2} spec={ml.specularScale:F2} inv={ml.inverseSquared} refl={ml.IsReflection} shadow={ml.isCastShadow} mask=0x{ml.cullingMask_:X} idx={ml.InfoIndex}]")));
        }
        Log($"R13 LIGHTS total={lights.Length} enabled={en}");
        foreach (var r in rows.OrderBy(r => r.D).Take(14)) Log("R13 LIGHT " + r.S);
        Log($"R13 COMPONENTS MultiLight={UnityEngine.Object.FindObjectsOfType(Il2CppType.Of<MultiLight>()).Length} EffectLight={UnityEngine.Object.FindObjectsOfType(Il2CppType.Of<EffectLight>()).Length} " +
            $"StaticLight={UnityEngine.Object.FindObjectsOfType(Il2CppType.Of<StaticLight>()).Length} CharacterLight={UnityEngine.Object.FindObjectsOfType(Il2CppType.Of<CharacterLight>()).Length} " +
            $"MultiLightWatcher={UnityEngine.Object.FindObjectsOfType(Il2CppType.Of<MultiLightWatcher>()).Length}");
    }

    private void R13DrawPass()
    {
        var p = ZDrawObjectsPassSingleton<ZDrawCharacterPass>.Instance;
        if (p == null) { Log("R13 DRAWCHAR null"); return; }
        var tags = new List<string>();
        var tl = p.m_ShaderTagIdList;
        if (tl != null) for (var i = 0; i < tl.Count; i++) tags.Add(tl[i].name);
        var ds = p.m_DrawingOpaqueSettings;
        var fs = p.m_FilterSettings;
        Log($"R13 DRAWCHAR active={ZDrawObjectsPassSingleton<ZDrawCharacterPass>.IsRuntimeActive} tags=[{string.Join(",", tags)}] perObjectData={ds.perObjectData} " +
            $"filter.layerMask=0x{fs.layerMask:X} filter.renderingLayerMask=0x{fs.renderingLayerMask:X} queue={fs.renderQueueRange.lowerBound}..{fs.renderQueueRange.upperBound}");
    }

    private IEnumerable<(string Part, Material M)> R13SelfMats()
    {
        var m = LiveModel(SelfEntity());
        var rc = m?.RenderComp?.TryCast<RenderCompBase>();
        if (rc == null) yield break;
        var rs = rc.Renderers;
        if (rs == null) yield break;
        var seen = new HashSet<IntPtr>();
        for (var i = 0; i < rs.Length; i++)
        {
            var r = rs[i];
            if (r == null) continue;
            var part = $"{r.ModelRenderPart}/0x{r.ModelRenderMask:X}";
            foreach (var target in new[] { ZModelRenderMatBase.EMatSetTarget.Render, ZModelRenderMatBase.EMatSetTarget.Origin })
            {
                int n;
                try { n = r.getMatCount(target); } catch { continue; }
                for (var k = 0; k < n; k++)
                {
                    Material? mat = null;
                    try { mat = r.getMat(target, k); } catch { }
                    if (mat == null || !seen.Add(mat.Pointer)) continue;
                    yield return ($"{part}/{target}#{k}", mat);
                }
            }
        }
    }

    private void R13SelfMaterials()
    {
        var shadersDone = new HashSet<string>();
        var count = 0;
        foreach (var (part, mat) in R13SelfMats())
        {
            count++;
            var sh = mat.shader;
            var kw = mat.shaderKeywords;
            Log($"R13 MAT {part} '{mat.name}' shader='{sh?.name}' queue={mat.renderQueue} passes={mat.passCount} kw=[{(kw == null ? "" : string.Join(" ", kw))}]");
            if (sh == null || !shadersDone.Add(sh.name)) continue;
            var space = sh.keywordSpace.keywordNames;
            var all = new List<string>();
            if (space != null) for (var i = 0; i < space.Length; i++) all.Add(space[i]);
            Log($"R13 SHADER '{sh.name}' keywords({all.Count}): {string.Join(" ", all)}");
            var props = new List<string>();
            var pc = sh.GetPropertyCount();
            for (var i = 0; i < pc; i++) props.Add($"{sh.GetPropertyName(i)}:{sh.GetPropertyType(i)}");
            var lightish = props.Where(p => R13LightWords.Any(w => p.ToUpperInvariant().Contains(w))).ToList();
            Log($"R13 SHADER '{sh.name}' props={pc} lightish({lightish.Count}): {string.Join(" ", lightish)}");
            var passes = new List<string>();
            for (var i = 0; i < mat.passCount; i++) { var pn = mat.GetPassName(i); passes.Add($"{pn}={mat.GetShaderPassEnabled(pn)}"); }
            Log($"R13 SHADER '{sh.name}' passes: {string.Join(" ", passes)}");
        }
        Log($"R13 MAT total={count}");
    }

    private void R13Xrefs()
    {
        var targets = new (Type T, string M, int Params)[]
        {
            (typeof(EffectLight), "OnEnable", -1),
            (typeof(MultiLight), "OnEnable", -1),
            (typeof(MultiLightManager), "AddLight", -1),
            (typeof(RenderCompBase), "SetFixedLight", -1),
            (typeof(ZModelRenderMatBase), "SetFixedLight", -1),
            (typeof(ZModelRenderMatBase), "SetFresnelEffect", -1),
        };
        foreach (var (t, name, np) in targets)
        {
            var ms = t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(m => m.Name == name && (np < 0 || m.GetParameters().Length == np)).ToList();
            if (ms.Count == 0) { Log($"R13 XREF {t.Name}.{name}: not found"); continue; }
            foreach (var mb in ms)
            {
                var sig = $"{t.Name}.{name}({string.Join(",", mb.GetParameters().Select(p => p.ParameterType.Name))})";
                Log($"R13 XREF-START {sig}");
                var strs = new List<string>();
                var calls = new List<string>();
                try
                {
                    foreach (var x in XrefScanner.XrefScan(mb))
                    {
                        if (x.Type == XrefType.Global)
                        {
                            try { var o = x.ReadAsObject(); if (o != null) strs.Add("\"" + o.ToString() + "\""); } catch { }
                        }
                        else if (x.Type == XrefType.Method)
                        {
                            try { var r = x.TryResolve(); calls.Add(r == null ? $"0x{x.Pointer:X}" : $"{r.DeclaringType?.Name}.{r.Name}"); } catch { calls.Add("?"); }
                        }
                    }
                }
                catch (Exception ex) { strs.Add($"scan-err:{ex.GetType().Name}"); }
                var users = new List<string> { "not-scanned" };
                Log($"R13 XREF {sig} strings=[{string.Join(" ", strs.Distinct())}] calls=[{string.Join(" ", calls.Distinct().Take(40))}] usedBy=[{string.Join(" ", users.Distinct())}]");
            }
        }
    }
}
