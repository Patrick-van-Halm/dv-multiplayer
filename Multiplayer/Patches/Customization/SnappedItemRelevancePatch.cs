using DV.Customization.Gadgets;
using DV.Customization.Gadgets.Implementations;
using HarmonyLib;
using Multiplayer.Components.Networking;
using Multiplayer.Components.Networking.World;
using Multiplayer.Networking.Data;
using UnityEngine;

namespace Multiplayer.Patches.Customization;

[HarmonyPatch(typeof(NetworkedItemManager), "UpdatePlayerItemLists")]
internal static class SnappedItemRelevancePatch
{
    [HarmonyPostfix]
    private static void Postfix()
    {
        NetworkLifecycle lifecycle = NetworkLifecycle.Instance;
        if (lifecycle == null || !lifecycle.IsHost() || lifecycle.Server == null)
            return;

        float now = Time.time;
        foreach (ServerPlayer player in lifecycle.Server.ServerPlayers)
        {
            if (player.LoadingState < PlayerLoadingState.ReadyForItems)
                continue;

            foreach (NetworkedItem ownerItem in NetworkedItem.GetAll())
            {
                GadgetBase gadget = ownerItem?.Item?.GetComponent<GadgetItem>()?.Gadget;
                if (gadget?.IsLinked != true)
                    continue;

                foreach (SnapPointGadget point in gadget.GetComponentsInChildren<SnapPointGadget>(true))
                {
                    if (point?.SnappedItem != null &&
                        NetworkedItem.TryGetNetworkedItem(point.SnappedItem, out NetworkedItem attached))
                        player.NearbyItems[attached] = now;
                }
            }
        }
    }
}
