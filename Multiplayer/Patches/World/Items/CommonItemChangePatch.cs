using HarmonyLib;
using Multiplayer.Components.Networking.World;
using Multiplayer.Networking.Managers.Server;
using Multiplayer.Networking.Packets.Common;
using Multiplayer.Networking.TransportLayers;

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
    }
}
