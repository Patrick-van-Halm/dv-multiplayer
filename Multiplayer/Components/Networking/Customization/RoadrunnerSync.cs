using DV.Customization.Gadgets;
using DV.Customization.Gadgets.Implementations;
using MPAPI.Interfaces;
using Multiplayer.API;
using Multiplayer.Components.Networking.World;
using Multiplayer.Networking.Data.Customization;
using Multiplayer.Networking.Managers.Client;
using Multiplayer.Networking.Managers.Server;
using Multiplayer.Networking.TransportLayers;
using UnityEngine;

namespace Multiplayer.Components.Networking.Customization;

internal static class RoadrunnerSync
{
    private static int nativeUpdateDepth;

    public static bool IsInsideNativeUpdate => nativeUpdateDepth > 0;

    public static void EnterNativeUpdate() => nativeUpdateDepth++;
    public static void ExitNativeUpdate() => nativeUpdateDepth = Mathf.Max(0, nativeUpdateDepth - 1);

    public static void RegisterClient(NetworkClient client)
    {
        client.RegisterExternalSerializablePacket<RoadrunnerSyncPacket>(ApplyAction);
    }

    public static void RegisterServer(NetworkServer server)
    {
        server.RegisterExternalSerializablePacket<RoadrunnerSyncPacket>((packet, sender) =>
        {
            if (packet == null || !TryGet(packet.GadgetItemNetId, out GadgetRoadrunner runner))
                return;

            using (CustomizationSyncScope.Remote())
                ApplyAction(runner, packet.Action);

            Broadcast(server, packet, sender);
        });
    }

    public static void SendObserved(GadgetRoadrunner runner, RoadrunnerSyncAction action)
    {
        if (CustomizationSyncScope.IsApplyingRemote || IsInsideNativeUpdate || !TryGetNetId(runner, out ushort netId))
            return;

        RoadrunnerSyncPacket packet = new() { GadgetItemNetId = netId, Action = action };
        if (NetworkLifecycle.Instance.IsHost())
            Broadcast(NetworkLifecycle.Instance.Server, packet, null);
        else if (NetworkLifecycle.Instance.Client?.IsRunning == true)
            NetworkLifecycle.Instance.Client.SendExternalSerializablePacketToServer(packet, true);
    }

    private static void ApplyAction(RoadrunnerSyncPacket packet)
    {
        if (packet != null && TryGet(packet.GadgetItemNetId, out GadgetRoadrunner runner))
        {
            using (CustomizationSyncScope.Remote())
                ApplyAction(runner, packet.Action);
        }
    }

    private static void ApplyAction(GadgetRoadrunner runner, RoadrunnerSyncAction action)
    {
        if (action == RoadrunnerSyncAction.Start)
            runner.StartMeasure();
        else if (action == RoadrunnerSyncAction.Acknowledge)
            runner.Acknowledge();
    }

    private static bool TryGetNetId(GadgetRoadrunner runner, out ushort netId)
    {
        netId = 0;
        if (runner?.GadgetItem?.Item == null ||
            !NetworkedItem.TryGetNetworkedItem(runner.GadgetItem.Item, out NetworkedItem item) || item.NetId == 0)
            return false;
        netId = item.NetId;
        return true;
    }

    private static bool TryGet(ushort itemNetId, out GadgetRoadrunner runner)
    {
        runner = null;
        if (!GadgetStructuralSync.TryGet(itemNetId, out _, out _, out GadgetBase gadget))
            return false;
        runner = gadget as GadgetRoadrunner ?? gadget.GetComponentInChildren<GadgetRoadrunner>(true);
        return runner != null;
    }

    private static void Broadcast(NetworkServer server, RoadrunnerSyncPacket packet, IPlayer sender)
    {
        if (server == null || packet == null)
            return;
        ITransportPeer excludePeer = (sender as ServerPlayerWrapper)?.Peer;
        server.SendExternalSerializablePacketToAll(packet, true, excludePeer, excludeSelf: true);
    }
}
