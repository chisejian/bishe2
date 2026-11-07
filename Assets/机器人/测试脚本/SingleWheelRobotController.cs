using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[DisallowMultipleComponent]
[RequireComponent(typeof(CharacterController))]
[AddComponentMenu("机器人/双腿机器人第三人称控制器")]
public sealed class SingleWheelRobotController : MonoBehaviour
{
    private static readonly int MoveSpeedHash = Animator.StringToHash("MoveSpeed");

    [Header("模型与动画")]
    [Tooltip("玩家根物体下的空物体 VisualPivot，NewRobot 模型放在它下面。倾斜只作用于这个空物体。")]
    [SerializeField] private Transform visualPivot;
    [Tooltip("使用带有 MoveSpeed 浮点参数及 Idle、Walk、WalkBack 状态的 Animator Controller。")]
    [SerializeField] private Animator robotAnimator;
    [Min(0f)] [SerializeField] private float animationDampTime = 0.1f;

    [Header("移动：W 前进 / S 倒退 / A、D 转向")]
    [Min(0.1f)] [SerializeField] private float forwardSpeed = 2.3f;
    [Min(0.1f)] [SerializeField] private float reverseSpeed = 1.6f;
    [Min(0.1f)] [SerializeField] private float acceleration = 7f;
    [Min(0.1f)] [SerializeField] private float braking = 10f;
    [Min(1f)] [SerializeField] private float turnSpeed = 120f;
    [Min(1f)] [SerializeField] private float turnAcceleration = 540f;
    [Min(0.1f)] [SerializeField] private float gravity = 20f;
    [Min(1f)] [SerializeField] private float terminalFallSpeed = 40f;

    [Header("转向侧倾")]
    [Range(0f, 40f)] [SerializeField] private float turnLeanAngle = 10f;
    [Tooltip("原地转向使用较小的侧倾。")]
    [Range(0f, 1f)] [SerializeField] private float stationaryTurnLean = 0.35f;
    [Min(0.1f)] [SerializeField] private float leanResponse = 10f;

    private CharacterController characterController;
    private Quaternion pivotRestRotation;
    private AnimatorCullingMode previousCullingMode;
    private bool previousApplyRootMotion;
    private bool controlsAnimator;
    private float currentSpeed;
    private float currentTurnSpeed;
    private float verticalSpeed;
    private float roll;

    private void Reset()
    {
        CharacterController capsule = GetComponent<CharacterController>();
        capsule.height = 2f;
        capsule.radius = 0.35f;
        capsule.center = Vector3.up;
        capsule.stepOffset = 0.25f;
        capsule.skinWidth = 0.03f;
        capsule.minMoveDistance = 0f;
        BindRobotReferences();
    }

    [ContextMenu("自动绑定机器人引用")]
    private void BindRobotReferences()
    {
        if (robotAnimator == null) robotAnimator = GetComponentInChildren<Animator>(true);
        if (visualPivot == null) visualPivot = transform.Find("VisualPivot");
        if (visualPivot == null && robotAnimator != null)
        {
            Transform parent = robotAnimator.transform.parent;
            if (parent != null && parent != transform && parent.IsChildOf(transform))
                visualPivot = parent;
        }
    }

    private void OnEnable()
    {
        characterController = GetComponent<CharacterController>();
        BindRobotReferences();
        if (visualPivot == null || visualPivot == transform || !visualPivot.IsChildOf(transform)
            || robotAnimator == null || robotAnimator.transform == visualPivot
            || !robotAnimator.transform.IsChildOf(visualPivot))
        {
            Debug.LogError("请按 RobotRoot → VisualPivot（空物体）→ NewRobot 的层级布置，并指定 Visual Pivot 和模型 Animator。", this);
            enabled = false;
            return;
        }

        if (robotAnimator.runtimeAnimatorController == null || !HasMoveSpeedParameter())
        {
            Debug.LogError("请为机器人指定包含 MoveSpeed 浮点参数及 Idle、Walk、WalkBack 状态的 Animator Controller。", this);
            enabled = false;
            return;
        }

        pivotRestRotation = visualPivot.localRotation;
        previousApplyRootMotion = robotAnimator.applyRootMotion;
        previousCullingMode = robotAnimator.cullingMode;
        controlsAnimator = true;
        // 位移由 CharacterController 负责，Animator 只播放原地动作。
        robotAnimator.applyRootMotion = false;
        robotAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        robotAnimator.SetFloat(MoveSpeedHash, 0f);
    }

    private bool HasMoveSpeedParameter()
    {
        foreach (AnimatorControllerParameter parameter in robotAnimator.parameters)
        {
            if (parameter.nameHash == MoveSpeedHash && parameter.type == AnimatorControllerParameterType.Float)
                return true;
        }

        return false;
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;
        if (!characterController.enabled)
        {
            currentSpeed = currentTurnSpeed = verticalSpeed = 0f;
            robotAnimator.SetFloat(MoveSpeedHash, 0f, animationDampTime, dt);
            roll = Mathf.Lerp(roll, 0f, 1f - Mathf.Exp(-leanResponse * dt));
            return;
        }

        Vector2 input = ReadKeyboard();
        float targetSpeed = input.y * (input.y >= 0f ? forwardSpeed : reverseSpeed);
        bool slowingDown = Mathf.Abs(targetSpeed) < Mathf.Abs(currentSpeed) || targetSpeed * currentSpeed < 0f;
        currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed, (slowingDown ? braking : acceleration) * dt);
        currentTurnSpeed = Mathf.MoveTowards(currentTurnSpeed, input.x * turnSpeed, turnAcceleration * dt);
        transform.Rotate(0f, currentTurnSpeed * dt, 0f, Space.World);

        if (characterController.isGrounded && verticalSpeed < 0f) verticalSpeed = -2f;
        verticalSpeed = Mathf.Max(verticalSpeed - gravity * dt, -terminalFallSpeed);
        Vector3 beforeMove = transform.position;
        characterController.Move((transform.forward * currentSpeed + Vector3.up * verticalSpeed) * dt);
        // 根据实际移动方向切换前进、倒退动画，抵住墙壁时回到待机。
        float actualSignedSpeed = Vector3.Dot(transform.position - beforeMove, transform.forward) / dt;
        float speedLimit = Mathf.Max(0.1f, actualSignedSpeed >= 0f ? forwardSpeed : reverseSpeed);
        float normalizedSpeed = Mathf.Clamp(actualSignedSpeed / speedLimit, -1f, 1f);
        if (Mathf.Abs(actualSignedSpeed) < 0.05f) normalizedSpeed = 0f;
        robotAnimator.SetFloat(MoveSpeedHash, normalizedSpeed, animationDampTime, dt);

        float targetRoll = -turnLeanAngle * Mathf.Clamp(currentTurnSpeed / Mathf.Max(1f, turnSpeed), -1f, 1f)
            * Mathf.Lerp(stationaryTurnLean, 1f, Mathf.Abs(normalizedSpeed));
        roll = Mathf.Lerp(roll, targetRoll, 1f - Mathf.Exp(-leanResponse * dt));
    }

    private static Vector2 ReadKeyboard()
    {
        if (!Application.isFocused) return Vector2.zero;
#if ENABLE_INPUT_SYSTEM
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null) return Vector2.zero;
        return new Vector2(
            (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f),
            (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f));
#elif ENABLE_LEGACY_INPUT_MANAGER
        return new Vector2(
            (Input.GetKey(KeyCode.D) ? 1f : 0f) - (Input.GetKey(KeyCode.A) ? 1f : 0f),
            (Input.GetKey(KeyCode.W) ? 1f : 0f) - (Input.GetKey(KeyCode.S) ? 1f : 0f));
#else
        return Vector2.zero;
#endif
    }

    private void LateUpdate()
    {
        if (!controlsAnimator) return;
        // 只叠加转向侧倾，不施加倒退后倾；碰撞胶囊始终保持直立。
        visualPivot.localRotation = Quaternion.Euler(0f, 0f, roll) * pivotRestRotation;
    }

    private void OnDisable()
    {
        if (controlsAnimator)
        {
            if (visualPivot != null) visualPivot.localRotation = pivotRestRotation;
            if (robotAnimator != null)
            {
                robotAnimator.SetFloat(MoveSpeedHash, 0f);
                robotAnimator.applyRootMotion = previousApplyRootMotion;
                robotAnimator.cullingMode = previousCullingMode;
            }
        }

        controlsAnimator = false;
        currentSpeed = currentTurnSpeed = verticalSpeed = roll = 0f;
    }
}
