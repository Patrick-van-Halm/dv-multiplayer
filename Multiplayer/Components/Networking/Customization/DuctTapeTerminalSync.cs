using DV.Customization.Gadgets.Implementations;
using DV.InventorySystem;
using DV.Items;
using DV.Utils;
using Multiplayer.API;
using Multiplayer.Components.Networking.World;
using Multiplayer.Networking.Data;
using Multiplayer.Networking.Data.Customization;
using Multiplayer.Networking.Data.Items;
using Multiplayer.Networking.Managers.Client;
using Multiplayer.Networking.Managers.Server;
using Multiplayer.Networking.TransportLayers;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Multiplayer.Components.Networking.Customization;

internal static class DuctTapeTerminalSync
{
    private static readonly Dictionary<ushort, WeakReference<NetworkedItem>> pendingLocalReplacements = new();

    public static void RegisterClient(NetworkClient client)
    {
        client.RegisterExternalSerializablePacket<DuctTapeConsumedPacket>(ApplyCanonicalReplacement);
    }

    public static void RegisterServer(NetworkServer server)
    {
        server.RegisterExternalSerializablePacket<DuctTapeConsumedPacket>((packet, sender) =>
        {
            if (packet == null || packet.EmptyTapeItemNetId != 0 || sender is not ServerPlayerWrapper wrapper)
                return;
            ReplaceForRemotePlayer(server, wrapper._serverPlayer, packet.TapeItemNetId);
        });
    }

    public static void ObserveTerminalUse(ushort tapeItemNetId, Vector3 previousPosition)
    {
        if (tapeItemNetId == 0 || CustomizationSyncScope.IsApplyingRemote)
            return;

        if (NetworkLifecycle.Instance.IsHost())
        {
            NetworkedItem replacement = FindEmptyReplacement(previousPosition, requireUnassigned: false);
            if (replacement != null && replacement.NetId != 0)
                BroadcastHostReplacement(tapeItemNetId, replacement);
            return;
        }

        NetworkedItem localReplacement = FindEmptyReplacement(previousPosition, requireUnassigned: true);
        if (localReplacement != null)
            pendingLocalReplacements[tapeItemNetId] = new WeakReference<NetworkedItem>(localReplacement);

        CustomizationPacketSend.SendToServer(NetworkLifecycle.Instance.Client, new DuctTapeConsumedPacket
        {
            TapeItemNetId = tapeItemNetId,
            EmptyTapeItemNetId = 0,
        });
    }

    private static void ReplaceForRemotePlayer(NetworkServer server, ServerPlayer player, ushort tapeItemNetId)
    {
        if (server == null || player == null || !player.OwnsItem(tapeItemNetId) ||
            !NetworkedItem.TryGet(tapeItemNetId, out NetworkedItem oldItem) || oldItem?.Item == null)
            return;

        DuctTape tape = oldItem.Item.GetComponent<DuctTape>();
        if (tape == null || tape.emptyTapeItemPrefab == null || tape.usesLeft > 1)
            return;

        GameObject emptyObject;
        NetworkedItem replacement;
        using (CustomizationSyncScope.Remote(rootAction: true))
        {
            emptyObject = UnityEngine.Object.Instantiate(tape.emptyTapeItemPrefab, oldItem.transform.position, oldItem.transform.rotation);
            RespawnOnDrop respawn = emptyObject.GetComponent<RespawnOnDrop>();
            if (respawn != null)
                respawn.ignoreDistanceFromSpawnPosition = true;

            DuctTapeEmpty emptyTape = emptyObject.GetComponent<DuctTapeEmpty>();
            replacement = emptyObject.GetComponent<NetworkedItem>() ?? emptyObject.AddComponent<NetworkedItem>();
            if (emptyTape != null)
                replacement.Initialize(emptyTape, createDirty: false);

            if (replacement.NetId == 0 || replacement.Item == null)
            {
                UnityEngine.Object.Destroy(emptyObject);
                return;
            }

            player.RemoveOwnedItem(tapeItemNetId);
            player.AddOwnedItem(replacement.NetId);
            emptyObject.SetActive(false);
        }

        player.KnownItems[replacement] = NetworkLifecycle.Instance.Tick;
        CustomizationPacketSend.SendJoinState(server, player.Peer, new DuctTapeConsumedPacket
        {
            TapeItemNetId = tapeItemNetId,
            EmptyTapeItemNetId = replacement.NetId,
        });

        foreach (ServerPlayer recipient in server.ServerPlayers)
        {
            if (recipient == player || recipient.Peer == server.SelfPeer ||
                recipient.LoadingState < PlayerLoadingState.ReadyForCustomizers)
                continue;

            SendReplacementCreate(server, recipient, replacement, player.PlayerId);
            CustomizationPacketSend.SendJoinState(server, recipient.Peer, new DuctTapeConsumedPacket
            {
                TapeItemNetId = tapeItemNetId,
                EmptyTapeItemNetId = replacement.NetId,
            });
        }

        UnityEngine.Object.Destroy(oldItem.gameObject);
    }

    private static void BroadcastHostReplacement(ushort oldItemNetId, NetworkedItem replacement)
    {
        NetworkServer server = NetworkLifecycle.Instance.Server;
        if (server == null || replacement?.Item == null)
            return;

        foreach (ServerPlayer recipient in server.ServerPlayers)
        {
            if (recipient.Peer == server.SelfPeer || recipient.LoadingState < PlayerLoadingState.ReadyForCustomizers)
                continue;

            SendReplacementCreate(server, recipient, replacement, 0);
            CustomizationPacketSend.SendJoinState(server, recipient.Peer, new DuctTapeConsumedPacket
            {
                TapeItemNetId = oldItemNetId,
                EmptyTapeItemNetId = replacement.NetId,
            });
        }
    }

    private static void SendReplacementCreate(NetworkServer server, ServerPlayer recipient, NetworkedItem replacement, byte ownerId)
    {
        if (recipient.KnownItems.ContainsKey(replacement))
            return;

        ItemUpdateData create = replacement.CreateUpdateData(ItemUpdateData.ItemUpdateType.Create);
        if (create == null)
            return;

        if (ownerId != 0)
        {
            create.ItemState = ItemState.InInventory;
            create.Player = ownerId;
        }

        server.SendItemsChangePacket(new List<ItemUpdateData> { create }, recipient);
        recipient.KnownItems[replacement] = NetworkLifecycle.Instance.Tick;
    }

    private static void ApplyCanonicalReplacement(DuctTapeConsumedPacket packet)
    {
        if (packet == null || packet.TapeItemNetId == 0 || packet.EmptyTapeItemNetId == 0)
            return;

        if (pendingLocalReplacements.TryGetValue(packet.TapeItemNetId, out WeakReference<NetworkedItem> weakReference))
        {
            pendingLocalReplacements.Remove(packet.TapeItemNetId);
            if (weakReference.TryGetTarget(out NetworkedItem replacement) && replacement != null && replacement.Item != null)
            {
                replacement.NetId = packet.EmptyTapeItemNetId;
                return;
            }
        }

        if (NetworkedItem.TryGet(packet.TapeItemNetId, out NetworkedItem oldItem) && oldItem != null)
            UnityEngine.Object.Destroy(oldItem.gameObject);
    }

    private static NetworkedItem FindEmptyReplacement(Vector3 previousPosition, bool requireUnassigned)
    {
        Inventory inventory = SingletonBehaviour<Inventory>.Instance;
        if (inventory == null)
            return null;

        NetworkedItem closest = null;
        float closestDistance = float.PositiveInfinity;
        foreach (DuctTapeEmpty emptyTape in Resources.FindObjectsOfTypeAll<DuctTapeEmpty>())
        {
            if (emptyTape == null || !emptyTape.gameObject.scene.IsValid() || !emptyTape.gameObject.activeInHierarchy)
                continue;

            NetworkedItem candidate = emptyTape.GetComponent<NetworkedItem>() ?? emptyTape.GetComponentInParent<NetworkedItem>();
            if (candidate == null || candidate.Item == null || (requireUnassigned && candidate.NetId != 0) ||
                inventory.GetEquipSlotForItem(candidate.Item.gameObject) < 0)
                continue;

            float distance = (candidate.transform.position - previousPosition).sqrMagnitude;
            if (distance >= closestDistance)
                continue;
            closest = candidate;
            closestDistance = distance;
        }

        return closest;
    }
}
