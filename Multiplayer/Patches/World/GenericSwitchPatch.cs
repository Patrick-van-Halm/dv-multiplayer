using DV.CabControls;
using DV.Interaction;
using HarmonyLib;
using Multiplayer.Components.Networking.World;

namespace Multiplayer.Patches.World;

[HarmonyPatch(typeof(GenericSwitch), MethodType.Constructor)]
internal static class GenericSwitchPatch
{
    [HarmonyPostfix]
    private static void Constructor(GenericSwitch __instance)
    {
        NetworkedGenericSwitchRegistration.RegisterWhenReady(__instance);
    }
}
