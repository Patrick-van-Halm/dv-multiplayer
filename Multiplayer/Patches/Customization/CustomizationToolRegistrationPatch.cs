using DV.Customization.Gadgets;
using DV.Customization.Gadgets.Implementations;
using HarmonyLib;
using Multiplayer.Components.Networking.Customization;
using Multiplayer.Components.Networking.World;
using Multiplayer.Networking.Managers.Client;
using Multiplayer.Networking.Managers.Server;
using UnityEngine;

namespace Multiplayer.Patches.Customization;

[HarmonyPatch]
internal static class CustomizationToolRegistrationPatch
{
    private struct DuctTapeUseState
    {
        public ushort NetId;
        public int UsesBefore;
        public Vector3 Position;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(DuctTape), "Awake")]
    private static void DuctTapeAwake(DuctTape __instance)
    {
        NetworkedItem networkedItem = __instance.GetComponent<NetworkedItem>() ?? __instance.gameObject.AddComponent<NetworkedItem>();
        networkedItem.Initialize(__instance);
        networkedItem.RegisterTrackedValue("ductTape.usesLeft", () => __instance.usesLeft, value =>
        {
            __instance.usesLeft = Mathf.Clamp(value, 0, __instance.numberOfUses);
            __instance.tapeModelUpdater?.UpdateActiveStates(__instance.PercentageUsesLeft);
        });
        networkedItem.FinaliseTrackedValues();
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(DuctTape), nameof(DuctTape.ConsumeOneUse))]
    private static void BeforeDuctTapeUse(DuctTape __instance, ref DuctTapeUseState __state)
    {
        __state.UsesBefore = __instance.usesLeft;
        __state.Position = __instance.transform.position;
        if (__instance.TryGetComponent(out DV.CabControls.ItemBase itemBase) &&
            NetworkedItem.TryGetNetworkedItem(itemBase, out NetworkedItem item))
            __state.NetId = item.NetId;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(DuctTape), nameof(DuctTape.ConsumeOneUse))]
    private static void AfterDuctTapeUse(DuctTapeUseState __state)
    {
        if (__state.UsesBefore == 1)
            DuctTapeTerminalSync.ObserveTerminalUse(__state.NetId, __state.Position);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(GadgetSolderingTool), "Awake")]
    private static void SolderingToolAwake(GadgetSolderingTool __instance)
    {
        NetworkedItem networkedItem = __instance.GetComponent<NetworkedItem>() ?? __instance.gameObject.AddComponent<NetworkedItem>();
        networkedItem.Initialize(__instance);
        networkedItem.RegisterTrackedValue("soldering.remaining", () => __instance.remainingUnits, value =>
        {
            __instance.remainingUnits = value;
            __instance.OnUnitsChanged();
        });
        networkedItem.RegisterTrackedValue<uint>("soldering.spool",
            () => SolderingMagazineSync.GetSpoolNetId(__instance),
            value => SolderingMagazineSync.ApplyCanonicalSpool(__instance, value),
            serverAuthoritative: true);
        networkedItem.FinaliseTrackedValues();
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(NetworkClient), "Subscribe")]
    private static void SubscribeDuctTapeClient(NetworkClient __instance) => DuctTapeTerminalSync.RegisterClient(__instance);

    [HarmonyPostfix]
    [HarmonyPatch(typeof(NetworkServer), "Subscribe")]
    private static void SubscribeDuctTapeServer(NetworkServer __instance) => DuctTapeTerminalSync.RegisterServer(__instance);
}
