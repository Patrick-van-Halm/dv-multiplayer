using DV.Customization;
using HarmonyLib;
using Multiplayer.Components.Networking.Customization;
using Multiplayer.Networking.Data.Customization;
using UnityEngine;

namespace Multiplayer.Patches.Customization;

[HarmonyPatch]
internal static class CustomizationHoleObservationPatch
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(Customization), nameof(Customization.AddHole))]
    private static void AfterAdd(Customization __instance, Vector3 localPosition, Vector3 localNormal, Collider __result)
    {
        if (CustomizationSyncScope.IsApplyingRemote || CustomizationSyncScope.IsApplyingRootAction || __result == null ||
            !CustomizationRef.TryFrom(__instance, out CustomizationRef target))
            return;

        GadgetStructuralSync.SendObserved(new CustomizationAddHolePacket
        {
            Target = target,
            LocalPosition = localPosition,
            LocalNormal = localNormal,
        });
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(Customization), nameof(Customization.MoveHole))]
    private static void BeforeMove(Collider hole, ref Vector3 __state)
    {
        __state = hole != null ? hole.transform.localPosition : default;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Customization), nameof(Customization.MoveHole))]
    private static void AfterMove(Customization __instance, Collider hole, Vector3 localPosition, Vector3 localNormal, Vector3 __state)
    {
        if (CustomizationSyncScope.IsApplyingRemote || CustomizationSyncScope.IsApplyingRootAction || hole == null ||
            !__instance.IsHole(hole) || !CustomizationRef.TryFrom(__instance, out CustomizationRef target))
            return;

        GadgetStructuralSync.SendObserved(new CustomizationMoveHolePacket
        {
            Target = target,
            PreviousLocalPosition = __state,
            LocalPosition = localPosition,
            LocalNormal = localNormal,
        });
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(Customization), nameof(Customization.RemoveHole))]
    private static void BeforeRemove(Collider hole, ref Vector3 __state)
    {
        __state = hole != null ? hole.transform.localPosition : default;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Customization), nameof(Customization.RemoveHole))]
    private static void AfterRemove(Customization __instance, bool __result, Vector3 __state)
    {
        if (CustomizationSyncScope.IsApplyingRemote || CustomizationSyncScope.IsApplyingRootAction || !__result ||
            !CustomizationRef.TryFrom(__instance, out CustomizationRef target))
            return;

        GadgetStructuralSync.SendObserved(new CustomizationRemoveHolePacket
        {
            Target = target,
            LocalPosition = __state,
        });
    }
}
