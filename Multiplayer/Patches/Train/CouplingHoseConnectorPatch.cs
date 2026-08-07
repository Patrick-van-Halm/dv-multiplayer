using DV.CabControls;
using HarmonyLib;
using Multiplayer.Components.Networking.Train;

namespace Multiplayer.Patches.Train;

[HarmonyPatch(typeof(CouplingHoseConnector))]
internal static class CouplingHoseConnectorPatch
{
    [HarmonyPatch("OnGrabbed")]
    [HarmonyPostfix]
    private static void OnGrabbed(CouplingHoseRig ___rig)
    {
        NetworkedHoseConnectorDrag.OnLocalGrabbed(___rig);
    }

    [HarmonyPatch("OnUngrabbed")]
    [HarmonyPostfix]
    private static void OnUngrabbed(CouplingHoseRig ___rig)
    {
        NetworkedHoseConnectorDrag.OnLocalUngrabbed(___rig);
    }

    [HarmonyPatch("OnTelegrabAttract")]
    [HarmonyPostfix]
    private static void OnTelegrabAttract(
        bool isBeingTelegrabbed,
        CouplingHoseRig ___rig)
    {
        NetworkedHoseConnectorDrag.OnLocalTelegrabChanged(
            isBeingTelegrabbed,
            ___rig);
    }
}
