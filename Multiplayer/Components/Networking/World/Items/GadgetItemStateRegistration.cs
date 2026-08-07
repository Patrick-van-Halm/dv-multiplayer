using DV.Customization.Gadgets;
using DV.Customization.Gadgets.Implementations;
using DV.Items;
using Multiplayer.Components.Networking.World;
using Multiplayer.Components.Networking.World.Items;
using Multiplayer.Utils;

namespace Multiplayer.Components.Networking.World.Items;

internal static class GadgetRemoverStateRegistration
{
    public static void Register(GadgetRemover __instance)
    {
        NetworkedItem item = ItemStateTracking.For(__instance);
        RemoteToolAnimationState state =
            __instance.gameObject.GetOrAddComponent<RemoteToolAnimationState>();
        state.Initialize(__instance, item);
        item.RegisterTrackedValue(
            "hammer.poseActive",
            state.GetPoseActive,
            state.SetPoseActive);
        item.FinaliseTrackedValues();
    }
}

internal static class GadgetItemStateRegistration
{
    public static void Register(GadgetItem __instance)
    {
        GadgetSyncState state =
            __instance.gameObject.GetOrAddComponent<GadgetSyncState>();
        if (state.IsRegistered)
            return;

        state.Item = __instance;

        NetworkedItem item = ItemStateTracking.For(__instance);
        item.RegisterTrackedValue(
            "gadget.placement",
            state.GetPlacement,
            state.SetPlacement);
        item.RegisterTrackedValue(
            "gadget.data",
            state.GetGadgetData,
            state.SetGadgetData);
        item.FinaliseTrackedValues();
        state.IsRegistered = true;
    }
}

internal static class GadgetSolderingToolStateRegistration
{
    public static bool ShouldRunSetParticles(
        GadgetSolderingTool __instance,
        bool on)
    {
        RemoteToolAnimationState state =
            __instance.GetComponent<RemoteToolAnimationState>();
        return !on ||
               state == null ||
               !state.ShouldSuppressRemoteParticles;
    }

    public static void Register(GadgetSolderingTool __instance)
    {
        NetworkedItem item = ItemStateTracking.For(__instance);
        RemoteToolAnimationState state =
            __instance.gameObject.GetOrAddComponent<RemoteToolAnimationState>();
        state.Initialize(__instance, item);
        item.RegisterTrackedValue(
            "solder.units",
            () => __instance.remainingUnits,
            value => SetRemainingUnits(__instance, value));
        item.RegisterTrackedValue(
            "solder.pressed",
            () => __instance.IsPressed,
            value => __instance.IsPressed = value);
        item.RegisterTrackedValue(
            "solder.working",
            () => __instance.shouldParticlesBeOn ||
                  __instance.soundSoldering.isPlaying,
            value => SetWorking(__instance, value));
        RemoteToolPoseTracking.Register(item, state, "solder");
        item.FinaliseTrackedValues();
    }

    private static void SetRemainingUnits(
        GadgetSolderingTool tool,
        int units)
    {
        tool.gameObject
            .GetOrAddComponent<SolderingToolSyncState>()
            .SetRemainingUnits(tool, units);
    }

    private static void SetWorking(
        GadgetSolderingTool tool,
        bool working)
    {
        tool.shouldParticlesBeOn = false;
        tool.SetParticles(false);

        if (working)
        {
            if (!tool.soundSoldering.isPlaying)
                tool.soundSoldering.PlayRandomTime();
        }
        else if (tool.soundSoldering.isPlaying)
        {
            tool.soundSoldering.Stop();
        }
    }
}

internal static class GadgetWiringToolStateRegistration
{
    public static void Register(GadgetWiringTool __instance)
    {
        RemoteToolAnimationState state =
            __instance.gameObject.GetOrAddComponent<RemoteToolAnimationState>();
        NetworkedItem item = ItemStateTracking.For(__instance);
        state.Initialize(__instance, item);
        RemoteToolPoseTracking.Register(item, state, "wiring");
        item.FinaliseTrackedValues();
    }
}

internal static class DuctTapeStateRegistration
{
    public static void Register(DuctTape __instance)
    {
        NetworkedItem item = ItemStateTracking.For(__instance);
        item.RegisterTrackedValue(
            "ductTape.uses",
            () => __instance.usesLeft,
            value =>
            {
                __instance.usesLeft = value;
                __instance.tapeModelUpdater?.UpdateActiveStates(
                    __instance.PercentageUsesLeft);
            });
        item.FinaliseTrackedValues();
    }
}
