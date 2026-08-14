using DV.CabControls;
using DV.Customization;
using DV.Customization.Gadgets;
using DV.Customization.Gadgets.Implementations;
using DV.Items.Snapping;
using HarmonyLib;
using Multiplayer.Components.Networking;
using Multiplayer.Components.Networking.Customization;
using Multiplayer.Components.Networking.Customization.Gadgets;
using Multiplayer.Components.Networking.World;
using Multiplayer.Networking.Data;
using Multiplayer.Networking.Data.Customization;
using Multiplayer.Networking.Managers.Client;
using Multiplayer.Networking.Managers.Server;
using Multiplayer.Networking.Packets.Serverbound;
using Multiplayer.Networking.TransportLayers;
using System;
using UnityEngine;

namespace Multiplayer.Patches.Customization;

[HarmonyPatch(typeof(GadgetItem), "Awake")]
internal static class GadgetItemRegistrationPatch
{
    [HarmonyPostfix]
    private static void Awake(GadgetItem __instance)
    {
        if (__instance?.Item == null || __instance.Gadget == null)
            return;
        NetworkedItem networkedItem = __instance.GetComponent<NetworkedItem>() ?? __instance.gameObject.AddComponent<NetworkedItem>();
        networkedItem.Initialize(__instance);
        GadgetTrackedValueRegistry.Register(networkedItem, __instance, __instance.Gadget);
        networkedItem.FinaliseTrackedValues();
    }
}

[HarmonyPatch]
internal static class GadgetStructuralObservationPatch
{
    [HarmonyPostfix, HarmonyPatch(typeof(GadgetItem), nameof(GadgetItem.Place))]
    private static void AfterPlace(Customization destination, GadgetItem gadgetItem, GadgetBase __result)
    {
        if (CustomizationSyncScope.IsApplyingRemote || __result == null || gadgetItem?.Item == null ||
            !NetworkedItem.TryGetNetworkedItem(gadgetItem.Item, out NetworkedItem networkedItem) || networkedItem.NetId == 0 ||
            !CustomizationRef.TryFrom(destination, out CustomizationRef target))
            return;
        if (NetworkLifecycle.Instance.IsHost())
            GadgetStructuralSync.ClearServerOwnership(networkedItem.NetId);
        GadgetStructuralSync.SendObserved(new GadgetPlacePacket
        {
            GadgetItemNetId = networkedItem.NetId,
            Target = target,
            LocalPosition = __result.transform.localPosition,
            LocalRotation = __result.transform.localRotation,
            IsOnGlass = __result.IsOnGlass,
        });
    }

    [HarmonyPrefix, HarmonyPatch(typeof(GadgetBase), nameof(GadgetBase.Remove))]
    private static void BeforeRemove(ref IDisposable __state)
    {
        if (!CustomizationSyncScope.IsApplyingRemote)
            __state = CustomizationSyncScope.LocalRoot();
    }

    [HarmonyPostfix, HarmonyPatch(typeof(GadgetBase), nameof(GadgetBase.Remove))]
    private static void AfterRemove(bool reparentToTrainCar, GadgetItem __result)
    {
        if (CustomizationSyncScope.IsApplyingRemote || __result?.Item == null ||
            !NetworkedItem.TryGetNetworkedItem(__result.Item, out NetworkedItem networkedItem) || networkedItem.NetId == 0)
            return;
        GadgetStructuralSync.SendObserved(new GadgetRemovePacket
        {
            GadgetItemNetId = networkedItem.NetId,
            ReparentToTrainCar = reparentToTrainCar,
        });
    }

    [HarmonyFinalizer, HarmonyPatch(typeof(GadgetBase), nameof(GadgetBase.Remove))]
    private static Exception FinishRemove(Exception __exception, IDisposable __state)
    {
        __state?.Dispose();
        return __exception;
    }
}

[HarmonyPatch]
internal static class GadgetSnapObservationPatch
{
    [HarmonyPostfix, HarmonyPatch(typeof(ItemSnapPointBase), nameof(ItemSnapPointBase.SnapItem), new[] { typeof(ItemBase), typeof(bool) })]
    private static void AfterSnap(ItemSnapPointBase __instance, ItemBase itemToSnap, bool __result)
    {
        if (!__result || __instance is not SnapPointGadget point || itemToSnap == null ||
            CustomizationSyncScope.IsApplyingRemote || CustomizationSyncScope.IsApplyingRootAction ||
            !GadgetSnapSync.TryDescribe(point, out ushort ownerId, out int pointIndex) ||
            !NetworkedItem.TryGetNetworkedItem(itemToSnap, out NetworkedItem attached) || attached.NetId == 0)
            return;
        Transform anchor = itemToSnap.SnappableItem?.GetAnchor(point.SnapPointType);
        SnapPointAnchorSliding sliding = anchor?.GetComponent<SnapPointAnchorSliding>();
        GadgetStructuralSync.SendObserved(new GadgetSnapPacket { State = new GadgetSnapState
        {
            TargetGadgetItemNetId = ownerId,
            AttachedItemNetId = attached.NetId,
            SnapPointIndex = pointIndex,
            HasSlidingAnchor = sliding != null,
            SlidingAnchorLocalPosition = sliding != null ? sliding.transform.localPosition : default,
        }});
    }

    [HarmonyPrefix, HarmonyPatch(typeof(ItemSnapPointBase), nameof(ItemSnapPointBase.UnsnapItem), new[] { typeof(bool) })]
    private static void BeforeUnsnap(ItemSnapPointBase __instance, ref ItemBase __state)
    {
        __state = __instance is SnapPointGadget ? __instance.SnappedItem : null;
    }

    [HarmonyPostfix, HarmonyPatch(typeof(ItemSnapPointBase), nameof(ItemSnapPointBase.UnsnapItem), new[] { typeof(bool) })]
    private static void AfterUnsnap(ItemSnapPointBase __instance, bool __result, ItemBase __state)
    {
        if (!__result || __instance is not SnapPointGadget point || __state == null ||
            CustomizationSyncScope.IsApplyingRemote || CustomizationSyncScope.IsApplyingRootAction ||
            !GadgetSnapSync.TryDescribe(point, out ushort ownerId, out int pointIndex) ||
            !NetworkedItem.TryGetNetworkedItem(__state, out NetworkedItem attached) || attached.NetId == 0)
            return;
        GadgetStructuralSync.SendObserved(new GadgetUnsnapPacket
        {
            TargetGadgetItemNetId = ownerId,
            AttachedItemNetId = attached.NetId,
            SnapPointIndex = pointIndex,
        });
    }
}

[HarmonyPatch]
internal static class RoadrunnerObservationPatch
{
    [HarmonyPrefix, HarmonyPatch(typeof(GadgetRoadrunner), "Update")]
    private static void BeforeUpdate() => RoadrunnerSync.EnterNativeUpdate();

    [HarmonyFinalizer, HarmonyPatch(typeof(GadgetRoadrunner), "Update")]
    private static Exception AfterUpdate(Exception __exception)
    {
        RoadrunnerSync.ExitNativeUpdate();
        return __exception;
    }

    [HarmonyPostfix, HarmonyPatch(typeof(GadgetRoadrunner), nameof(GadgetRoadrunner.StartMeasure))]
    private static void AfterStart(GadgetRoadrunner __instance) => RoadrunnerSync.SendObserved(__instance, RoadrunnerSyncAction.Start);

    [HarmonyPostfix, HarmonyPatch(typeof(GadgetRoadrunner), nameof(GadgetRoadrunner.Acknowledge))]
    private static void AfterAcknowledge(GadgetRoadrunner __instance) => RoadrunnerSync.SendObserved(__instance, RoadrunnerSyncAction.Acknowledge);

    [HarmonyPostfix, HarmonyPatch(typeof(NetworkClient), "Subscribe")]
    private static void SubscribeClient(NetworkClient __instance) => RoadrunnerSync.RegisterClient(__instance);

    [HarmonyPostfix, HarmonyPatch(typeof(NetworkServer), "Subscribe")]
    private static void SubscribeServer(NetworkServer __instance) => RoadrunnerSync.RegisterServer(__instance);

    [HarmonyPrefix, HarmonyPriority(Priority.High), HarmonyPatch(typeof(NetworkServer), "OnServerboundLoadStateUpdatePacket")]
    private static void SendJoinState(NetworkServer __instance, ServerboundLoadStateUpdatePacket packet, ITransportPeer peer)
    {
        if (packet.LoadState == PlayerLoadingState.ReadyForCustomizers &&
            __instance.TryGetServerPlayer(peer, out ServerPlayer player) &&
            player.LoadingState == PlayerLoadingState.ReadyForTrainSets)
            RoadrunnerSync.SendJoinState(__instance, player);
    }
}
