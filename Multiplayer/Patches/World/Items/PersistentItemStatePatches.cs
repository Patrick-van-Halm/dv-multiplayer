using DV;
using DV.CabControls;
using DV.Customization.Gadgets;
using DV.Customization.Gadgets.Implementations;
using DV.Interaction;
using DV.Items;
using DV.Items.Brick;
using DV.TimeKeeping;
using DV.Utils;
using HarmonyLib;
using Multiplayer.Components.Networking.World.Items;

namespace Multiplayer.Patches.World.Items;

[HarmonyPatch(typeof(PageBook), "Start")]
internal static class PageBookPatch
{
    [HarmonyPostfix]
    private static void Register(PageBook __instance) =>
        PageBookStateRegistration.Register(__instance);
}

[HarmonyPatch(typeof(LabelableItem), "Start")]
internal static class LabelableItemPatch
{
    [HarmonyPostfix]
    private static void Register(LabelableItem __instance) =>
        LabelableItemStateRegistration.Register(__instance);
}

[HarmonyPatch(typeof(DrillTool), "Awake")]
internal static class DrillToolPatch
{
    [HarmonyPostfix]
    private static void Register(DrillTool __instance) =>
        DrillToolStateRegistration.Register(__instance);
}

[HarmonyPatch(typeof(Cassette), "Start")]
internal static class CassettePatch
{
    [HarmonyPostfix]
    private static void Register(Cassette __instance) =>
        CassetteStateRegistration.Register(__instance);
}

[HarmonyPatch(typeof(PaintCan), "Start")]
internal static class PaintCanAmmoPatch
{
    [HarmonyPostfix]
    private static void Register(PaintCan __instance) =>
        PaintCanStateRegistration.Register(__instance);
}

[HarmonyPatch(typeof(GadgetSolderingResource), "Start")]
internal static class SolderingResourceAmmoPatch
{
    [HarmonyPostfix]
    private static void Register(GadgetSolderingResource __instance) =>
        SolderingResourceStateRegistration.Register(__instance);
}

[HarmonyPatch(typeof(AnalogAlarmClock), "Start")]
internal static class AnalogAlarmClockPatch
{
    [HarmonyPostfix]
    private static void Register(AnalogAlarmClock __instance) =>
        AnalogAlarmClockStateRegistration.Register(__instance);
}

[HarmonyPatch(typeof(BoomboxInteractionController), "Awake")]
internal static class BoomboxPatch
{
    [HarmonyPostfix]
    private static void Register(BoomboxInteractionController __instance) =>
        BoomboxStateRegistration.Register(__instance);
}

[HarmonyPatch(typeof(LocomotiveRemoteController), "Start")]
internal static class LocomotiveRemoteItemPatch
{
    [HarmonyPostfix]
    private static void Register(LocomotiveRemoteController __instance) =>
        LocomotiveRemoteStateRegistration.Register(__instance);
}

[HarmonyPatch(typeof(LocomotiveRemoteController))]
internal static class LocomotiveRemotePairingPatch
{
    [HarmonyPatch(
        nameof(LocomotiveRemoteController.Pair),
        typeof(TrainCar))]
    [HarmonyPostfix]
    private static void Pair(LocomotiveRemoteController __instance) =>
        NetworkedLocomotiveRemotePairing
            .OnLocalPairingChanged(__instance);

    [HarmonyPatch(nameof(LocomotiveRemoteController.Unpair))]
    [HarmonyPostfix]
    private static void Unpair(LocomotiveRemoteController __instance) =>
        NetworkedLocomotiveRemotePairing
            .OnLocalPairingChanged(__instance);
}

[HarmonyPatch(typeof(ProximitySensor), "Start")]
internal static class ProximitySensorPatch
{
    [HarmonyPostfix]
    private static void Register(ProximitySensor __instance) =>
        ProximitySensorStateRegistration.Register(__instance);
}

[HarmonyPatch(typeof(CommsRadioController), "Start")]
internal static class CommsRadioItemPatch
{
    [HarmonyPostfix]
    private static void Register(CommsRadioController __instance) =>
        CommsRadioStateRegistration.Register(__instance);
}

[HarmonyPatch(typeof(BrickConsole))]
internal static class BrickConsolePatch
{
    [HarmonyPatch("Start")]
    [HarmonyPostfix]
    private static void Register(BrickConsole __instance) =>
        BrickConsoleStateRegistration.Register(__instance);

    [HarmonyPatch(nameof(BrickConsole.ExecuteInputAction))]
    [HarmonyPrefix]
    private static void CaptureInput(
        BrickConsole __instance,
        BrickInput.BrickInputAction brickInputAction)
    {
        BrickConsoleStateRegistration.CaptureInput(
            __instance,
            brickInputAction);
    }
}
