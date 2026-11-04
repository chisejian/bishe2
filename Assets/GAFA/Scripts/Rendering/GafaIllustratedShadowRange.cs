using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// 插画版场景的阴影范围。原项目的阴影贴图覆盖建筑周围约 ±46 m，远处的背景树也有自阴影（暗部 / 中间调）；
/// URP 的阴影距离是管线资源里的全局设置（这里默认 50 m），超出范围的树会整片变成亮面。
/// 本组件只在所在场景为当前场景时、相机渲染期间临时改成 shadowDistance，每个相机渲染完立即还原，其它场景不受影响。
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public class GafaIllustratedShadowRange : MonoBehaviour
{
    [Min(1f)] public float shadowDistance = 120f;

    UniversalRenderPipelineAsset m_Asset;
    float m_Saved = -1f;

    void OnEnable()
    {
        RenderPipelineManager.beginCameraRendering += OnBeginCamera;
        RenderPipelineManager.endCameraRendering += OnEndCamera;
    }

    void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
        RenderPipelineManager.endCameraRendering -= OnEndCamera;
        Restore();
    }

    void OnBeginCamera(ScriptableRenderContext context, Camera camera)
    {
        if (camera.cameraType != CameraType.Game && camera.cameraType != CameraType.SceneView) return;
        if (gameObject.scene != SceneManager.GetActiveScene()) return;
        m_Asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (m_Asset == null) return;
        if (m_Saved < 0f) m_Saved = m_Asset.shadowDistance;
        m_Asset.shadowDistance = shadowDistance;
    }

    void OnEndCamera(ScriptableRenderContext context, Camera camera) => Restore();

    void Restore()
    {
        if (m_Asset != null && m_Saved >= 0f) m_Asset.shadowDistance = m_Saved;
        m_Saved = -1f;
    }
}
