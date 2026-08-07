using DV.InventorySystem;
using DV.Items;
using DV.Utils;
using Multiplayer.Networking.Data.Items;
using Multiplayer.Utils;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Multiplayer.Components.Networking.World;

internal sealed class NetworkedItemCache
{
    private readonly Dictionary<string, List<NetworkedItem>> cachedItems =
        new(1024);

    public bool TryCreate(
        ItemUpdateData snapshot,
        InventoryItemSpec spec)
    {
        if (snapshot == null ||
            snapshot.ItemNetId == 0 ||
            spec == null)
            return false;

        NetworkedItem item = Take(snapshot.PrefabName);
        if (item == null)
        {
            GameObject gameObject = UnityEngine.Object.Instantiate(
                spec.gameObject,
                NetworkedItem.ToWorldPosition(snapshot.ItemPosition),
                snapshot.ItemRotation);
            item = gameObject.GetOrAddComponent<NetworkedItem>();
        }

        item.gameObject.SetActive(true);
        item.NetId = snapshot.ItemNetId;
        item.ReceiveSnapshot(snapshot);
        return true;
    }

    public void CacheEligibleWorldItems()
    {
        foreach (NetworkedItem item in NetworkedItem.GetAll())
        {
            try
            {
                if (item.Item != null &&
                    !item.Item.IsEssential() &&
                    !item.Item.IsGrabbed() &&
                    !StorageController.Instance.StorageInventory
                        .ContainsItem(item.Item))
                {
                    Store(item);
                }
            }
            catch (Exception exception)
            {
                NetworkLifecycle.Instance.Client.LogError(
                    $"Error caching spawned item: {exception.Message}");
            }
        }
    }

    public void Store(NetworkedItem item)
    {
        if (item?.Item?.InventorySpecs == null)
            return;

        string prefabName = item.Item.InventorySpecs.itemPrefabName;
        if (string.IsNullOrEmpty(prefabName))
            return;

        item.gameObject.SetActive(false);
        UnityEngine.Object.Destroy(
            item.Item.GetComponent<RespawnOnDrop>());

        StorageController storage = StorageController.Instance;
        if (storage.StorageWorld.ContainsItem(item.Item))
            storage.RemoveItemFromWorldStorage(item.Item);
        if (storage.StorageInventory.ContainsItem(item.Item) ||
            storage.StorageLostAndFound.ContainsItem(item.Item))
        {
            storage.RemoveItemFromStorageItemList(item.Item);
        }

        item.Item.InventorySpecs.BelongsToPlayer = false;
        item.NetId = 0;

        if (!cachedItems.TryGetValue(
                prefabName,
                out List<NetworkedItem> items))
        {
            items = [];
            cachedItems[prefabName] = items;
        }
        items.Add(item);
    }

    private NetworkedItem Take(string prefabName)
    {
        if (string.IsNullOrEmpty(prefabName) ||
            !cachedItems.TryGetValue(
                prefabName,
                out List<NetworkedItem> items) ||
            items.Count == 0)
        {
            return null;
        }

        int lastIndex = items.Count - 1;
        NetworkedItem item = items[lastIndex];
        items.RemoveAt(lastIndex);
        return item;
    }
}
