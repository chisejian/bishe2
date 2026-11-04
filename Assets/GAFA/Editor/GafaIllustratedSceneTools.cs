using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// GAFA 插画版场景工具：
///   GAFA/Illustrated/5. 设置插画版光照、天空与后期 —— 只改当前场景：太阳、环境光、天空盒、Volume，关掉 Cozy；
///                                       给 URP 渲染器加上 GafaIllustratedFeature（没有启用的 Volume 时不运行）
/// </summary>
public static class GafaIllustratedSceneTools
{
    const string Root = "Assets/GAFA";
    const string ProfilePath = "Assets/GAFA/Settings/GafaIllustratedProfile.asset";
    const string SkyMatPath = "Assets/GAFA/Materials/Illustrated/GAFA_Ill_Sky.mat";
    const string NoisePath = "Assets/GAFA/Textures/GafaNoise.png";

    // 插画版的太阳（illustrated/src/main.js，three.js 场地坐标，指向太阳）
    static readonly Vector3 SunDirSite = new Vector3(-0.75f, 0.56f, 0.36f).normalized;
    static readonly Color SunColor = new Color(1f, 0xf0 / 255f, 0xd6 / 255f);
    const float SunIntensity = 1.35f;

    // ================================================================ 5. 光照、天空、后期
    [MenuItem("GAFA/Illustrated/5. Setup Illustrated Lighting, Sky And Post (Active Scene)")]
    public static void SetupScene()
    {
        var scene = EditorSceneManager.GetActiveScene();
        EnsureRendererFeature();

        // ---- Volume：插画版专用配置，只含 GafaIllustratedPost（URP 自带的色调映射 / 泛光 / 暗角都不启用）
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
        if (profile == null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ProfilePath));
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, ProfilePath);
        }
        if (!profile.TryGet(out GafaIllustratedPost post))
        {
            post = profile.Add<GafaIllustratedPost>(true);
            post.name = nameof(GafaIllustratedPost);
            AssetDatabase.AddObjectToAsset(post, profile);
        }
        post.active = true;
        post.enable.Override(true);
        EditorUtility.SetDirty(post);
        // 管线资源上的默认 Volume（SampleSceneProfile）带了 Neutral 色调映射、泛光和暗角，会叠在插画调色之后，这里显式关掉
        var tonemap = GetOrAdd<Tonemapping>(profile);
        tonemap.mode.Override(TonemappingMode.None);
        GetOrAdd<Bloom>(profile).intensity.Override(0f);
        GetOrAdd<Vignette>(profile).intensity.Override(0f);
        EditorUtility.SetDirty(profile);
        var volumes = Object.FindObjectsByType<Volume>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Where(v => v.isGlobal).ToList();
        Volume volume = volumes.FirstOrDefault();
        if (volume == null)
        {
            var go = new GameObject("Global Volume");
            Undo.RegisterCreatedObjectUndo(go, "Create Volume");
            volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
        }
        foreach (var v in volumes)
        {
            Undo.RecordObject(v, "GAFA Illustrated Volume");
            v.sharedProfile = v == volume ? profile : v.sharedProfile;
            if (v != volume) v.enabled = false;
        }

        // ---- 天空盒
        var sky = AssetDatabase.LoadAssetAtPath<Material>(SkyMatPath);
        if (sky == null)
        {
            sky = new Material(Shader.Find("GAFA/Illustrated/Sky"));
            AssetDatabase.CreateAsset(sky, SkyMatPath);
        }
        sky.SetTexture("_NoiseTex", AssetDatabase.LoadAssetAtPath<Texture2D>(NoisePath));
        EditorUtility.SetDirty(sky);

        // ---- Cozy 天气系统会接管太阳、天空和环境光，插画版场景里关掉
        foreach (var root in scene.GetRootGameObjects())
        {
            if (!root.activeSelf) continue;
            bool cozy = root.name.Contains("Cozy") || root.GetComponents<MonoBehaviour>().Any(c => c != null && c.GetType().Namespace != null && c.GetType().Namespace.StartsWith("DistantLands.Cozy"));
            if (!cozy) continue;
            Undo.RecordObject(root, "Disable Cozy");
            root.SetActive(false);
        }

        // ---- 太阳：方向、颜色、强度与原项目一致（three.js 场地坐标经 GafaSiteSpace 换到世界坐标）
        var site = Object.FindFirstObjectByType<GafaSiteSpace>();
        Vector3 dirLocal = site != null && site.mirrorX ? new Vector3(-SunDirSite.x, SunDirSite.y, SunDirSite.z) : SunDirSite;
        Vector3 sunWorld = site != null ? site.transform.TransformDirection(dirLocal).normalized : dirLocal;
        var sun = Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).FirstOrDefault(l => l.type == LightType.Directional);
        if (sun == null)
        {
            var go = new GameObject("GAFA Sun");
            Undo.RegisterCreatedObjectUndo(go, "Create Sun");
            sun = go.AddComponent<Light>();
            sun.type = LightType.Directional;
        }
        foreach (var l in Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (l.type != LightType.Directional || l == sun) continue;
            Undo.RecordObject(l, "GAFA Illustrated Lights");
            l.enabled = false;
        }
        Undo.RecordObject(sun, "GAFA Illustrated Sun");
        Undo.RecordObject(sun.transform, "GAFA Illustrated Sun");
        sun.enabled = true;
        sun.transform.rotation = Quaternion.LookRotation(-sunWorld);
        sun.color = SunColor;
        sun.intensity = SunIntensity;
        sun.useColorTemperature = false;
        sun.shadows = LightShadows.Soft;
        sun.shadowStrength = 1f;
        // 原项目的阴影覆盖到远处背景树；URP 的阴影距离是全局设置，只在本场景渲染时临时加大
        if (sun.GetComponent<GafaIllustratedShadowRange>() == null) Undo.AddComponent<GafaIllustratedShadowRange>(sun.gameObject);

        // ---- 环境：天空盒 + 与插画材质相同的半球环境光（给第三方模型用），关雾（空气透视在后期里做）
        RenderSettings.skybox = sky;
        RenderSettings.sun = sun;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        var skyAmb = new Color(0.4f, 0.42f, 0.47f);
        var groundAmb = new Color(0.36f, 0.33f, 0.28f);
        RenderSettings.ambientSkyColor = skyAmb.gamma;
        RenderSettings.ambientEquatorColor = Color.Lerp(skyAmb, groundAmb, 0.5f).gamma;
        RenderSettings.ambientGroundColor = groundAmb.gamma;
        RenderSettings.ambientIntensity = 1f;
        RenderSettings.fog = false;

        // ---- 相机：后期后做 SMAA（原项目是 4×MSAA），开抖动去色带
        foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            var data = cam.GetUniversalAdditionalCameraData();
            if (data == null) continue;
            Undo.RecordObject(data, "GAFA Illustrated Camera");
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.antialiasingQuality = AntialiasingQuality.High;
            data.dithering = true;
        }

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log($"[GAFA] 插画版光照 / 天空 / 后期已设置到场景 {scene.name}（Volume 配置：{ProfilePath}）。");
    }

    static T GetOrAdd<T>(VolumeProfile profile) where T : VolumeComponent
    {
        if (profile.TryGet(out T c)) return c;
        c = profile.Add<T>(false);
        c.name = typeof(T).Name;
        AssetDatabase.AddObjectToAsset(c, profile);
        EditorUtility.SetDirty(profile);
        return c;
    }

    // 给当前管线的每个 Universal Renderer 加上 GafaIllustratedFeature（与 Inspector 里 Add Renderer Feature 相同的做法）
    static void EnsureRendererFeature()
    {
        var shader = Shader.Find("Hidden/GAFA/IllustratedPost");
        var assets = new HashSet<UniversalRenderPipelineAsset>();
        if (GraphicsSettings.defaultRenderPipeline is UniversalRenderPipelineAsset d) assets.Add(d);
        if (QualitySettings.renderPipeline is UniversalRenderPipelineAsset q) assets.Add(q);
        foreach (var asset in assets)
        {
            var list = new SerializedObject(asset).FindProperty("m_RendererDataList");
            for (int i = 0; i < list.arraySize; i++)
            {
                if (!(list.GetArrayElementAtIndex(i).objectReferenceValue is UniversalRendererData data)) continue;
                var existing = data.rendererFeatures.OfType<GafaIllustratedFeature>().FirstOrDefault();
                if (existing == null)
                {
                    existing = ScriptableObject.CreateInstance<GafaIllustratedFeature>();
                    existing.name = "GafaIllustrated";
                    Undo.RegisterCreatedObjectUndo(existing, "Add GAFA Renderer Feature");
                    AssetDatabase.AddObjectToAsset(existing, data);
                    AssetDatabase.TryGetGUIDAndLocalFileIdentifier(existing, out string _, out long localId);
                    var so = new SerializedObject(data);
                    var features = so.FindProperty("m_RendererFeatures");
                    var map = so.FindProperty("m_RendererFeatureMap");
                    features.arraySize++;
                    features.GetArrayElementAtIndex(features.arraySize - 1).objectReferenceValue = existing;
                    map.arraySize++;
                    map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
                    so.ApplyModifiedProperties();
                    Debug.Log($"[GAFA] 已给 {data.name} 添加 GafaIllustratedFeature（没有启用 GAFA/Illustrated Post 的场景不受影响）。");
                }
                var fso = new SerializedObject(existing);
                fso.FindProperty("m_PostShader").objectReferenceValue = shader;
                fso.ApplyModifiedPropertiesWithoutUndo();
                existing.SetActive(true);
                EditorUtility.SetDirty(data);
            }
        }
    }
}
