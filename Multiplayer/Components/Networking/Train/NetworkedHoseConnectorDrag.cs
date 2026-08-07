using DV.CabControls;
using HarmonyLib;
using Multiplayer.Components.Networking;
using Multiplayer.Components.Networking.Player;
using Multiplayer.Networking.Packets.Common.Train;
using Multiplayer.Utils;
using UnityEngine;

namespace Multiplayer.Components.Networking.Train;

internal static class NetworkedHoseConnectorDrag
{
    private static readonly System.Reflection.MethodInfo OnGrabbedMethod =
        AccessTools.Method(typeof(CouplingHoseConnector), "OnGrabbed");
    private static readonly System.Reflection.MethodInfo OnUngrabbedMethod =
        AccessTools.Method(typeof(CouplingHoseConnector), "OnUngrabbed");
    private static bool applyingRemote;

    public static void OnLocalGrabbed(CouplingHoseRig rig)
    {
        if (!applyingRemote &&
            !NetworkLifecycle.Instance.IsProcessingPacket)
            NetworkLifecycle.Instance.Client?.SendHoseConnectorDrag(rig, true);
    }

    public static void OnLocalUngrabbed(CouplingHoseRig rig)
    {
        if (!applyingRemote &&
            !NetworkLifecycle.Instance.IsProcessingPacket)
            NetworkLifecycle.Instance.Client?.SendHoseConnectorDrag(rig, false);
    }

    public static void OnLocalTelegrabChanged(
        bool isBeingTelegrabbed,
        CouplingHoseRig rig)
    {
        // Vanilla only forwards the true transition to OnGrabbed. Explicitly
        // replicate the false transition so remote hands release telegrabbed
        // hose connectors too.
        if (!isBeingTelegrabbed &&
            !applyingRemote &&
            !NetworkLifecycle.Instance.IsProcessingPacket)
        {
            NetworkLifecycle.Instance.Client?.SendHoseConnectorDrag(
                rig,
                false);
        }
    }

    public static void ApplyRemoteDrag(CommonHoseConnectorDragPacket packet)
    {
        if (!NetworkedTrainCar.TryGet(packet.NetId, out TrainCar trainCar) ||
            NetworkLifecycle.Instance.Client == null ||
            packet.PlayerId == NetworkLifecycle.Instance.Client.PlayerId ||
            !NetworkLifecycle.Instance.Client.ClientPlayerManager.TryGetPlayer(
                packet.PlayerId,
                out NetworkedPlayer player))
        {
            return;
        }

        if (!packet.Grabbed &&
            player.RightHandItemGO != null &&
            player.RightHandItemGO.TryGetComponent(
                out CouplingHoseConnector heldConnector))
        {
            // Detach first: releasing/connecting a hose can swap or unload its
            // rig before this packet arrives, making exact connector lookup
            // impossible while the old object remains parented to the hand.
            player.DropItem();
        }

        CouplingHoseRig rig = GetRig(trainCar, packet);
        CouplingHoseConnector connector =
            rig?.GetComponentInChildren<CouplingHoseConnector>(true);
        if (connector == null)
            return;

        applyingRemote = true;
        try
        {
            if (packet.Grabbed)
            {
                OnGrabbedMethod?.Invoke(connector, new object[] { null });

                if (player.RightHandItemGO != null)
                    player.DropItem();

                player.HoldItem(
                    connector.gameObject,
                    followTrackedHand: true);
            }
            else
            {
                if (player.RightHandItemGO == connector.gameObject)
                    player.DropItem();

                OnUngrabbedMethod?.Invoke(connector, new object[] { null });
            }
        }
        finally
        {
            applyingRemote = false;
        }
    }

    internal static bool ValidateClientDrag(
        CommonHoseConnectorDragPacket packet,
        Vector3 playerWorldPosition)
    {
        if (packet == null ||
            !NetworkedTrainCar.TryGet(
                packet.NetId,
                out TrainCar trainCar))
        {
            return false;
        }

        CouplingHoseRig rig = GetRig(trainCar, packet);
        CouplingHoseConnector connector =
            rig?.GetComponentInChildren<CouplingHoseConnector>(true);
        return playerWorldPosition.IsFinite() &&
               connector != null &&
               connector.IsWithinGrabbableDistance(playerWorldPosition);
    }

    private static CouplingHoseRig GetRig(
        TrainCar trainCar,
        CommonHoseConnectorDragPacket packet)
    {
        if (packet.IsMultipleUnit)
        {
            if (trainCar.muModule == null)
                return null;

            var cable = packet.IsFront
                ? trainCar.muModule.frontCable
                : trainCar.muModule.rearCable;
            return cable?.HoseAdapter?.rig;
        }

        Coupler coupler = packet.IsFront
            ? trainCar.frontCoupler
            : trainCar.rearCoupler;
        return CouplingHoseRig.GetRig(coupler);
    }
}
