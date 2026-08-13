using DV.Customization.Gadgets;
using DV.InventorySystem;
using DV.Items;
using HarmonyLib;
using MPAPI.Interfaces;
using Multiplayer.API;
using Multiplayer.Components.Networking.World;
using Multiplayer.Networking.Data;
using Multiplayer.Networking.Data.Customization;
using Multiplayer.Networking.Data.Items;
using Multiplayer.Networking.Managers.Server;
using Multiplayer.Networking.TransportLayers;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Multiplayer.Components.Networking.Customization;

internal static class SolderingMagazineSync
{
    private const int DependencyWaitFrames = 120;
    private static readonly FieldInfo MagazineField = AccessTools.Field(typeof(GadgetSolderingTool), "magazine");
    private static readonly FieldInfo EmptyCoilPrefabField = AccessTools.Field(typeof(GadgetSolderingTool), "emptyCoilItemPrefab");
    private static readonly FieldInfo ReelInteractionPointField = AccessTools.Field(typeof(GadgetSolderingTool), "reelInteractionPoint");
    private static readonly MethodInfo UpdateEmptySpoolParams = AccessTools.Method(typeof(GadgetSolderingTool), "UpdateEmptySpoolItemParams");
    private static readonly MethodInfo DropEmptySpoolMethod = AccessTools.Method(typeof(GadgetSolderingTool), "DropEmptySpool");

    public static uint GetSpoolNetId(GadgetSolderingTool tool)
    {
        ItemMagazine magazine = GetMagazine(tool);
        GameObject spool = magazine != null && magazine.Capacity > 0 ? magazine[0] : null;
        if (spool == null)
            return 0;
        ItemBase item = spool.GetComponent<ItemBase>();
        return item != null && NetworkedItem.TryGetNetworkedItem(item, out NetworkedItem networked) ? networked.NetId : 0;
    }

    public static void ApplyCanonicalSpool(GadgetSolderingTool tool, uint spoolNetId)
    {
        if (tool == null || spoolNetId > ushort.MaxValue)
            return;
        if (!TryApplyCanonicalSpool(tool, (ushort)spoolNetId))
            NetworkLifecycle.Instance.StartCoroutine(ApplyWhenReady(tool, (ushort)spoolNetId));
    }

    public static void RegisterClient(Multiplayer.Networking.Managers.Client.NetworkClient client)
    {
        client.RegisterExternalSerializablePacket<SolderingMagazinePacket>(packet =>
        {
            if (packet != null && TryGetTool(packet.ToolItemNetId, out GadgetSolderingTool tool))
                ApplyCanonicalSpool(tool, packet.SpoolItemNetId);
        });
        client.RegisterExternalSerializablePacket<SolderingDropEmptySpoolPacket>(packet =>
        {
            if (packet != null && TryGetTool(packet.ToolItemNetId, out GadgetSolderingTool tool))
                DropEmptySpool(tool);
        });
    }

    public static void RegisterServer(NetworkServer server)
    {
        server.RegisterExternalSerializablePacket<SolderingMagazinePacket>((packet, sender) => OnServerMagazine(server, packet, sender));
        server.RegisterExternalSerializablePacket<SolderingDropEmptySpoolPacket>((packet, sender) => OnServerDrop(server, packet, sender));
    }

    public static void ObserveLoadedSpool(GadgetSolderingTool tool, GameObject spool)
    {
        if (CustomizationSyncScope.IsApplyingRemote || tool == null || spool == null || !TryGetToolNetId(tool, out ushort toolNetId))
            return;

        ItemBase spoolItem = spool.GetComponent<ItemBase>();
        ushort spoolNetId = 0;
        if (spoolItem != null && NetworkedItem.TryGetNetworkedItem(spoolItem, out NetworkedItem networked))
            spoolNetId = networked.NetId;

        MagazineAmmo ammo = spool.GetComponent<MagazineAmmo>();
        if (spoolNetId == 0 && (NetworkLifecycle.Instance.IsHost() || ammo == null || !ammo.isSpent))
            return;

        SolderingMagazinePacket packet = new() { ToolItemNetId = toolNetId, SpoolItemNetId = spoolNetId };
        if (NetworkLifecycle.Instance.IsHost())
            BroadcastCanonical(NetworkLifecycle.Instance.Server, packet, null);
        else
            NetworkLifecycle.Instance.Client.SendExternalSerializablePacketToServer(packet, true);
    }

    public static void ObserveDropEmptySpool(GadgetSolderingTool tool)
    {
        if (CustomizationSyncScope.IsApplyingRemote || !TryGetToolNetId(tool, out ushort toolNetId))
            return;
        SolderingDropEmptySpoolPacket packet = new() { ToolItemNetId = toolNetId };
        if (NetworkLifecycle.Instance.IsHost())
            BroadcastDrop(NetworkLifecycle.Instance.Server, packet, null);
        else
            NetworkLifecycle.Instance.Client.SendExternalSerializablePacketToServer(packet, true);
    }

    public static void SendJoinState(NetworkServer server, ServerPlayer player)
    {
        if (server == null || player == null)
            return;

        foreach (GadgetSolderingTool tool in Resources.FindObjectsOfTypeAll<GadgetSolderingTool>())
        {
            if (tool == null || !tool.gameObject.scene.IsValid() || !TryGetToolNetId(tool, out ushort toolNetId))
                continue;

            bool owned = false;
            foreach (ServerPlayer other in server.ServerPlayers)
            {
                if (other.OwnsItem(toolNetId))
                {
                    owned = true;
                    break;
                }
            }
            if (owned || (player.WorldPosition - tool.transform.position).sqrMagnitude > NetworkedItemManager.MAX_DISTANCE_TO_ITEM_SQR)
                continue;

            ItemMagazine magazine = GetMagazine(tool);
            GameObject spoolObject = magazine != null && magazine.Capacity > 0 ? magazine[0] : null;
            if (spoolObject == null)
                continue;

            ItemBase spoolItem = spoolObject.GetComponent<ItemBase>();
            if (spoolItem == null || !NetworkedItem.TryGetNetworkedItem(spoolItem, out NetworkedItem spool) || spool.NetId == 0)
                continue;

            EnsureKnown(server, player, GetNetworkedItem(tool), tool.transform.position, tool.transform.rotation);
            EnsureKnown(server, player, spool, spool.transform.position, spool.transform.rotation);
            CustomizationPacketSend.SendJoinState(server, player.Peer, new SolderingMagazinePacket
            {
                ToolItemNetId = toolNetId,
                SpoolItemNetId = spool.NetId,
            });
        }
    }

    private static void OnServerMagazine(NetworkServer server, SolderingMagazinePacket packet, IPlayer sender)
    {
        if (packet == null || !TryGetTool(packet.ToolItemNetId, out GadgetSolderingTool tool))
            return;

        NetworkedItem canonicalSpool;
        if (packet.SpoolItemNetId == 0)
        {
            if (!ReplaceFullSpoolWithSpent(tool, out canonicalSpool))
                return;
            packet = new SolderingMagazinePacket { ToolItemNetId = packet.ToolItemNetId, SpoolItemNetId = canonicalSpool.NetId };
        }
        else
        {
            if (!NetworkedItem.TryGet(packet.SpoolItemNetId, out canonicalSpool) || canonicalSpool?.Item == null || !TryApplyCanonicalSpool(tool, packet.SpoolItemNetId))
                return;
        }

        BroadcastCanonical(server, packet, sender, canonicalSpool);
    }

    private static void OnServerDrop(NetworkServer server, SolderingDropEmptySpoolPacket packet, IPlayer sender)
    {
        if (packet == null || !TryGetTool(packet.ToolItemNetId, out GadgetSolderingTool tool) || !tool.HasEjectableSpool)
            return;
        DropEmptySpool(tool);
        BroadcastDrop(server, packet, sender);
    }

    private static bool TryApplyCanonicalSpool(GadgetSolderingTool tool, ushort spoolNetId)
    {
        ItemMagazine magazine = GetMagazine(tool);
        if (magazine == null)
            return false;

        if (spoolNetId == 0)
        {
            if (magazine[0] != null)
            {
                using (CustomizationSyncScope.Remote())
                    magazine.RemoveItem(0, true, false);
            }
            return true;
        }

        if (!NetworkedItem.TryGet(spoolNetId, out NetworkedItem spool) || spool?.Item == null)
            return false;
        if (magazine[0] == spool.Item.gameObject)
            return true;
        if (!magazine.ValidItem(spool.Item.gameObject, false))
            return false;

        using (CustomizationSyncScope.Remote())
        {
            GameObject current = magazine[0];
            if (current != null)
            {
                ItemBase currentItem = current.GetComponent<ItemBase>();
                magazine.RemoveItem(0, true, false);
                if (currentItem != null && NetworkedItem.TryGetNetworkedItem(currentItem, out NetworkedItem oldNetworked) && oldNetworked.NetId == 0)
                    SingletonBehaviour<Inventory>.Instance.DestroyItem(current);
            }
            return magazine.AddItem(spool.Item.gameObject, 0);
        }
    }

    private static IEnumerator ApplyWhenReady(GadgetSolderingTool tool, ushort spoolNetId)
    {
        for (int frame = 0; frame < DependencyWaitFrames; frame++)
        {
            if (TryApplyCanonicalSpool(tool, spoolNetId))
                yield break;
            yield return null;
        }
        Multiplayer.LogWarning($"Soldering magazine dependency did not resolve for spool {spoolNetId}");
    }

    private static bool ReplaceFullSpoolWithSpent(GadgetSolderingTool tool, out NetworkedItem replacement)
    {
        replacement = null;
        ItemMagazine magazine = GetMagazine(tool);
        GameObject current = magazine != null ? magazine[0] : null;
        MagazineAmmo ammo = current?.GetComponent<MagazineAmmo>();
        GameObject prefab = EmptyCoilPrefabField?.GetValue(tool) as GameObject;
        GameObject interaction = ReelInteractionPointField?.GetValue(tool) as GameObject;
        if (current == null || ammo == null || ammo.isSpent || prefab == null || interaction == null)
            return false;

        using (CustomizationSyncScope.Remote(rootAction: true))
        {
            magazine.RemoveItem(0, true, true);
            SingletonBehaviour<Inventory>.Instance.DestroyItem(current);
            GameObject spent = Object.Instantiate(prefab, interaction.transform.position, interaction.transform.rotation);
            UpdateEmptySpoolParams?.Invoke(tool, new object[] { spent });
            if (!magazine.AddItem(spent, 0))
                return false;
            ItemBase item = spent.GetComponent<ItemBase>();
            return item != null && NetworkedItem.TryGetNetworkedItem(item, out replacement) && replacement.NetId != 0;
        }
    }

    private static void DropEmptySpool(GadgetSolderingTool tool)
    {
        using (CustomizationSyncScope.Remote(rootAction: true))
            DropEmptySpoolMethod?.Invoke(tool, null);
    }

    private static void BroadcastCanonical(NetworkServer server, SolderingMagazinePacket packet, IPlayer sender, NetworkedItem spool = null)
    {
        if (server == null || packet == null || !NetworkedItem.TryGet(packet.ToolItemNetId, out NetworkedItem toolItem))
            return;
        spool ??= NetworkedItem.TryGet(packet.SpoolItemNetId, out NetworkedItem resolved) ? resolved : null;
        ITransportPeer senderPeer = (sender as ServerPlayerWrapper)?.Peer;
        foreach (ServerPlayer recipient in server.ServerPlayers)
        {
            if (recipient.Peer == server.SelfPeer || recipient.LoadingState < PlayerLoadingState.ReadyForItems)
                continue;
            if (!recipient.KnownItems.ContainsKey(toolItem) && recipient.Peer != senderPeer)
                continue;
            if (spool != null)
                EnsureKnown(server, recipient, spool, spool.transform.position, spool.transform.rotation);
            server.SendExternalSerializablePacketToPlayer(packet, recipient.Peer, true);
        }
    }

    private static void BroadcastDrop(NetworkServer server, SolderingDropEmptySpoolPacket packet, IPlayer sender)
    {
        if (server == null || packet == null || !NetworkedItem.TryGet(packet.ToolItemNetId, out NetworkedItem toolItem))
            return;
        ITransportPeer senderPeer = (sender as ServerPlayerWrapper)?.Peer;
        foreach (ServerPlayer recipient in server.ServerPlayers)
        {
            if (recipient.Peer == server.SelfPeer || recipient.Peer == senderPeer || recipient.LoadingState < PlayerLoadingState.ReadyForItems ||
                !recipient.KnownItems.ContainsKey(toolItem))
                continue;
            server.SendExternalSerializablePacketToPlayer(packet, recipient.Peer, true);
        }
    }

    private static void EnsureKnown(NetworkServer server, ServerPlayer player, NetworkedItem item, Vector3 position, Quaternion rotation)
    {
        if (item == null || player.KnownItems.ContainsKey(item))
            return;
        ItemUpdateData create = item.CreateUpdateData(ItemUpdateData.ItemUpdateType.Create);
        if (create == null)
            return;
        create.ItemState = ItemState.Dropped;
        create.ItemPosition = position - WorldMover.currentMove;
        create.ItemRotation = rotation;
        server.SendItemsChangePacket(new List<ItemUpdateData> { create }, player);
        player.KnownItems[item] = NetworkLifecycle.Instance.Tick;
    }

    private static ItemMagazine GetMagazine(GadgetSolderingTool tool) => MagazineField?.GetValue(tool) as ItemMagazine;

    private static bool TryGetToolNetId(GadgetSolderingTool tool, out ushort netId)
    {
        netId = 0;
        NetworkedItem item = GetNetworkedItem(tool);
        if (item == null || item.NetId == 0)
            return false;
        netId = item.NetId;
        return true;
    }

    private static NetworkedItem GetNetworkedItem(GadgetSolderingTool tool)
    {
        ItemBase item = tool?.GetComponent<ItemBase>();
        return item != null && NetworkedItem.TryGetNetworkedItem(item, out NetworkedItem networked) ? networked : null;
    }

    private static bool TryGetTool(ushort netId, out GadgetSolderingTool tool)
    {
        tool = null;
        if (!NetworkedItem.TryGet(netId, out NetworkedItem item) || item?.Item == null)
            return false;
        tool = item.Item.GetComponent<GadgetSolderingTool>();
        return tool != null;
    }
}
