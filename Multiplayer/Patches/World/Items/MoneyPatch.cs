using DV.Interaction;
using DV.Items;
using HarmonyLib;
using Multiplayer.Components.Networking.World.Items;
using UnityEngine;

namespace Multiplayer.Patches.World.Items;

[HarmonyPatch(typeof(Money), "Awake")]
internal static class MoneyPatch
{
    [HarmonyPostfix]
    private static void Register(Money __instance) =>
        MoneyStateRegistration.Register(__instance);
}

[HarmonyPatch(typeof(MoneyPrinter), nameof(MoneyPrinter.PrintMoney))]
internal static class JobPaymentSpawnPatch
{
    [HarmonyPostfix]
    private static void MarkPayment(
        MoneyPrinter __instance,
        GameObject __result)
    {
        NetworkedJobPaymentInteractions.MarkSpawnedPayment(
            __instance,
            __result);
    }
}

[HarmonyPatch(typeof(MoneyUse), nameof(MoneyUse.HandleUse))]
internal static class JobPaymentUsePatch
{
    [HarmonyPrefix]
    private static bool HandleUse(
        MoneyUse __instance,
        ItemUseTarget target,
        ref bool __result)
    {
        return NetworkedJobPaymentInteractions.TryHandleUse(
            __instance,
            target,
            ref __result);
    }
}
