using DV.Customization.Gadgets;
using MPAPI.Interfaces;
using Multiplayer.API;
using Multiplayer.Components.Networking.World;
using Multiplayer.Networking.Data;
using Multiplayer.Networking.Data.Customization;
using Multiplayer.Networking.Managers.Client;
using Multiplayer.Networking.Managers.Server;
using Multiplayer.Networking.TransportLayers;
using System.Collections.Generic;
using System.Linq;

namespace Multiplayer.Components.Networking.Customization;

internal static class GadgetWireSync
{
    public static void RegisterClient(NetworkClient client)
    {
        client.RegisterExternalSerializablePacket<GadgetWirePacket>(ApplyWire);
        client.RegisterExternalSerializablePacket<GadgetUnwirePacket>(ApplyUnwire);
    }

    public static void RegisterServer(NetworkServer server)
    {
        server.RegisterExternalSerializablePacket<GadgetWirePacket>((packet, sender) => OnServerWire(server, packet, sender));
        server.RegisterExternalSerializablePacket<GadgetUnwirePacket>((packet, sender) => OnServerUnwire(server, packet, sender));
    }

    public static void AppendSnapshot(ClientboundCustomizationStatePacket packet)
    {
        HashSet<string> seen = new();
        List<GadgetWiringModule.WireLinkPort> links = new();

        foreach (NetworkedItem item in NetworkedItem.GetAll().ToArray())
        {
            GadgetBase gadget = item?.Item?.GetComponent<GadgetItem>()?.Gadget;
            if (gadget == null || !gadget.IsLinked)
                continue;

            for (int portIndex = 0; portIndex < gadget.WireLinkPorts.Count; portIndex++)
            {
                GadgetWiringModule.WireLinkPort port = gadget.WireLinkPorts[portIndex];
                links.Clear();
                port.GetLinks(links);
                foreach (GadgetWiringModule.WireLinkPort other in links)
                {
                    if (!TryDescribe(port, out ushort aId, out int aIndex) || !TryDescribe(other, out ushort bId, out int bIndex))
                        continue;

                    string key = aId < bId || aId == bId && aIndex <= bIndex
                        ? $"{aId}:{aIndex}-{bId}:{bIndex}"
                        : $"{bId}:{bIndex}-{aId}:{aIndex}";
                    if (!seen.Add(key))
                        continue;

                    packet.Wires.Add(new GadgetWireState
                    {
                        GadgetAItemNetId = aId,
                        PortAIndex = aIndex,
                        GadgetBItemNetId = bId,
                        PortBIndex = bIndex,
                    });
                }
            }
        }
    }

    public static void ApplySnapshot(ClientboundCustomizationStatePacket packet)
    {
        foreach (NetworkedItem item in NetworkedItem.GetAll().ToArray())
        {
            GadgetBase gadget = item?.Item?.GetComponent<GadgetItem>()?.Gadget;
            if (gadget == null)
                continue;
            foreach (GadgetWiringModule.WireLinkPort port in gadget.WireLinkPorts)
                GadgetWiringModule.WireLinkPort.Unwire(port);
        }

        foreach (GadgetWireState state in packet.Wires)
        {
            if (state == null)
                continue;
            ApplyWire(new GadgetWirePacket
            {
                GadgetAItemNetId = state.GadgetAItemNetId,
                PortAIndex = state.PortAIndex,
                GadgetBItemNetId = state.GadgetBItemNetId,
                PortBIndex = state.PortBIndex,
            });
        }
    }

    public static bool TryDescribe(GadgetWiringModule.WireLinkPort port, out ushort itemNetId, out int portIndex)
    {
        itemNetId = 0;
        portIndex = -1;
        GadgetBase owner = port?.owner;
        if (owner?.GadgetItem?.Item == null || !NetworkedItem.TryGetNetworkedItem(owner.GadgetItem.Item, out NetworkedItem item) || item.NetId == 0)
            return false;
        portIndex = owner.WireLinkPorts.IndexOf(port);
        if (portIndex < 0)
            return false;
        itemNetId = item.NetId;
        return true;
    }

    private static void OnServerWire(NetworkServer server, GadgetWirePacket packet, IPlayer sender)
    {
        if (!TryResolve(packet?.GadgetAItemNetId ?? 0, packet?.PortAIndex ?? -1, out var a) ||
            !TryResolve(packet.GadgetBItemNetId, packet.PortBIndex, out var b) ||
            !GadgetWiringModule.WireLinkPort.ReadyToWire(a, b))
            return;
        using (CustomizationSyncScope.Remote())
            GadgetWiringModule.WireLinkPort.Wire(a, b);
        Broadcast(server, packet, sender);
    }

    private static void OnServerUnwire(NetworkServer server, GadgetUnwirePacket packet, IPlayer sender)
    {
        if (!TryResolve(packet?.GadgetAItemNetId ?? 0, packet?.PortAIndex ?? -1, out var a) ||
            !TryResolve(packet.GadgetBItemNetId, packet.PortBIndex, out var b) ||
            !GadgetWiringModule.WireLinkPort.AreWired(a, b))
            return;
        using (CustomizationSyncScope.Remote())
            GadgetWiringModule.WireLinkPort.Unwire(a, b);
        Broadcast(server, packet, sender);
    }

    private static void ApplyWire(GadgetWirePacket packet)
    {
        if (!TryResolve(packet?.GadgetAItemNetId ?? 0, packet?.PortAIndex ?? -1, out var a) ||
            !TryResolve(packet.GadgetBItemNetId, packet.PortBIndex, out var b))
            return;
        using (CustomizationSyncScope.Remote())
        {
            if (!GadgetWiringModule.WireLinkPort.AreWired(a, b))
                GadgetWiringModule.WireLinkPort.Wire(a, b);
        }
    }

    private static void ApplyUnwire(GadgetUnwirePacket packet)
    {
        if (!TryResolve(packet?.GadgetAItemNetId ?? 0, packet?.PortAIndex ?? -1, out var a) ||
            !TryResolve(packet.GadgetBItemNetId, packet.PortBIndex, out var b))
            return;
        using (CustomizationSyncScope.Remote())
            GadgetWiringModule.WireLinkPort.Unwire(a, b);
    }

    private static bool TryResolve(ushort itemNetId, int portIndex, out GadgetWiringModule.WireLinkPort port)
    {
        port = null;
        if (portIndex < 0 || !GadgetStructuralSync.TryGet(itemNetId, out _, out _, out GadgetBase gadget) || portIndex >= gadget.WireLinkPorts.Count)
            return false;
        port = gadget.WireLinkPorts[portIndex];
        return port != null;
    }

    private static void Broadcast<T>(NetworkServer server, T packet, IPlayer sender) where T : class, MPAPI.Interfaces.Packets.ISerializablePacket, new()
    {
        ITransportPeer excludePeer = (sender as ServerPlayerWrapper)?.Peer;
        foreach (ServerPlayer player in server.ServerPlayers)
        {
            if (player.Peer == server.SelfPeer || player.Peer == excludePeer || player.LoadingState < PlayerLoadingState.ReadyForCustomizers)
                continue;
            CustomizationPacketSend.SendJoinState(server, player.Peer, packet);
        }
    }
}
