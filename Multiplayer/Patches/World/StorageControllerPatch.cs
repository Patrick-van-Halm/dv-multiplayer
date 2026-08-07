using DV.CabControls;
using HarmonyLib;
using Multiplayer.Components.Networking.World;
using System;
using System.Collections.Generic;

namespace Multiplayer.Patches.World.Items;

[HarmonyPatch(typeof(StorageController))]
internal static class StorageControllerPatch
{
    [HarmonyPatch(nameof(StorageController.AddItemToLostAndFound))]
    [HarmonyPostfix]
    private static void AddItemToLostAndFound(ItemBase item)
    {
        NetworkedStorage.AssignPurchasedItemOwner(item);
    }

    [HarmonyPatch(nameof(StorageController.ForceSummonAllWorldItemsToLostAndFound))]
    [HarmonyPrefix]
    private static void BeforeSummon(
        StorageController __instance,
        out Dictionary<ItemBase, bool> __state)
    {
        __state = NetworkedStorage.FilterItemsByLastOwner(__instance);
    }

    [HarmonyPatch(nameof(StorageController.ForceSummonAllWorldItemsToLostAndFound))]
    [HarmonyFinalizer]
    private static Exception AfterSummon(
        StorageController __instance,
        Dictionary<ItemBase, bool> __state,
        Exception __exception)
    {
        return NetworkedStorage.AfterSummon(
            __instance,
            __state,
            __exception);
    }
}

[HarmonyPatch(typeof(StorageItemTransformController))]
internal static class StorageItemTransformControllerPatch
{
    [HarmonyPatch(nameof(StorageItemTransformController.ActivateItems))]
    [HarmonyPrefix]
    private static void BeforeActivation(
        out List<NetworkedStorageItemTransforms.RemovedStorageItem> __state)
    {
        __state =
            NetworkedStorageItemTransforms
                .RemoveOtherPlayersItemsFromLostAndFound();
    }

    [HarmonyPatch(nameof(StorageItemTransformController.ActivateItems))]
    [HarmonyFinalizer]
    private static Exception AfterActivation(
        List<NetworkedStorageItemTransforms.RemovedStorageItem> __state,
        Exception __exception)
    {
        NetworkedStorageItemTransforms.RestoreLostAndFoundItems(__state);
        return __exception;
    }

    [HarmonyPatch(nameof(StorageItemTransformController.DeactivateItems))]
    [HarmonyPrefix]
    private static void BeforeDeactivation(
        out List<NetworkedStorageItemTransforms.RemovedStorageItem> __state)
    {
        __state =
            NetworkedStorageItemTransforms
                .RemoveOtherPlayersItemsFromLostAndFound();
    }

    [HarmonyPatch(nameof(StorageItemTransformController.DeactivateItems))]
    [HarmonyFinalizer]
    private static Exception AfterDeactivation(
        List<NetworkedStorageItemTransforms.RemovedStorageItem> __state,
        Exception __exception)
    {
        NetworkedStorageItemTransforms.RestoreLostAndFoundItems(__state);
        return __exception;
    }
}
