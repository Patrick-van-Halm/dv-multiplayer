using DV.Customization.Gadgets;
using DV.Customization.Gadgets.Implementations;
using MPAPI.Interfaces;
using Multiplayer.API;
using Multiplayer.Components.Networking.World;
using Multiplayer.Networking.Data;
using Multiplayer.Networking.Data.Customization;
using Multiplayer.Networking.Managers.Client;
using Multiplayer.Networking.Managers.Server;
using Multiplayer.Networking.TransportLayers;
using UnityEngine;

namespace Multiplayer.Components.Networking.Customization;

internal static class RoadrunnerActionSync
{
    private static int nativeUpdateDepth;

    public static bool IsInsideNativeUpdate => nativeUpdateDepth > 0;

    public static void EnterNativeUpdate() => nativeUpdateDepth++;
    public static void ExitNativeUpdate() => nativeUpdateDepth = Mathf.Max(0, nativeUpdateDepth - 1);

    public static void RegisterClient(NetworkClient client)
    {
        client.RegisterExternalSerializablePacket<RoadrunnerActionPacket>(Apply);
    }

    public static void RegisterServer(NetworkServer server)
    {
        server.RegisterExternalSerializablePacket<RoadrunnerActionPacket>((packet, sender) =>
        {
            if (packet == null || !TryGet(packet.GadgetItemNetId, out GadgetRoadrunner runner))
                return;

            using (CustomizationSyncScope.Remote())
                Apply(runner, packet.Action);

            ITransportPeer excludePeer = (sender as ServerPlayerWrapper)?.Peer;
            foreach (ServerPlayer player in server.ServerPlayers)
            {
                if (player.Peer == server.SelfPeer || player.Peer == excludePeer || player.LoadingState < PlayerLoadingState.ReadyForCustomizers)
                    continue;
                CustomizationPacketSend.SendJoinState(server, player.Peer, packet);
            }
        });
    }

    public static void SendObserved(GadgetRoadrunner runner, RoadrunnerAction action)
    {
        if (CustomizationSyncScope.IsApplyingRemote || IsInsideNativeUpdate || !TryGetNetId(runner, out ushort netId))
            return;

        RoadrunnerActionPacket packet = new() { GadgetItemNetId = netId, Action = action };
        if (NetworkLifecycle.Instance.IsHost())
        {
            foreach (ServerPlayer player in NetworkLifecycle.Instance.Server.ServerPlayers)
            {
                if (player.Peer == NetworkLifecycle.Instance.Server.SelfPeer || player.LoadingState < PlayerLoadingState.ReadyForCustomizers)
                    continue;
                CustomizationPacketSend.SendJoinState(NetworkLifecycle.Instance.Server, player.Peer, packet);
            }
        }
        else
        {
            NetworkLifecycle.Instance.Client.SendExternalSerializablePacketToServer(packet, true);
        }
    }

    private static void Apply(RoadrunnerActionPacket packet)
    {
        if (packet == null || !TryGet(packet.GadgetItemNetId, out GadgetRoadrunner runner))
            return;

        using (CustomizationSyncScope.Remote())
            Apply(runner, packet.Action);
    }

    private static void Apply(GadgetRoadrunner runner, RoadrunnerAction action)
    {
        if (action == RoadrunnerAction.Start)
            runner.StartMeasure();
        else if (action == RoadrunnerAction.Acknowledge)
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
}
