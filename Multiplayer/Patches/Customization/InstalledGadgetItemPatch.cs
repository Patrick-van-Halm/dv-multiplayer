using DV.Customization.Gadgets;
using HarmonyLib;
using Multiplayer.Components.Networking;
using Multiplayer.Components.Networking.World;
using Multiplayer.Networking.Data;
using Multiplayer.Networking.Managers.Server;
using System.Reflection;
using UnityEngine;

namespace Multiplayer.Patches.Customization;

[HarmonyPatch(typeof(NetworkedItem), nameof(NetworkedItem.GetSnapshot))]
internal static class InstalledGadgetItemStatePatch
{
    private static readonly FieldInfo StateDirty = AccessTools.Field(typeof(NetworkedItem), "stateDirty");

    [HarmonyPrefix]
    private static void Prefix(NetworkedItem __instance)
    {
        if (__instance?.Item?.GetComponent<GadgetItem>()?.Gadget?.IsLinked == true)
            StateDirty?.SetValue(__instance, false);
    }
}

[HarmonyPatch(typeof(NetworkedItemManager), "UpdatePlayerItemLists")]
internal static class CustomizationItemRelevancePatch
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

                GadgetSolderingTool tool = item?.Item?.GetComponent<GadgetSolderingTool>();
                if (tool == null || !player.NearbyItems.ContainsKey(item))
                    continue;

                uint spoolNetId = SolderingMagazineSync.GetSpoolNetId(tool);
                if (spoolNetId != 0 && spoolNetId <= ushort.MaxValue &&
                    NetworkedItem.TryGet((ushort)spoolNetId, out NetworkedItem spool))
                    player.NearbyItems[spool] = now;
            }
        }
    }
}
