using DV.Customization;
using MPAPI.Interfaces;
using Multiplayer.API;
using Multiplayer.Networking.Data.Customization;
using Multiplayer.Networking.Managers.Client;
using Multiplayer.Networking.Managers.Server;
using Multiplayer.Networking.TransportLayers;
using UnityEngine;

namespace Multiplayer.Components.Networking.Customization;

internal static class CustomizationHoleSync
{
    public static void RegisterClient(NetworkClient client)
    {
        client.RegisterExternalSerializablePacket<CustomizationAddHolePacket>(ApplyAdd);
        client.RegisterExternalSerializablePacket<CustomizationMoveHolePacket>(ApplyMove);
        client.RegisterExternalSerializablePacket<CustomizationRemoveHolePacket>(ApplyRemove);
    }

    public static void RegisterServer(NetworkServer server)
    {
        server.RegisterExternalSerializablePacket<CustomizationAddHolePacket>((packet, sender) => OnServerAdd(server, packet, sender));
        server.RegisterExternalSerializablePacket<CustomizationMoveHolePacket>((packet, sender) => OnServerMove(server, packet, sender));
        server.RegisterExternalSerializablePacket<CustomizationRemoveHolePacket>((packet, sender) => OnServerRemove(server, packet, sender));
    }

    private static void OnServerAdd(NetworkServer server, CustomizationAddHolePacket packet, IPlayer sender)
    {
        if (!TryResolve(packet?.Target, out Customization target) || !Finite(packet.LocalPosition) || !Finite(packet.LocalNormal))
            return;

        using (CustomizationSyncScope.Remote())
            target.AddHole(packet.LocalPosition, packet.LocalNormal);

        Broadcast(server, packet, sender);
    }

    private static void OnServerMove(NetworkServer server, CustomizationMoveHolePacket packet, IPlayer sender)
    {
        if (!TryResolve(packet?.Target, out Customization target) || !Finite(packet.PreviousLocalPosition) ||
            !Finite(packet.LocalPosition) || !Finite(packet.LocalNormal) ||
            !target.FindHole(packet.PreviousLocalPosition, out Collider hole))
            return;

        using (CustomizationSyncScope.Remote())
            target.MoveHole(hole, packet.LocalPosition, packet.LocalNormal);

        Broadcast(server, packet, sender);
    }

    private static void OnServerRemove(NetworkServer server, CustomizationRemoveHolePacket packet, IPlayer sender)
    {
        if (!TryResolve(packet?.Target, out Customization target) || !Finite(packet.LocalPosition) ||
            !target.FindHole(packet.LocalPosition, out Collider hole))
            return;

        using (CustomizationSyncScope.Remote())
            target.RemoveHole(hole);

        Broadcast(server, packet, sender);
    }

    private static void ApplyAdd(CustomizationAddHolePacket packet)
    {
        if (!TryResolve(packet?.Target, out Customization target))
            return;
        using (CustomizationSyncScope.Remote())
            target.AddHole(packet.LocalPosition, packet.LocalNormal);
    }

    private static void ApplyMove(CustomizationMoveHolePacket packet)
    {
        if (!TryResolve(packet?.Target, out Customization target) ||
            !target.FindHole(packet.PreviousLocalPosition, out Collider hole))
            return;
        using (CustomizationSyncScope.Remote())
            target.MoveHole(hole, packet.LocalPosition, packet.LocalNormal);
    }

    private static void ApplyRemove(CustomizationRemoveHolePacket packet)
    {
        if (!TryResolve(packet?.Target, out Customization target) ||
            !target.FindHole(packet.LocalPosition, out Collider hole))
            return;
        using (CustomizationSyncScope.Remote())
            target.RemoveHole(hole);
    }

    private static bool TryResolve(CustomizationRef? reference, out Customization target)
    {
        target = null;
        return reference.HasValue && reference.Value.TryResolve(out target);
    }

    private static void Broadcast<T>(NetworkServer server, T packet, IPlayer sender) where T : class, MPAPI.Interfaces.Packets.ISerializablePacket, new()
    {
        ITransportPeer excludePeer = (sender as ServerPlayerWrapper)?.Peer;
        server.SendExternalSerializablePacketToAll(packet, true, excludePeer, excludeSelf: true);
    }

    private static bool Finite(Vector3 value) =>
        !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
        !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
        !float.IsNaN(value.z) && !float.IsInfinity(value.z);
}
