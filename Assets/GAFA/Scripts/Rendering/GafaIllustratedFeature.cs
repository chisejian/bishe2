using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

/// <summary>
/// GAFA 插画版后期（Render Graph），移植自 TestGAFA/illustrated/src/post.js：
///   墨线缓冲（GafaInk 通道）→ 半分辨率 SAO + 双边模糊 → 合成（AO 着色 + 墨线 + 空气透视）→ 泛光 → 调色
/// 只在当前相机的 Volume 里有启用的 GafaIllustratedPost 时运行；URP 自带的色调映射 / 泛光应在该 Volume 里关掉。
/// </summary>
public class GafaIllustratedFeature : ScriptableRendererFeature
{
    [SerializeField] Shader m_PostShader;

    Material m_Material;
    GafaIllustratedPass m_Pass;

    public override void Create()
    {
        m_Pass = new GafaIllustratedPass { renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        ref var cameraData = ref renderingData.cameraData;
        if (cameraData.cameraType != CameraType.Game && cameraData.cameraType != CameraType.SceneView) return;
        if (!cameraData.postProcessEnabled) return;
        var settings = VolumeManager.instance.stack.GetComponent<GafaIllustratedPost>();
        if (settings == null || !settings.IsActive()) return;
        if (m_Material == null)
        {
            if (m_PostShader == null) m_PostShader = Shader.Find("Hidden/GAFA/IllustratedPost");
            if (m_PostShader == null) return;
            m_Material = CoreUtils.CreateEngineMaterial(m_PostShader);
        }
        m_Pass.Setup(m_Material, settings);
        m_Pass.ConfigureInput(ScriptableRenderPassInput.Depth);
        renderer.EnqueuePass(m_Pass);
    }

    protected override void Dispose(bool disposing)
    {
        CoreUtils.Destroy(m_Material);
        m_Material = null;
    }
}

class GafaIllustratedPass : ScriptableRenderPass
{
    static readonly ShaderTagId k_InkTag = new ShaderTagId("GafaInk");
    static readonly int[] k_BloomKernels = { 3, 5, 7, 9, 11 };

    static readonly int s_BlitTexture = Shader.PropertyToID("_BlitTexture");
    static readonly int s_BlitScaleBias = Shader.PropertyToID("_BlitScaleBias");
    static readonly int s_Depth = Shader.PropertyToID("_GafaDepth");
    static readonly int s_Ink = Shader.PropertyToID("_GafaInk");
    static readonly int s_AO = Shader.PropertyToID("_GafaAO");
    static readonly int[] s_Bloom =
    {
        Shader.PropertyToID("_GafaBloom0"), Shader.PropertyToID("_GafaBloom1"), Shader.PropertyToID("_GafaBloom2"),
        Shader.PropertyToID("_GafaBloom3"), Shader.PropertyToID("_GafaBloom4"),
    };
    static readonly int s_Proj = Shader.PropertyToID("_GafaProj");
    static readonly int s_Res = Shader.PropertyToID("_GafaRes");
    static readonly int s_CamToWorld = Shader.PropertyToID("_GafaCamToWorld");
    static readonly int s_CamPos = Shader.PropertyToID("_GafaCamPos");
    static readonly int s_SunDir = Shader.PropertyToID("_GafaSunDir");
    static readonly int s_AOParams = Shader.PropertyToID("_GafaAOParams");
    static readonly int s_AOTint = Shader.PropertyToID("_GafaAOTint");
    static readonly int s_BlurDir = Shader.PropertyToID("_GafaBlurDir");
    static readonly int s_Line = Shader.PropertyToID("_GafaLine");
    static readonly int s_InkCol = Shader.PropertyToID("_GafaInkCol");
    static readonly int s_InkParams = Shader.PropertyToID("_GafaInkParams");
    static readonly int s_HazeCol = Shader.PropertyToID("_GafaHazeCol");
    static readonly int s_HazeSun = Shader.PropertyToID("_GafaHazeSun");
    static readonly int s_Haze = Shader.PropertyToID("_GafaHaze");
    static readonly int s_BloomParams = Shader.PropertyToID("_GafaBloomParams");
    static readonly int s_BlurInvSize = Shader.PropertyToID("_GafaBlurInvSize");
    static readonly int s_BlurRadius = Shader.PropertyToID("_GafaBlurRadius");
    static readonly int s_Grade = Shader.PropertyToID("_GafaGrade");

    const int PassAO = 0, PassAOBlur = 1, PassComposite = 2, PassBloomPrefilter = 3, PassBloomBlur = 4, PassFinal = 5;

    Material m_Material;
    GafaIllustratedPost m_Settings;

    public void Setup(Material material, GafaIllustratedPost settings)
    {
        m_Material = material;
        m_Settings = settings;
    }

    class InkData
    {
        public RendererListHandle list;
    }

    // 每个全屏通道一份参数；纹理在执行时才可用，所以放进各自的 MaterialPropertyBlock（录制命令时复制）
    class FsData
    {
        public Material material;
        public int pass;
        public TextureHandle src, depth, ink, ao;
        public readonly TextureHandle[] bloom = new TextureHandle[5];
        public Shared shared;
        public Vector4 blurDir, blurInvSize;
        public float blurRadius;
        public readonly MaterialPropertyBlock mpb = new MaterialPropertyBlock();
    }

    // 每帧共享的常量
    class Shared
    {
        public Vector4 proj, res, aoParams, line, inkParams, haze, bloomParams, grade;
        public Vector3 aoTint, inkCol, hazeCol, hazeSun, sunDir, camPos;
        public Matrix4x4 camToWorld;
    }

    readonly Shared m_Shared = new Shared();

    static Vector3 Lin(Color c)
    {
        var l = c.linear;
        return new Vector3(l.r, l.g, l.b);
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
    {
        var resources = frameData.Get<UniversalResourceData>();
        var cameraData = frameData.Get<UniversalCameraData>();
        var renderingData = frameData.Get<UniversalRenderingData>();
        var lightData = frameData.Get<UniversalLightData>();
        if (resources.isActiveTargetBackBuffer || m_Material == null || m_Settings == null) return;

        TextureHandle color = resources.activeColorTexture;
        TextureHandle depthAttachment = resources.activeDepthTexture;
        TextureHandle depthTexture = resources.cameraDepthTexture;
        if (!color.IsValid() || !depthAttachment.IsValid() || !depthTexture.IsValid()) return;

        var desc = cameraData.cameraTargetDescriptor;
        int w = desc.width, h = desc.height;
        int hw = Mathf.Max(1, w / 2), hh = Mathf.Max(1, h / 2);
        var s = m_Settings;

        // ---------------------------------------------------------------- 每帧常量
        Matrix4x4 proj = cameraData.GetProjectionMatrix();
        Matrix4x4 view = cameraData.GetViewMatrix();
        var sh = m_Shared;
        sh.proj = new Vector4(proj.m00, proj.m11, proj.m02, proj.m12);
        sh.res = new Vector4(w, h, hw, hh);
        sh.camToWorld = view.inverse;
        sh.camPos = cameraData.worldSpaceCameraPos;
        sh.sunDir = new Vector3(0.75f, 0.56f, 0.36f).normalized;
        if (lightData.mainLightIndex >= 0 && lightData.mainLightIndex < lightData.visibleLights.Length)
            sh.sunDir = -(Vector3)lightData.visibleLights[lightData.mainLightIndex].localToWorldMatrix.GetColumn(2);
        sh.aoParams = new Vector4(s.aoRadius.value, s.aoIntensity.value, Time.frameCount % 64, s.aoStrength.value);
        sh.aoTint = Lin(s.aoTint.value);
        // 线宽随分辨率缩放：1080p 约 2px（近处一侧描线，偏移量就是线宽）
        sh.line = new Vector4(Mathf.Max(1.5f, h / 540f) * s.lineWidthScale.value, s.contourThreshold.value, s.creaseCos.value, s.lineFadeDistance.value);
        sh.inkCol = Lin(s.inkColor.value);
        sh.inkParams = new Vector4(s.inkAmount.value, s.outlineOtherObjects.value ? 1 : 0, 0, 0);
        sh.hazeCol = Lin(s.hazeColor.value);
        sh.hazeSun = Lin(s.hazeSunColor.value);
        sh.haze = new Vector4(s.hazeDensity.value, s.hazeHeight.value, s.hazeMax.value, s.hazeStart.value);
        sh.bloomParams = new Vector4(s.bloomThreshold.value, 0.01f, s.bloomStrength.value, s.bloomRadius.value);
        sh.grade = new Vector4(s.exposure.value, s.saturation.value, s.vignette.value, 0);

        // ---------------------------------------------------------------- 1. 墨线缓冲
        // 清屏 w = 3：没有墨线通道的物体（第三方模型）保持这个值，合成时只描轮廓；天空按深度判断
        var inkDesc = new TextureDesc(w, h)
        {
            name = "_GafaInk",
            colorFormat = GraphicsFormat.R16G16B16A16_SFloat,
            msaaSamples = (MSAASamples)Mathf.Max(1, desc.msaaSamples),
            filterMode = FilterMode.Point,
            clearBuffer = true,
            clearColor = new Color(0, 0, 0, 3),
        };
        TextureHandle ink = renderGraph.CreateTexture(inkDesc);
        using (var builder = renderGraph.AddRasterRenderPass<InkData>("GAFA Ink Buffer", out var data))
        {
            var drawing = RenderingUtils.CreateDrawingSettings(k_InkTag, renderingData, cameraData, lightData, SortingCriteria.CommonOpaque);
            var filtering = new FilteringSettings(RenderQueueRange.all);
            data.list = renderGraph.CreateRendererList(new RendererListParams(renderingData.cullResults, drawing, filtering));
            builder.UseRendererList(data.list);
            builder.SetRenderAttachment(ink, 0, AccessFlags.Write);
            builder.SetRenderAttachmentDepth(depthAttachment, AccessFlags.Read);
            builder.SetRenderFunc((InkData d, RasterGraphContext ctx) => ctx.cmd.DrawRendererList(d.list));
        }

        // ---------------------------------------------------------------- 2. SAO（半分辨率）+ 深度感知模糊
        var aoDesc = new TextureDesc(hw, hh) { colorFormat = GraphicsFormat.R16G16_SFloat, filterMode = FilterMode.Point, name = "_GafaAO_A" };
        TextureHandle aoA = renderGraph.CreateTexture(aoDesc);
        aoDesc.name = "_GafaAO_B";
        TextureHandle aoB = renderGraph.CreateTexture(aoDesc);
        Fullscreen(renderGraph, "GAFA SAO", PassAO, depthTexture, aoA, d => d.depth = depthTexture);
        Fullscreen(renderGraph, "GAFA SAO Blur H", PassAOBlur, aoA, aoB, d => d.blurDir = new Vector4(1f / hw, 0));
        Fullscreen(renderGraph, "GAFA SAO Blur V", PassAOBlur, aoB, aoA, d => d.blurDir = new Vector4(0, 1f / hh));

        // ---------------------------------------------------------------- 3. 合成
        var hdrDesc = new TextureDesc(w, h) { colorFormat = GraphicsFormat.R16G16B16A16_SFloat, filterMode = FilterMode.Bilinear, name = "_GafaComposite" };
        TextureHandle comp = renderGraph.CreateTexture(hdrDesc);
        Fullscreen(renderGraph, "GAFA Composite", PassComposite, color, comp, d =>
        {
            d.depth = depthTexture;
            d.ink = ink;
            d.ao = aoA;
        });

        // ---------------------------------------------------------------- 4. 泛光（UnrealBloomPass：半分辨率亮部 → 5 级高斯模糊）
        var bloomDesc = new TextureDesc(hw, hh) { colorFormat = GraphicsFormat.B10G11R11_UFloatPack32, filterMode = FilterMode.Bilinear, name = "_GafaBloomBright" };
        TextureHandle bright = renderGraph.CreateTexture(bloomDesc);
        Fullscreen(renderGraph, "GAFA Bloom Prefilter", PassBloomPrefilter, comp, bright, null);
        var mips = new TextureHandle[5];
        TextureHandle input = bright;
        for (int i = 0; i < 5; i++)
        {
            int mw = Mathf.Max(1, hw >> i), mh = Mathf.Max(1, hh >> i);
            bloomDesc.width = mw;
            bloomDesc.height = mh;
            bloomDesc.name = "_GafaBloomH" + i;
            TextureHandle hTex = renderGraph.CreateTexture(bloomDesc);
            bloomDesc.name = "_GafaBloomV" + i;
            TextureHandle vTex = renderGraph.CreateTexture(bloomDesc);
            var inv = new Vector4(1f / mw, 1f / mh);
            float radius = k_BloomKernels[i];
            Fullscreen(renderGraph, "GAFA Bloom Blur H" + i, PassBloomBlur, input, hTex, d => { d.blurDir = new Vector4(1, 0); d.blurInvSize = inv; d.blurRadius = radius; });
            Fullscreen(renderGraph, "GAFA Bloom Blur V" + i, PassBloomBlur, hTex, vTex, d => { d.blurDir = new Vector4(0, 1); d.blurInvSize = inv; d.blurRadius = radius; });
            mips[i] = vTex;
            input = vTex;
        }

        // ---------------------------------------------------------------- 5. 泛光叠加 + 调色，写回相机颜色
        var finalDesc = renderGraph.GetTextureDesc(color);
        finalDesc.name = "_GafaFinal";
        finalDesc.clearBuffer = false;
        finalDesc.msaaSamples = MSAASamples.None;
        TextureHandle final = renderGraph.CreateTexture(finalDesc);
        Fullscreen(renderGraph, "GAFA Grade", PassFinal, comp, final, d =>
        {
            for (int i = 0; i < 5; i++) d.bloom[i] = mips[i];
        });
        resources.cameraColor = final;
    }

    void Fullscreen(RenderGraph renderGraph, string name, int pass, TextureHandle src, TextureHandle dst, System.Action<FsData> configure)
    {
        using (var builder = renderGraph.AddRasterRenderPass<FsData>(name, out var data))
        {
            data.material = m_Material;
            data.pass = pass;
            data.src = src;
            data.depth = TextureHandle.nullHandle;
            data.ink = TextureHandle.nullHandle;
            data.ao = TextureHandle.nullHandle;
            for (int i = 0; i < 5; i++) data.bloom[i] = TextureHandle.nullHandle;
            data.shared = m_Shared;
            data.blurDir = Vector4.zero;
            data.blurInvSize = Vector4.zero;
            data.blurRadius = 0;
            configure?.Invoke(data);

            builder.UseTexture(src, AccessFlags.Read);
            if (data.depth.IsValid() && !data.depth.Equals(src)) builder.UseTexture(data.depth, AccessFlags.Read);
            if (data.ink.IsValid()) builder.UseTexture(data.ink, AccessFlags.Read);
            if (data.ao.IsValid()) builder.UseTexture(data.ao, AccessFlags.Read);
            for (int i = 0; i < 5; i++) if (data.bloom[i].IsValid()) builder.UseTexture(data.bloom[i], AccessFlags.Read);
            builder.SetRenderAttachment(dst, 0, AccessFlags.Write);
            builder.SetRenderFunc((FsData d, RasterGraphContext ctx) => Execute(d, ctx));
        }
    }

    static void SetTex(MaterialPropertyBlock p, int id, TextureHandle h)
    {
        if (!h.IsValid()) return;
        Texture t = (RTHandle)h;
        p.SetTexture(id, t);
    }

    static void Execute(FsData d, RasterGraphContext ctx)
    {
        var p = d.mpb;
        p.Clear();
        SetTex(p, s_BlitTexture, d.src);
        p.SetVector(s_BlitScaleBias, new Vector4(1, 1, 0, 0));
        SetTex(p, s_Depth, d.depth);
        SetTex(p, s_Ink, d.ink);
        SetTex(p, s_AO, d.ao);
        for (int i = 0; i < 5; i++) SetTex(p, s_Bloom[i], d.bloom[i]);

        var sh = d.shared;
        p.SetVector(s_Proj, sh.proj);
        p.SetVector(s_Res, sh.res);
        p.SetMatrix(s_CamToWorld, sh.camToWorld);
        p.SetVector(s_CamPos, sh.camPos);
        p.SetVector(s_SunDir, sh.sunDir);
        p.SetVector(s_AOParams, sh.aoParams);
        p.SetVector(s_AOTint, sh.aoTint);
        p.SetVector(s_Line, sh.line);
        p.SetVector(s_InkCol, sh.inkCol);
        p.SetVector(s_InkParams, sh.inkParams);
        p.SetVector(s_HazeCol, sh.hazeCol);
        p.SetVector(s_HazeSun, sh.hazeSun);
        p.SetVector(s_Haze, sh.haze);
        p.SetVector(s_BloomParams, sh.bloomParams);
        p.SetVector(s_Grade, sh.grade);
        p.SetVector(s_BlurDir, d.blurDir);
        p.SetVector(s_BlurInvSize, d.blurInvSize);
        p.SetFloat(s_BlurRadius, d.blurRadius);

        ctx.cmd.DrawProcedural(Matrix4x4.identity, d.material, d.pass, MeshTopology.Triangles, 3, 1, p);
    }
}
