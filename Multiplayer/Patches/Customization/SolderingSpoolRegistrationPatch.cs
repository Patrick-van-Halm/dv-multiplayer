using DV.Customization.Gadgets;
using HarmonyLib;
using Multiplayer.Components.Networking.Customization;
using Multiplayer.Components.Networking.World;

namespace Multiplayer.Patches.Customization;

[HarmonyPatch(typeof(GadgetSolderingTool), "Awake")]
internal static class SolderingSpoolRegistrationPatch
{
    [HarmonyPostfix]
    [HarmonyPriority(Priority.First)]
    private static void Postfix(GadgetSolderingTool __instance)
    {
        var networkedItem = __instance.GetComponent<NetworkedItem>() ?? __instance.gameObject.AddComponent<NetworkedItem>();
        networkedItem.Initialize(__instance);
        networkedItem.RegisterTrackedValue<uint>("soldering.spool",
            () => SolderingMagazineSync.GetSpoolNetId(__instance),
            value => SolderingMagazineSync.ApplyCanonicalSpool(__instance, value), null, true);
    }
}
