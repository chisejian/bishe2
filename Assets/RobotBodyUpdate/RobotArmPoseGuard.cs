using UnityEngine;

[DefaultExecutionOrder(1000)]
public sealed class RobotArmPoseGuard : MonoBehaviour
{
    public Animator animator;

    private Transform hips, left, right, leftElbow, rightElbow, leftHand, rightHand;

    private void Awake()
    {
        Bind();
    }

    private bool Bind()
    {
        if (!animator || !animator.isHuman) return false;
        hips = animator.GetBoneTransform(HumanBodyBones.Hips);
        left = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        right = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
        leftElbow = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
        rightElbow = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
        leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
        rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
        return hips && left && right && leftElbow && rightElbow && leftHand && rightHand;
    }

    private void LateUpdate()
    {
        if (!hips && !Bind()) return;
        Vector3 up = animator.transform.up;
        Vector3 leftOut = Vector3.ProjectOnPlane(left.position - right.position, up).normalized;
        Vector3 rightOut = -leftOut;
        if (!IsStraightOut(left, leftElbow, leftHand, leftOut)
            || !IsStraightOut(right, rightElbow, rightHand, rightOut)) return;
        Lower(left, leftElbow, leftOut, up);
        Lower(right, rightElbow, rightOut, up);
    }

    public void SetStandingArms()
    {
        if (!Bind()) return;
        Vector3 up = animator.transform.up;
        Vector3 outward = Vector3.ProjectOnPlane(left.position - right.position, up).normalized;
        Lower(left, leftElbow, outward, up);
        Lower(right, rightElbow, -outward, up);
    }
    private static bool IsStraightOut(Transform arm, Transform elbow, Transform hand, Vector3 outward)
    {
        Vector3 upper = (elbow.position - arm.position).normalized;
        Vector3 lower = (hand.position - elbow.position).normalized;
        return Vector3.Dot(upper, outward) > .91f && Vector3.Dot(upper, lower) > .91f;
    }

    private static void Lower(Transform arm, Transform elbow, Vector3 outward, Vector3 up)
    {
        Vector3 target = (-up + .25f * outward).normalized;
        arm.rotation = Quaternion.FromToRotation(elbow.position - arm.position, target) * arm.rotation;
    }
}


