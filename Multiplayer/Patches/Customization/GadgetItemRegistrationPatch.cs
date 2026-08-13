using DV.Customization;
using DV.Customization.Gadgets;
using HarmonyLib;
using Multiplayer.Components.Networking;
using Multiplayer.Components.Networking.Customization;
using Multiplayer.Components.Networking.Customization.Gadgets;
using Multiplayer.Components.Networking.World;
using Multiplayer.Networking.Data.Customization;
using System;

namespace Multiplayer.Patches.Customization;

[HarmonyPatch(typeof(GadgetItem), "Awake")]
internal static class GadgetItemRegistrationPatch
{
    [HarmonyPostfix]
    private static void Awake(GadgetItem __instance)
    {
        if (__instance?.Item == null || __instance.Gadget == null)
            return;

        NetworkedItem networkedItem = __instance.GetComponent<NetworkedItem>() ?? __instance.gameObject.AddComponent<NetworkedItem>();
        networkedItem.Initialize(__instance);
        GadgetTrackedValueRegistry.Register(networkedItem, __instance, __instance.Gadget);
        networkedItem.FinaliseTrackedValues();
    }
}

[HarmonyPatch]
internal static class GadgetStructuralObservationPatch
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(GadgetItem), nameof(GadgetItem.Place))]
    private static void AfterPlace(Customization destination, GadgetItem gadgetItem, GadgetBase __result)
    {
        if (CustomizationSyncScope.IsApplyingRemote || __result == null || gadgetItem?.Item == null ||
            !NetworkedItem.TryGetNetworkedItem(gadgetItem.Item, out NetworkedItem networkedItem) || networkedItem.NetId == 0 ||
            !CustomizationRef.TryFrom(destination, out CustomizationRef target))
            return;

        if (NetworkLifecycle.Instance.IsHost())
            GadgetStructuralSync.ClearServerOwnership(networkedItem.NetId);

        GadgetStructuralSync.SendObserved(new GadgetPlacePacket
        {
            GadgetItemNetId = networkedItem.NetId,
            Target = target,
            LocalPosition = __result.transform.localPosition,
            LocalRotation = __result.transform.localRotation,
            IsOnGlass = __result.IsOnGlass,
        });
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(GadgetBase), nameof(GadgetBase.Remove))]
    private static void BeforeRemove(ref IDisposable __state)
    {
        if (!CustomizationSyncScope.IsApplyingRemote)
            __state = CustomizationSyncScope.LocalRoot();
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(GadgetBase), nameof(GadgetBase.Remove))]
    private static void AfterRemove(bool reparentToTrainCar, GadgetItem __result)
    {
        if (CustomizationSyncScope.IsApplyingRemote || __result?.Item == null ||
            !NetworkedItem.TryGetNetworkedItem(__result.Item, out NetworkedItem networkedItem) || networkedItem.NetId == 0)
            return;

        GadgetStructuralSync.SendObserved(new GadgetRemovePacket
        {
            GadgetItemNetId = networkedItem.NetId,
            ReparentToTrainCar = reparentToTrainCar,
        });
    }

    [HarmonyFinalizer]
    [HarmonyPatch(typeof(GadgetBase), nameof(GadgetBase.Remove))]
    private static Exception FinishRemove(Exception __exception, IDisposable __state)
    {
        __state?.Dispose();
        return __exception;
    }
}
