using DV.RemoteControls;
using HarmonyLib;
using Multiplayer.Components.Networking.Train;

namespace Multiplayer.Patches.World.Items;

[HarmonyPatch(typeof(RemoteControllerModule))]
public static class RemoteControllerModulePatch
{
    [HarmonyPatch(nameof(RemoteControllerModule.RemoteControllerCouple))]
    [HarmonyPostfix]
    static void RemoteControllerCouple(RemoteControllerModule __instance)
    {
        NetworkedRemoteCoupler.SendCouple(__instance);
    }

    [HarmonyPatch(nameof(RemoteControllerModule.Uncouple))]
    [HarmonyPrefix]
    static void Uncouple(RemoteControllerModule __instance, int selectedCoupler)
    {
        NetworkedRemoteCoupler.SendUncouple(__instance, selectedCoupler);
    }
}
