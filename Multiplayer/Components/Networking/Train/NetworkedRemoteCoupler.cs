using DV.CabControls;
using DV.RemoteControls;
using Multiplayer.Components.Networking.World;
using Multiplayer.Networking.Data;
using Multiplayer.Networking.Data.Train;
using Multiplayer.Networking.Packets.Common.Train;
using Multiplayer.Utils;
using UnityEngine;

namespace Multiplayer.Components.Networking.Train;

internal static class NetworkedRemoteCoupler
{
    public static void SendCouple(RemoteControllerModule module)
    {
        if (module?.car?.frontCoupler == null ||
            !TryGetRemoteItemNetId(module, out ushort remoteItemNetId))
        {
            return;
        }

        NetworkLifecycle.Instance.Client?.SendCouplerInteraction(
            CouplerInteractionType.Start |
            CouplerInteractionType.CoupleViaRemote,
            module.car.frontCoupler,
            remoteItemNetId: remoteItemNetId);
    }

    public static void SendUncouple(
        RemoteControllerModule module,
        int selectedCoupler)
    {
        TrainCar startCar = module?.car;
        if (startCar == null ||
            !TryGetRemoteItemNetId(module, out ushort remoteItemNetId))
        {
            return;
        }

        Coupler selected = CouplerLogic.GetNthCouplerFrom(
            selectedCoupler > 0
                ? startCar.frontCoupler
                : startCar.rearCoupler,
            Mathf.Abs(selectedCoupler) - 1);
        if (selected == null)
            return;

        NetworkLifecycle.Instance.Client?.SendCouplerInteraction(
            CouplerInteractionType.Start |
            CouplerInteractionType.UncoupleViaRemote,
            selected,
            remoteItemNetId: remoteItemNetId);
    }

    public static bool ValidateAuthority(
        CommonCouplerInteractionPacket packet,
        CouplerInteractionType interaction,
        ServerPlayer player,
        TrainCar requestedCar)
    {
        if (!CouplerInteractionRules.IsRemote(interaction))
            return packet.RemoteItemNetId == 0;

        NetworkedItem networkedItem = null;
        bool ownsRemoteItem =
            packet.RemoteItemNetId != 0 &&
            player.TryGetOwnedItem(
                packet.RemoteItemNetId,
                out networkedItem);

        LocomotiveRemoteController remote = ownsRemoteItem
            ?
            networkedItem.GetTrackedItem<LocomotiveRemoteController>() ??
            networkedItem.GetComponent<LocomotiveRemoteController>()
            : null;
        RemoteControllerModule pairedModule =
            remote?.pairedLocomotive as RemoteControllerModule;
        TrainCar pairedCar = pairedModule?.car;
        bool isCoupleAction =
            interaction.HasFlag(CouplerInteractionType.CoupleViaRemote);

        return RemoteCouplerAuthorityRules.IsAuthorized(
            ownsRemoteItem,
            remote?.InControl == true,
            pairedCar != null,
            isCoupleAction,
            requestedCar == pairedCar,
            requestedCar?.trainset != null &&
                requestedCar.trainset == pairedCar?.trainset);
    }

    private static bool TryGetRemoteItemNetId(
        RemoteControllerModule module,
        out ushort remoteItemNetId)
    {
        remoteItemNetId = 0;
        LocomotiveRemoteController remote =
            module?.pairedLocomotiveRemote;
        ItemBase item = remote?.GetComponentInParent<ItemBase>();
        if (item != null &&
            NetworkedItem.TryGetNetworkedItem(
                item,
                out NetworkedItem networkedItem) &&
            networkedItem.NetId != 0)
        {
            remoteItemNetId = networkedItem.NetId;
            return true;
        }

        Multiplayer.LogWarning(
            "Skipping remote coupler synchronization because the paired " +
            "locomotive remote has no authoritative network item ID.");
        return false;
    }
}

internal static class RemoteCouplerAuthorityRules
{
    public static bool IsAuthorized(
        bool ownsRemoteItem,
        bool remoteInControl,
        bool hasPairedModule,
        bool isCoupleAction,
        bool targetsPairedCar,
        bool sharesPairedTrainset)
    {
        return ownsRemoteItem &&
               remoteInControl &&
               hasPairedModule &&
               (isCoupleAction
                   ? targetsPairedCar
                   : sharesPairedTrainset);
    }
}
