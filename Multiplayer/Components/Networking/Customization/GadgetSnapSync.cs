using DV.Customization.Gadgets;
using DV.Customization.Gadgets.Implementations;
using DV.Items.Snapping;
using MPAPI.Interfaces;
using Multiplayer.API;
using Multiplayer.Components.Networking.World;
using Multiplayer.Networking.Data.Customization;
using Multiplayer.Networking.Managers.Client;
using Multiplayer.Networking.Managers.Server;
using Multiplayer.Networking.TransportLayers;
using System;
using System.Linq;
using UnityEngine;

namespace Multiplayer.Components.Networking.Customization;

internal static class GadgetSnapSync
{
    public static void RegisterClient(NetworkClient client)
    {
        client.RegisterExternalSerializablePacket<GadgetSnapPacket>(p => { if (p?.State != null) ApplyState(p.State); });
        client.RegisterExternalSerializablePacket<GadgetUnsnapPacket>(ApplyUnsnap);
    }

    public static void RegisterServer(NetworkServer server)
    {
        server.RegisterExternalSerializablePacket<GadgetSnapPacket>((packet, sender) =>
        {
            if (packet?.State != null && ApplyState(packet.State))
                Broadcast(server, packet, sender);
        });
        server.RegisterExternalSerializablePacket<GadgetUnsnapPacket>((packet, sender) =>
        {
            if (ApplyUnsnap(packet))
                Broadcast(server, packet, sender);
        });
    }

    public static void AppendSnapshot(ClientboundCustomizationStatePacket packet)
    {
        foreach (NetworkedItem ownerItem in NetworkedItem.GetAll().ToArray())
        {
            GadgetBase gadget = ownerItem?.Item?.GetComponent<GadgetItem>()?.Gadget;
            if (gadget == null || !gadget.IsLinked)
                continue;
            SnapPointGadget[] points = gadget.GetComponentsInChildren<SnapPointGadget>(true);
            for (int index = 0; index < points.Length; index++)
            {
                SnapPointGadget point = points[index];
                if (point?.SnappedItem == null || !NetworkedItem.TryGetNetworkedItem(point.SnappedItem, out NetworkedItem attached) || attached.NetId == 0)
                    continue;
                Transform anchor = point.SnappedItem.SnappableItem?.GetAnchor(point.SnapPointType);
                SnapPointAnchorSliding sliding = anchor?.GetComponent<SnapPointAnchorSliding>();
                packet.Snaps.Add(new GadgetSnapState
                {
                    TargetGadgetItemNetId = ownerItem.NetId,
                    AttachedItemNetId = attached.NetId,
                    SnapPointIndex = index,
                    HasSlidingAnchor = sliding != null,
                    SlidingAnchorLocalPosition = sliding != null ? sliding.transform.localPosition : default,
                });
            }
        }
    }

    public static bool DependenciesReady(ClientboundCustomizationStatePacket packet)
    {
        foreach (GadgetSnapState state in packet.Snaps)
        {
            if (state == null || !GadgetStructuralSync.TryGet(state.TargetGadgetItemNetId, out _, out _, out _) ||
                !NetworkedItem.TryGet(state.AttachedItemNetId, out NetworkedItem attached) || attached?.Item == null)
                return false;
        }
        return true;
    }

    public static void ApplySnapshot(ClientboundCustomizationStatePacket packet)
    {
        foreach (GadgetSnapState state in packet.Snaps)
        {
            if (state != null)
                ApplyState(state);
        }
    }

    public static bool TryDescribe(SnapPointGadget point, out ushort ownerItemNetId, out int pointIndex)
    {
        ownerItemNetId = 0;
        pointIndex = -1;
        GadgetBase owner = point?.gadgetBase ?? point?.GetComponentInParent<GadgetBase>();
        if (owner?.GadgetItem?.Item == null || !NetworkedItem.TryGetNetworkedItem(owner.GadgetItem.Item, out NetworkedItem item) || item.NetId == 0)
            return false;
        pointIndex = Array.IndexOf(owner.GetComponentsInChildren<SnapPointGadget>(true), point);
        if (pointIndex < 0)
            return false;
        ownerItemNetId = item.NetId;
        return true;
    }

    private static bool ApplyState(GadgetSnapState state)
    {
        if (!TryResolve(state.TargetGadgetItemNetId, state.SnapPointIndex, out SnapPointGadget point) ||
            !NetworkedItem.TryGet(state.AttachedItemNetId, out NetworkedItem attached) || attached?.Item == null)
            return false;

        using (CustomizationSyncScope.Remote())
        {
            if (point.SnappedItem != attached.Item)
            {
                if (point.SnappedItem != null)
                    point.UnsnapItem(true);
                if (attached.Item.SnappableItem?.SnappedTo != null)
                    attached.Item.SnappableItem.SnappedTo.UnsnapItem(true);
                if (!point.SnapItem(attached.Item, true))
                    return false;
            }

            if (state.HasSlidingAnchor)
            {
                Transform anchor = attached.Item.SnappableItem?.GetAnchor(point.SnapPointType);
                SnapPointAnchorSliding sliding = anchor?.GetComponent<SnapPointAnchorSliding>();
                if (sliding != null)
                {
                    sliding.transform.localPosition = state.SlidingAnchorLocalPosition;
                    var pose = point.CalculateWorldEndPose(attached.Item);
                    attached.Item.transform.SetPositionAndRotation(pose.Item1, pose.Item2);
                }
            }
        }
        return true;
    }

    private static bool ApplyUnsnap(GadgetUnsnapPacket packet)
    {
        if (packet == null || !TryResolve(packet.TargetGadgetItemNetId, packet.SnapPointIndex, out SnapPointGadget point) || point.SnappedItem == null)
            return false;
        using (CustomizationSyncScope.Remote())
            return point.UnsnapItem(true);
    }

    private static bool TryResolve(ushort ownerItemNetId, int pointIndex, out SnapPointGadget point)
    {
        point = null;
        if (pointIndex < 0 || !GadgetStructuralSync.TryGet(ownerItemNetId, out _, out _, out GadgetBase owner))
            return false;
        SnapPointGadget[] points = owner.GetComponentsInChildren<SnapPointGadget>(true);
        if (pointIndex >= points.Length)
            return false;
        point = points[pointIndex];
        return point != null;
    }

    private static void Broadcast<T>(NetworkServer server, T packet, IPlayer sender) where T : class, MPAPI.Interfaces.Packets.ISerializablePacket, new()
    {
        ITransportPeer excludePeer = (sender as ServerPlayerWrapper)?.Peer;
        foreach (ServerPlayer recipient in server.ServerPlayers)
        {
            if (recipient.Peer == server.SelfPeer || recipient.Peer == excludePeer || recipient.LoadingState < Multiplayer.Networking.Data.PlayerLoadingState.ReadyForCustomizers)
                continue;
            server.SendExternalSerializablePacketToPlayer(packet, recipient.Peer, true);
        }
    }
}
