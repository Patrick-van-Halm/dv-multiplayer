using LiteNetLib;
using MPAPI.Interfaces.Packets;
using Multiplayer.API;
using Multiplayer.Networking.Managers;
using Multiplayer.Networking.Managers.Client;
using Multiplayer.Networking.Managers.Server;
using Multiplayer.Networking.TransportLayers;
using System.Reflection;

namespace Multiplayer.Components.Networking.Customization;

internal static class CustomizationPacketSend
{
    private static readonly MethodInfo SendSerializable = typeof(NetworkManager).GetMethod(
        "SendNetSerializablePacket", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly MethodInfo SendSerializableToServer = typeof(NetworkClient).GetMethod(
        "SendNetSerializablePacketToServer", BindingFlags.Instance | BindingFlags.NonPublic);

    public static bool SendJoinState<T>(NetworkServer server, ITransportPeer peer, T packet)
        where T : class, ISerializablePacket, new()
    {
        if (server == null || peer == null || packet == null || SendSerializable == null)
            return false;

        ExternalSerializablePacketWrapper<T> wrapper = new() { Packet = packet };
        SendSerializable.MakeGenericMethod(typeof(ExternalSerializablePacketWrapper<T>)).Invoke(
            server, new object[] { peer, wrapper, DeliveryMethod.ReliableOrdered });
        return true;
    }

    public static bool SendToServer<T>(NetworkClient client, T packet)
        where T : class, ISerializablePacket, new()
    {
        if (client == null || packet == null || SendSerializableToServer == null)
            return false;

        ExternalSerializablePacketWrapper<T> wrapper = new() { Packet = packet };
        SendSerializableToServer.MakeGenericMethod(typeof(ExternalSerializablePacketWrapper<T>)).Invoke(
            client, new object[] { wrapper, DeliveryMethod.ReliableOrdered });
        return true;
    }
}
