using DV.CabControls;
using DV.Customization.Gadgets;
using DV.Interaction;
using DV.InventorySystem;
using DV.Items;
using Multiplayer.Components.Networking.Train;
using Multiplayer.Components.Networking.Player;
using Multiplayer.Networking.Data;
using Multiplayer.Networking.Data.Items;
using Multiplayer.Networking.Managers.Client;
using Multiplayer.Utils;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Multiplayer.Components.Networking.World;

public enum ItemState : byte
{
    Dropped,        //belongs to the world
    Thrown,         //was thrown by player
    InHand,         //held by player
    InInventory,    //in player's inventory
    Attached,       //attached to another object (e.g. EOT Lanterns)
    InContainer,    //stored inside another networked item
    InstalledGadget //the source item is represented by its linked GadgetBase
}

public interface IRemoteItemPoseOverride
{
    bool IsPoseOverrideActive { get; }
    void SetIdlePose(Vector3 position, Quaternion rotation);
    void SetNetworkPoseDriven(bool value);
}

public class NetworkedItem : IdMonoBehaviour<ushort, NetworkedItem>
{
    public static Vector3 ToNetworkPosition(Vector3 worldPosition) => worldPosition - WorldMover.currentMove;

    public static Vector3 ToWorldPosition(Vector3 networkPosition) => networkPosition + WorldMover.currentMove;

    #region Lookup Cache
    private static readonly Dictionary<ItemBase, NetworkedItem> itemBaseToNetworkedItem = new(4096);

    public static Dictionary<ItemBase, NetworkedItem>.ValueCollection GetAll() => itemBaseToNetworkedItem.Values;
    
    public static bool Get(ushort netId, out NetworkedItem obj)
    {
        bool b = Get(netId, out IdMonoBehaviour<ushort, NetworkedItem> rawObj);
        obj = (NetworkedItem)rawObj;
        return b;
    }

    public static bool TryGet(ushort netId, out NetworkedItem obj)
    {
        bool b = TryGet(netId, out IdMonoBehaviour<ushort, NetworkedItem> rawObj);
        obj = (NetworkedItem)rawObj;
        return b;
    }

    public static bool GetItem(ushort netId, out ItemBase obj)
    {
        bool b = Get(netId, out NetworkedItem networkedItem);
        obj = b ? networkedItem.Item : null;
        return b;
    }

    public static bool TryGetNetworkedItem(ItemBase item, out NetworkedItem networkedItem)
    {
        return itemBaseToNetworkedItem.TryGetValue(item, out networkedItem);
    }

    public static bool TryGetNetId(ItemBase item, out ushort netID)
    {
        if (itemBaseToNetworkedItem.TryGetValue(item, out var networkedItem))
        {
            netID = networkedItem.NetId;
            return true;
        }

        netID = 0;
        return false;
    }
    #endregion

    public ItemBase Item { get; private set; }
    private GadgetItem gadgetItem;
    private Component trackedItem;
    private readonly NetworkedItemTrackedState trackedState = new();
    public bool UsefulItem { get; private set; } = false;
    public Type TrackedItemType { get; private set; }
    public bool IsJobPayment { get; set; }
    public uint LastDirtyTick { get; private set; }
    private bool initialised;
    private bool registrationComplete = false;
    private Queue<ItemUpdateData> pendingSnapshots = new Queue<ItemUpdateData>();
    private NetworkedItemSnapshotApplicator snapshotApplicator;
    private NetworkedItemSnapshotBuilder snapshotBuilder;
    private static uint nextCreationRequestId = 1;
    private bool authoritativeCreationRejected;

    // The player who most recently held this item. Unlike the transient
    // ServerPlayer.OwnedItems set, this remains assigned after the item is dropped.
    public byte LastOwnerId { get; private set; }
    public uint CreationRequestId { get; private set; }

    //public void SetOwner(ushort playerId)
    //{
    //    if (OwnerId != playerId)
    //    {
    //        if (OwnerId != 0)
    //        {
    //            NetworkedItemManager.Instance.RemoveItemFromPlayerInventory(this);
    //        }
    //        OwnerId = playerId;
    //        if (playerId != 0)
    //        {
    //            NetworkedItemManager.Instance.AddItemToPlayerInventory(playerId, this);
    //        }
    //    }
    //}

    protected override bool IsIdServerAuthoritative => true;
    // Item IDs are also consumption tokens. Reusing one while an older
    // create/destroy/claim packet is in flight can revive or consume the wrong
    // object, so keep them unique for the lifetime of the multiplayer session.
    protected override bool RecycleIds => false;

    protected override void Awake()
    {
        base.Awake();
        snapshotApplicator =
            new NetworkedItemSnapshotApplicator(this);
        snapshotBuilder =
            new NetworkedItemSnapshotBuilder(this);
        //Multiplayer.LogDebug(() => $"NetworkedItem.Awake() {name}");
        NetworkedItemManager.Instance.CheckInstance(); //Ensure the NetworkedItemManager is initialised

        Register();
    }

    protected void Start()
    {
        if (!initialised)
            Register();

        // Mark registration as complete for items that don't need tracked values
        if (!registrationComplete && !UsefulItem)
        {
            registrationComplete = true;
            ApplyPendingSnapshots();
        }
    }

    private void ApplyPendingSnapshots()
    {
        while (pendingSnapshots.Count > 0)
        {
            Multiplayer.LogDebug(() =>
                $"NetworkedItem.ApplyPendingSnapshots() itemNetId: {NetId}, item name: {name}. Dequeuing");
            ApplySnapshot(pendingSnapshots.Dequeue());
        }
    }

    public T GetTrackedItem<T>() where T : Component
    {
        return UsefulItem ? trackedItem as T : null;
    }

    public void Initialize<T>(T item, ushort netId = 0, bool createDirty = true) where T : Component
    {
        //Multiplayer.LogDebug(() => $"NetworkedItem.Initialize<{typeof(T)}>(netId: {netId}, name: {name}, createDirty: {createdDirty})");

        if (netId != 0)
            NetId = netId;

        trackedItem = item;
        TrackedItemType = typeof(T);
        UsefulItem = true;

        snapshotBuilder.SetCreatedDirty(createDirty);

        if (Item == null)
            Register();

    }

    private bool Register()
    {
        if (initialised)
            return false;

        try
        {

            if (!TryGetComponent(out ItemBase itemBase))
            {
                Multiplayer.LogError($"NetworkedItem.Register() Unable to find ItemBase for {name}");
                return false;
            }

            Item = itemBase;
            itemBaseToNetworkedItem[Item] = this;

            Item.Grabbed += OnGrabbed;
            Item.Ungrabbed += OnUngrabbed;
            Item.ItemInContainerStateChanged += OnContainerChanged;

            //Find special interaction components
            TryGetComponent<GadgetItem>(out gadgetItem);

            snapshotBuilder.InitializeCurrentState();

            initialised = true;
            return true;
        }
        catch (Exception ex)
        {
            Multiplayer.LogError($"NetworkedItem.Register() Unable to find ItemBase for {name}\r\n{ex.Message}");
            return false;
        }
    }

    private void OnUngrabbed(ControlImplBase obj)
    {
        //Multiplayer.LogDebug(() => $"NetworkedItem.OnUngrabbed() NetID: {NetId}, {name}");
        snapshotBuilder.MarkStateDirty();
    }

    private void OnGrabbed(ControlImplBase obj)
    {
        //Multiplayer.LogDebug(() => $"NetworkedItem.OnGrabbed() NetID: {NetId}, {name}");
        byte localPlayerId = NetworkLifecycle.Instance.Client?.PlayerId ?? 0;
        if (localPlayerId != 0)
            SetLastOwner(localPlayerId);
        if (NetId == 0)
            authoritativeCreationRejected = false;

        snapshotBuilder.MarkStateDirty();
    }

    private void OnContainerChanged(ItemBase item, AItemContainer newContainer, AItemContainer oldContainer, bool added)
    {
        snapshotBuilder.MarkStateDirty();
    }

    public void MarkTransformDirty()
    {
        // ItemState snapshots already include the transform for dropped items.
        // Some game systems teleport an item without firing any ItemBase event,
        // so let those systems explicitly request a snapshot.
        snapshotBuilder.MarkStateDirty();
    }

    public void SetLastOwner(byte playerId, bool markDirty = true)
    {
        if (playerId == 0 || LastOwnerId == playerId)
            return;

        LastOwnerId = playerId;
        if (markDirty)
            snapshotBuilder.MarkStateDirty();

        // A held storage transfers everything inside it to the new holder too.
        // Cascade recursively so nested storage items follow the same rule.
        if (!TryGetComponent(out ItemContainer container))
            return;

        foreach (GameObject containedObject in container.GetItemsArray(false))
        {
            if (containedObject == null ||
                !containedObject.TryGetComponent(out ItemBase containedItem) ||
                !TryGetNetworkedItem(containedItem, out NetworkedItem networkedItem))
            {
                continue;
            }

            networkedItem.SetLastOwner(playerId, markDirty);
        }
    }

    public void OnThrow(Vector3 direction)
    {
        snapshotBuilder.CaptureThrow(direction);
    }


    #region Item Value Tracking
    public void RegisterTrackedValue<T>(string key, Func<T> valueGetter, Action<T> valueSetter, Func<T, T, bool> thresholdComparer = null, bool serverAuthoritative = false)
    {
        trackedState.Register(
            key,
            valueGetter,
            valueSetter,
            thresholdComparer,
            serverAuthoritative,
            NetworkLifecycle.Instance.IsHost(),
            $"{name} ({NetId})");
    }

    public void FinaliseTrackedValues()
    {
        //Multiplayer.LogDebug(() => $"NetworkedItem.FinaliseTrackedValues() itemNetId: {NetId}, item name: {name}");

        registrationComplete = true;
        ApplyPendingSnapshots();

    }

    public bool CanRequestAuthoritativeCreation()
    {
        if (NetId != 0 ||
            Item == null ||
            authoritativeCreationRejected)
            return false;

        return Item.IsGrabbed() ||
            (Inventory.Instance != null &&
             Inventory.Instance.Contains(gameObject, false));
    }

    public uint PrepareAuthoritativeCreationRequest()
    {
        if (CreationRequestId == 0)
        {
            CreationRequestId = nextCreationRequestId++;
            if (CreationRequestId == 0)
                CreationRequestId = nextCreationRequestId++;
        }

        byte localPlayerId =
            NetworkLifecycle.Instance.Client?.PlayerId ?? 0;
        if (localPlayerId != 0)
            SetLastOwner(localPlayerId, false);

        return CreationRequestId;
    }

    public void ConfirmAuthoritativeCreation(
        ushort netId,
        byte ownerId)
    {
        NetId = netId;
        SetLastOwner(ownerId, false);
        snapshotBuilder.MarkAuthoritativeCreationConfirmed();
        CreationRequestId = 0;
        authoritativeCreationRejected = false;
    }

    internal bool EnsureRegistered()
    {
        return initialised || Register();
    }

    internal void SetLastDirtyTick(uint tick)
    {
        LastDirtyTick = tick;
    }

    public void RejectAuthoritativeCreation()
    {
        authoritativeCreationRejected = true;
    }

    internal bool HasDirtyValues()
    {
        return trackedState.HasDirtyValues(
            NetworkLifecycle.Instance.IsHost());
    }

    internal Dictionary<string, object> GetDirtyStateData()
    {
        return trackedState.GetDirtyValues(
            NetworkLifecycle.Instance.IsHost());
    }
    internal Dictionary<string, object> GetAllStateData()
    {
        return trackedState.GetAllValues();
    }

    public bool ValidateClientTrackedValues(
        Dictionary<string, object> newValues)
    {
        return trackedState.AcceptsClientValues(newValues);
    }

    internal void MarkValuesClean()
    {
        trackedState.MarkClean();
    }

    #endregion

    public ItemUpdateData GetSnapshot()
    {
        return snapshotBuilder.GetSnapshot();
    }

    public void ReceiveSnapshot(ItemUpdateData snapshot)
    {
        if (snapshot == null || snapshot.UpdateType == ItemUpdateData.ItemUpdateType.None)
            return;

        if (!registrationComplete)
        {
            Multiplayer.Log($"NetworkedItem.ReceiveSnapshot() netId: {snapshot?.ItemNetId}, ItemUpdateType: {snapshot?.UpdateType}. Queuing");
            pendingSnapshots.Enqueue(snapshot);
            return;
        }

        ApplySnapshot(snapshot);
    }

    private void ApplySnapshot(ItemUpdateData snapshot)
    {
        snapshotApplicator.Apply(snapshot);
        snapshotBuilder.MarkApplied();
    }

    public ItemUpdateData CreateUpdateData(ItemUpdateData.ItemUpdateType updateType)
    {
        return snapshotBuilder.Create(updateType);
    }

    internal void ApplyTrackedValues(
        Dictionary<string, object> newValues)
    {
        trackedState.Apply(
            newValues,
            NetworkLifecycle.Instance.IsHost(),
            $"{name} ({NetId})");
    }

    internal void MarkRemoteThrow()
    {
        snapshotBuilder.MarkRemoteThrow();
    }

    public Vector3 GetRelevancePosition()
    {
        if (gadgetItem != null && gadgetItem.Gadget != null && gadgetItem.Gadget.IsLinked)
            return gadgetItem.Gadget.transform.position;

        return transform.position;
    }

    protected override void OnDestroy()
    {
        snapshotApplicator?.Dispose();
        if (UnloadWatcher.isQuitting || UnloadWatcher.isUnloading)
            return;

        if (NetworkLifecycle.Instance.IsHost())
        {
            var updateData = CreateUpdateData(ItemUpdateData.ItemUpdateType.Destroy);
            if (updateData != null)
                NetworkedItemManager.Instance.AddDirtyItemSnapshot(this, updateData);
        }

        if (Item != null)
        {
            Item.Grabbed -= OnGrabbed;
            Item.Ungrabbed -= OnUngrabbed;
            Item.ItemInContainerStateChanged -= OnContainerChanged;
            itemBaseToNetworkedItem.Remove(Item);
        }
        else
        {
            Multiplayer.LogWarning($"NetworkedItem.OnDestroy({name}, {NetId}) Item is null!");
        }

        base.OnDestroy();

    }

    public string GetDirtyValuesDebugString()
    {
        return trackedState.GetDirtyDebugString(
            $"{name}, NetId: {NetId}");
    }
}
