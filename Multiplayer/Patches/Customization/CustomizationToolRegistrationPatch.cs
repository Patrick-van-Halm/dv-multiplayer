using DV.Customization.Gadgets;
using DV.Customization.Gadgets.Implementations;
using DV.Utils;
using HarmonyLib;
using Multiplayer.Components.Networking.World;
using System.Reflection;
using UnityEngine;

namespace Multiplayer.Patches.Customization;

[HarmonyPatch]
internal static class CustomizationToolRegistrationPatch
{
    private static readonly FieldInfo SolderRemainingField = AccessTools.Field(typeof(GadgetSolderingTool), "remainingUnits");
    private static readonly MethodInfo SolderUnitsChanged = AccessTools.Method(typeof(GadgetSolderingTool), "OnUnitsChanged");

    [HarmonyPostfix]
    [HarmonyPatch(typeof(DuctTape), "Awake")]
    private static void DuctTapeAwake(DuctTape __instance)
    {
        NetworkedItem networkedItem = __instance.GetComponent<NetworkedItem>() ?? __instance.gameObject.AddComponent<NetworkedItem>();
        networkedItem.Initialize(__instance);
        networkedItem.RegisterTrackedValue("ductTape.usesLeft", () => __instance.usesLeft, value =>
        {
            __instance.usesLeft = Mathf.Clamp(value, 0, __instance.numberOfUses);
            ThresholdGameObjectActivator updater = __instance.GetComponent<ThresholdGameObjectActivator>();
            updater?.UpdateActiveStates(__instance.numberOfUses > 0 ? (float)__instance.usesLeft / __instance.numberOfUses : 0f);
        });
        networkedItem.FinaliseTrackedValues();
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(GadgetSolderingTool), "Awake")]
    private static void SolderingToolAwake(GadgetSolderingTool __instance)
    {
        NetworkedItem networkedItem = __instance.GetComponent<NetworkedItem>() ?? __instance.gameObject.AddComponent<NetworkedItem>();
        networkedItem.Initialize(__instance);
        if (SolderRemainingField != null)
        {
            networkedItem.RegisterTrackedValue("soldering.remaining",
                () => (int)SolderRemainingField.GetValue(__instance),
                value =>
                {
                    SolderRemainingField.SetValue(__instance, Mathf.Clamp(value, -1, 72089));
                    SolderUnitsChanged?.Invoke(__instance, null);
                });
        }
        networkedItem.FinaliseTrackedValues();
    }
}
