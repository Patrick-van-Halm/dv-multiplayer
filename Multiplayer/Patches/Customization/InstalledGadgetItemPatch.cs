using DV.Customization.Gadgets;
using HarmonyLib;
using Multiplayer.Components.Networking;
using Multiplayer.Components.Networking.World;
using Multiplayer.Networking.Data;
using UnityEngine;

namespace Multiplayer.Patches.Customization;

[HarmonyPatch(typeof(NetworkedItem), nameof(NetworkedItem.GetSnapshot))]
internal static class InstalledGadgetItemStatePatch
{
    private static readonly AccessTools.FieldRef<NetworkedItem, bool> StateDirty =
        AccessTools.FieldRefAccess<NetworkedItem, bool>("stateDirty");

    [HarmonyPrefix]
    private static void Prefix(NetworkedItem __instance)
    {
        if (__instance?.Item?.GetComponent<GadgetItem>()?.Gadget?.IsLinked == true)
            StateDirty(__instance) = false;
    }
}

[HarmonyPatch(typeof(NetworkedItemManager), "UpdatePlayerItemLists")]
internal static class InstalledGadgetItemRelevancePatch
{
    [HarmonyPostfix]
    private static void Postfix()
    {
        NetworkLifecycle lifecycle = NetworkLifecycle.Instance;
        if (lifecycle?.IsHost() != true || lifecycle.Server == null)
            return;

        float now = Time.time;
        foreach (ServerPlayer player in lifecycle.Server.ServerPlayers)
        {
            if (player.LoadingState < PlayerLoadingState.ReadyForItems)
                continue;

            foreach (NetworkedItem item in NetworkedItem.GetAll())
            {
                GadgetBase gadget = item?.Item?.GetComponent<GadgetItem>()?.Gadget;
                if (gadget?.IsLinked == true &&
                    (player.WorldPosition - gadget.transform.position).sqrMagnitude <= NetworkedItemManager.MAX_DISTANCE_TO_ITEM_SQR)
                    player.NearbyItems[item] = now;
            }
        }
    }
}
