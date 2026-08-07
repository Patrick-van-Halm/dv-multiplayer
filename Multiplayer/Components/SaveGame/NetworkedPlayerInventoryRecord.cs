using DV.CabControls;
using DV.ThingTypes;
using Multiplayer.Components.Networking.World;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Multiplayer.Components.SaveGame;

internal enum MultiplayerStorageType
{
    PlayerInventory = 100000,
}

internal readonly struct PlayerInventoryLayout
{
    public int Slot { get; }
    public bool IsGrabbed { get; }
    public bool IsLocked { get; }
    public bool IsDropped { get; }
    public string ContainerId { get; }
    public int ContainerSlot { get; }

    public PlayerInventoryLayout(
        int slot,
        bool isGrabbed,
        bool isLocked,
        bool isDropped,
        string containerId = null,
        int containerSlot = -1)
    {
        Slot = slot;
        IsGrabbed = isGrabbed;
        IsLocked = isLocked;
        IsDropped = isDropped;
        ContainerId = containerId;
        ContainerSlot = containerSlot;
    }
}

internal sealed class NetworkedPlayerInventoryRecord : IDisposable
{
    public const string StorageId = "MP_Player_Inventory";

    private readonly List<StorageItemData> persistedItems;
    private readonly Dictionary<NetworkedItem, PlayerInventoryLayout>
        liveItems = [];

    public Guid PlayerGuid { get; }
    public StorageBase Storage { get; }
    public bool IsEstablished { get; private set; }

    public NetworkedPlayerInventoryRecord(
        Guid playerGuid,
        Transform parent,
        IEnumerable<StorageItemData> savedItems,
        bool isEstablished)
    {
        PlayerGuid = playerGuid;
        IsEstablished = isEstablished;
        persistedItems = savedItems?
            .Where(item => item != null)
            .Select(Clone)
            .ToList() ?? [];

        var storageObject = new GameObject(
            $"[Storage {StorageId} {playerGuid}]");
        storageObject.transform.SetParent(parent, false);
        Storage = storageObject.AddComponent<StorageBase>();
        Storage.storageType =
            (StorageType)MultiplayerStorageType.PlayerInventory;
        Storage.acceptsNonEssential = true;
        StorageController.Instance?.AddStorageToList(Storage);
    }

    public IReadOnlyList<StorageItemData> CreateSnapshot()
    {
        var snapshot = persistedItems.Select(Clone).ToList();
        foreach (KeyValuePair<NetworkedItem, PlayerInventoryLayout> entry in
                 liveItems)
        {
            StorageItemData item = CreateItemData(entry.Key, entry.Value);
            if (item == null)
                continue;

            RemoveMatchingPersistedItem(snapshot, item);
            snapshot.Add(item);
        }

        return snapshot;
    }

    public void Track(
        NetworkedItem item,
        PlayerInventoryLayout layout)
    {
        if (item?.Item == null)
            return;

        StorageController storageController = StorageController.Instance;
        if (storageController != null)
        {
            storageController.AddItemToStorageItemList(
                Storage,
                item.Item);
        }
        else if (!Storage.ContainsItem(item.Item))
        {
            Storage.AddItem(item.Item);
        }

        if (liveItems.TryGetValue(
                item,
                out PlayerInventoryLayout previousLayout))
        {
            StorageItemData previous =
                CreateItemData(item, previousLayout);
            if (previous != null)
                RemoveMatchingPersistedItem(persistedItems, previous);
        }

        StorageItemData current = CreateItemData(item, layout);
        if (current != null)
            RemoveMatchingPersistedItem(persistedItems, current);
        liveItems[item] = layout;
    }

    public void MarkEstablished()
    {
        IsEstablished = true;
    }

    public bool Remove(NetworkedItem item)
    {
        if (item == null || !liveItems.Remove(item))
            return false;

        if (item.Item != null && Storage.ContainsItem(item.Item))
            Storage.RemoveItem(item.Item);
        return true;
    }

    public IReadOnlyList<NetworkedItem> Freeze()
    {
        persistedItems.Clear();
        persistedItems.AddRange(CreateSnapshot().Select(Clone));

        List<NetworkedItem> detachedItems = liveItems.Keys
            .Where(item => item != null)
            .ToList();
        foreach (NetworkedItem item in detachedItems)
        {
            if (item.Item != null && Storage.ContainsItem(item.Item))
                Storage.RemoveItem(item.Item);
        }
        liveItems.Clear();
        return detachedItems;
    }

    public void Dispose()
    {
        StorageController.Instance?.RemoveStorageFromList(Storage);
        if (Storage != null)
            UnityEngine.Object.Destroy(Storage.gameObject);
    }

    private static StorageItemData CreateItemData(
        NetworkedItem item,
        PlayerInventoryLayout layout)
    {
        if (item?.Item?.InventorySpecs == null)
            return null;

        JObject state = null;
        if (item.TryGetComponent(out ItemSaveData itemSaveData))
        {
            try
            {
                state = itemSaveData.SaveItemData();
            }
            catch (Exception exception)
            {
                Multiplayer.LogWarning(
                    $"Could not save player inventory item " +
                    $"{item.Item.InventorySpecs.ItemPrefabName}: " +
                    $"{exception.Message}");
            }
        }

        return new StorageItemData(
            item.Item.InventorySpecs.ItemPrefabName,
            Vector3.zero,
            Quaternion.identity,
            item.Item.InventorySpecs.BelongsToPlayer,
            layout.IsGrabbed,
            state: state,
            inventorySlotIndex: layout.Slot,
            containerSlotIndex: layout.ContainerSlot,
            inLockedSlot: layout.IsLocked,
            isDropped: layout.IsDropped,
            containerId: layout.ContainerId);
    }

    private static void RemoveMatchingPersistedItem(
        List<StorageItemData> items,
        StorageItemData current)
    {
        int index;
        if (!string.IsNullOrEmpty(current.containerId))
        {
            index = items.FindIndex(
                item =>
                    item.containerId == current.containerId &&
                    item.containerSlotIndex ==
                    current.containerSlotIndex);
        }
        else if (current.inventorySlotIndex >= 0)
        {
            index = items.FindIndex(
                item =>
                    string.IsNullOrEmpty(item.containerId) &&
                    item.inventorySlotIndex ==
                    current.inventorySlotIndex);
        }
        else
        {
            index = items.FindIndex(
                item =>
                    item.isGrabbed &&
                    item.itemPrefabName ==
                    current.itemPrefabName);
        }
        if (index >= 0)
            items.RemoveAt(index);
    }

    private static StorageItemData Clone(StorageItemData item)
    {
        return new StorageItemData(
            item.itemPrefabName,
            item.ItemPosition,
            item.ItemRotation,
            item.belongsToPlayer,
            item.isGrabbed,
            item.carGuid,
            item.state?.DeepClone() as JObject,
            item.inventorySlotIndex,
            item.containerSlotIndex,
            item.inLockedSlot,
            item.isDropped,
            item.containerId);
    }
}
