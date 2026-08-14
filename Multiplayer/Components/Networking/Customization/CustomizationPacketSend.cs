using LiteNetLib;
using MPAPI.Interfaces.Packets;
using Multiplayer.API;
using Multiplayer.Networking.Managers;
using Multiplayer.Networking.Managers.Server;
using Multiplayer.Networking.TransportLayers;
using System.Reflection;

namespace Multiplayer.Components.Networking.Customization;

internal static class CustomizationPacketSend
{
    private static readonly MethodInfo SendSerializable = typeof(NetworkManager).GetMethod(
        "SendNetSerializablePacket", BindingFlags.Instance | BindingFlags.NonPublic);

    public static bool SendJoinState<T>(NetworkServer server, ITransportPeer peer, T packet)
        where T : class, ISerializablePacket, new()
    {
        if (server == null || peer == null || packet == null || SendSerializable == null)
            return false;

        var wrapper = new ExternalSerializablePacketWrapper<T> { Packet = packet };
        SendSerializable.MakeGenericMethod(typeof(ExternalSerializablePacketWrapper<T>)).Invoke(
            server, new object[] { peer, wrapper, DeliveryMethod.ReliableOrdered });
        return true;
    }
}
