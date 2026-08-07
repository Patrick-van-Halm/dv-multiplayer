using DV.CabControls;
using DV.CabControls.VRTK;
using DV.Interaction;
using DV.Items;
using Multiplayer.Components.Networking.World;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Multiplayer.Components.Networking.Player;

internal sealed class NetworkedPlayerHeldItem
{
    private sealed class ManualItemPose
    {
        public ManualItemPose(
            string key,
            Vector3 holdPointLocal,
            Vector3 handOffset,
            Quaternion rotationOffset)
        {
            Key = key;
            HoldPointLocal = holdPointLocal;
            HandOffset = handOffset;
            RotationOffset = rotationOffset;
        }

        public string Key { get; }
        public Vector3 HoldPointLocal { get; }
        public Vector3 HandOffset { get; }
        public Quaternion RotationOffset { get; }
    }

    private static readonly Quaternion HandheldToolRotationOffset =
        Quaternion.Euler(90f, 0f, 0f);
    private static readonly Vector3 UnanchoredToolRootOffset =
        new(0.8f, 0f, 0f);
    // These are deliberately explicit item-local values. Add a profile when
    // an item's visual mesh does not place its origin/anchor in the handle.
    // HoldPointLocal is the point that should sit in the palm, in item-root
    // coordinates; HandOffset is relative to the idle hand in player space.
    private static readonly ManualItemPose[] ManualItemPoses =
    [
        new(
            "drill",
            Vector3.zero,
            Vector3.zero,
            Quaternion.identity)
    ];

    private readonly NetworkedPlayer player;
    private readonly Transform playerTransform;
    private readonly List<Collider> disabledColliders = [];
    private readonly List<Renderer> hiddenRenderers = [];

    private Transform rightHandTransform;
    private Transform rightPalmReferenceTransform;
    private Vector3? holdPosition;
    private Quaternion? holdRotation;
    private Transform grip;
    private bool hasConfiguredGrip;
    private Bounds? meshBounds;
    private Transform originalParent;
    private bool isTool;
    private bool followsTrackedHand;
    private Rigidbody body;
    private bool bodyStateCaptured;
    private bool originalIsKinematic;
    private bool originalUseGravity;
    private ManualItemPose manualPose;
    private float nextPoseDebugTime;

    public GameObject Item { get; private set; }

    public NetworkedPlayerHeldItem(NetworkedPlayer player)
    {
        this.player = player;
        playerTransform = player.transform;
    }

    public void ConfigureModel(
        Transform rightHand,
        Transform rightPalmReference)
    {
        rightHandTransform = rightHand;
        rightPalmReferenceTransform = rightPalmReference;
    }

    public void Tick()
    {
        if (Item == null ||
            (player.IsVR && !followsTrackedHand))
        {
            return;
        }

        IRemoteItemPoseOverride poseOverride =
            Item.GetComponent<IRemoteItemPoseOverride>();
        if (poseOverride == null || !poseOverride.IsPoseOverrideActive)
            PlaceAtAnchor(poseOverride);
    }

    public void LateTick()
    {
        if (Item == null)
            return;

        FreezeBody();

        if ((player.IsVR && !followsTrackedHand) ||
            rightHandTransform == null)
        {
            return;
        }

        IRemoteItemPoseOverride poseOverride =
            Item.GetComponent<IRemoteItemPoseOverride>();
        if (poseOverride == null || !poseOverride.IsPoseOverrideActive)
            PlaceAtAnchor(poseOverride);
    }

    public void Hold(
        GameObject item,
        Vector3? targetPosition,
        Quaternion? targetRotation,
        bool followTrackedHand)
    {
        if (Item != null && Item != item)
            Drop();

        Multiplayer.LogDebug(
            () =>
                $"NetworkedPlayer.HoldItem({item.GetPath()}) Player: {player.Username}, Before position: {item.transform.localPosition}, rotation: {item.transform.localRotation}, Target pos: {targetPosition}, Target rot: {targetRotation}");

        originalParent = item.transform.parent;
        body = null;
        bodyStateCaptured = false;
        CaptureBody(item);

        GrabHandlerItem grabHandler =
            item.GetComponentInChildren<GrabHandlerItem>();
        if (grabHandler != null)
        {
            grabHandler.TogglePhysics(false);
            grabHandler.interactionAllowed = false;
        }

        disabledColliders.Clear();
        foreach (Collider collider in
                 item.GetComponentsInChildren<Collider>(true))
        {
            if (collider == null || !collider.enabled)
                continue;

            Multiplayer.LogDebug(
                () =>
                    $"NetworkedPlayer.HoldItem() Collider: {collider.name}, Enabled: {collider.enabled}, Type: {collider.GetType()}");
            collider.enabled = false;
            disabledColliders.Add(collider);
        }

        item.transform.SetParent(playerTransform, true);
        Item = item;
        FreezeBody();
        holdPosition = targetPosition;
        holdRotation = targetRotation;
        grip = FindGrip(item, out hasConfiguredGrip);
        meshBounds = CalculateMeshBounds(item.transform);
        manualPose = FindManualPose(item);
        UpdateHiddenRenderers(item);
        isTool =
            item.GetComponentInChildren<ItemWorkingAnimation>(true) != null;
        followsTrackedHand = followTrackedHand;

        IRemoteItemPoseOverride poseOverride =
            item.GetComponent<IRemoteItemPoseOverride>();
        poseOverride?.SetNetworkPoseDriven(player.IsVR);
        if (!player.IsVR || followsTrackedHand)
            PlaceAtAnchor(poseOverride);
    }

    public void Refresh(GameObject item)
    {
        if (item == null || Item != item)
            return;

        GrabHandlerItem grabHandler =
            item.GetComponentInChildren<GrabHandlerItem>(true);
        if (grabHandler != null)
        {
            grabHandler.TogglePhysics(false);
            grabHandler.interactionAllowed = false;
        }
        FreezeBody();

        foreach (Collider collider in
                 item.GetComponentsInChildren<Collider>(true))
        {
            if (collider != null &&
                collider.enabled &&
                !disabledColliders.Contains(collider))
            {
                collider.enabled = false;
                disabledColliders.Add(collider);
            }
        }

        item.transform.SetParent(playerTransform, true);
        grip = FindGrip(item, out hasConfiguredGrip);
        meshBounds = CalculateMeshBounds(item.transform);
        manualPose = FindManualPose(item);
        isTool =
            item.GetComponentInChildren<ItemWorkingAnimation>(true) != null;
        RestoreHiddenRenderers();
        UpdateHiddenRenderers(item);

        IRemoteItemPoseOverride poseOverride =
            item.GetComponent<IRemoteItemPoseOverride>();
        poseOverride?.SetNetworkPoseDriven(player.IsVR);
        if (!player.IsVR || followsTrackedHand)
            PlaceAtAnchor(poseOverride);
    }

    public void Drop()
    {
        Detach();
    }

    public void Detach()
    {
        foreach (Collider collider in disabledColliders)
        {
            if (collider == null)
                continue;

            Multiplayer.LogDebug(
                () =>
                    $"NetworkedPlayer.DropItem() Re-enabling collider: {collider.name}, Type: {collider.GetType()}");
            collider.enabled = true;
        }
        disabledColliders.Clear();
        RestoreHiddenRenderers();

        IRemoteItemPoseOverride poseOverride =
            Item?.GetComponent<IRemoteItemPoseOverride>();
        poseOverride?.SetNetworkPoseDriven(false);

        GrabHandlerItem grabHandler =
            Item?.GetComponentInChildren<GrabHandlerItem>();
        if (grabHandler != null)
        {
            grabHandler.TogglePhysics(true);
            grabHandler.interactionAllowed = true;
        }
        RestoreBody();

        if (Item != null)
        {
            Item.transform.SetParent(
                originalParent != null
                    ? originalParent
                    : WorldMover.OriginShiftParent,
                true);
        }

        Item = null;
        holdPosition = null;
        holdRotation = null;
        grip = null;
        hasConfiguredGrip = false;
        meshBounds = null;
        manualPose = null;
        originalParent = null;
        isTool = false;
        followsTrackedHand = false;
    }

    private void UpdateHiddenRenderers(GameObject item)
    {
        if (hasConfiguredGrip ||
            item.GetComponentInChildren<ItemBase>(true) == null)
        {
            return;
        }

        foreach (Renderer renderer in
                 item.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer != null && renderer.enabled)
            {
                renderer.enabled = false;
                hiddenRenderers.Add(renderer);
            }
        }
    }

    private void RestoreHiddenRenderers()
    {
        foreach (Renderer renderer in hiddenRenderers)
        {
            if (renderer != null)
                renderer.enabled = true;
        }
        hiddenRenderers.Clear();
    }

    private void CaptureBody(GameObject item)
    {
        if (bodyStateCaptured || item == null)
            return;

        ItemBase itemBase =
            item.GetComponentInChildren<ItemBase>(true);
        body =
            itemBase?.ItemRigidbody ??
            item.GetComponentInChildren<Rigidbody>(true);
        if (body == null)
            return;

        originalIsKinematic = body.isKinematic;
        originalUseGravity = body.useGravity;
        bodyStateCaptured = true;
    }

    private void FreezeBody()
    {
        if (Item == null)
            return;

        CaptureBody(Item);
        if (body == null)
            return;

        body.velocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        body.useGravity = false;
        body.isKinematic = true;
    }

    private void RestoreBody()
    {
        if (bodyStateCaptured && body != null)
        {
            body.velocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.isKinematic = originalIsKinematic;
            body.useGravity = originalUseGravity;
        }

        body = null;
        bodyStateCaptured = false;
    }

    private static Transform FindGrip(
        GameObject item,
        out bool configured)
    {
        ItemVRTK itemVrtk =
            item.GetComponentInChildren<ItemVRTK>(true);
        if (itemVrtk != null && itemVrtk.GrabAnchorRight != null)
        {
            configured = true;
            return itemVrtk.GrabAnchorRight;
        }

        Transform fallback = item.transform;
        foreach (Transform candidate in
                 item.GetComponentsInChildren<Transform>(true))
        {
            if (candidate.name == "[right anchor]")
            {
                configured = true;
                return candidate;
            }

            if (candidate.name == "[anchor]")
                fallback = candidate;
        }

        configured = fallback != item.transform;
        return fallback;
    }

    private void PlaceAtAnchor(
        IRemoteItemPoseOverride poseOverride)
    {
        Vector3 baseOffset =
            holdPosition.HasValue
                ? NetworkedPlayerItemAnchor.PositionOffset +
                  holdPosition.Value
                : NetworkedPlayerItemAnchor.PositionOffset;
        Vector3 position =
            playerTransform.position +
            playerTransform.TransformDirection(baseOffset);
        Quaternion rotation =
            playerTransform.rotation *
            (holdRotation ??
             NetworkedPlayerItemAnchor.RotationOffset);
        if (!player.IsVR && isTool)
            rotation *= HandheldToolRotationOffset;

        if (manualPose != null)
            rotation *= manualPose.RotationOffset;

        if (player.IsVR &&
            followsTrackedHand &&
            rightHandTransform != null)
        {
            rotation = rightHandTransform.rotation;
        }

        Item.transform.SetPositionAndRotation(position, rotation);

        if ((!player.IsVR || followsTrackedHand) &&
            rightHandTransform != null &&
            grip != null)
        {
            Vector3 handTarget = GetRightPalmPosition();
            if (manualPose != null)
            {
                handTarget += playerTransform.TransformDirection(
                    manualPose.HandOffset);
            }
            else if (hasConfiguredGrip)
            {
                Quaternion gripRotationDelta =
                    rightHandTransform.rotation *
                    Quaternion.Inverse(grip.rotation);
                Item.transform.rotation =
                    gripRotationDelta * Item.transform.rotation;
            }
            else if (isTool)
            {
                handTarget += playerTransform.TransformDirection(
                    UnanchoredToolRootOffset);
            }

            Vector3 holdPoint = manualPose != null
                ? Item.transform.TransformPoint(manualPose.HoldPointLocal)
                : GetMeshHoldPoint(handTarget);
            Item.transform.position += handTarget - holdPoint;
            position = Item.transform.position;
            rotation = Item.transform.rotation;

            LogPoseDebug(handTarget, holdPoint);
        }

        poseOverride?.SetIdlePose(position, rotation);
    }

    private void LogPoseDebug(
        Vector3 handTarget,
        Vector3 holdPoint)
    {
        if (Time.unscaledTime < nextPoseDebugTime)
            return;

        nextPoseDebugTime = Time.unscaledTime + 0.5f;
        string itemName = Item == null ? "<none>" : Item.name;
        string itemType = Item == null
            ? "<none>"
            : Item.GetComponentInChildren<ItemBase>(true)?.GetType().FullName ??
              "<no ItemBase>";
        if (Multiplayer.Settings?.DebugLogging != true)
            return;

        string message =
            $"[MultiplayerDebug] HeldItemPose player={player.Username} " +
            $"item={itemName} type={itemType} " +
            $"manual={(manualPose?.Key ?? "none")} tool={isTool} " +
            $"configuredGrip={hasConfiguredGrip} vr={player.IsVR} " +
            $"tracked={followsTrackedHand} hand={handTarget} " +
            $"hold={holdPoint} root={Item.transform.position} " +
            $"rootEuler={Item.transform.rotation.eulerAngles} " +
            $"grip={(grip == null ? Vector3.zero : grip.position)} " +
            $"bounds={(meshBounds.HasValue ? meshBounds.Value.ToString() : "none")}";
        Debug.Log(message);
        Multiplayer.LogDebug(() => message);
    }

    private static ManualItemPose FindManualPose(GameObject item)
    {
        if (item == null)
            return null;

        ItemBase itemBase =
            item.GetComponentInChildren<ItemBase>(true);
        string key =
            $"{item.name}|{itemBase?.GetType().FullName ?? ""}";
        foreach (ManualItemPose pose in ManualItemPoses)
        {
            if (key.IndexOf(
                    pose.Key,
                    StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return pose;
            }
        }

        return null;
    }

    private Vector3 GetRightPalmPosition()
    {
        if (rightPalmReferenceTransform == null)
            return rightHandTransform.position;

        return Vector3.Lerp(
            rightHandTransform.position,
            rightPalmReferenceTransform.position,
            0.5f);
    }

    private Vector3 GetMeshHoldPoint(Vector3 handTarget)
    {
        if (!meshBounds.HasValue)
            return grip.position;

        Bounds bounds = meshBounds.Value;
        Vector3 gripLocal =
            Item.transform.InverseTransformPoint(grip.position);
        Vector3 holdLocal = new(
            Mathf.Clamp(
                gripLocal.x,
                bounds.min.x,
                bounds.max.x),
            Mathf.Clamp(
                gripLocal.y,
                bounds.min.y,
                bounds.max.y),
            Mathf.Clamp(
                gripLocal.z,
                bounds.min.z,
                bounds.max.z));

        // Put the body-facing side of the visible mesh in the palm. This
        // leaves the bulk of wide or long items extending away from the
        // avatar instead of passing through the torso.
        Vector3 torsoPosition =
            playerTransform.position +
            playerTransform.up * 1.1f;
        Vector3 bodyDirection =
            Item.transform.InverseTransformDirection(
                torsoPosition - handTarget);
        int holdAxis = GetDominantAxis(bodyDirection);
        float direction =
            GetAxis(bodyDirection, holdAxis);
        float extent = GetAxis(bounds.extents, holdAxis);
        float palmInset = Mathf.Min(0.035f, extent * 0.25f);
        float face =
            direction >= 0f
                ? GetAxis(bounds.max, holdAxis) - palmInset
                : GetAxis(bounds.min, holdAxis) + palmInset;
        SetAxis(ref holdLocal, holdAxis, face);

        return Item.transform.TransformPoint(holdLocal);
    }

    private static Bounds? CalculateMeshBounds(Transform root)
    {
        Bounds combined = default;
        bool hasBounds = false;

        foreach (MeshFilter filter in
                 root.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter == null || filter.sharedMesh == null)
                continue;

            EncapsulateBounds(
                root,
                filter.transform,
                filter.sharedMesh.bounds,
                ref combined,
                ref hasBounds);
        }

        foreach (SkinnedMeshRenderer renderer in
                 root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (renderer == null || renderer.sharedMesh == null)
                continue;

            EncapsulateBounds(
                root,
                renderer.transform,
                renderer.localBounds,
                ref combined,
                ref hasBounds);
        }

        return hasBounds ? combined : null;
    }

    private static void EncapsulateBounds(
        Transform root,
        Transform source,
        Bounds sourceBounds,
        ref Bounds combined,
        ref bool hasBounds)
    {
        Vector3 min = sourceBounds.min;
        Vector3 max = sourceBounds.max;
        for (int x = 0; x < 2; x++)
        {
            for (int y = 0; y < 2; y++)
            {
                for (int z = 0; z < 2; z++)
                {
                    Vector3 sourceCorner = new(
                        x == 0 ? min.x : max.x,
                        y == 0 ? min.y : max.y,
                        z == 0 ? min.z : max.z);
                    Vector3 rootCorner =
                        root.InverseTransformPoint(
                            source.TransformPoint(sourceCorner));
                    if (hasBounds)
                        combined.Encapsulate(rootCorner);
                    else
                    {
                        combined = new Bounds(
                            rootCorner,
                            Vector3.zero);
                        hasBounds = true;
                    }
                }
            }
        }
    }

    private static int GetDominantAxis(Vector3 value)
    {
        Vector3 absolute = new(
            Mathf.Abs(value.x),
            Mathf.Abs(value.y),
            Mathf.Abs(value.z));
        if (absolute.x >= absolute.y &&
            absolute.x >= absolute.z)
        {
            return 0;
        }

        return absolute.y >= absolute.z ? 1 : 2;
    }

    private static float GetAxis(Vector3 value, int axis)
    {
        return axis switch
        {
            0 => value.x,
            1 => value.y,
            _ => value.z
        };
    }

    private static void SetAxis(
        ref Vector3 value,
        int axis,
        float component)
    {
        switch (axis)
        {
            case 0:
                value.x = component;
                break;
            case 1:
                value.y = component;
                break;
            default:
                value.z = component;
                break;
        }
    }
}
