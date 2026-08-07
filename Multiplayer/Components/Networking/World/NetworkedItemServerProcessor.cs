using DV.InventorySystem;
using DV.Items;
using Multiplayer.Components.SaveGame;
using Multiplayer.Networking.Data;
using Multiplayer.Networking.Data.Items;
using Multiplayer.Utils;
using System.Collections.Generic;
using UnityEngine;

namespace Multiplayer.Components.Networking.World;

internal sealed class NetworkedItemServerProcessor
{
    private readonly NetworkedItemRequestTracker requestTracker;
    private readonly NetworkedItemPrefabCatalog prefabCatalog;

    public NetworkedItemServerProcessor(
        NetworkedItemRequestTracker requestTracker,
        NetworkedItemPrefabCatalog prefabCatalog)
    {
        this.requestTracker = requestTracker;
        this.prefabCatalog = prefabCatalog;
    }

    public bool Process(
        ItemUpdateData snapshot,
        ServerPlayer player)
    {
        if (snapshot.UpdateType ==
            ItemUpdateData.ItemUpdateType.Create)
        {
            ProcessCreation(snapshot, player);
            return false;
        }

        if (!NetworkedItem.TryGet(
                snapshot.ItemNetId,
                out NetworkedItem item))
        {
            NetworkLifecycle.Instance.Server.LogError(
                $"Networked item {snapshot.ItemNetId} was not found for " +
                $"{snapshot.UpdateType}.");
            return false;
        }

        if (!NetworkedItemSnapshotValidator.ValidateClientAction(
                snapshot,
                player,
                NetworkLifecycle.Instance.Server.ServerPlayers,
                NetworkedItemManager.REACH_DISTANCE_BUFFER))
        {
            NetworkLifecycle.Instance.Server.LogWarning(
                $"Rejected player item action for " +
                $"{snapshot.ItemNetId}.");
            return false;
        }

        item.ReceiveSnapshot(snapshot);
        UpdatePlayerInventory(snapshot, player, item);
        return true;
    }

    private void ProcessCreation(
        ItemUpdateData snapshot,
        ServerPlayer player)
    {
        if (player != null &&
            requestTracker.TryGetCreationResponse(
                player.Guid,
                snapshot.CreationRequestId,
                out ItemUpdateData previousResponse))
        {
            NetworkLifecycle.Instance.Server.SendItemsChangePacket(
                new List<ItemUpdateData> { previousResponse },
                player);
            return;
        }

        if (!TryValidateCreation(
                snapshot,
                player,
                out InventoryItemSpec spec))
        {
            NetworkLifecycle.Instance.Server.LogWarning(
                $"Rejected invalid item creation request " +
                $"{snapshot.CreationRequestId} from " +
                $"{player?.Username ?? "unknown"}.");
            return;
        }

        if (!requestTracker.TryConsumeCreationRequest(
                player.Guid,
                Time.unscaledTime))
        {
            NetworkLifecycle.Instance.Server.LogWarning(
                $"Rate-limited item creation request " +
                $"{snapshot.CreationRequestId} from {player.Username}.");
            return;
        }

        Vector3 spawnPosition = player.WorldPosition;
        GameObject gameObject = Object.Instantiate(
            spec.gameObject,
            spawnPosition,
            snapshot.ItemRotation);
        NetworkedItem item =
            gameObject.GetOrAddComponent<NetworkedItem>();

        snapshot.ItemNetId = item.NetId;
        snapshot.Player = player.PlayerId;
        snapshot.ItemPosition =
            NetworkedItem.ToNetworkPosition(spawnPosition);

        item.SetLastOwner(player.PlayerId, false);
        player.AddOwnedItem(item.NetId);
        player.NearbyItems[item] = Time.time;
        player.KnownItems[item] = NetworkLifecycle.Instance.Tick;
        item.ReceiveSnapshot(snapshot);
        UpdatePlayerInventory(snapshot, player, item);

        requestTracker.CacheCreationResponse(
            player.Guid,
            snapshot);
        NetworkLifecycle.Instance.Server.SendItemsChangePacket(
            new List<ItemUpdateData> { snapshot },
            player);
    }

    private static void UpdatePlayerInventory(
        ItemUpdateData snapshot,
        ServerPlayer player,
        NetworkedItem item)
    {
        if (snapshot.UpdateType !=
                ItemUpdateData.ItemUpdateType.Create &&
            !snapshot.UpdateType.HasFlag(
                ItemUpdateData.ItemUpdateType.ItemState))
        {
            return;
        }

        if (snapshot.ItemState == ItemState.InInventory ||
            snapshot.ItemState == ItemState.InHand)
        {
            if (snapshot.InventorySlot < 0)
                return;

            NetworkedSaveGameManager.Instance
                ?.Server_TrackPlayerInventoryItem(
                    player,
                    item,
                    new PlayerInventoryLayout(
                        snapshot.InventorySlot,
                        snapshot.ItemState == ItemState.InHand,
                        snapshot.InLockedSlot,
                        snapshot.IsDropped));
        }
        else if (snapshot.ItemState == ItemState.InContainer &&
                 player.OwnsItem(snapshot.ContainerNetId) &&
                 NetworkedItem.TryGet(
                     snapshot.ContainerNetId,
                     out NetworkedItem containerItem) &&
                 containerItem.TryGetComponent(
                     out ItemContainer container) &&
                 !string.IsNullOrEmpty(container.ContainerId) &&
                 snapshot.ContainerSlot >= 0 &&
                 snapshot.ContainerSlot < container.Capacity)
        {
            NetworkedSaveGameManager.Instance
                ?.Server_TrackPlayerInventoryItem(
                    player,
                    item,
                    new PlayerInventoryLayout(
                        -1,
                        false,
                        false,
                        false,
                        container.ContainerId,
                        snapshot.ContainerSlot));
        }
        else
        {
            NetworkedSaveGameManager.Instance
                ?.Server_RemovePlayerInventoryItem(item);
        }
    }

    private bool TryValidateCreation(
        ItemUpdateData snapshot,
        ServerPlayer player,
        out InventoryItemSpec spec)
    {
        spec = null;
        return player != null &&
               snapshot.ItemNetId == 0 &&
               snapshot.CreationRequestId != 0 &&
               !string.IsNullOrEmpty(snapshot.PrefabName) &&
               NetworkedItemSnapshotValidator
                   .IsSafeTrackedValuePayload(snapshot.States) &&
               snapshot.HasValidInventoryLayout() &&
               snapshot.ItemRotation.IsFinite() &&
               HasValidCreationDestination(snapshot, player) &&
               prefabCatalog.TryGet(
                   snapshot.PrefabName,
                   out spec);
    }

    private static bool HasValidCreationDestination(
        ItemUpdateData snapshot,
        ServerPlayer player)
    {
        if (snapshot.ItemState == ItemState.InHand ||
            snapshot.ItemState == ItemState.InInventory)
        {
            return true;
        }

        if (snapshot.ItemState != ItemState.InContainer ||
            !player.OwnsItem(snapshot.ContainerNetId) ||
            !NetworkedItem.TryGet(
                snapshot.ContainerNetId,
                out NetworkedItem containerItem) ||
            !containerItem.TryGetComponent(
                out ItemContainer container) ||
            snapshot.ContainerSlot < 0 ||
            snapshot.ContainerSlot >= container.Capacity)
        {
            return false;
        }

        GameObject occupyingItem = container[snapshot.ContainerSlot];
        return occupyingItem == null;
    }
}
