using DV.Customization.Gadgets;
using HarmonyLib;
using Multiplayer.Components.Networking;
using Multiplayer.Components.Networking.World;
using Multiplayer.Networking.Data.Items;
using Multiplayer.Networking.Data.Player;
using Multiplayer.Networking.Managers.Server;
using Multiplayer.Networking.Packets.Common;
using Multiplayer.Networking.TransportLayers;
using System.Collections.Generic;
using UnityEngine;

namespace Multiplayer.Patches.World.Items;

[HarmonyPatch(typeof(NetworkServer), "OnCommonItemChangePacket")]
internal static class CommonItemChangePatch
{
    [HarmonyPostfix]
    private static void ReceiveItemChanges(NetworkServer __instance, CommonItemChangePacket packet, ITransportPeer peer)
    {
        if (!__instance.TryGetServerPlayer(peer, out var player) || packet?.Items == null)
            return;

        NetworkedItemManager.Instance.ReceiveSnapshots(packet.Items, player);

        List<ItemUpdateData> relay = null;
        foreach (var snapshot in packet.Items)
        {
            if (snapshot == null || snapshot.UpdateType == ItemUpdateData.ItemUpdateType.Create)
                continue;
            if (!NetworkedItem.TryGet(snapshot.ItemNetId, out _))
                continue;
            relay ??= new List<ItemUpdateData>();
            relay.Add(snapshot);
        }

        if (relay == null)
            return;

        foreach (var otherPlayer in __instance.ServerPlayers)
        {
            if (otherPlayer == player || otherPlayer.LoadingState < PlayerLoadingState.ReadyForItems)
                continue;
            __instance.SendItemsChangePacket(relay, otherPlayer);
        }
    }
}

[HarmonyPatch(typeof(NetworkedItemManager), "ProcessReceivedAsClient")]
internal static class MissingClientDestroyPatch
{
    [HarmonyPrefix]
    private static bool Prefix(ItemUpdateData snapshot)
    {
        if (snapshot?.UpdateType != ItemUpdateData.ItemUpdateType.Destroy)
            return true;
        return NetworkedItem.TryGet(snapshot.ItemNetId, out _);
    }
}

[HarmonyPatch(typeof(NetworkedItemManager), "UpdatePlayerItemLists")]
internal static class InstalledGadgetRelevancePatch
{
    [HarmonyPostfix]
    private static void Postfix()
    {
        if (!NetworkLifecycle.Instance.IsHost() || NetworkLifecycle.Instance.Server == null)
            return;

        float now = Time.time;
        foreach (var player in NetworkLifecycle.Instance.Server.ServerPlayers)
        {
            if (player.LoadingState < PlayerLoadingState.ReadyForItems)
                continue;

            foreach (NetworkedItem item in NetworkedItem.GetAll())
            {
                if (item?.Item?.GetComponent<GadgetItem>()?.Gadget?.IsLinked == true)
                    player.NearbyItems[item] = now;
            }
        }
    }
}
