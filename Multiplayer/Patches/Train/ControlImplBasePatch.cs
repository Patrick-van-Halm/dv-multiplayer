using DV.CabControls;
using HarmonyLib;
using Multiplayer.Components.Networking.Player;

namespace Multiplayer.Patches.Train;

[HarmonyPatch(typeof(ControlImplBase), "Start")]
internal static class ControlImplBasePatch
{
    [HarmonyPostfix]
    private static void Start(ControlImplBase __instance)
    {
        ControlIKHandEventRelay.Attach(__instance);
    }
}
