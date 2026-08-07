using DV.Customization.Gadgets;
using DV.Customization.Gadgets.Implementations;
using DV.Interaction;
using DV.Items;
using Multiplayer.Utils;
using System;
using UnityEngine;

namespace Multiplayer.Components.Networking.World.Items;

internal sealed class RemoteDrillPoseState :
    MonoBehaviour,
    IRemoteItemPoseOverride
{
    private DrillTool drill;
    private NetworkedItem item;
    private Vector3 animationStartPosition;
    private Quaternion animationStartRotation = Quaternion.identity;
    private float transitionProgress;
    private bool hasAnimationStart;
    private bool networkPoseDriven;
    private bool localWorkStarted;

    public bool Active { get; private set; }
    public Vector3 TargetPosition { get; private set; }
    public Quaternion TargetRotation { get; private set; } =
        Quaternion.identity;
    public bool IsPoseOverrideActive =>
        !networkPoseDriven && (Active || transitionProgress > 0f);

    public void Initialize(
        DrillTool drillTool,
        NetworkedItem networkedItem)
    {
        drill = drillTool;
        item = networkedItem;
    }

    public bool GetActive()
    {
        CaptureLocalPose();
        return Active;
    }

    public Vector3 GetPosition()
    {
        CaptureLocalPose();
        return TargetPosition;
    }

    public Quaternion GetRotation()
    {
        CaptureLocalPose();
        return TargetRotation;
    }

    public void SetActive(bool value)
    {
        if (value && !Active)
        {
            animationStartPosition = transform.position;
            animationStartRotation = transform.rotation;
            transitionProgress = 0f;
            hasAnimationStart = true;
        }

        Active = value;
    }

    public void SetPosition(Vector3 value) => TargetPosition = value;
    public void SetRotation(Quaternion value) => TargetRotation = value;

    public void SetIdlePose(Vector3 position, Quaternion rotation)
    {
        if (transitionProgress > 0f)
            return;

        animationStartPosition = position;
        animationStartRotation = rotation;
        hasAnimationStart = true;
    }

    public void SetNetworkPoseDriven(bool value)
    {
        networkPoseDriven = value;
        if (value)
        {
            transitionProgress = 0f;
            hasAnimationStart = false;
        }
    }

    private bool IsLocallyControlled()
    {
        byte localPlayerId =
            NetworkLifecycle.Instance.Client?.PlayerId ?? 0;
        return localPlayerId != 0 &&
               item != null &&
               item.LastOwnerId == localPlayerId;
    }

    private void CaptureLocalPose()
    {
        if (!IsLocallyControlled() || drill == null)
            return;

        bool isAnimating =
            drill.itemWorkingAnimation != null &&
            drill.itemWorkingAnimation.IsAnimating &&
            drill.lastTargetDrillable != null &&
            drill.lastTargetHoleIndex >= 0 &&
            drill.lastTargetHoleIndex <
            drill.lastTargetDrillable.MountPointCount;

        if (!isAnimating)
        {
            localWorkStarted = false;
            Active = false;
            return;
        }

        if (drill.TargetIsValid)
            localWorkStarted = true;

        Active = !localWorkStarted || drill.TargetIsValid;
        if (!Active)
            return;

        MountPoint mountPoint =
            drill.lastTargetDrillable.GetMountPoint(
                drill.lastTargetHoleIndex);
        TargetPosition = NetworkedItem.ToNetworkPosition(
            mountPoint.transform.position);
        TargetRotation = mountPoint.transform.rotation;
    }

    private void LateUpdate()
    {
        if (networkPoseDriven ||
            IsLocallyControlled() ||
            drill == null ||
            drill.vrInteractionPoint == null)
        {
            return;
        }

        if (!Active && transitionProgress <= 0f)
        {
            hasAnimationStart = false;
            return;
        }

        if (!hasAnimationStart)
        {
            animationStartPosition = transform.position;
            animationStartRotation = transform.rotation;
            hasAnimationStart = true;
        }

        Vector3 mountPosition =
            NetworkedItem.ToWorldPosition(TargetPosition);
        Vector3 mountForward =
            TargetRotation * Vector3.forward;
        var alignedPose = TransformUtils.CalculateAlignmentTargets(
            transform,
            mountPosition,
            TargetRotation,
            drill.vrInteractionPoint);

        float drillDepth =
            drill.drillBitLength *
            (1f -
             drill.ProcessingProgress *
             drill.drillMaterialPercentage);
        float transitionTime = Active
            ? drill.itemWorkingAnimation.moveInTime
            : drill.itemWorkingAnimation.moveOutTime;
        transitionProgress = Mathf.MoveTowards(
            transitionProgress,
            Active ? 1f : 0f,
            Time.deltaTime / Mathf.Max(0.01f, transitionTime));
        float easedProgress =
            ItemWorkingAnimation.EaseInOutCubic(transitionProgress);
        transform.SetPositionAndRotation(
            Vector3.Lerp(
                animationStartPosition,
                alignedPose.Item1 - mountForward * drillDepth,
                easedProgress),
            Quaternion.Slerp(
                animationStartRotation,
                alignedPose.Item2,
                easedProgress));
    }
}
