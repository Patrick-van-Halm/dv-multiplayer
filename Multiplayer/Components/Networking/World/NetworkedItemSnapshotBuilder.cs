using DV.Customization.Gadgets;
using DV.InventorySystem;
using DV.Items;
using Multiplayer.Components.Networking.Train;
using Multiplayer.Networking.Data.Items;
using Multiplayer.Utils;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Multiplayer.Components.Networking.World;

internal sealed class NetworkedItemSnapshotBuilder
{
    private const float VrHeldPositionThreshold = 0.01f;
    private const float VrHeldRotationThreshold = 0.5f;

    private readonly NetworkedItem owner;
    private bool createdDirty = true;
    private ItemState lastState;
    private bool stateDirty;
    private bool wasThrown;
    private Vector3 thrownPosition;
    private Quaternion thrownRotation;
    private Vector3 throwDirection;
    private Vector3 lastSentVrHeldPosition;
    private Quaternion lastSentVrHeldRotation =
        Quaternion.identity;
    private bool hasSentVrHeldPose;
    private int lastInventorySlot = -1;
    private bool lastInventorySlotLocked;
    private bool lastInventorySlotDropped;

    public NetworkedItemSnapshotBuilder(NetworkedItem owner)
    {
        this.owner = owner ??
            throw new ArgumentNullException(nameof(owner));
    }

    public void InitializeCurrentState()
    {
        lastState = GetItemState();
        GetInventoryLayout(
            lastState,
            out lastInventorySlot,
            out lastInventorySlotLocked,
            out lastInventorySlotDropped);
        stateDirty = false;
    }

    public void SetCreatedDirty(bool value)
    {
        createdDirty = value;
    }

    public void MarkStateDirty()
    {
        stateDirty = true;
    }

    public void CaptureThrow(Vector3 direction)
    {
        if (wasThrown)
        {
            wasThrown = false;
            return;
        }

        throwDirection = direction;
        thrownPosition =
            NetworkedItem.ToNetworkPosition(
                owner.Item.transform.position);
        thrownRotation = owner.Item.transform.rotation;
        wasThrown = true;
        stateDirty = true;
    }

    public void MarkRemoteThrow()
    {
        wasThrown = true;
    }

    public void MarkAuthoritativeCreationConfirmed()
    {
        createdDirty = false;
        stateDirty = false;
    }

    public void MarkApplied()
    {
        createdDirty = false;
        stateDirty = false;
        owner.MarkValuesClean();
    }

    public ItemUpdateData GetSnapshot()
    {
        if (owner.Item == null && !owner.EnsureRegistered())
            return null;

        bool hasDirtyValues = owner.HasDirtyValues();
        ItemState currentState = GetItemState();
        GetInventoryLayout(
            currentState,
            out int inventorySlot,
            out bool inventorySlotLocked,
            out bool inventorySlotDropped);
        bool inventoryLayoutChanged =
            (currentState == ItemState.InHand ||
             currentState == ItemState.InInventory) &&
            (inventorySlot != lastInventorySlot ||
             inventorySlotLocked != lastInventorySlotLocked ||
             inventorySlotDropped != lastInventorySlotDropped);
        bool locallyHeldInVr =
            VRManager.IsVREnabled() &&
            currentState == ItemState.InHand &&
            owner.Item.IsGrabbed();
        Vector3 networkPosition =
            NetworkedItem.ToNetworkPosition(
                owner.transform.position);
        Quaternion rotation = owner.transform.rotation;
        bool vrPoseDirty =
            locallyHeldInVr &&
            (!hasSentVrHeldPose ||
             Vector3.Distance(
                 networkPosition,
                 lastSentVrHeldPosition) >
             VrHeldPositionThreshold ||
             Quaternion.Angle(
                 rotation,
                 lastSentVrHeldRotation) >
             VrHeldRotationThreshold);

        if (!locallyHeldInVr)
            hasSentVrHeldPose = false;

        byte localPlayerId =
            NetworkLifecycle.Instance.Client?.PlayerId ?? 0;
        if (currentState == ItemState.InInventory &&
            localPlayerId != 0)
        {
            owner.SetLastOwner(localPlayerId);
        }
        else if (owner.LastOwnerId == 0 &&
                 NetworkLifecycle.Instance.IsHost())
        {
            owner.SetLastOwner(localPlayerId);
        }

        bool stateChanged = lastState != currentState;
        if (!stateDirty &&
            !stateChanged &&
            !hasDirtyValues &&
            !vrPoseDirty &&
            !inventoryLayoutChanged)
        {
            return null;
        }

        ItemUpdateData.ItemUpdateType updateType =
            ItemUpdateData.ItemUpdateType.None;
        if (createdDirty)
        {
            updateType = ItemUpdateData.ItemUpdateType.Create;
        }
        else
        {
            if (stateDirty || stateChanged || inventoryLayoutChanged)
            {
                updateType |=
                    ItemUpdateData.ItemUpdateType.ItemState;
            }
            if (hasDirtyValues)
            {
                Multiplayer.LogDebug(
                    owner.GetDirtyValuesDebugString);
                updateType |=
                    ItemUpdateData.ItemUpdateType.ObjectState;
            }
            if (vrPoseDirty)
            {
                updateType |=
                    ItemUpdateData.ItemUpdateType.ItemPosition;
            }
        }

        if (updateType == ItemUpdateData.ItemUpdateType.None)
            return null;

        lastState = currentState;
        lastInventorySlot = inventorySlot;
        lastInventorySlotLocked = inventorySlotLocked;
        lastInventorySlotDropped = inventorySlotDropped;
        owner.SetLastDirtyTick(NetworkLifecycle.Instance.Tick);
        ItemUpdateData snapshot = Create(updateType);
        if (locallyHeldInVr)
        {
            lastSentVrHeldPosition = networkPosition;
            lastSentVrHeldRotation = rotation;
            hasSentVrHeldPose = true;
        }

        createdDirty = false;
        stateDirty = false;
        wasThrown = false;
        owner.MarkValuesClean();
        return snapshot;
    }

    public ItemUpdateData Create(
        ItemUpdateData.ItemUpdateType updateType)
    {
        if (owner.transform == null ||
            owner.Item?.InventorySpecs?.ItemPrefabName == null)
        {
            return null;
        }

        if (updateType.HasFlag(
                ItemUpdateData.ItemUpdateType.Create) ||
            updateType.HasFlag(
                ItemUpdateData.ItemUpdateType.FullSync))
        {
            lastState = GetItemState();
        }

        Vector3 position = wasThrown
            ? thrownPosition
            : NetworkedItem.ToNetworkPosition(
                owner.transform.position);
        Quaternion rotation = wasThrown
            ? thrownRotation
            : owner.transform.rotation;
        Dictionary<string, object> states =
            updateType.HasFlag(
                ItemUpdateData.ItemUpdateType.Create) ||
            updateType.HasFlag(
                ItemUpdateData.ItemUpdateType.FullSync)
                ? owner.GetAllStateData()
                : owner.GetDirtyStateData();

        ushort carId = 0;
        bool frontCoupler = true;
        ushort containerNetId = 0;
        int containerSlot = -1;
        GetInventoryLayout(
            lastState,
            out int inventorySlot,
            out bool inventorySlotLocked,
            out bool inventorySlotDropped);
        if (lastState == ItemState.Attached &&
            owner.TryGetComponent(
                out SnappableItem snappableItem) &&
            snappableItem.SnappedTo is
                ItemSnapPointCoupler couplerPoint)
        {
            carId = couplerPoint.Car.GetNetId();
            frontCoupler = couplerPoint.IsFront;
        }
        else if (lastState == ItemState.InContainer &&
                 owner.Item.InContainer is
                     ItemContainer container)
        {
            if (container.ItemBase != null)
            {
                NetworkedItem.TryGetNetId(
                    container.ItemBase,
                    out containerNetId);
            }
            containerSlot = Array.IndexOf(
                container.GetItemsArray(false),
                owner.Item.gameObject);
        }

        return new ItemUpdateData
        {
            UpdateType = updateType,
            ItemNetId = owner.NetId,
            CreationRequestId = owner.CreationRequestId,
            PrefabName =
                owner.Item.InventorySpecs.ItemPrefabName,
            ItemState = lastState,
            ItemPosition = position,
            ItemRotation = rotation,
            ThrowDirection = throwDirection,
            Player = owner.LastOwnerId,
            CarNetId = carId,
            AttachedFront = frontCoupler,
            ContainerNetId = containerNetId,
            ContainerSlot = containerSlot,
            InventorySlot = inventorySlot,
            InLockedSlot = inventorySlotLocked,
            IsDropped = inventorySlotDropped,
            States = states,
        };
    }

    private void GetInventoryLayout(
        ItemState state,
        out int slot,
        out bool locked,
        out bool dropped)
    {
        slot = -1;
        locked = false;
        dropped = false;
        if (state != ItemState.InHand &&
            state != ItemState.InInventory ||
            Inventory.Instance == null)
        {
            return;
        }

        slot = Inventory.Instance.IndexOf(owner.gameObject);
        if (slot < 0)
            return;

        locked = Inventory.Instance.GetSlotLockState(slot);
        dropped = Inventory.Instance.GetSlotDroppedState(slot);
    }

    private ItemState GetItemState()
    {
        if (wasThrown)
            return ItemState.Thrown;
        if (owner.Item.IsGrabbed())
            return ItemState.InHand;
        if (Inventory.Instance != null &&
            Inventory.Instance.Contains(
                owner.gameObject,
                false))
        {
            return ItemState.InInventory;
        }
        if (owner.Item.InContainer != null)
            return ItemState.InContainer;
        if (owner.TryGetComponent(out GadgetItem gadgetItem) &&
            gadgetItem.Gadget != null &&
            gadgetItem.Gadget.IsLinked)
        {
            return ItemState.InstalledGadget;
        }
        if (owner.TryGetComponent(
                out SnappableItem snappableItem) &&
            snappableItem.IsSnapped)
        {
            return ItemState.Attached;
        }
        return ItemState.Dropped;
    }
}
