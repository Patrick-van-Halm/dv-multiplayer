using HarmonyLib;
using Multiplayer.Components.Networking.World.Items;

namespace Multiplayer.Patches.World.Items;

[HarmonyPatch(typeof(Lantern))]
public static class LanternPatch
{
    [HarmonyPatch(nameof(Lantern.Awake))]
    [HarmonyPostfix]
    private static void Awake(Lantern __instance) =>
        LanternStateRegistration.InitializeItem(__instance);

    [HarmonyPatch(nameof(Lantern.Initialize))]
    [HarmonyPostfix]
    private static void Initialize(Lantern __instance) =>
        LanternStateRegistration.Register(__instance);
}
