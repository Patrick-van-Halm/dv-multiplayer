using DV.CabControls;
using DV.Shops;
using Multiplayer.Components.Networking;
using Multiplayer.Components.Networking.World;
using System;
using System.Collections;
using System.Collections.Generic;

namespace Multiplayer.Components.Networking.World;

internal static class NetworkedStorage
{
    public static void AssignPurchasedItemOwner(ItemBase item)
    {
        if (item == null ||
            GlobalShopController.Instance?.isInstantiatingItems != true ||
            !item.TryGetComponent(out ShopRestocker restocker) ||
            !restocker.restockOnItemDestroyed ||
            !NetworkedItem.TryGetNetworkedItem(item, out NetworkedItem networkedItem))
        {
            return;
        }

        byte ownerId;
        if (!NetworkLifecycle.Instance.IsHost() ||
            !NetworkedShopPurchaseOwnership.TryTake(
                restocker.itemPrefabName,
                out ownerId))
        {
            ownerId =
                NetworkLifecycle.Instance.Client?.PlayerId ?? 0;
        }

        if (ownerId != 0)
            networkedItem.SetLastOwner(ownerId);
    }

    public static Dictionary<ItemBase, bool> FilterItemsByLastOwner(
        StorageController storage)
    {
        var itemStates = new Dictionary<ItemBase, bool>();

        byte localPlayerId = NetworkLifecycle.Instance.Client?.PlayerId ?? 0;
        if (localPlayerId == 0 || storage.StorageWorld == null)
            return itemStates;

        foreach (ItemBase item in storage.StorageWorld.GetStorageItemList())
        {
            if (item == null ||
                !NetworkedItem.TryGetNetworkedItem(item, out NetworkedItem networkedItem))
            {
                continue;
            }

            if (networkedItem.LastOwnerId == 0 && NetworkLifecycle.Instance.IsHost())
                networkedItem.SetLastOwner(localPlayerId);

            if (networkedItem.LastOwnerId == localPlayerId)
                continue;

            itemStates[item] = item.InventorySpecs.BelongsToPlayer;
            item.InventorySpecs.BelongsToPlayer = false;
        }

        return itemStates;
    }

    public static Exception AfterSummon(
        StorageController storage,
        Dictionary<ItemBase, bool> itemStates,
        Exception exception)
    {
        foreach (KeyValuePair<ItemBase, bool> itemState in itemStates)
        {
            if (itemState.Key != null)
                itemState.Key.InventorySpecs.BelongsToPlayer = itemState.Value;
        }

        if (exception != null ||
            storage.StorageLostAndFound == null)
        {
            return exception;
        }

        // Activation is deferred and places at most one new item per frame.
        // Wait until each summoned item has received its final shack transform
        // before asking NetworkedItem to publish it.
        var summonedItems = new HashSet<ItemBase>(
            storage.StorageLostAndFound.GetStorageItemList());
        storage.StartCoroutine(SyncSummonedItemTransforms(summonedItems));
        return null;
    }

    private static IEnumerator SyncSummonedItemTransforms(HashSet<ItemBase> pendingItems)
    {
        const int maxFrames = 300;
        for (int frame = 0;
             frame < maxFrames && pendingItems.Count > 0;
             frame++)
        {
            ICollection<ItemBase> currentLostAndFound =
                StorageController.Instance?.StorageLostAndFound
                    ?.GetStorageItemList();
            pendingItems.RemoveWhere(item =>
            {
                if (item == null)
                    return true;
                if (currentLostAndFound == null ||
                    !currentLostAndFound.Contains(item))
                {
                    return true;
                }

                if (!item.gameObject.activeInHierarchy)
                    return false;

                if (NetworkedItem.TryGetNetworkedItem(item, out NetworkedItem networkedItem))
                    networkedItem.MarkTransformDirty();

                return true;
            });

            yield return null;
        }

        if (pendingItems.Count > 0)
        {
            Multiplayer.LogWarning(
                $"Timed out waiting for {pendingItems.Count} lost-and-found item(s) to activate.");
        }
    }
}

internal static class NetworkedStorageItemTransforms
{
    internal readonly struct RemovedStorageItem
    {
        public ItemBase Item { get; }
        public int Index { get; }

        public RemovedStorageItem(ItemBase item, int index)
        {
            Item = item;
            Index = index;
        }
    }

    public static List<RemovedStorageItem>
        RemoveOtherPlayersItemsFromLostAndFound()
    {
        var removedItems = new List<RemovedStorageItem>();
        StorageController storageController = StorageController.Instance;
        byte localPlayerId = NetworkLifecycle.Instance.Client?.PlayerId ?? 0;
        if (storageController?.StorageLostAndFound == null || localPlayerId == 0)
            return removedItems;

        List<ItemBase> storageItems =
            storageController.StorageLostAndFound.GetStorageItemList();
        for (int index = storageItems.Count - 1; index >= 0; index--)
        {
            ItemBase item = storageItems[index];
            if (item == null ||
                !NetworkedItem.TryGetNetworkedItem(item, out NetworkedItem networkedItem))
            {
                continue;
            }

            if (networkedItem.LastOwnerId == 0 && NetworkLifecycle.Instance.IsHost())
                networkedItem.SetLastOwner(localPlayerId);

            if (networkedItem.LastOwnerId == localPlayerId)
                continue;

            // This is a temporary view filter, not a real storage transfer, so
            // do not fire ItemRemoved/ItemAdded events.
            storageItems.RemoveAt(index);
            removedItems.Add(new RemovedStorageItem(item, index));
        }

        return removedItems;
    }

    public static void RestoreLostAndFoundItems(
        List<RemovedStorageItem> removedItems)
    {
        StorageController storageController = StorageController.Instance;
        if (storageController?.StorageLostAndFound == null)
            return;

        List<ItemBase> storageItems =
            storageController.StorageLostAndFound.GetStorageItemList();
        for (int index = removedItems.Count - 1; index >= 0; index--)
        {
            RemovedStorageItem removedItem = removedItems[index];
            if (removedItem.Item == null ||
                storageItems.Contains(removedItem.Item))
            {
                continue;
            }

            storageItems.Insert(
                Math.Min(removedItem.Index, storageItems.Count),
                removedItem.Item);
        }
    }
}
