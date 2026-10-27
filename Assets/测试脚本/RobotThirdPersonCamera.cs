using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
[AddComponentMenu("测试脚本/机器人第三人称跟随相机")]
public sealed class RobotThirdPersonCamera : MonoBehaviour
{
    [Tooltip("拖入挂有 SingleWheelRobotController 的玩家根物体，不要拖会倾斜的模型。")]
    [SerializeField] private Transform target = null;
    [Tooltip("相对于机器人朝向的相机位置，Z 为负数表示身后。")]
    [SerializeField] private Vector3 followOffset = new Vector3(0f, 2.2f, -4.5f);
    [Min(0f)] [SerializeField] private float lookAtHeight = 1.1f;

    [Header("构图偏移（不改变相机角度）")]
    [Tooltip("沿画面右方、上方平移相机，单位为米。X 为正时机器人出现在画面偏左，Y 为正时机器人偏下；(0, 0) 恢复居中。")]
    [SerializeField] private Vector2 framingOffset = new Vector2(1f, 0f);

    [Header("跟随平滑")]
    [Min(0.01f)] [SerializeField] private float positionSmoothTime = 0.15f;
    [Min(0.1f)] [SerializeField] private float rotationResponse = 12f;

    private Vector3 followPosition;
    private Vector3 followVelocity;
    private bool needsSnap = true;

    private void OnEnable()
    {
        needsSnap = true;
        followVelocity = Vector3.zero;
    }

    private void LateUpdate()
    {
        if (target == null) return;
        Quaternion heading = Quaternion.Euler(0f, target.eulerAngles.y, 0f);
        Vector3 desiredPosition = target.position + heading * followOffset;
        if (needsSnap)
        {
            followPosition = desiredPosition;
            needsSnap = false;
            LookAtTarget(1f);
            ApplyFramingOffset();
            return;
        }

        float dt = Time.deltaTime;
        if (dt <= 0f) return;
        // 平滑的是未偏移的跟随位置，防止构图偏移混入下一帧而产生累积漂移。
        followPosition = Vector3.SmoothDamp(followPosition, desiredPosition,
            ref followVelocity, positionSmoothTime, Mathf.Infinity, dt);
        LookAtTarget(1f - Mathf.Exp(-rotationResponse * dt));
        ApplyFramingOffset();
    }

    private void LookAtTarget(float blend)
    {
        Vector3 direction = target.position + Vector3.up * lookAtHeight - followPosition;
        if (direction.sqrMagnitude < 0.0001f) return;
        transform.rotation = Quaternion.Slerp(transform.rotation,
            Quaternion.LookRotation(direction, Vector3.up), blend);
    }

    private void ApplyFramingOffset()
    {
        // 先确定原有朝向，再在相机自身平面内平移，不重新朝向目标。
        transform.position = followPosition + transform.right * framingOffset.x
            + transform.up * framingOffset.y;
    }
}
