using Multiplayer.Components.Networking.World;
using Multiplayer.Networking.Data;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Multiplayer.Components.SaveGame;

internal sealed class NetworkedPlayerInventoryStore : IDisposable
{
    private readonly Transform parent;
    private readonly Dictionary<Guid, NetworkedPlayerInventoryRecord>
        records = [];
    private readonly Dictionary<NetworkedItem, Guid> itemOwners = [];
    private bool loaded;

    public NetworkedPlayerInventoryStore(Transform parent)
    {
        this.parent = parent;
    }

    public bool TryPreparePlayer(
        ServerPlayer player,
        JObject players,
        out IReadOnlyList<StorageItemData> inventory)
    {
        EnsureLoaded(players);
        if (records.TryGetValue(
                player.Guid,
                out NetworkedPlayerInventoryRecord existing))
        {
            player.Storage = existing.Storage;
            inventory = existing.CreateSnapshot();
            return existing.IsEstablished;
        }

        NetworkedPlayerInventoryRecord created =
            CreateRecord(player.Guid, [], false);
        player.Storage = created.Storage;
        inventory = [];
        return false;
    }

    public void Track(
        ServerPlayer player,
        NetworkedItem item,
        PlayerInventoryLayout layout)
    {
        if (player == null || item == null)
            return;

        NetworkedPlayerInventoryRecord record =
            GetOrCreate(player.Guid);
        player.Storage = record.Storage;
        record.Track(item, layout);
        itemOwners[item] = player.Guid;
    }

    public void Remove(NetworkedItem item)
    {
        if (item == null ||
            !itemOwners.TryGetValue(item, out Guid playerGuid))
        {
            return;
        }

        itemOwners.Remove(item);
        if (records.TryGetValue(
                playerGuid,
                out NetworkedPlayerInventoryRecord record))
        {
            record.Remove(item);
        }
    }

    public IReadOnlyList<NetworkedItem> Freeze(ServerPlayer player)
    {
        if (player == null ||
            !records.TryGetValue(
                player.Guid,
                out NetworkedPlayerInventoryRecord record))
        {
            return Array.Empty<NetworkedItem>();
        }

        IReadOnlyList<NetworkedItem> items = record.Freeze();
        foreach (NetworkedItem item in items)
            itemOwners.Remove(item);
        return items;
    }

    public void MarkEstablished(ServerPlayer player)
    {
        if (player == null)
            return;
        GetOrCreate(player.Guid).MarkEstablished();
    }

    public void Save(JObject players)
    {
        EnsureLoaded(players);
        foreach (KeyValuePair<Guid, NetworkedPlayerInventoryRecord> entry in
                 records)
        {
            if (!entry.Value.IsEstablished)
                continue;

            string key = entry.Key.ToString();
            JObject playerData =
                players[key] as JObject ?? [];
            playerData[NetworkedPlayerInventoryRecord.StorageId] =
                JArray.FromObject(entry.Value.CreateSnapshot());
            players[key] = playerData;
        }
    }

    public void Dispose()
    {
        foreach (NetworkedPlayerInventoryRecord record in records.Values)
            record.Dispose();
        records.Clear();
        itemOwners.Clear();
    }

    private void EnsureLoaded(JObject players)
    {
        if (loaded)
            return;
        loaded = true;
        if (players == null)
            return;

        foreach (JProperty property in players.Properties())
        {
            if (!Guid.TryParse(property.Name, out Guid playerGuid) ||
                property.Value is not JObject playerData ||
                playerData[NetworkedPlayerInventoryRecord.StorageId]
                    is not JArray savedInventory)
            {
                continue;
            }

            List<StorageItemData> items;
            try
            {
                items = savedInventory
                    .ToObject<List<StorageItemData>>() ?? [];
            }
            catch (Exception exception)
            {
                Multiplayer.LogWarning(
                    $"Could not load saved inventory for {playerGuid}: " +
                    $"{exception.Message}");
                items = [];
            }
            CreateRecord(playerGuid, items, true);
        }
    }

    private NetworkedPlayerInventoryRecord GetOrCreate(Guid playerGuid)
    {
        if (!records.TryGetValue(
                playerGuid,
                out NetworkedPlayerInventoryRecord record))
        {
            record = CreateRecord(playerGuid, [], false);
        }
        return record;
    }

    private NetworkedPlayerInventoryRecord CreateRecord(
        Guid playerGuid,
        IEnumerable<StorageItemData> items,
        bool isEstablished)
    {
        var record = new NetworkedPlayerInventoryRecord(
            playerGuid,
            parent,
            items,
            isEstablished);
        records[playerGuid] = record;
        return record;
    }
}
