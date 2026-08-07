using DV.Customization.Gadgets;
using DV.Customization.Gadgets.Implementations;
using DV.Items;
using HarmonyLib;
using Multiplayer.Components.Networking.World.Items;

namespace Multiplayer.Patches.World.Items;

[HarmonyPatch(typeof(GadgetRemover), "Awake")]
internal static class GadgetRemoverPatch
{
    [HarmonyPostfix]
    private static void Register(GadgetRemover __instance) =>
        GadgetRemoverStateRegistration.Register(__instance);
}

[HarmonyPatch(typeof(GadgetItem), "Awake")]
internal static class GadgetItemPatch
{
    [HarmonyPostfix]
    private static void Register(GadgetItem __instance) =>
        GadgetItemStateRegistration.Register(__instance);
}

[HarmonyPatch(typeof(GadgetItem), "Start")]
internal static class GadgetItemStartPatch
{
    [HarmonyPostfix]
    private static void Register(GadgetItem __instance) =>
        GadgetItemStateRegistration.Register(__instance);
}

[HarmonyPatch(typeof(GadgetSolderingTool))]
internal static class GadgetSolderingToolPatch
{
    [HarmonyPatch("SetParticles")]
    [HarmonyPrefix]
    private static bool SetParticles(
        GadgetSolderingTool __instance,
        bool on)
    {
        return GadgetSolderingToolStateRegistration
            .ShouldRunSetParticles(__instance, on);
    }

    [HarmonyPatch("Start")]
    [HarmonyPostfix]
    private static void Register(GadgetSolderingTool __instance) =>
        GadgetSolderingToolStateRegistration.Register(__instance);
}

[HarmonyPatch(typeof(GadgetWiringTool), "Awake")]
internal static class GadgetWiringToolPatch
{
    [HarmonyPostfix]
    private static void Register(GadgetWiringTool __instance) =>
        GadgetWiringToolStateRegistration.Register(__instance);
}

[HarmonyPatch(typeof(DuctTape), "Awake")]
internal static class DuctTapePatch
{
    [HarmonyPostfix]
    private static void Register(DuctTape __instance) =>
        DuctTapeStateRegistration.Register(__instance);
}
