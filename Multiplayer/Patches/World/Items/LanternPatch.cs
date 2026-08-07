using HarmonyLib;
<<<<<<< HEAD
using Multiplayer.Components.Networking.World.Items;
=======
using Multiplayer.Components.Networking.World;
using Multiplayer.Utils;
using System;
>>>>>>> 64b64ddac4c01653a5f35094f35d4762e08207d6

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
