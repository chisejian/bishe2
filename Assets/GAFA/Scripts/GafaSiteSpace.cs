using UnityEngine;

/// <summary>
/// 挂在导入的 GAFA 模型根节点上。把“世界 → 原 three.js 场地坐标”矩阵传给 GAFA/Weathered 着色器，
/// 这样模型被移动 / 旋转 / 缩放后，按高度计算的污垢、水渍和地砖范围仍与原项目一致。
/// glTFast 导入时会把 X 轴取反（右手 → 左手），mirrorX 用来还原。
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public class GafaSiteSpace : MonoBehaviour
{
    static readonly int WorldToSiteId = Shader.PropertyToID("_GafaWorldToSite");

    [Tooltip("glTFast 导入 glb 时会翻转 X 轴，勾选以还原 three.js 的场地坐标")]
    public bool mirrorX = true;

    void OnEnable() => Apply();

    void LateUpdate() => Apply();

    void OnValidate() => Apply();

    void OnDisable() => Shader.SetGlobalMatrix(WorldToSiteId, Matrix4x4.zero);

    void Apply()
    {
        Matrix4x4 mirror = Matrix4x4.Scale(new Vector3(mirrorX ? -1f : 1f, 1f, 1f));
        Shader.SetGlobalMatrix(WorldToSiteId, mirror * transform.worldToLocalMatrix);
    }
}
