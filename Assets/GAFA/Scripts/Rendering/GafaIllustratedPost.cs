using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// GAFA 插画版后期参数（Volume 覆盖项）。默认值与 TestGAFA/illustrated/src/post.js、main.js 一致。
/// 只有勾选 Enable 的 Volume 生效时，GafaIllustratedFeature 才会运行，其它场景不受影响。
/// 颜色在面板里是 gamma 值，传给着色器前转成线性（原项目里这些颜色是线性值）。
/// </summary>
[Serializable, VolumeComponentMenu("GAFA/Illustrated Post")]
[SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
public sealed class GafaIllustratedPost : VolumeComponent
{
    public BoolParameter enable = new BoolParameter(false);

    [Header("AO（蓝紫 / 灰褐色调的 SAO）")]
    public ClampedFloatParameter aoStrength = new ClampedFloatParameter(0.6f, 0f, 1f);
    public ColorParameter aoTint = new ColorParameter(new Color(0.42f, 0.38f, 0.33f).gamma, false, false, true);
    public MinFloatParameter aoRadius = new MinFloatParameter(1.2f, 0.01f);
    public MinFloatParameter aoIntensity = new MinFloatParameter(1f, 0f);

    [Header("墨线")]
    public ClampedFloatParameter inkAmount = new ClampedFloatParameter(0.85f, 0f, 1f);
    public ColorParameter inkColor = new ColorParameter(new Color(0.05f, 0.04f, 0.03f).gamma, false, false, true);
    [Tooltip("线宽：每 540 像素高 1 像素，至少 1.5 像素（1080p 约 2 像素）")]
    public MinFloatParameter lineWidthScale = new MinFloatParameter(1f, 0f);
    public MinFloatParameter contourThreshold = new MinFloatParameter(0.05f, 0.0001f);
    [Tooltip("相邻法线夹角余弦低于此值时描折边")]
    public ClampedFloatParameter creaseCos = new ClampedFloatParameter(0.6f, -1f, 1f);
    public MinFloatParameter lineFadeDistance = new MinFloatParameter(110f, 1f);
    [Tooltip("给没有墨线通道的其它物体（第三方模型）描轮廓")]
    public BoolParameter outlineOtherObjects = new BoolParameter(true);

    [Header("空气透视")]
    public ColorParameter hazeColor = new ColorParameter(new Color(0.6f, 0.68f, 0.72f).gamma, false, false, true);
    public ColorParameter hazeSunColor = new ColorParameter(new Color(1f, 0.95f, 0.85f).gamma, false, false, true);
    public MinFloatParameter hazeDensity = new MinFloatParameter(0.0042f, 0f);
    public MinFloatParameter hazeHeight = new MinFloatParameter(16f, 0.01f);
    public ClampedFloatParameter hazeMax = new ClampedFloatParameter(0.75f, 0f, 1f);
    public MinFloatParameter hazeStart = new MinFloatParameter(25f, 0f);

    [Header("泛光（UnrealBloomPass）")]
    public MinFloatParameter bloomStrength = new MinFloatParameter(0.08f, 0f);
    public ClampedFloatParameter bloomRadius = new ClampedFloatParameter(0.5f, 0f, 1f);
    public MinFloatParameter bloomThreshold = new MinFloatParameter(1.2f, 0f);

    [Header("调色（Khronos Neutral）")]
    public MinFloatParameter exposure = new MinFloatParameter(1f, 0f);
    public ClampedFloatParameter saturation = new ClampedFloatParameter(0.8f, 0f, 2f);
    public ClampedFloatParameter vignette = new ClampedFloatParameter(0.5f, 0f, 1f);

    public bool IsActive() => enable.value && active;
}
