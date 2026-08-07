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

<<<<<<< HEAD
    #endregion

=======
    private void ProcessReceivedAsHost(ItemUpdateData snapshot, ServerPlayer player)
    {
        if (snapshot.UpdateType == ItemUpdateData.ItemUpdateType.Create)
        {
            NetworkLifecycle.Instance.Server.LogError($"NetworkedItemManager.ProcessReceivedAsHost() Host received Create snapshot! ItemNetId: {snapshot.ItemNetId}, prefabName: {snapshot.PrefabName}");
            return;
        }

        if (NetworkedItem.TryGet(snapshot.ItemNetId, out NetworkedItem netItem))
        {
            if (ValidatePlayerAction(snapshot, player)) //Ensure the player can do this
            {
                NetworkLifecycle.Instance.Server.LogWarning($"NetworkedItemManager.ProcessReceivedAsHost() ItemNetId: {snapshot.ItemNetId}, snapshot type: {snapshot.UpdateType}");
                netItem.ReceiveSnapshot(snapshot);
            }
            else
            {
                NetworkLifecycle.Instance.Server.LogWarning($"NetworkedItemManager.ProcessReceivedAsHost() Player action validation failed for ItemNetId: {snapshot.ItemNetId}");
            }
        }
        else
        {
            NetworkLifecycle.Instance.Server.LogError($"NetworkedItemManager.ProcessReceivedAsHost() NetworkedItem not found! Update Type: {snapshot.UpdateType}, ItemNetId: {snapshot.ItemNetId}, prefabName: {snapshot.PrefabName}");
        }
    }

    private bool ValidatePlayerAction(ItemUpdateData snapshot, ServerPlayer player)
    {
        return true;
        // Must have valid item
        if (!NetworkedItem.TryGet(snapshot.ItemNetId, out NetworkedItem networkedItem))
            return false;

        Multiplayer.LogDebug(() => $"ValidatePlayerAction() ItemId: {snapshot.ItemNetId}, name: {networkedItem.name} Update Type: {snapshot.UpdateType}, Item State: {snapshot.ItemState}, Player: {player.Username}");

        switch (snapshot.ItemState)
        {
            case ItemState.InHand:
            case ItemState.InInventory:
                // Check if someone else owns it
                GetItemOwner(snapshot.ItemNetId, out ServerPlayer currentOwner);
                Multiplayer.LogDebug(() => $"ValidatePlayerAction() ItemId: {snapshot.ItemNetId}, name: {networkedItem.name} Update Type: {snapshot.UpdateType}, Item State: {snapshot.ItemState}, Player: {player?.Username}, Current Owner: {currentOwner?.Username}");

                if (currentOwner != null && currentOwner != player)
                    return false;

                // Check pickup distance
                float distance = Vector3.Distance(player.WorldPosition, networkedItem.transform.position);
                if (distance > MAX_REACH_DISTANCE)
                    return false;

                Multiplayer.LogDebug(() => $"ValidatePlayerAction() ItemId: {snapshot.ItemNetId}, name: {networkedItem.name} Update Type: {snapshot.UpdateType}, Item State: {snapshot.ItemState}, Player: {player.Username}, Distance check: {distance}");
                break;

            case ItemState.Dropped:
            case ItemState.Thrown:
            case ItemState.Attached: //needs additional checks for distance to coupler
            case ItemState.InContainer: //container ownership is represented by the contained item's owner
            case ItemState.InstalledGadget: //keep ownership while the source item represents an installed gadget
                // Only owner can drop/throw
                if (!player.OwnsItem(snapshot.ItemNetId))
                    return false;
                break;
        }

        return true;
    }

    private bool GetItemOwner(ushort itemNetId, out ServerPlayer owner)
    {
        owner = NetworkLifecycle.Instance.Server.ServerPlayers.FirstOrDefault(p => p.OwnsItem(itemNetId));
        return owner != null;
    }
    #endregion

    #region Client

    private void ProcessClientChanges(uint tick)
    {
        List<ItemUpdateData> changedItems = new List<ItemUpdateData>();

        if(!ClientInitialised)
            return;

        foreach (var item in NetworkedItem.GetAll())
        {
            ItemUpdateData snapshot = item.GetSnapshot();
            if (snapshot != null)
            {
                changedItems.Add(snapshot);
            }
        }

        if (changedItems.Count > 0)
        {
            NetworkLifecycle.Instance.Client.SendItemsChangePacket(changedItems);
        }
    }

    private void ProcessReceivedAsClient(ItemUpdateData snapshot)
    {
        NetworkedItem.TryGet(snapshot.ItemNetId, out NetworkedItem netItem);

        NetworkLifecycle.Instance.Client.LogDebug(() => $"NetworkedItemManager.ProcessReceivedAsClient() Update Type: {snapshot?.UpdateType}, ItemNetId: {snapshot?.ItemNetId}, prefabName: {snapshot?.PrefabName}");
        if (snapshot.UpdateType == ItemUpdateData.ItemUpdateType.Create)
        {
            //if the item already exists we need to remove it
            if (netItem != null)
                SendToCache(netItem);

            CreateItem(snapshot);
        }
        else if (snapshot.UpdateType == ItemUpdateData.ItemUpdateType.Destroy)
        {
            SendToCache(netItem);
        }
        else if (netItem != null)
        {
            netItem.ReceiveSnapshot(snapshot);
        }
        else
        {
            NetworkLifecycle.Instance.Client.LogError($"NetworkedItemManager.ProcessReceivedAsClient() NetworkedItem not found on client! Update Type: {snapshot.UpdateType}, ItemNetId: {snapshot.ItemNetId}, prefabName: {snapshot.PrefabName}");
        }
    }
    #endregion

    #region Item Cache And Management
    private void CreateItem(ItemUpdateData snapshot)
    {
        if(snapshot == null || snapshot.ItemNetId == 0)
        {
            Multiplayer.LogError($"NetworkedItemManager.CreateItem() Invalid snapshot! ItemNetId: {snapshot?.ItemNetId}, prefabName: {snapshot?.PrefabName}");
            return;
        }

        NetworkedItem newItem = GetFromCache(snapshot.PrefabName);

        if(newItem == null)
        {
            //GameObject prefabObj = Resources.Load(snapshot.PrefabName) as GameObject;
            
            if (!ItemPrefabs.TryGetValue(snapshot.PrefabName, out InventoryItemSpec spec))
            {
                Multiplayer.LogError($"NetworkedItemManager.CreateItem() Unable to load prefab for ItemNetId: {snapshot.ItemNetId}, prefabName: {snapshot.PrefabName}");
                return;
            }

            //create a new item
            GameObject gameObject = Instantiate(spec.gameObject, NetworkedItem.ToWorldPosition(snapshot.ItemPosition), snapshot.ItemRotation);

            //Make sure we have a NetworkedItem
            newItem = gameObject.GetOrAddComponent<NetworkedItem>();
        }

        newItem.gameObject.SetActive(true);
        newItem.NetId = snapshot.ItemNetId;

        newItem.ReceiveSnapshot(snapshot);
    }

    private void BuildPrefabLookup()
    {
        NetworkLifecycle.Instance.Client.LogDebug(() => $"BuildPrefabLookup()");

        foreach (var item in Globals.G.Items.items)
        {
            if (!ItemPrefabs.ContainsKey(item.ItemPrefabName))
            {
                ItemPrefabs[item.itemPrefabName] = item;
            }
        }
    }
>>>>>>> 64b64ddac4c01653a5f35094f35d4762e08207d6
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
