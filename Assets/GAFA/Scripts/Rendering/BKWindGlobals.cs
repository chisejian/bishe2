using UnityEngine;

/// <summary>
/// BK（PureNature）植被 shader 的风是全局变量驱动的：WindPower / WindSpeed / MicroPower 等不在材质上，
/// 材质里的 BaseWind / MicroWind Multiplier 只是乘在这些全局值上的倍率。原来由 Cozy 的 PureNature 模块设置，
/// 去掉 Cozy 后全局值为 0，树就不动了。本组件替代那个模块，编辑器和运行时都会每帧写入。
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public class BKWindGlobals : MonoBehaviour
{
    [Header("Base Wind（树干 / 整体摆动）")]
    [Range(0f, 5f)] public float windPower = 3f;
    public float windSpeed = 1f;

    [Header("Bursts（世界空间噪声阵风）")]
    [Range(0f, 10f)] public float burstsPower = 0.5f;
    public float burstsSpeed = 5f;
    public float burstsScale = 10f;

    [Header("Micro Wind（叶片抖动）")]
    [Range(0f, 1f)] public float microPower = 0.1f;
    public float microSpeed = 1f;
    public float microFrequency = 3f;

    [Header("Grass")]
    public float grassRenderDistance = 30f;

    void OnEnable() => Apply();
    void Update() => Apply();
    void OnValidate() => Apply();

    void Apply()
    {
        Shader.SetGlobalFloat("WindPower", windPower);
        Shader.SetGlobalFloat("WindSpeed", windSpeed);
        Shader.SetGlobalFloat("WindBurstsPower", burstsPower);
        Shader.SetGlobalFloat("WindBurstsSpeed", burstsSpeed);
        Shader.SetGlobalFloat("WindBurstsScale", burstsScale);
        Shader.SetGlobalFloat("MicroPower", microPower);
        Shader.SetGlobalFloat("MicroSpeed", microSpeed);
        Shader.SetGlobalFloat("MicroFrequency", microFrequency);
        Shader.SetGlobalFloat("GrassRenderDist", grassRenderDistance);
    }
}
