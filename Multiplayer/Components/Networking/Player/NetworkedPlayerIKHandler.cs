using UnityEngine;

namespace Multiplayer.Components.Networking.Player;

internal sealed class NetworkedPlayerIKHandler : MonoBehaviour
{
    private const float MAX_ARM_REACH = 0.7f;

    private Animator animator;

    public NetworkedPlayerIKHandController HandController
    {
        get;
        private set;
    }

    public Quaternion LeftHandCorrection = Quaternion.identity;
    public Quaternion RightHandCorrection = Quaternion.identity;

    public Vector3 LeftHandPosition;
    public Quaternion LeftHandRotation = Quaternion.identity;
    public Vector3 RightHandPosition;
    public Quaternion RightHandRotation = Quaternion.identity;

    public bool IsActive { get; set; }
    public bool LeftHandActive { get; set; }
    public bool RightHandActive { get; set; }
    public bool LeftHandRotationActive { get; set; }
    public bool RightHandRotationActive { get; set; }

    private float leftHandWeight;
    private float rightHandWeight;
    private NetworkedPlayer owner;
    private float nextDebugLogTime;

    private void Awake()
    {
        InitializeFromCurrentPose();
    }

    private void OnDestroy()
    {
        HandController?.Dispose();
        HandController = null;
    }

    public void AttachHandController(
        NetworkedPlayer player,
        Transform rightHand)
    {
        owner = player;
        Debug.Log(
            $"[MultiplayerDebug] RemoteIK setup " +
            $"player={player?.Username ?? "<unknown>"} " +
            $"animator={(animator == null ? "none" : animator.name)} " +
            $"rightHand={(rightHand == null ? "none" : rightHand.name)} " +
            $"layers={(animator == null ? 0 : animator.layerCount)}");
        HandController?.Dispose();
        HandController =
            new NetworkedPlayerIKHandController(
                player,
                this,
                rightHand);
    }

    public void InitializeFromCurrentPose()
    {
        animator =
            GetComponent<Animator>() ??
            GetComponentInChildren<Animator>(true);

        IsActive = false;
        LeftHandActive = false;
        RightHandActive = false;
        LeftHandRotationActive = false;
        RightHandRotationActive = false;
        leftHandWeight = 0f;
        rightHandWeight = 0f;

        InitializeHandTarget(
            HumanBodyBones.LeftHand,
            ref LeftHandPosition,
            ref LeftHandRotation);
        InitializeHandTarget(
            HumanBodyBones.RightHand,
            ref RightHandPosition,
            ref RightHandRotation);
    }

    private void InitializeHandTarget(
        HumanBodyBones handBone,
        ref Vector3 position,
        ref Quaternion rotation)
    {
        Transform hand = animator?.GetBoneTransform(handBone);
        if (hand == null)
            return;

        position = hand.position;
        rotation = hand.rotation;
    }

    private void OnAnimatorIK(int layerIndex)
    {
        if (animator == null)
            return;

        leftHandWeight = Mathf.MoveTowards(
            leftHandWeight,
            IsActive && LeftHandActive ? 1f : 0f,
            Time.deltaTime * 8f);
        rightHandWeight = Mathf.MoveTowards(
            rightHandWeight,
            IsActive && RightHandActive ? 1f : 0f,
            Time.deltaTime * 8f);

        animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, leftHandWeight);
        animator.SetIKRotationWeight(
            AvatarIKGoal.LeftHand,
            LeftHandRotationActive ? leftHandWeight : 0f);
        Vector3 clampedLeft = LeftHandPosition;
        Vector3 clampedRight = RightHandPosition;
        if (leftHandWeight > 0f)
        {
            clampedLeft =
                ClampToReach(AvatarIKGoal.LeftHand, LeftHandPosition);
            animator.SetIKPosition(AvatarIKGoal.LeftHand, clampedLeft);
            if (LeftHandRotationActive)
            {
                animator.SetIKRotation(
                    AvatarIKGoal.LeftHand,
                    LeftHandRotation * LeftHandCorrection);
            }
        }

        animator.SetIKPositionWeight(AvatarIKGoal.RightHand, rightHandWeight);
        animator.SetIKRotationWeight(
            AvatarIKGoal.RightHand,
            RightHandRotationActive ? rightHandWeight : 0f);
        if (rightHandWeight > 0f)
        {
            clampedRight =
                ClampToReach(AvatarIKGoal.RightHand, RightHandPosition);
            animator.SetIKPosition(AvatarIKGoal.RightHand, clampedRight);
            if (RightHandRotationActive)
            {
                animator.SetIKRotation(
                    AvatarIKGoal.RightHand,
                    RightHandRotation * RightHandCorrection);
            }
        }

        if (layerIndex == 0 &&
            Time.unscaledTime >= nextDebugLogTime)
        {
            nextDebugLogTime = Time.unscaledTime + 0.5f;
            Transform leftHand =
                animator.GetBoneTransform(HumanBodyBones.LeftHand);
            Transform rightHand =
                animator.GetBoneTransform(HumanBodyBones.RightHand);
            if (Multiplayer.Settings?.DebugLogging == true)
            {
                string message =
                    $"[MultiplayerDebug] RemoteIK " +
                    $"player={owner?.Username ?? "<unknown>"} " +
                    $"active={IsActive} leftActive={LeftHandActive} " +
                    $"rightActive={RightHandActive} " +
                    $"weights=({leftHandWeight:0.00},{rightHandWeight:0.00}) " +
                    $"targets=({LeftHandPosition},{RightHandPosition}) " +
                    $"clamped=({clampedLeft},{clampedRight}) " +
                    $"bones=({leftHand?.position.ToString() ?? "none"}," +
                    $"{rightHand?.position.ToString() ?? "none"}) " +
                    $"rotActive=({LeftHandRotationActive}," +
                    $"{RightHandRotationActive}) " +
                    $"animatorEnabled={animator.enabled} layers={animator.layerCount}";
                Debug.Log(message);
                Multiplayer.LogDebug(() => message);
            }
        }
    }

    private Vector3 ClampToReach(AvatarIKGoal goal, Vector3 worldTarget)
    {
        HumanBodyBones shoulderBone = goal == AvatarIKGoal.LeftHand
           ? HumanBodyBones.LeftUpperArm
           : HumanBodyBones.RightUpperArm;

        Transform shoulder = animator.GetBoneTransform(shoulderBone);
        if (shoulder == null)
            return worldTarget;

        Vector3 delta = worldTarget - shoulder.position;
        if (delta.magnitude > MAX_ARM_REACH)
            return shoulder.position + delta.normalized * MAX_ARM_REACH;

        return worldTarget;
    }
}
