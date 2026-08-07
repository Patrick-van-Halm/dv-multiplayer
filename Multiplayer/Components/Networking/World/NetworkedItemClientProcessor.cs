using DV.InventorySystem;
using Multiplayer.Networking.Data.Items;
using System.Collections.Generic;
using UnityEngine;

namespace Multiplayer.Components.Networking.World;

internal sealed class NetworkedItemClientProcessor
{
    private readonly NetworkedItemRequestTracker requestTracker;
    private readonly NetworkedItemPrefabCatalog prefabCatalog;
    private readonly NetworkedItemCache itemCache;
    private bool initialized;

    public NetworkedItemClientProcessor(
        NetworkedItemRequestTracker requestTracker,
        NetworkedItemPrefabCatalog prefabCatalog,
        NetworkedItemCache itemCache)
    {
        this.requestTracker = requestTracker;
        this.prefabCatalog = prefabCatalog;
        this.itemCache = itemCache;
    }

    public void CacheWorldItems()
    {
        itemCache.CacheEligibleWorldItems();
        initialized = true;
    }

    public void ProcessChanges()
    {
        if (!initialized)
            return;

        var changedItems = new List<ItemUpdateData>();
        requestTracker.PruneUnavailablePendingCreations();
        foreach (NetworkedItem item in NetworkedItem.GetAll())
        {
            if (item.NetId == 0 &&
                !ShouldSendCreationRequest(item))
            {
                continue;
            }

            ItemUpdateData snapshot = item.GetSnapshot();
            if (snapshot != null)
                changedItems.Add(snapshot);
        }

        if (changedItems.Count > 0)
        {
            NetworkLifecycle.Instance.Client
                .SendItemsChangePacket(changedItems);
        }
    }

    public void ProcessReceived(ItemUpdateData snapshot)
    {
        NetworkedItem.TryGet(
            snapshot.ItemNetId,
            out NetworkedItem item);
        switch (snapshot.UpdateType)
        {
            case ItemUpdateData.ItemUpdateType.Create:
                ProcessCreate(snapshot, item);
                break;
            case ItemUpdateData.ItemUpdateType.Destroy:
                if (item != null)
                    itemCache.Store(item);
                break;
            default:
                if (item != null)
                {
                    item.ReceiveSnapshot(snapshot);
                }
                else
                {
                    NetworkLifecycle.Instance.Client.LogError(
                        $"Item {snapshot.ItemNetId} was not found for " +
                        $"{snapshot.UpdateType}.");
                }
                break;
        }
    }

    private bool ShouldSendCreationRequest(NetworkedItem item)
    {
        if (!item.CanRequestAuthoritativeCreation())
            return false;

        uint requestId =
            item.PrepareAuthoritativeCreationRequest();
        PendingCreationDecision decision =
            requestTracker.TrackPendingCreation(
                requestId,
                item,
                Time.unscaledTime);
        if (decision == PendingCreationDecision.Exhausted)
        {
            item.RejectAuthoritativeCreation();
            NetworkLifecycle.Instance.Client.LogError(
                $"Timed out requesting authoritative creation for " +
                $"{item.name} (request {requestId}). Drop and pick up " +
                "the item to retry.");
        }

        return decision == PendingCreationDecision.Attempt;
    }

    private void ProcessCreate(
        ItemUpdateData snapshot,
        NetworkedItem item)
    {
        if (snapshot.Player ==
                NetworkLifecycle.Instance.Client.PlayerId &&
            snapshot.CreationRequestId != 0 &&
            requestTracker.TryTakePendingCreation(
                snapshot.CreationRequestId,
                out NetworkedItem requestedItem))
        {
            requestedItem.ConfirmAuthoritativeCreation(
                snapshot.ItemNetId,
                snapshot.Player);
            return;
        }

        if (item != null)
        {
            item.ReceiveSnapshot(snapshot);
            return;
        }

        if (!prefabCatalog.TryGet(
                snapshot.PrefabName,
                out InventoryItemSpec spec) ||
            !itemCache.TryCreate(snapshot, spec))
        {
            NetworkLifecycle.Instance.Client.LogError(
                $"Unable to create item {snapshot.ItemNetId} from prefab " +
                $"{snapshot.PrefabName}.");
        }
    }
}
