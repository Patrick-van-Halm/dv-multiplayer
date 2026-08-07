using System.Collections.Generic;
using System.Linq;
using DV.Utils;
using UnityEngine;
using JetBrains.Annotations;
using Multiplayer.Networking.Data;
using System;
using Multiplayer.Utils;
using DV.InventorySystem;
using DV.Items;
using Multiplayer.Components.SaveGame;
using Multiplayer.Networking.Data.Items;

namespace Multiplayer.Components.Networking.World;

public class NetworkedItemManager : SingletonBehaviour<NetworkedItemManager>
{
    /*
     * Server 
     */

    //Culling distance for items
    public const float MAX_DISTANCE_TO_ITEM = 100f;
    public const float MAX_DISTANCE_TO_ITEM_SQR = MAX_DISTANCE_TO_ITEM * MAX_DISTANCE_TO_ITEM;
    public const float NEARBY_REMOVAL_DELAY = 3f; // 3 seconds delay
    public const float REACH_DISTANCE_BUFFER = 0.5f;

    //caches for item snapshots
    private List<ItemUpdateData> DestroyedItems = new(64);
    private List<Tuple<ItemUpdateData, ServerPlayer>> RelayedItems = new(64);
    private readonly HashSet<NetworkedItem> globallyRelevantItems = [];
    private readonly NetworkedItemRequestTracker requestTracker = new();
    private readonly NetworkedJobPaymentClaims jobPaymentClaims = new();
    private readonly NetworkedItemPrefabCatalog prefabCatalog = new();
    private NetworkedItemServerProcessor serverProcessor;

    /*
     * Client
     */

    private readonly NetworkedItemCache itemCache = new();
    private NetworkedItemClientProcessor clientProcessor;


    /* 
     * Common
     */
    private Queue<Tuple<ItemUpdateData, ServerPlayer>> ReceivedSnapshots = new(64);

    protected void Start()
    {
        NetworkLifecycle.Instance.OnTick += Common_OnTick;

        prefabCatalog.Build();
        serverProcessor = new NetworkedItemServerProcessor(
            requestTracker,
            prefabCatalog);
        clientProcessor = new NetworkedItemClientProcessor(
            requestTracker,
            prefabCatalog,
            itemCache);
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        if (UnloadWatcher.isQuitting)
            return;

        NetworkLifecycle.Instance.OnTick -= Common_OnTick;
    }

    public void AddDirtyItemSnapshot(NetworkedItem netItem, ItemUpdateData snapshot)
    {
        if (NetworkLifecycle.Instance.IsHost())
        {
            NetworkedSaveGameManager.Instance
                ?.Server_RemovePlayerInventoryItem(netItem);
        }
        DestroyedItems.Add(snapshot);
        globallyRelevantItems.Remove(netItem);

        foreach(var player in NetworkLifecycle.Instance.Server.ServerPlayers)
        {
            if(player.KnownItems.ContainsKey(netItem))
                player.KnownItems.Remove(netItem);

            if(player.NearbyItems.ContainsKey(netItem))
                player.NearbyItems.Remove(netItem);
        }
    }

    public void ReceiveSnapshots(List<ItemUpdateData> snapshots, ServerPlayer sender)
    {
        if (snapshots == null)
            return;

        foreach (var snapshot in snapshots)
        {
            ReceivedSnapshots.Enqueue(new (snapshot, sender));
        }

        //Multiplayer.LogDebug(() => $"NetworkItemManager.ReceiveSnapshots() count: {ReceivedSnapshots.Count}, from: ");
    }

    #region Common

    private void Common_OnTick(uint tick)
    {
        ProcessReceived();

        if (NetworkLifecycle.Instance.IsHost())
        {
            UpdatePlayerItemLists();
            ProcessChanged(tick);
        }
        else
        {
            clientProcessor.ProcessChanges();
        }
    }

    private void ProcessReceived()
    {
        while (ReceivedSnapshots.Count > 0)
        {
            var snapshotInfo = ReceivedSnapshots.Dequeue();
            ItemUpdateData snapshot = snapshotInfo.Item1;
            try
            {
                //Multiplayer.LogDebug(() => $"ProcessReceived: {snapshot.UpdateType}");

                if (snapshot == null || snapshot.UpdateType == ItemUpdateData.ItemUpdateType.None)
                {
                    Multiplayer.LogError($"NetworkedItemManager.ProcessReceived() Invalid Update Type: {snapshot?.UpdateType}, ItemNetId: {snapshot?.ItemNetId}, prefabName: {snapshot?.PrefabName}");
                    continue;
                }

                if (NetworkLifecycle.Instance.IsHost())
                {
                    if (serverProcessor.Process(
                            snapshot,
                            snapshotInfo.Item2))
                    {
                        RelayedItems.Add(
                            new Tuple<ItemUpdateData, ServerPlayer>(
                                snapshot,
                                snapshotInfo.Item2));
                    }
                }
                else
                {
                    clientProcessor.ProcessReceived(snapshot);
                }
            }
            catch (Exception ex)
            {
                Multiplayer.LogError($"NetworkedItemManager.ProcessReceived() Error! {ex.Message}\r\n{ex.StackTrace}");
            }
        }
    }

    #endregion

    #region Server

    private void UpdatePlayerItemLists()
    {
        float currentTime = Time.time;

        var allItems = NetworkedItem.GetAll();

        foreach (var player in NetworkLifecycle.Instance.Server.ServerPlayers)
        {
            if (player.LoadingState < PlayerLoadingState.ReadyForItems)
                continue;

            foreach (var item in allItems)
            {
                if (item == null)
                {
                    NetworkLifecycle.Instance.Server.LogDebug(() => $"UpdatePlayerItemLists() Null item found in allItems!");
                    continue;
                }

                float sqrDistance = (player.WorldPosition - item.GetRelevancePosition()).sqrMagnitude;

                if (globallyRelevantItems.Contains(item) || sqrDistance <= MAX_DISTANCE_TO_ITEM_SQR)
                {
                    //NetworkLifecycle.Instance.Server.LogDebug(() => $"UpdatePlayerItemLists() Adding for player: {player?.Username}, Nearby Item: {item?.NetId}, {item?.name}");
                    player.NearbyItems[item] = currentTime;
                }
            }

            // Remove items that are no longer nearby
            for (int i = 0; i < player.NearbyItems.Count; i++)
            {
                var kvp = player.NearbyItems.ElementAt(i);

                if (currentTime - kvp.Value > NEARBY_REMOVAL_DELAY)
                {
                    //NetworkLifecycle.Instance.Server.LogDebug(() => $"UpdatePlayerItemLists() Removing for player: {player?.Username}, Nearby Item: {kvp.Key?.NetId}, {kvp.Key?.name}");
                    player.NearbyItems.Remove(kvp.Key);
                }
            }
        }
    }

    private void ProcessChanged(uint tick)
    {
        List<ItemUpdateData> dirtyItems = new List<ItemUpdateData>();
        foreach (var item in NetworkedItem.GetAll())
        {
            ItemUpdateData snapshot = item.GetSnapshot();
            if (snapshot != null)
                dirtyItems.Add(snapshot);
        }

        //NetworkLifecycle.Instance.Server.LogDebug(() => $"ProcessChanged({tick}) DirtyItems: {dirtyItems.Count}");

        foreach (var player in NetworkLifecycle.Instance.Server.ServerPlayers)
        {
            if (player.LoadingState < PlayerLoadingState.ReadyForItems)
                continue;

            List<ItemUpdateData> playerUpdates = new List<ItemUpdateData>();

            // Process nearby items
            foreach (var nearbyItem in player.NearbyItems.Keys)
            {
                if (!player.KnownItems.ContainsKey(nearbyItem))
                {
                    // This is a new item for the player
                    //NetworkLifecycle.Instance.Server.LogDebug(() => $"ProcessChanged({tick}) New item for: {player.Username}, itemNetID{nearbyItem.NetId}");

                    ItemUpdateData snapshot = nearbyItem.CreateUpdateData(ItemUpdateData.ItemUpdateType.Create);
                    player.KnownItems[nearbyItem] = tick;

                    //prevent propagation of creates for special items
                    Type trackedItemType =
                        nearbyItem.TrackedItemType ??
                        nearbyItem.Item?.GetType();
                    if (!NetworkedItemPrefabCatalog.ShouldSkipAutoCreate(
                            trackedItemType))
                        playerUpdates.Add(snapshot);
                }
                else
                {
                    // Check if this item is in the dirty items list
                    var dirtyUpdate = dirtyItems.FirstOrDefault(di => di.ItemNetId == nearbyItem.NetId);

                    if (dirtyUpdate == null)
                    {
                        dirtyUpdate = RelayedItems
                            .LastOrDefault(update =>
                                update.Item2 != player &&
                                update.Item1.ItemNetId == nearbyItem.NetId)
                            ?.Item1;
                    }

                    //NetworkLifecycle.Instance.Server.LogDebug(() => $"ProcessChanged({tick}) Item exists for: {player.Username}, {dirtyUpdate != null}");

                    if (dirtyUpdate == null)
                    {
                        //NetworkLifecycle.Instance.Server.LogDebug(() => $"ProcessChanged({tick}) Item exists for: {player.Username}, LastDirtyTick: {player.KnownItems[nearbyItem] < nearbyItem.LastDirtyTick}");
                        if (player.KnownItems[nearbyItem] < nearbyItem.LastDirtyTick)
                        {
                            dirtyUpdate = nearbyItem.CreateUpdateData(ItemUpdateData.ItemUpdateType.FullSync);
                        }
                    }

                    if (dirtyUpdate != null)
                    {
                        Multiplayer.LogDebug(() => $"ProcessChanged({tick}) Update Type: {dirtyUpdate.UpdateType}, Item State: {dirtyUpdate.ItemState}");
                        playerUpdates.Add(dirtyUpdate);
                        player.KnownItems[nearbyItem] = tick;
                    }
                }
            }

            //NetworkLifecycle.Instance.Server.LogDebug(() => $"ProcessChanged({tick}) Adding {DestroyedItems.Count()} DestroyedItems for: {player.Username}");

            playerUpdates.AddRange(DestroyedItems);

            if (playerUpdates.Count > 0)
            {
                //NetworkLifecycle.Instance.Server.LogDebug(() => $"ProcessChanged({tick}) Sending {playerUpdates.Count()} to player: {player.Username}");
                NetworkLifecycle.Instance.Server.SendItemsChangePacket(playerUpdates, player);
            }
        }

        DestroyedItems.Clear();
        RelayedItems.Clear();
    }

    #endregion

    public void CacheWorldItems()
    {
        if (NetworkLifecycle.Instance.IsHost())
            return;

        clientProcessor.CacheWorldItems();
    }

    public void MarkJobPayment(NetworkedItem item)
    {
        if (jobPaymentClaims.Mark(item))
            globallyRelevantItems.Add(item);
    }

    public bool TryClaimJobPayment(ushort itemNetId, ServerPlayer player)
    {
        return jobPaymentClaims.TryClaim(
            itemNetId,
            player,
            REACH_DISTANCE_BUFFER);
    }

    internal void RemovePlayerRequestState(Guid playerGuid)
    {
        requestTracker.RemovePlayer(playerGuid);
    }

    [UsedImplicitly]
    public new static string AllowAutoCreate()
    {
        return $"[{nameof(NetworkedItemManager)}]";
    }
}
