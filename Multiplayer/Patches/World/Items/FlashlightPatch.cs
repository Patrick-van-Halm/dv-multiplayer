using HarmonyLib;
using Multiplayer.Components.Networking.World.Items;

namespace Multiplayer.Patches.World.Items;

[HarmonyPatch(typeof(FlashlightItem))]
public static class FlashlightItemPatch
{
    [HarmonyPatch(nameof(FlashlightItem.Start))]
    [HarmonyPostfix]
    private static void Register(FlashlightItem __instance) =>
        FlashlightStateRegistration.Register(__instance);

    [HarmonyPatch(nameof(FlashlightItem.ToggleFlashlight))]
    [HarmonyPostfix]
    private static void EnforceBatteryAuthority(
        FlashlightItem __instance) =>
        FlashlightStateRegistration
            .OnFlashlightToggled(__instance);
}
