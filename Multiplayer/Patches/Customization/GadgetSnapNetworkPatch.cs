using HarmonyLib;
using Multiplayer.Components.Networking.Customization;
using Multiplayer.Components.Networking.World;
using Multiplayer.Networking.Data;
using Multiplayer.Networking.Data.Customization;
using Multiplayer.Networking.Data.Items;
using Multiplayer.Networking.Managers.Client;
using Multiplayer.Networking.Managers.Server;
using Multiplayer.Networking.Packets.Serverbound;
using Multiplayer.Networking.TransportLayers;
using System.Collections.Generic;

namespace Multiplayer.Patches.Customization;

[HarmonyPatch]
internal static class GadgetSnapNetworkPatch
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(NetworkClient), "Subscribe")]
    private static void SubscribeClient(NetworkClient __instance) => GadgetSnapSync.RegisterClient(__instance);

    [HarmonyPostfix]
    [HarmonyPatch(typeof(NetworkServer), "Subscribe")]
    private static void SubscribeServer(NetworkServer __instance) => GadgetSnapSync.RegisterServer(__instance);

    [HarmonyPrefix]
    [HarmonyPriority(Priority.High)]
    [HarmonyPatch(typeof(NetworkServer), "OnServerboundLoadStateUpdatePacket")]
    private static void SeedSnapItems(NetworkServer __instance, ServerboundLoadStateUpdatePacket packet, ITransportPeer peer)
    {
        if (packet.LoadState != PlayerLoadingState.ReadyForCustomizers ||
            !__instance.TryGetServerPlayer(peer, out ServerPlayer player) ||
            player.LoadingState != PlayerLoadingState.ReadyForTrainSets)
            return;

        ClientboundCustomizationStatePacket snapState = new();
        GadgetSnapSync.AppendSnapshot(snapState);
        List<ItemUpdateData> creates = null;
        uint tick = NetworkLifecycle.Instance.Tick;

        foreach (GadgetSnapState snap in snapState.Snaps)
        {
            if (snap == null || !NetworkedItem.TryGet(snap.AttachedItemNetId, out NetworkedItem item) ||
                item?.Item == null || player.KnownItems.ContainsKey(item))
                continue;

            ItemUpdateData create = item.CreateUpdateData(ItemUpdateData.ItemUpdateType.Create);
            if (create == null)
                continue;

            create.ItemState = ItemState.Dropped;
            create.ItemPosition = item.transform.position - WorldMover.currentMove;
            create.ItemRotation = item.transform.rotation;
            creates ??= new List<ItemUpdateData>();
            creates.Add(create);
            player.KnownItems[item] = tick;
        }

        if (creates != null)
            __instance.SendItemsChangePacket(creates, player);
    }
}
