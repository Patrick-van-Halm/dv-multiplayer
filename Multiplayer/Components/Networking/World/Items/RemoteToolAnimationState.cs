using DV.Common;
using DV.Customization;
using DV.Customization.Gadgets;
using DV.Customization.Gadgets.Implementations;
using DV.Items;
using DV.Player;
using Multiplayer.Components.Networking;
using Multiplayer.Components.Networking.Train;
using Multiplayer.Components.Networking.World;
using Multiplayer.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using UnityEngine;

namespace Multiplayer.Components.Networking.World.Items;

internal sealed class RemoteToolAnimationState : MonoBehaviour, IRemoteItemPoseOverride
{
    private GadgetSolderingTool solderingTool;
    private GadgetWiringTool wiringTool;
    private GadgetRemover removerTool;
    private NetworkedItem item;
    private Vector3 animationStartPosition;
    private Quaternion animationStartRotation = Quaternion.identity;
    private float transitionProgress;
    private bool hasAnimationStart;
    private bool networkPoseDriven;
    private float hammerAnimationElapsed;
    private float hammerSwingAngle;

    public bool PoseActive { get; private set; }
    public Vector3 TargetPosition { get; private set; }
    public Quaternion TargetRotation { get; private set; } = Quaternion.identity;
    public bool IsPoseOverrideActive =>
        !networkPoseDriven && (PoseActive || transitionProgress > 0f);
    public bool ShouldSuppressRemoteParticles =>
        solderingTool != null && !IsLocallyControlled();

    public void Initialize(GadgetSolderingTool tool, NetworkedItem networkedItem)
    {
        solderingTool = tool;
        item = networkedItem;
    }

    public void Initialize(GadgetWiringTool tool, NetworkedItem networkedItem)
    {
        wiringTool = tool;
        item = networkedItem;
    }

    public void Initialize(GadgetRemover tool, NetworkedItem networkedItem)
    {
        removerTool = tool;
        item = networkedItem;
    }

    public bool GetPoseActive()
    {
        CaptureLocalPose();
        return PoseActive;
    }

    public Vector3 GetTargetPosition()
    {
        CaptureLocalPose();
        return TargetPosition;
    }

    public Quaternion GetTargetRotation()
    {
        CaptureLocalPose();
        return TargetRotation;
    }

    public void SetPoseActive(bool value)
    {
        if (value && !PoseActive)
        {
            animationStartPosition = transform.position;
            animationStartRotation = transform.rotation;
            transitionProgress = 0f;
            hasAnimationStart = true;
            hammerAnimationElapsed = 0f;
            hammerSwingAngle = 0f;
        }

        PoseActive = value;
    }
    public void SetTargetPosition(Vector3 value) => TargetPosition = value;
    public void SetTargetRotation(Quaternion value) => TargetRotation = value;
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
        byte localPlayerId = NetworkLifecycle.Instance.Client?.PlayerId ?? 0;
        return localPlayerId != 0 && item != null && item.LastOwnerId == localPlayerId;
    }

    private void CaptureLocalPose()
    {
        if (!IsLocallyControlled())
            return;

        if (solderingTool != null)
        {
            PoseActive =
                solderingTool.itemWorkingAnimation != null &&
                solderingTool.itemWorkingAnimation.IsAnimating &&
                solderingTool.solderingTarget != null;
            if (!PoseActive)
                return;

            GadgetBase target = solderingTool.solderingTarget;
            TargetPosition = NetworkedItem.ToNetworkPosition(
                target.transform.TransformPoint(
                    new Vector3(target.Bounds.extents.x, 0f, 0f)));
            TargetRotation = target.transform.rotation;
            return;
        }

        if (wiringTool != null)
        {
            Transform target = wiringTool.animationTarget?.transform;
            PoseActive =
                wiringTool.itemWorkingAnimation != null &&
                wiringTool.itemWorkingAnimation.IsAnimating &&
                target != null;
            if (!PoseActive)
                return;

            TargetPosition = NetworkedItem.ToNetworkPosition(target.position);
            TargetRotation = Quaternion.LookRotation(target.forward, Vector3.up);
            return;
        }

        if (removerTool != null)
        {
            PoseActive =
                removerTool.itemWorkingAnimation != null &&
                removerTool.itemWorkingAnimation.IsAnimating &&
                !removerTool.itemWorkingAnimation.WorkDone;
        }
    }

    private void LateUpdate()
    {
        if (networkPoseDriven || IsLocallyControlled())
            return;

        if (!PoseActive && transitionProgress <= 0f)
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

        Transform interactionPoint;
        ItemWorkingAnimation animation;
        if (solderingTool != null)
        {
            interactionPoint = solderingTool.vrInteractionPoint;
            animation = solderingTool.itemWorkingAnimation;
        }
        else if (wiringTool != null)
        {
            interactionPoint = wiringTool.vrInteractionPoint;
            animation = wiringTool.itemWorkingAnimation;
        }
        else
        {
            interactionPoint = removerTool?.vrInteractionPoint;
            animation = removerTool?.itemWorkingAnimation;
        }

        if (interactionPoint == null)
            return;

        if (removerTool != null)
        {
            AnimateHammerInHand(animation);
            return;
        }

        float transitionTime = PoseActive
            ? animation.moveInTime
            : animation.moveOutTime;
        transitionProgress = Mathf.MoveTowards(
            transitionProgress,
            PoseActive ? 1f : 0f,
            Time.deltaTime / Mathf.Max(0.01f, transitionTime));

        Quaternion targetRotation = TargetRotation;
        if (solderingTool != null)
        {
            targetRotation *= Quaternion.Euler(
                Mathf.Sin(Time.timeSinceLevelLoad),
                Mathf.Sin(Time.timeSinceLevelLoad * 2.423f) - 30f,
                Mathf.Sin(Time.timeSinceLevelLoad * 1.623f));
        }

        var alignedPose = TransformUtils.CalculateAlignmentTargets(
            transform,
            NetworkedItem.ToWorldPosition(TargetPosition),
            targetRotation,
            interactionPoint);
        float easedProgress =
            ItemWorkingAnimation.EaseInOutCubic(transitionProgress);
        transform.SetPositionAndRotation(
            Vector3.Lerp(
                animationStartPosition,
                alignedPose.Item1,
                easedProgress),
            Quaternion.Slerp(
                animationStartRotation,
                alignedPose.Item2,
                easedProgress));
    }

    private void AnimateHammerInHand(ItemWorkingAnimation animation)
    {
        if (PoseActive)
        {
            transitionProgress = 1f;
            hammerAnimationElapsed += Time.deltaTime;

            float slapDuration = Mathf.Max(
                0.15f,
                animation.moveInTime + animation.minWorkTime);
            float progress = Mathf.Clamp01(
                hammerAnimationElapsed / slapDuration);
            if (progress < 0.35f)
            {
                hammerSwingAngle = Mathf.Lerp(
                    0f,
                    -55f,
                    ItemWorkingAnimation.EaseInOutCubic(
                        progress / 0.35f));
            }
            else
            {
                hammerSwingAngle = Mathf.Lerp(
                    -55f,
                    25f,
                    ItemWorkingAnimation.EaseOutCubic(
                        (progress - 0.35f) / 0.65f));
            }
        }
        else
        {
            transitionProgress = Mathf.MoveTowards(
                transitionProgress,
                0f,
                Time.deltaTime / Mathf.Max(0.01f, animation.moveOutTime));
            hammerSwingAngle = Mathf.Lerp(
                0f,
                hammerSwingAngle,
                ItemWorkingAnimation.EaseInOutCubic(
                    transitionProgress));
        }

        // Unlike the other tools, the remover does not travel to its target.
        // Its root stays at the captured hand pose and only performs a slap.
        transform.SetPositionAndRotation(
            animationStartPosition,
            animationStartRotation *
            Quaternion.Euler(hammerSwingAngle, 0f, 0f));
    }
}

