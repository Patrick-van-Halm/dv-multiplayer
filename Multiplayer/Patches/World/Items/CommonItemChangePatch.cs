using DV.Customization.Gadgets;
using DV.Customization.Gadgets.Implementations;
using HarmonyLib;
using Multiplayer.Components.Networking;
using Multiplayer.Components.Networking.World;
using Multiplayer.Networking.Data;
using Multiplayer.Networking.Data.Items;
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
    }
}

[HarmonyPatch(typeof(NetworkedItemManager), "ProcessReceivedAsHost")]
internal static class AcceptedClientItemRelayContext
{
    internal static ServerPlayer Sender;
    internal static ItemUpdateData Snapshot;

    [HarmonyPrefix]
    private static void Prefix(ItemUpdateData snapshot, ServerPlayer sender)
    {
        Sender = sender;
        Snapshot = snapshot;
    }

    [HarmonyFinalizer]
    private static System.Exception Finalizer(System.Exception __exception)
    {
        Sender = null;
        Snapshot = null;
        return __exception;
    }
}

[HarmonyPatch(typeof(NetworkedItem), nameof(NetworkedItem.ReceiveSnapshot))]
internal static class AcceptedClientItemRelayPatch
{
    [HarmonyPostfix]
    private static void Postfix(NetworkedItem __instance, ItemUpdateData snapshot)
    {
        ServerPlayer sender = AcceptedClientItemRelayContext.Sender;
        NetworkServer server = NetworkLifecycle.Instance?.Server;
        if (server == null || sender == null || !ReferenceEquals(snapshot, AcceptedClientItemRelayContext.Snapshot) ||
            snapshot == null || snapshot.UpdateType == ItemUpdateData.ItemUpdateType.Create)
            return;

        List<ItemUpdateData> relay = new() { snapshot };
        uint tick = NetworkLifecycle.Instance.Tick;
        foreach (ServerPlayer recipient in server.ServerPlayers)
        {
            if (recipient == sender || recipient.LoadingState < PlayerLoadingState.ReadyForItems ||
                !recipient.KnownItems.ContainsKey(__instance))
                continue;

            server.SendItemsChangePacket(relay, recipient);
            recipient.KnownItems[__instance] = tick;
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
        foreach (ServerPlayer player in NetworkLifecycle.Instance.Server.ServerPlayers)
        {
            if (player.LoadingState < PlayerLoadingState.ReadyForItems)
                continue;

            foreach (NetworkedItem item in NetworkedItem.GetAll())
            {
                GadgetBase gadget = item?.Item?.GetComponent<GadgetItem>()?.Gadget;
                if (gadget?.IsLinked != true)
                    continue;

                player.NearbyItems[item] = now;
                foreach (SnapPointGadget point in gadget.GetComponentsInChildren<SnapPointGadget>(true))
                {
                    if (point?.SnappedItem != null && NetworkedItem.TryGetNetworkedItem(point.SnappedItem, out NetworkedItem attached))
                        player.NearbyItems[attached] = now;
                }
            }
        }
    }
}
