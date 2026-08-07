using DV.CashRegister;
using DV.Shops;
using HarmonyLib;
using Multiplayer.Components.Networking.World;

namespace Multiplayer.Patches.World;

[HarmonyPatch(typeof(CashRegisterWithModules))]
public class CashRegisterWithModulesPatch
{
    [HarmonyPrefix]
    [HarmonyPatch(nameof(CashRegisterWithModules.OnDisable))]
    private static bool OnDisable(
        CashRegisterWithModules __instance) =>
        NetworkedCashRegisterRouting
            .BeforeModulesDisable(__instance);

    [HarmonyPrefix]
    [HarmonyPatch(nameof(CashRegisterWithModules.OnBuyPressed))]
    private static bool OnBuyPressed(
        CashRegisterWithModules __instance) =>
        NetworkedCashRegisterRouting.RouteBuy(__instance);

    [HarmonyPostfix]
    [HarmonyPatch(nameof(CashRegisterWithModules.OnBuyPressed))]
    private static void OnBuyPressed_Postfix(
        CashRegisterWithModules __instance) =>
        NetworkedCashRegisterRouting.AfterBuy(__instance);

    [HarmonyPrefix]
    [HarmonyPatch(nameof(CashRegisterWithModules.Cancel))]
    private static bool Cancel(
        CashRegisterWithModules __instance) =>
        NetworkedCashRegisterRouting.RouteCancel(__instance);

    [HarmonyPostfix]
    [HarmonyPatch(nameof(CashRegisterWithModules.Cancel))]
    private static void Cancel_Postfix(
        CashRegisterWithModules __instance) =>
        NetworkedCashRegisterRouting.AfterCancel(__instance);
}

[HarmonyPatch(
    typeof(ScanItemCashRegisterModule),
    nameof(ScanItemCashRegisterModule.AddItemsToBuy))]
internal static class ScanItemCashRegisterModulePatch
{
    [HarmonyPostfix]
    private static void AddedToCart(
        ScanItemCashRegisterModule __instance,
        bool __result) =>
        NetworkedCashRegisterRouting.OnShopItemAdded(
            __instance,
            __result);
}
