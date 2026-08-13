using DV.Customization.Gadgets;
using HarmonyLib;
using Multiplayer.Components.Networking.Customization;
using Multiplayer.Networking.Data.Customization;

namespace Multiplayer.Patches.Customization;

[HarmonyPatch]
internal static class GadgetWireObservationPatch
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(GadgetWiringModule.WireLinkPort), nameof(GadgetWiringModule.WireLinkPort.Wire),
        new[] { typeof(GadgetWiringModule.WireLinkPort), typeof(GadgetWiringModule.WireLinkPort) })]
    private static void AfterWire(GadgetWiringModule.WireLinkPort a, GadgetWiringModule.WireLinkPort b, bool __result)
    {
        if (!__result || CustomizationSyncScope.IsApplyingRemote || CustomizationSyncScope.IsApplyingRootAction ||
            !GadgetWireSync.TryDescribe(a, out ushort aId, out int aIndex) ||
            !GadgetWireSync.TryDescribe(b, out ushort bId, out int bIndex))
            return;

        GadgetStructuralSync.SendObserved(new GadgetWirePacket
        {
            GadgetAItemNetId = aId,
            PortAIndex = aIndex,
            GadgetBItemNetId = bId,
            PortBIndex = bIndex,
        });
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(GadgetWiringModule.WireLinkPort), nameof(GadgetWiringModule.WireLinkPort.Unwire),
        new[] { typeof(GadgetWiringModule.WireLinkPort), typeof(GadgetWiringModule.WireLinkPort) })]
    private static void AfterUnwire(GadgetWiringModule.WireLinkPort a, GadgetWiringModule.WireLinkPort b, bool __result)
    {
        if (!__result || CustomizationSyncScope.IsApplyingRemote || CustomizationSyncScope.IsApplyingRootAction ||
            !GadgetWireSync.TryDescribe(a, out ushort aId, out int aIndex) ||
            !GadgetWireSync.TryDescribe(b, out ushort bId, out int bIndex))
            return;

        GadgetStructuralSync.SendObserved(new GadgetUnwirePacket
        {
            GadgetAItemNetId = aId,
            PortAIndex = aIndex,
            GadgetBItemNetId = bId,
            PortBIndex = bIndex,
        });
    }
}
