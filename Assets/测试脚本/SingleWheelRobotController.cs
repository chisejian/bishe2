using System;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[DisallowMultipleComponent]
[RequireComponent(typeof(CharacterController))]
[AddComponentMenu("测试脚本/单轮机器人控制器")]
public sealed class SingleWheelRobotController : MonoBehaviour
{
    [Header("模型与动画")]
    [Tooltip("根物体下的空物体 VisualPivot，模型放在它下面。不要指定骨骼或 Animator 所在物体。")]
    [SerializeField] private Transform visualPivot;
    [SerializeField] private Animator robotAnimator;
    [SerializeField] private AnimationClip idleClip = null;
    [SerializeField] private AnimationClip driveFastClip = null;

    [Header("移动：W 前进 / S 倒退 / A、D 转向")]
    [Min(0.1f)] [SerializeField] private float forwardSpeed = 4f;
    [Min(0.1f)] [SerializeField] private float reverseSpeed = 2f;
    [Min(0.1f)] [SerializeField] private float acceleration = 7f;
    [Min(0.1f)] [SerializeField] private float braking = 10f;
    [Min(1f)] [SerializeField] private float turnSpeed = 120f;
    [Min(1f)] [SerializeField] private float turnAcceleration = 540f;
    [Min(0.1f)] [SerializeField] private float gravity = 20f;
    [Min(1f)] [SerializeField] private float terminalFallSpeed = 40f;

    [Header("程序化倾斜")]
    [Range(0f, 40f)] [SerializeField] private float turnLeanAngle = 16f;
    [Range(0f, 40f)] [SerializeField] private float reverseLeanAngle = 12f;
    [Tooltip("原地转向使用较小的侧倾。")]
    [Range(0f, 1f)] [SerializeField] private float stationaryTurnLean = 0.35f;
    [Min(0.1f)] [SerializeField] private float leanResponse = 10f;
    [Min(0.1f)] [SerializeField] private float animationBlendSpeed = 6f;

    [Header("倒退轮子（可选，自动查找名为 Wheel 的骨骼）")]
    [SerializeField] private bool animateReverseWheel = true;
    [SerializeField] private Transform wheelBone;
    [Tooltip("世界空间中的轮子半径，模型缩放后需要相应调整。")]
    [Min(0.01f)] [SerializeField] private float wheelRadius = 0.23f;
    [Tooltip("轮子骨骼自身的滚动轴；该模型通常使用 X 轴。方向相反时改成 (-1, 0, 0)。")]
    [SerializeField] private Vector3 wheelLocalAxis = Vector3.right;

    private CharacterController characterController;
    private PlayableGraph animationGraph;
    private AnimationMixerPlayable animationMixer;
    private AnimationClipPlayable idlePlayable;
    private AnimationClipPlayable drivePlayable;
    private RuntimeAnimatorController previousAnimatorController;
    private AnimatorCullingMode previousCullingMode;
    private bool previousApplyRootMotion;
    private bool ownsAnimator;
    private Quaternion pivotRestRotation;
    private Quaternion animatedWheelRotation;
    private bool wheelOffsetApplied;
    private float currentSpeed;
    private float currentTurnSpeed;
    private float verticalSpeed;
    private float actualSignedSpeed;
    private float pitch;
    private float roll;
    private float driveWeight;
    private float reverseWheelAngle;
    private double idleTime;
    private double driveTime;

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

    [ContextMenu("自动绑定机器人引用和动画")]
    private void BindRobotReferences()
    {
        FindModelReferences();
#if UNITY_EDITOR
        const string modelPath = "Assets/Simiyaworks/3D Models/Future Friend Series/Cute Robot 05/Model and Motions/Cute_Robot_05.fbx";
        foreach (UnityEngine.Object asset in UnityEditor.AssetDatabase.LoadAllAssetsAtPath(modelPath))
        {
            if (!(asset is AnimationClip clip)) continue;
            if (idleClip == null && clip.name.EndsWith("_ Idle_01", StringComparison.Ordinal))
                idleClip = clip;
            if (driveFastClip == null && clip.name.EndsWith("_ Drive Fast", StringComparison.Ordinal))
                driveFastClip = clip;
        }
#endif
    }

    private void FindModelReferences()
    {
        if (robotAnimator == null) robotAnimator = GetComponentInChildren<Animator>(true);
        if (visualPivot == null && robotAnimator != null && robotAnimator.transform.parent != transform)
            visualPivot = robotAnimator.transform.parent;

        if (wheelBone == null && robotAnimator != null)
        {
            foreach (Transform bone in robotAnimator.GetComponentsInChildren<Transform>(true))
            {
                if (bone.name != "Wheel") continue;
                wheelBone = bone;
                break;
            }
        }
    }

    private void OnEnable()
    {
        characterController = GetComponent<CharacterController>();
        FindModelReferences();
        if (visualPivot == null || visualPivot == transform || !visualPivot.IsChildOf(transform)
            || robotAnimator == null || robotAnimator.transform == visualPivot
            || !robotAnimator.transform.IsChildOf(visualPivot))
        {
            Debug.LogError("请按 RobotRoot → VisualPivot（空物体）→ 机器人模型 的层级布置，指定 Visual Pivot 和模型的 Animator。", this);
            enabled = false;
            return;
        }

        if (idleClip == null || driveFastClip == null || idleClip.length <= 0f || driveFastClip.length <= 0f)
        {
            Debug.LogError("请指定 Idle_01 与 Drive Fast 动画；也可以在组件右上角菜单选择“自动绑定机器人引用和动画”。", this);
            enabled = false;
            return;
        }

        if (wheelBone != null && !wheelBone.IsChildOf(robotAnimator.transform))
        {
            Debug.LogWarning("Wheel Bone 需要是模型内部的轮子骨骼，本次禁用轮子程序动画。", this);
            wheelBone = null;
        }

        pivotRestRotation = visualPivot.localRotation;
        previousAnimatorController = robotAnimator.runtimeAnimatorController;
        previousApplyRootMotion = robotAnimator.applyRootMotion;
        previousCullingMode = robotAnimator.cullingMode;
        ownsAnimator = true;
        // 暂时接管模型 Animator，避免素材自带的演示状态机同时播放其他动作。
        robotAnimator.runtimeAnimatorController = null;
        robotAnimator.applyRootMotion = false;
        robotAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        animationGraph = PlayableGraph.Create("SingleWheelRobot_" + GetInstanceID());
        animationGraph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
        animationMixer = AnimationMixerPlayable.Create(animationGraph, 2);
        idlePlayable = AnimationClipPlayable.Create(animationGraph, idleClip);
        drivePlayable = AnimationClipPlayable.Create(animationGraph, driveFastClip);
        animationGraph.Connect(idlePlayable, 0, animationMixer, 0);
        animationGraph.Connect(drivePlayable, 0, animationMixer, 1);
        animationMixer.SetInputWeight(0, 1f);
        animationMixer.SetInputWeight(1, 0f);
        AnimationPlayableOutput output = AnimationPlayableOutput.Create(animationGraph, "Robot Animation", robotAnimator);
        output.SetSourcePlayable(animationMixer);
        animationGraph.Play();
        animationGraph.Evaluate(0f);
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f || !characterController.enabled) return;

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
        // 用真正发生的位移驱动动画和后倾，抵住墙壁时不会继续高速播放移动动作。
        actualSignedSpeed = Vector3.Dot(transform.position - beforeMove, transform.forward) / dt;

        float movingAmount = Mathf.Clamp01(Mathf.Abs(actualSignedSpeed) / forwardSpeed);
        float targetRoll = -turnLeanAngle * Mathf.Clamp(currentTurnSpeed / turnSpeed, -1f, 1f)
            * Mathf.Lerp(stationaryTurnLean, 1f, movingAmount);
        float targetPitch = -reverseLeanAngle * Mathf.Clamp01(-actualSignedSpeed / reverseSpeed);
        float leanBlend = 1f - Mathf.Exp(-leanResponse * dt);
        pitch = Mathf.Lerp(pitch, targetPitch, leanBlend);
        roll = Mathf.Lerp(roll, targetRoll, leanBlend);

        UpdateAnimation(dt);
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

    private void UpdateAnimation(float dt)
    {
        if (!animationGraph.IsValid()) return;
        RemoveWheelOffset();
        float forwardAmount = Mathf.Clamp01(actualSignedSpeed / forwardSpeed);
        driveWeight = Mathf.MoveTowards(driveWeight, forwardAmount, animationBlendSpeed * dt);
        animationMixer.SetInputWeight(0, 1f - driveWeight);
        animationMixer.SetInputWeight(1, driveWeight);

        // 倒退使用 Idle 作基础姿态，避免倒放 Drive Fast 时仍保持前冲姿势。
        // 手动循环时间，不依赖素材的 Loop Time 开关，也不会修改原始动画资源。
        idleTime = (idleTime + dt) % idleClip.length;
        driveTime = (driveTime + dt * Mathf.Clamp(forwardAmount, 0.15f, 1f)) % driveFastClip.length;
        idlePlayable.SetTime(idleTime);
        drivePlayable.SetTime(driveTime);
        animationGraph.Evaluate(0f);

        if (animateReverseWheel && wheelBone != null && actualSignedSpeed < 0f)
        {
            reverseWheelAngle = Mathf.Repeat(reverseWheelAngle
                + actualSignedSpeed / Mathf.Max(0.01f, wheelRadius) * Mathf.Rad2Deg * dt, 360f);
        }
    }

    private void LateUpdate()
    {
        if (!ownsAnimator) return;
        // 倾斜单独的父物体，既不倾斜碰撞胶囊，也不被模型内的动画曲线覆盖。
        visualPivot.localRotation = Quaternion.Euler(pitch, 0f, roll) * pivotRestRotation;
        if (animateReverseWheel && wheelBone != null && wheelLocalAxis.sqrMagnitude > 0.001f)
        {
            RemoveWheelOffset();
            animatedWheelRotation = wheelBone.localRotation;
            wheelBone.localRotation = animatedWheelRotation
                * Quaternion.AngleAxis(reverseWheelAngle, wheelLocalAxis.normalized);
            wheelOffsetApplied = true;
        }
    }

    private void RemoveWheelOffset()
    {
        // 每次叠加前先移除上次的程序旋转，避免动画不含轮子曲线时逐帧累积。
        if (wheelOffsetApplied && wheelBone != null) wheelBone.localRotation = animatedWheelRotation;
        wheelOffsetApplied = false;
    }

    private void OnDisable()
    {
        RemoveWheelOffset();
        if (animationGraph.IsValid()) animationGraph.Destroy();
        if (ownsAnimator)
        {
            if (visualPivot != null) visualPivot.localRotation = pivotRestRotation;
            if (robotAnimator != null)
            {
                robotAnimator.runtimeAnimatorController = previousAnimatorController;
                robotAnimator.applyRootMotion = previousApplyRootMotion;
                robotAnimator.cullingMode = previousCullingMode;
            }
        }

        ownsAnimator = false;
        currentSpeed = currentTurnSpeed = verticalSpeed = actualSignedSpeed = 0f;
        pitch = roll = driveWeight = reverseWheelAngle = 0f;
        idleTime = driveTime = 0d;
    }
}
