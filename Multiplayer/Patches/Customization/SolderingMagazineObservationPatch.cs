using DV.Customization.Gadgets;
using DV.Items;
using HarmonyLib;
using Multiplayer.Components.Networking.Customization;
using Multiplayer.Networking.Managers.Client;
using Multiplayer.Networking.Managers.Server;
using UnityEngine;

namespace Multiplayer.Patches.Customization;

[HarmonyPatch]
internal static class SolderingMagazineObservationPatch
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(ItemContainer), nameof(ItemContainer.AddItem))]
    private static void AfterAdd(ItemContainer __instance, GameObject item, bool __result)
    {
        if (!__result || __instance is not ItemMagazine magazine || item == null || magazine[0] != item)
            return;

        GadgetSolderingTool tool = magazine.GetComponent<GadgetSolderingTool>();
        if (tool != null)
            SolderingMagazineSync.ObserveLoadedSpool(tool, item);
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(GadgetSolderingTool), "DropEmptySpool")]
    private static void BeforeDrop(GadgetSolderingTool __instance, ref bool __state)
    {
        __state = __instance.HasEjectableSpool;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(GadgetSolderingTool), "DropEmptySpool")]
    private static void AfterDrop(GadgetSolderingTool __instance, bool __state)
    {
        if (__state)
            SolderingMagazineSync.ObserveDropEmptySpool(__instance);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(NetworkClient), "Subscribe")]
    private static void SubscribeClient(NetworkClient __instance) => SolderingMagazineSync.RegisterClient(__instance);

    [HarmonyPostfix]
    [HarmonyPatch(typeof(NetworkServer), "Subscribe")]
    private static void SubscribeServer(NetworkServer __instance) => SolderingMagazineSync.RegisterServer(__instance);
}
