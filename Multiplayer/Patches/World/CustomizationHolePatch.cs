using DV.Customization;
using HarmonyLib;
using Multiplayer.Components.Networking.World;
using UnityEngine;

namespace Multiplayer.Patches.World;

[HarmonyPatch(typeof(Customization))]
internal static class CustomizationHolePatch
{
    [HarmonyPatch(
        nameof(Customization.AddHole),
        typeof(Vector3),
        typeof(Vector3))]
    [HarmonyPostfix]
    private static void AddHole(
        Customization __instance,
        Vector3 localPosition,
        Vector3 localNormal)
    {
        NetworkedCustomizationHoles.OnHoleAdded(
            __instance,
            localPosition,
            localNormal);
    }

    [HarmonyPatch(nameof(Customization.MoveHole))]
    [HarmonyPrefix]
    private static void CaptureMovePosition(
        [HarmonyArgument(0)] Collider hole,
        out Vector3 __state)
    {
        __state = NetworkedCustomizationHoles.CaptureHolePosition(hole);
    }

    [HarmonyPatch(nameof(Customization.RemoveHole))]
    [HarmonyPrefix]
    private static void CaptureRemovePosition(
        [HarmonyArgument(0)] Collider hole,
        out Vector3 __state)
    {
        __state = NetworkedCustomizationHoles.CaptureHolePosition(hole);
    }

    [HarmonyPatch(nameof(Customization.MoveHole))]
    [HarmonyPostfix]
    private static void MoveHole(
        Customization __instance,
        Collider hole,
        Vector3 localPosition,
        Vector3 localNormal,
        Vector3 __state)
    {
        NetworkedCustomizationHoles.OnHoleMoved(
            __instance,
            hole,
            localPosition,
            localNormal,
            __state);
    }

    [HarmonyPatch(nameof(Customization.RemoveHole))]
    [HarmonyPostfix]
    private static void RemoveHole(
        Customization __instance,
        bool __result,
        Vector3 __state)
    {
        NetworkedCustomizationHoles.OnHoleRemoved(
            __instance,
            __result,
            __state);
    }
}
