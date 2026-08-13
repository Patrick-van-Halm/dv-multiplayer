using HarmonyLib;
using Multiplayer.Components.Networking.World;
using Multiplayer.Networking.Data.Items;
using Multiplayer.Networking.Data.Player;
using Multiplayer.Networking.Managers.Server;
using Multiplayer.Networking.Packets.Common;
using Multiplayer.Networking.TransportLayers;
using System.Collections.Generic;

namespace Multiplayer.Patches.World.Items;

[HarmonyPatch(typeof(NetworkServer), "OnCommonItemChangePacket")]
internal static class CommonItemChangePatch
{
    [HarmonyPostfix]
    private static void ReceiveItemChanges(
        NetworkServer __instance,
        CommonItemChangePacket packet,
        ITransportPeer peer)
    {
        if (!__instance.TryGetServerPlayer(peer, out var player))
            return;

        if (packet?.Items == null)
            return;

        NetworkedItemManager.Instance.ReceiveSnapshots(packet.Items, player);

        // Applying a received tracked value makes the host copy clean. Forward valid
        // updates so other clients see accepted client changes without a new protocol.
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
