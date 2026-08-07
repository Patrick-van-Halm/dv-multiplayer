using DV.CabControls;
using DV.RemoteControls;
using Multiplayer.Components.Networking.Train;
using Multiplayer.Networking.Data;
using Multiplayer.Networking.Packets.Serverbound;
using Multiplayer.Utils;
using UnityEngine;

namespace Multiplayer.Components.Networking.World.Items;

internal static class NetworkedLocomotiveRemotePairing
{
    private static bool applyingAuthoritativeState;

    public static void OnLocalPairingChanged(
        LocomotiveRemoteController remote)
    {
        if (applyingAuthoritativeState ||
            NetworkLifecycle.Instance.IsProcessingPacket ||
            NetworkLifecycle.Instance.Client == null)
        {
            return;
        }

        ItemBase item = remote?.GetComponentInParent<ItemBase>();
        if (item == null ||
            !NetworkedItem.TryGetNetworkedItem(
                item,
                out NetworkedItem networkedItem) ||
            networkedItem.NetId == 0)
        {
            return;
        }

        ushort locomotiveNetId = 0;
        if (remote.pairedLocomotive is
                RemoteControllerModule pairedModule &&
            pairedModule.car != null)
        {
            NetworkedTrainCar.TryGetNetId(
                pairedModule.car,
                out locomotiveNetId);
        }

        NetworkLifecycle.Instance.Client
            .SendLocomotiveRemotePairRequest(
                networkedItem.NetId,
                locomotiveNetId);
    }

    public static void ApplyAuthoritative(
        LocomotiveRemoteController remote,
        TrainCar locomotive)
    {
        if (remote == null)
            return;

        applyingAuthoritativeState = true;
        try
        {
            if (locomotive == null)
                remote.Unpair();
            else
                remote.Pair(locomotive);
        }
        finally
        {
            applyingAuthoritativeState = false;
        }
    }

    public static bool TryApplyRequest(
        ServerboundLocomotiveRemotePairPacket packet,
        ServerPlayer player,
        out NetworkedItem remoteItem)
    {
        remoteItem = null;
        if (packet == null ||
            player == null ||
            packet.RemoteItemNetId == 0 ||
            !player.TryGetOwnedItem(
                packet.RemoteItemNetId,
                out remoteItem))
        {
            return false;
        }

        LocomotiveRemoteController remote =
            remoteItem.GetTrackedItem<LocomotiveRemoteController>() ??
            remoteItem.GetComponent<LocomotiveRemoteController>();
        if (remote == null)
            return false;

        bool isUnpairRequest = packet.LocomotiveNetId == 0;
        TrainCar locomotive = null;
        bool targetExists =
            !isUnpairRequest &&
            NetworkedTrainCar.TryGet(
                packet.LocomotiveNetId,
                out locomotive);
        object pairingTarget = targetExists
            ? remote.GetPairingTarget(locomotive)
            : null;
        bool targetSupportsRemote = pairingTarget != null;
        bool targetIsReachable =
            pairingTarget is Component pairingComponent &&
            pairingComponent.transform.PlayerCanReach(
                player,
                NetworkedItemManager.REACH_DISTANCE_BUFFER);
        if (!CanApplyRequest(
                ownsRemote: true,
                isUnpairRequest,
                targetExists,
                targetSupportsRemote,
                targetIsReachable))
        {
            return false;
        }

        ApplyAuthoritative(
            remote,
            isUnpairRequest ? null : locomotive);
        return true;
    }

    internal static bool CanApplyRequest(
        bool ownsRemote,
        bool isUnpairRequest,
        bool targetExists,
        bool targetSupportsRemote,
        bool targetIsReachable)
    {
        return ownsRemote &&
               (isUnpairRequest ||
                targetExists &&
                targetSupportsRemote &&
                targetIsReachable);
    }
}
