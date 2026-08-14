using DV.Customization.Gadgets;
using DV.Customization.Gadgets.Implementations;
using HarmonyLib;
using Multiplayer.Components.Networking.World;

namespace Multiplayer.Patches.Customization;

[HarmonyPatch]
internal static class CustomizationToolRegistrationPatch
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(DuctTape), "Awake")]
    private static void DuctTapeAwake(DuctTape __instance)
    {
        NetworkedItem networkedItem = __instance.GetComponent<NetworkedItem>() ?? __instance.gameObject.AddComponent<NetworkedItem>();
        networkedItem.Initialize(__instance);
        networkedItem.RegisterTrackedValue("ductTape.usesLeft", () => __instance.usesLeft, value =>
        {
            __instance.usesLeft = value;
            __instance.tapeModelUpdater?.UpdateActiveStates(__instance.PercentageUsesLeft);
        });
        networkedItem.FinaliseTrackedValues();
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(GadgetSolderingTool), "Awake")]
    private static void SolderingToolAwake(GadgetSolderingTool __instance)
    {
        NetworkedItem networkedItem = __instance.GetComponent<NetworkedItem>() ?? __instance.gameObject.AddComponent<NetworkedItem>();
        networkedItem.Initialize(__instance);
        networkedItem.RegisterTrackedValue("soldering.remaining", () => __instance.remainingUnits, value =>
        {
            __instance.remainingUnits = value;
            __instance.OnUnitsChanged();
        });
        networkedItem.FinaliseTrackedValues();
    }
}
