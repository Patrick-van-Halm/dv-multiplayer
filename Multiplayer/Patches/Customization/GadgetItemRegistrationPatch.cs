using DV.Customization.Gadgets;
using HarmonyLib;
using Multiplayer.Components.Networking.Customization.Gadgets;
using Multiplayer.Components.Networking.World;

namespace Multiplayer.Patches.Customization;

[HarmonyPatch(typeof(GadgetItem), "Awake")]
internal static class GadgetItemRegistrationPatch
{
    [HarmonyPostfix]
    private static void Awake(GadgetItem __instance)
    {
        if (__instance?.Gadget == null)
            return;

        NetworkedItem networkedItem = __instance.GetComponent<NetworkedItem>() ?? __instance.gameObject.AddComponent<NetworkedItem>();
        networkedItem.Initialize(__instance);
        GadgetTrackedValueRegistry.Register(networkedItem, __instance.Gadget);
        networkedItem.FinaliseTrackedValues();
    }
}
