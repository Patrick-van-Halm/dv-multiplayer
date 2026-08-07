using Multiplayer.Components.Networking.Train;
using Multiplayer.Networking.Data.Player;
using System.Collections;
using UnityEngine;

namespace Multiplayer.Components.Networking.Player;

internal sealed class NetworkedPlayerIKHandController
{
    private const float LerpSpeed = 5f;
    private const float HeldItemControlAnchorRange = 1.5f;

    private readonly NetworkedPlayer player;
    private readonly NetworkedPlayerIKHandler handler;
    private readonly Transform rightHandTransform;

    private Vector3 targetLeftHandPosition;
    private Quaternion targetLeftHandRotation = Quaternion.identity;
    private Vector3 targetRightHandPosition;
    private Quaternion targetRightHandRotation = Quaternion.identity;
    private Vector3 currentLeftHandPosition;
    private Quaternion currentLeftHandRotation = Quaternion.identity;
    private Vector3 currentRightHandPosition;
    private Quaternion currentRightHandRotation = Quaternion.identity;
    private bool receivedLeftHandPosition;
    private bool receivedLeftHandRotation;
    private bool receivedRightHandPosition;
    private bool receivedRightHandRotation;
    private bool trackingInitialized;

    private bool chainTargetActive;
    private Vector3 chainTargetPosition;
    private Quaternion chainTargetRotation = Quaternion.identity;
    private Vector3 currentChainPosition;
    private Quaternion currentChainRotation = Quaternion.identity;

    private Coroutine controlPulse;
    private Transform controlPulseAnchor;
    private Vector3 controlPulseLocalPosition;
    private bool controlGrabActive;
    private Vector3 controlGrabPosition;
    private Transform controlGrabAnchor;
    private Vector3 controlGrabLocalPosition;

    public bool IsChainTargetActive => chainTargetActive;

    public NetworkedPlayerIKHandController(
        NetworkedPlayer player,
        NetworkedPlayerIKHandler handler,
        Transform rightHand)
    {
        this.player = player;
        this.handler = handler;
        rightHandTransform = rightHand;
    }

    public void Dispose()
    {
        chainTargetActive = false;
        controlGrabActive = false;
        controlGrabAnchor = null;
        controlPulseAnchor = null;
        StopControlPulse();
        Deactivate();
    }

    public void UpdateTracking(
        PlayerTrackingData data,
        Vector3 playerPosition,
        Quaternion playerRotation)
    {
        if (data.LeftHandPosition.HasValue)
        {
            targetLeftHandPosition =
                data.LeftHandPosition.Value;
            receivedLeftHandPosition = true;
        }
        if (data.LeftHandRotation.HasValue)
        {
            targetLeftHandRotation =
                data.LeftHandRotation.Value;
            receivedLeftHandRotation = true;
        }
        if (data.RightHandPosition.HasValue)
        {
            targetRightHandPosition =
                data.RightHandPosition.Value;
            receivedRightHandPosition = true;
        }
        if (data.RightHandRotation.HasValue)
        {
            targetRightHandRotation =
                data.RightHandRotation.Value;
            receivedRightHandRotation = true;
        }

        if (!player.IsVR ||
            trackingInitialized ||
            !receivedLeftHandPosition ||
            !receivedLeftHandRotation ||
            !receivedRightHandPosition ||
            !receivedRightHandRotation)
        {
            return;
        }

        currentLeftHandPosition = targetLeftHandPosition;
        currentLeftHandRotation = targetLeftHandRotation;
        currentRightHandPosition = targetRightHandPosition;
        currentRightHandRotation = targetRightHandRotation;
        trackingInitialized = true;

        Debug.Log(
            $"[MultiplayerDebug] RemoteIK tracking initialized " +
            $"player={player.Username} left={currentLeftHandPosition} " +
            $"right={currentRightHandPosition}");

        ApplyVrTracking(playerPosition, playerRotation);
        handler.IsActive = true;
        handler.LeftHandActive = true;
        handler.RightHandActive = true;
        handler.LeftHandRotationActive = true;
        handler.RightHandRotationActive = true;
    }

    public void LateTick(
        Vector3 playerPosition,
        Quaternion playerRotation)
    {
        if (player.IsVR)
        {
            ApplyVrTracking(playerPosition, playerRotation);
            return;
        }

        ApplyChainTarget();
        ApplyControlPulseTarget();
        ApplyControlGrabTarget();
    }

    public void SetChainTarget(
        Vector3 worldPosition,
        Quaternion worldRotation)
    {
        if (player.IsVR)
            return;

        ClearControl();

        if (!chainTargetActive)
        {
            currentChainPosition =
                rightHandTransform != null
                    ? rightHandTransform.position
                    : worldPosition;
            currentChainRotation =
                rightHandTransform != null
                    ? rightHandTransform.rotation
                    : worldRotation;
            Debug.Log(
                $"[MultiplayerDebug] RemoteIK chain target " +
                $"player={player.Username} target={worldPosition} " +
                $"handStart={currentChainPosition}");
        }

        chainTargetActive = true;
        chainTargetPosition = worldPosition;
        chainTargetRotation = worldRotation;
        handler.RightHandPosition = currentChainPosition;
        handler.RightHandRotation = currentChainRotation;
        ActivateRightHand();
    }

    public void ClearChainTarget()
    {
        chainTargetActive = false;
        if (player.IsVR)
            return;

        Deactivate();
    }

    public void PulseControl(Vector3 worldPosition)
    {
        if (player.IsVR ||
            chainTargetActive)
        {
            return;
        }

        StopControlPulse();
        controlGrabPosition = worldPosition;
        controlPulseAnchor =
            GetHeldItemControlAnchor(worldPosition);
        controlPulseLocalPosition =
            controlPulseAnchor != null
                ? controlPulseAnchor.InverseTransformPoint(worldPosition)
                : Vector3.zero;

        if (Multiplayer.Settings?.DebugLogging == true)
        {
            Debug.Log(
                $"[MultiplayerDebug] RemoteIK control pulse " +
                $"player={player.Username} target={worldPosition} " +
                $"anchor={(controlPulseAnchor == null ? "world" : controlPulseAnchor.name)}");
        }

        controlPulse =
            player.StartCoroutine(ControlPulse(worldPosition));
    }

    public void SetControl(Vector3 worldPosition)
    {
        if (player.IsVR ||
            chainTargetActive)
        {
            return;
        }

        StopControlPulse();
        controlGrabActive = true;
        Transform heldItemAnchor =
            GetHeldItemControlAnchor(worldPosition);
        NetworkedTrainCar occupiedCar = player.OccupiedCar;
        controlGrabAnchor =
            heldItemAnchor ??
            (occupiedCar != null ? occupiedCar.transform : null);
        controlGrabLocalPosition =
            controlGrabAnchor != null
                ? controlGrabAnchor.InverseTransformPoint(worldPosition)
                : Vector3.zero;
        controlGrabPosition = worldPosition;
        Debug.Log(
            $"[MultiplayerDebug] RemoteIK control grab " +
            $"player={player.Username} target={worldPosition} " +
            $"anchor={(controlGrabAnchor == null ? "world" : controlGrabAnchor.name)}");
        ApplyControlTarget(worldPosition);
    }

    private Transform GetHeldItemControlAnchor(Vector3 worldPosition)
    {
        Transform heldItem = player.RightHandItemGO?.transform;
        if (heldItem == null ||
            (heldItem.position - worldPosition).sqrMagnitude >
                HeldItemControlAnchorRange * HeldItemControlAnchorRange)
        {
            return null;
        }

        return heldItem;
    }

    public void ClearControl()
    {
        controlGrabActive = false;
        controlGrabAnchor = null;
        StopControlPulse();

        if (chainTargetActive)
            return;

        Deactivate();
    }

    private void ApplyVrTracking(
        Vector3 playerPosition,
        Quaternion playerRotation)
    {
        if (!trackingInitialized)
            return;

        float t = Time.deltaTime * LerpSpeed;
        currentLeftHandPosition = Vector3.Lerp(
            currentLeftHandPosition,
            targetLeftHandPosition,
            t);
        currentLeftHandRotation = Quaternion.Lerp(
            currentLeftHandRotation,
            targetLeftHandRotation,
            t);
        currentRightHandPosition = Vector3.Lerp(
            currentRightHandPosition,
            targetRightHandPosition,
            t);
        currentRightHandRotation = Quaternion.Lerp(
            currentRightHandRotation,
            targetRightHandRotation,
            t);

        handler.LeftHandPosition =
            playerPosition +
            playerRotation * currentLeftHandPosition;
        handler.LeftHandRotation =
            playerRotation * currentLeftHandRotation;
        handler.RightHandPosition =
            playerPosition +
            playerRotation * currentRightHandPosition;
        handler.RightHandRotation =
            playerRotation * currentRightHandRotation;
    }

    private IEnumerator ControlPulse(Vector3 worldPosition)
    {
        ApplyControlTarget(GetControlPulsePosition(worldPosition));
        yield return new WaitForSeconds(0.35f);

        controlPulse = null;
        controlPulseAnchor = null;
        if (controlGrabActive)
            ApplyControlTarget(GetControlGrabPosition());
        else
            Deactivate();
    }

    private void ApplyControlTarget(Vector3 worldPosition)
    {
        handler.RightHandPosition = worldPosition;
        ActivateRightHand();
    }

    private void ApplyControlGrabTarget()
    {
        if (!controlGrabActive || chainTargetActive)
        {
            return;
        }

        controlGrabPosition = GetControlGrabPosition();
        if (controlPulse == null)
            ApplyControlTarget(controlGrabPosition);
    }

    private void ApplyControlPulseTarget()
    {
        if (controlPulse == null)
            return;

        ApplyControlTarget(GetControlPulsePosition(controlGrabPosition));
    }

    private Vector3 GetControlPulsePosition(Vector3 fallbackPosition)
    {
        return controlPulseAnchor != null
            ? controlPulseAnchor.TransformPoint(controlPulseLocalPosition)
            : fallbackPosition;
    }

    private Vector3 GetControlGrabPosition()
    {
        return controlGrabAnchor != null
            ? controlGrabAnchor.TransformPoint(controlGrabLocalPosition)
            : controlGrabPosition;
    }

    private void StopControlPulse()
    {
        if (controlPulse == null)
            return;

        player.StopCoroutine(controlPulse);
        controlPulse = null;
        controlPulseAnchor = null;
        controlPulseLocalPosition = Vector3.zero;
    }

    private void ApplyChainTarget()
    {
        if (!chainTargetActive)
            return;

        float t = Mathf.Clamp01(Time.deltaTime * LerpSpeed);
        currentChainPosition = Vector3.Lerp(
            currentChainPosition,
            chainTargetPosition,
            t);
        currentChainRotation = Quaternion.Slerp(
            currentChainRotation,
            chainTargetRotation,
            t);
        handler.RightHandPosition = currentChainPosition;
        handler.RightHandRotation = currentChainRotation;
    }

    private void ActivateRightHand()
    {
        handler.IsActive = true;
        handler.LeftHandActive = false;
        handler.RightHandActive = true;
        handler.LeftHandRotationActive = false;
        handler.RightHandRotationActive = false;
    }

    private void Deactivate()
    {
        handler.RightHandActive = false;
        handler.RightHandRotationActive = false;
        handler.IsActive = false;
    }
}
