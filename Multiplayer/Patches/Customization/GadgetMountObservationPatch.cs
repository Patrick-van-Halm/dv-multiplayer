using DV.Customization.Gadgets;
using HarmonyLib;
using Multiplayer.Components.Networking.Customization;
using Multiplayer.Components.Networking.World;
using Multiplayer.Networking.Data.Customization;

namespace Multiplayer.Patches.Customization;

[HarmonyPatch]
internal static class GadgetMountObservationPatch
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(Mount), nameof(Mount.MountGadget))]
    private static void AfterMount(Mount __instance, GadgetBase gadget)
    {
        if (CustomizationSyncScope.IsApplyingRemote || CustomizationSyncScope.IsApplyingRootAction ||
            gadget == null || __instance.MountedGadget != gadget ||
            !GadgetMountSync.TryDescribe(__instance, out ushort ownerItemNetId, out int mountIndex) ||
            gadget.GadgetItem?.Item == null || !NetworkedItem.TryGetNetworkedItem(gadget.GadgetItem.Item, out NetworkedItem mountedItem) ||
            mountedItem.NetId == 0)
            return;

        GadgetStructuralSync.SendObserved(new GadgetMountPacket
        {
            MountOwnerItemNetId = ownerItemNetId,
            MountedItemNetId = mountedItem.NetId,
            MountIndex = mountIndex,
        });
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(Mount), nameof(Mount.UnmountGadget))]
    private static void BeforeUnmount(Mount __instance, ref bool __state)
    {
        __state = __instance.MountedGadget != null;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Mount), nameof(Mount.UnmountGadget))]
    private static void AfterUnmount(Mount __instance, bool __state)
    {
        if (CustomizationSyncScope.IsApplyingRemote || CustomizationSyncScope.IsApplyingRootAction || !__state ||
            __instance.MountedGadget != null ||
            !GadgetMountSync.TryDescribe(__instance, out ushort ownerItemNetId, out int mountIndex))
            return;

        GadgetStructuralSync.SendObserved(new GadgetUnmountPacket
        {
            MountOwnerItemNetId = ownerItemNetId,
            MountIndex = mountIndex,
        });
    }
}
