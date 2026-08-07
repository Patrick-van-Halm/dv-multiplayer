using DV.CabControls;
using Multiplayer.Components.Networking.Train;
using Multiplayer.Editor.Components.Player;
using Multiplayer.Networking.Data.Player;
using UnityEngine;

namespace Multiplayer.Components.Networking.Player;

/// <summary>
/// Represents a networked player in the multiplayer environment, handling movement, item holding, and visual state
/// </summary>
public class NetworkedPlayer : MonoBehaviour
{
    public static void CaptureItemAnchorOffset()
    {
        NetworkedPlayerItemAnchor.Capture();
    }

    private const float LERP_SPEED = 5.0f;
    private const float MAX_LEAN_ANGLE = 50f;
    private const float LEAN_SMOOTHING_DURATION = 0.1f;
    private const float HEAD_LEAN_MULTIPLIER = 1.5f;

    public byte PlayerId { get; set; }
    public string CrewName { get; set; }
    public bool IsVR { get; set; }

    private GameObject playerModel;
    private AnimationHandler animationHandler;
    private NameTag nameTag;
    private int ping;
    private NetworkedPlayerIKHandler ikHandler;

    private string username;

    public string Username
    {
        get => username;
        set
        {
            username = value;
            nameTag?.SetUsername(value);
        }
    }

    public string DisplayName
    {
        get
        {
            if (string.IsNullOrEmpty(CrewName))
                return username;
            return $"[{CrewName}] {username}";
        }
    }

    // World Positioning
    internal bool IsOnCar { get; private set; }
    internal NetworkedTrainCar OccupiedCar { get; private set; }

    private Transform selfTransform;
    private PlayerPostureFlags currentPosture;

    // Head tracking
    private Transform headTransform;
    private Quaternion headBaseWorldRotation = Quaternion.identity;
    private float currentHeadPitch;
    private float targetHeadPitch;

    // Spine tracking
    private Transform spineTransform;
    private Quaternion spineBaseWorldRotation = Quaternion.identity;

    // Player movement and rotation
    private Vector3 targetPos;
    private Quaternion targetRotation;
    private Vector2 moveDir;
    private Vector2 targetMoveDir;

    private float currentLeanAngle;
    private float angleSmoothRefVel;
    private float currentSitHeight;

    public bool IsChainCouplerHandTargetActive =>
        ikHandler?.HandController?.IsChainTargetActive ?? false;

    private NetworkedPlayerHeldItem heldItem;
    public GameObject RightHandItemGO => heldItem?.Item;

    /// <summary>
    /// Returns true when an interaction point belongs to the item currently
    /// held by this remote player. Handheld items must follow the idle hand;
    /// they must not turn a control interaction into a persistent IK target.
    /// </summary>
    public bool IsHeldItemInteraction(Vector3 worldPosition, float maxDistance = 1.5f)
    {
        GameObject item = RightHandItemGO;
        if (item == null)
            return false;

        float maxDistanceSqr = maxDistance * maxDistance;
        Transform itemTransform = item.transform;
        if ((itemTransform.position - worldPosition).sqrMagnitude <= maxDistanceSqr)
            return true;

        foreach (Renderer renderer in item.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer == null)
                continue;

            Vector3 closestPoint = renderer.bounds.ClosestPoint(worldPosition);
            if ((closestPoint - worldPosition).sqrMagnitude <= maxDistanceSqr)
                return true;
        }

        return false;
    }

    protected void Awake()
    {
        heldItem = new NetworkedPlayerHeldItem(this);
        nameTag = GetComponentInChildren<NameTag>();

        nameTag.LookTarget = PlayerManager.ActiveCamera.transform;
        PlayerManager.CameraChanged += () => nameTag.LookTarget = PlayerManager.ActiveCamera.transform;

        if (name != null)
            nameTag.SetUsername(name);

        OnSettingsUpdated(Multiplayer.Settings);
        Settings.OnSettingsUpdated += OnSettingsUpdated;

        selfTransform = transform;
        targetPos = selfTransform.position;
        targetRotation = selfTransform.rotation;

        targetHeadPitch = 0f;

        moveDir = Vector2.zero;
        targetMoveDir = Vector2.zero;

        currentPosture = PlayerPostureFlags.None;

        var clampedSitHeight = Mathf.Clamp
            (
                CustomFirstPersonController.PLAYER_SITTING_HEIGHT,
                CustomFirstPersonController.MIN_PLAYER_SITTING_HEIGHT,
                CustomFirstPersonController.MAX_PLAYER_SITTING_HEIGHT
            );
        currentSitHeight = Mathf.InverseLerp
            (
                CustomFirstPersonController.MIN_PLAYER_SITTING_HEIGHT,
                CustomFirstPersonController.MAX_PLAYER_SITTING_HEIGHT,
                clampedSitHeight
            );
    }

    protected void OnDestroy()
    {
        Settings.OnSettingsUpdated -= OnSettingsUpdated;

        // Only detach the visual child before destroying the avatar hierarchy.
        // Inventory persistence owns the item state; disconnect is not a drop.
        if (!UnloadWatcher.isQuitting && !UnloadWatcher.isUnloading)
            heldItem?.Detach();
    }

    private void OnSettingsUpdated(Settings settings)
    {
        nameTag.ShowUsername(settings.ShowNameTags);
        nameTag.ShowPing(settings.ShowNameTags && settings.ShowPingInNameTags);
    }

    public void ChangeModel(GameObject newModel)
    {
        if (newModel == playerModel || newModel == null)
            return;

        if (playerModel != null)
        {
            animationHandler = null;
            DestroyImmediate(playerModel);
            headTransform = null;
            ikHandler = null;
            heldItem.ConfigureModel(null, null);
        }

        playerModel = Instantiate(newModel, transform);
        animationHandler = playerModel.GetComponent<AnimationHandler>();

        var animator = playerModel.GetComponentInChildren<Animator>(true);
        if (animator != null)
        {
            ikHandler =
                animator.gameObject.GetComponent<NetworkedPlayerIKHandler>() ??
                animator.gameObject.AddComponent<NetworkedPlayerIKHandler>();
            ikHandler.InitializeFromCurrentPose();

            headTransform = animator.GetBoneTransform(HumanBodyBones.Head);
            if (headTransform == null)
                Multiplayer.LogWarning($"Head bone not found in model {newModel.name}. Head tracking will not work");

            spineTransform = animator.GetBoneTransform(HumanBodyBones.Spine);

            Transform leftHandTransform =
                animator.GetBoneTransform(HumanBodyBones.LeftHand);
            Transform rightHandTransform =
                animator.GetBoneTransform(HumanBodyBones.RightHand);
            Transform rightPalmReference =
                animator.GetBoneTransform(HumanBodyBones.RightMiddleProximal) ??
                animator.GetBoneTransform(HumanBodyBones.RightIndexProximal);
            heldItem.ConfigureModel(
                rightHandTransform,
                rightPalmReference);
            ikHandler.AttachHandController(
                this,
                rightHandTransform);

            if (leftHandTransform == null || rightHandTransform == null)
                Multiplayer.LogWarning($"Hand bones not found in model {newModel.name}. VR hand tracking will not work");
        }
        else
        {
            Multiplayer.LogWarning($"Animator not found in model {newModel.name}. Tracking will not work");
        }

        if (spineTransform == null)
        {
            // Fall back to using the model's transform if the spine bone is not found
            spineTransform = playerModel.transform;
        }

        spineBaseWorldRotation = Quaternion.Inverse(selfTransform.rotation) * spineTransform.rotation;

        if (headTransform != null)
            headBaseWorldRotation = Quaternion.Inverse(selfTransform.rotation) * headTransform.rotation;

        SetPosture(currentPosture);
    }

    public void SetPing(int ping)
    {
        nameTag?.SetPing(ping);
        this.ping = ping;
    }

    public int GetPing()
    {
        return ping;
    }

    protected void Update()
    {
        float t = Time.deltaTime * LERP_SPEED;

        Vector3 position = Vector3.Lerp(
            IsOnCar ? selfTransform.localPosition : selfTransform.position,
            IsOnCar ? targetPos : targetPos + WorldMover.currentMove,
            t);

        // Calculate smoothed head pitch for use in VR and nonVR head positioning and nonVR item positioning
        currentHeadPitch = Mathf.Lerp(currentHeadPitch, targetHeadPitch, t);

        moveDir = Vector2.Lerp(moveDir, targetMoveDir, t);
        animationHandler?.SetMoveDir(moveDir);

        if (!IsVR)
            animationHandler?.SetSitHeight(currentSitHeight);

        if (IsOnCar && OccupiedCar != null)
        {
            selfTransform.localPosition = position;

            // Calculate a world-up-respecting rotation
            // This creates a rotation where Y points up in world space
            // but the forward direction aligns with the car's forward projected onto the horizontal plane
            Vector3 carForward = OccupiedCar.transform.forward;
            Vector3 worldUp = Vector3.up;

            // Project car's forward onto the horizontal plane
            Vector3 horizontalForward = Vector3.ProjectOnPlane(carForward, worldUp).normalized;
            if (horizontalForward.sqrMagnitude < 0.001f)
                horizontalForward = Vector3.ProjectOnPlane(OccupiedCar.transform.right, worldUp).normalized;

            // Create base orientation aligned with world up but facing car's forward direction
            Quaternion baseRotation = Quaternion.LookRotation(horizontalForward, worldUp);

            // Calculate the relative rotation: how much is the player rotated relative to the car?
            float carYaw = baseRotation.eulerAngles.y;
            float playerYaw = targetRotation.eulerAngles.y;
            float relativeYaw = playerYaw - carYaw;

            // Apply the desired Y rotation (player's facing direction) on top of this base rotation
            Quaternion targetWorldRotation = baseRotation * Quaternion.Euler(0, relativeYaw, 0);

            // Apply rotation in world space despite being a child transform
            selfTransform.rotation = Quaternion.Lerp(selfTransform.rotation, targetWorldRotation, t);
        }
        else
        {
            selfTransform.position = position;
            selfTransform.rotation = Quaternion.Lerp(transform.rotation, targetRotation, t);
        }

        heldItem.Tick();
    }

    /// <summary>
    /// LateUpdate is called after animators have updated, allowing us to apply our own transformations on top of the animated posture.
    /// </summary>
    protected void LateUpdate()
    {
        if (!IsVR)
        {
            float targetLeanAngle = 0f;
            if (currentPosture.HasFlag(PlayerPostureFlags.LeanLeft))
                targetLeanAngle = MAX_LEAN_ANGLE;
            else if (currentPosture.HasFlag(PlayerPostureFlags.LeanRight))
                targetLeanAngle = -MAX_LEAN_ANGLE;

            currentLeanAngle = Mathf.SmoothDamp(currentLeanAngle, targetLeanAngle, ref angleSmoothRefVel, LEAN_SMOOTHING_DURATION);
        }

        ApplySpineAndHeadRotation();

        ikHandler?.HandController?.LateTick(
            selfTransform.position,
            selfTransform.rotation);

        heldItem.LateTick();
    }

    public void SetChainCouplerHandTarget(
        Vector3 worldPosition,
        Quaternion worldRotation)
    {
        ikHandler?.HandController?.SetChainTarget(
            worldPosition,
            worldRotation);
    }

    public void ClearChainCouplerHandTarget()
    {
        ikHandler?.HandController?.ClearChainTarget();
    }

    public void PulseControlHandIK(Vector3 worldPosition)
    {
        ikHandler?.HandController?.PulseControl(worldPosition);
    }

    public void SetControlHandIK(Vector3 worldPosition)
    {
        ikHandler?.HandController?.SetControl(worldPosition);
    }

    public void ClearControlHandIK()
    {
        ikHandler?.HandController?.ClearControl();
    }

    private void ApplySpineAndHeadRotation()
    {
        if (spineTransform != null)
        {
            // Reconstruct the base animated posture for this model in world space
            Quaternion currentModelSpineBase = selfTransform.rotation * spineBaseWorldRotation;

            // Define standard look/lean vectors using the main uniform player root
            // Side lean is always spinning around the root's global FORWARD axis
            Quaternion leanOffset = Quaternion.AngleAxis(currentLeanAngle, selfTransform.forward);

            // Directly assign the uniform world rotation
            spineTransform.rotation = leanOffset * currentModelSpineBase;
        }

        if (headTransform == null)
            return;

        Quaternion currentModelHeadBase = selfTransform.rotation * headBaseWorldRotation;
        Quaternion pitchRotation = Quaternion.AngleAxis(currentHeadPitch, selfTransform.right);
        Quaternion leanTiltRotation = Quaternion.AngleAxis(currentLeanAngle * HEAD_LEAN_MULTIPLIER, selfTransform.forward);
        headTransform.rotation = pitchRotation * leanTiltRotation * currentModelHeadBase;
    }

    /// <summary>
    /// Feed networked tracking data into the NetworkedPlayer to update its position, rotation, and posture.
    /// </summary>
    /// <param name="trackingData"></param>
    /// <param name="posture"></param>
    /// <param name="movePacketIsOnCar"></param>
    public void UpdatePosition(PlayerTrackingData trackingData, PlayerPostureFlags posture, bool movePacketIsOnCar)
    {
        if (trackingData.Position.HasValue)
            targetPos = trackingData.Position.Value;

        if (trackingData.MoveDirection.HasValue)
        {
            targetMoveDir = trackingData.MoveDirection.Value;
        }

        if (trackingData.SitHeight.HasValue)
            currentSitHeight = Mathf.Clamp01(trackingData.SitHeight.Value);

        SetPosture(posture);

        if (IsOnCar != movePacketIsOnCar)
            return;

        if (trackingData.RotationY.HasValue)
            targetRotation = Quaternion.Euler(0, trackingData.RotationY.Value, 0);

        if (trackingData.LookPosition.HasValue)
            targetHeadPitch = trackingData.LookPosition.Value;

        ikHandler?.HandController?.UpdateTracking(
            trackingData,
            selfTransform.position,
            selfTransform.rotation);
    }

    private void SetPosture(PlayerPostureFlags posture)
    {
        currentPosture = posture;
        // Swimming overrides other postures
        bool isSwimming = posture.HasFlag(PlayerPostureFlags.Swim);
        animationHandler?.SetIsSwimming(isSwimming);
        if (isSwimming)
        {
            animationHandler?.SetIsCrouching(false);
            animationHandler?.SetIsSitting(false);
            animationHandler?.SetIsJumping(false);
        }
        else
        {
            animationHandler?.SetIsJumping(posture.HasFlag(PlayerPostureFlags.Jump));
            animationHandler?.SetIsCrouching(posture.HasFlag(PlayerPostureFlags.Crouch));
            animationHandler?.SetIsSitting(posture.HasFlag(PlayerPostureFlags.Sit));
        }
    }

    public void UpdateCar(ushort netId)
    {
       bool willBeOnCar = NetworkedTrainCar.TryGet(netId, out NetworkedTrainCar newTrainCar);

        if (OccupiedCar != null)
        {
            if (OccupiedCar == newTrainCar)
                return;

            OccupiedCar.Client_RemovePlayer(this);
        }

        IsOnCar = willBeOnCar && newTrainCar != null;

        if (IsOnCar)
        {
            OccupiedCar = newTrainCar;
            selfTransform.SetParent(OccupiedCar.transform, true);
            OccupiedCar.Client_PlayerOnCar(this);
        }
        else
        {
            OccupiedCar = null;
            selfTransform.SetParent(null, true);
        }
    }

    public void HoldItem(
        GameObject itemGo,
        Vector3? targetPos = null,
        Quaternion? targetRot = null,
        bool rightHand = true,
        bool followTrackedHand = false)
    {
        // An item arriving from another player can race a stale control packet.
        // Reset control IK before attaching the item so the hand remains in
        // its animated idle pose while the item is placed at it.
        ClearControlHandIK();
        heldItem.Hold(
            itemGo,
            targetPos,
            targetRot,
            followTrackedHand);
    }

    public void RefreshHeldItem(GameObject itemGo)
    {
        heldItem.Refresh(itemGo);
    }

    public void DropItem()
    {
        ClearControlHandIK();
        heldItem.Drop();
    }
}
