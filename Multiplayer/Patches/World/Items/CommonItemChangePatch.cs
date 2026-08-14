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
    private static void ReceiveItemChanges(NetworkServer __instance, CommonItemChangePacket packet, ITransportPeer peer)
    {
        if (!__instance.TryGetServerPlayer(peer, out var player) || packet?.Items == null)
            return;

        NetworkedItemManager.Instance.ReceiveSnapshots(packet.Items, player);

        // ReceiveSnapshot marks tracked values clean on the host, so pass the received
        // update on to clients that already know the item.
        foreach (var otherPlayer in __instance.ServerPlayers)
        {
            if (otherPlayer == player || otherPlayer.LoadingState < PlayerLoadingState.ReadyForItems)
                continue;

            List<ItemUpdateData> updates = null;
            foreach (var snapshot in packet.Items)
            {
                if (snapshot == null || snapshot.UpdateType == ItemUpdateData.ItemUpdateType.Create ||
                    !NetworkedItem.TryGet(snapshot.ItemNetId, out var item) || !otherPlayer.KnownItems.ContainsKey(item))
                    continue;

                updates ??= new List<ItemUpdateData>();
                updates.Add(snapshot);
            }

            if (updates != null)
                __instance.SendItemsChangePacket(updates, otherPlayer);
        }
    }
}
