using DV.Customization.Gadgets;
using DV.Customization.Gadgets.Implementations;
using HarmonyLib;
using MPAPI.Interfaces;
using Multiplayer.API;
using Multiplayer.Components.Networking.World;
using Multiplayer.Networking.Data;
using Multiplayer.Networking.Data.Customization;
using Multiplayer.Networking.Managers.Client;
using Multiplayer.Networking.Managers.Server;
using Multiplayer.Networking.TransportLayers;
using System.Collections;
using System.Reflection;
using UnityEngine;

namespace Multiplayer.Components.Networking.Customization;

internal static class RoadrunnerSync
{
    private const int DependencyWaitFrames = 120;
    private static readonly FieldInfo CountupField = AccessTools.Field(typeof(GadgetRoadrunner), "countup");
    private static readonly FieldInfo LastDirectionField = AccessTools.Field(typeof(GadgetRoadrunner), "lastDirectionReversed");
    private static readonly PropertyInfo HasCompletedProperty = AccessTools.Property(typeof(GadgetRoadrunner), nameof(GadgetRoadrunner.HasCompleted));

    private static int pendingSnapshotApplies;
    private static int nativeUpdateDepth;

    public static bool HasPendingSnapshot => pendingSnapshotApplies > 0;
    public static bool IsInsideNativeUpdate => nativeUpdateDepth > 0;

    public static void EnterNativeUpdate() => nativeUpdateDepth++;
    public static void ExitNativeUpdate() => nativeUpdateDepth = Mathf.Max(0, nativeUpdateDepth - 1);

    public static void RegisterClient(NetworkClient client)
    {
        client.RegisterExternalSerializablePacket<RoadrunnerSyncPacket>(packet =>
        {
            if (packet == null)
                return;
            if (packet.Action == RoadrunnerSyncAction.Snapshot)
            {
                pendingSnapshotApplies++;
                NetworkLifecycle.Instance.StartCoroutine(ApplySnapshotWhenReady(packet));
                return;
            }
            ApplyAction(packet);
        });
    }

    public static void RegisterServer(NetworkServer server)
    {
        server.RegisterExternalSerializablePacket<RoadrunnerSyncPacket>((packet, sender) =>
        {
            if (packet == null || packet.Action == RoadrunnerSyncAction.Snapshot ||
                !TryGet(packet.GadgetItemNetId, out GadgetRoadrunner runner))
                return;

            using (CustomizationSyncScope.Remote())
            {
                if (packet.Action == RoadrunnerSyncAction.Start)
                    runner.StartMeasure();
                else if (packet.Action == RoadrunnerSyncAction.Acknowledge)
                    runner.Acknowledge();
                else
                    return;
            }

            Broadcast(server, packet, sender);
        });
    }

    public static void SendObserved(GadgetRoadrunner runner, RoadrunnerSyncAction action)
    {
        if (CustomizationSyncScope.IsApplyingRemote || IsInsideNativeUpdate ||
            !TryGetNetId(runner, out ushort netId))
            return;

        RoadrunnerSyncPacket packet = new()
        {
            GadgetItemNetId = netId,
            Action = action,
        };

        if (NetworkLifecycle.Instance.IsHost())
            Broadcast(NetworkLifecycle.Instance.Server, packet, null);
        else if (NetworkLifecycle.Instance.Client?.IsRunning == true)
            NetworkLifecycle.Instance.Client.SendExternalSerializablePacketToServer(packet, true);
    }

    public static void SendJoinState(NetworkServer server, ServerPlayer player)
    {
        if (server == null || player == null)
            return;

        foreach (NetworkedItem item in NetworkedItem.GetAll())
        {
            GadgetRoadrunner runner = item?.Item?.GetComponent<GadgetItem>()?.Gadget as GadgetRoadrunner;
            if (runner == null || !runner.IsLinked || item.NetId == 0)
                continue;

            CustomizationPacketSend.SendJoinState(server, player.Peer, new RoadrunnerSyncPacket
            {
                GadgetItemNetId = item.NetId,
                Action = RoadrunnerSyncAction.Snapshot,
                LengthMeters = runner.LengthMeters,
                Countup = runner.Countup,
                HasCompleted = runner.HasCompleted,
            });
        }
    }

    private static IEnumerator ApplySnapshotWhenReady(RoadrunnerSyncPacket packet)
    {
        try
        {
            for (int frame = 0; frame < DependencyWaitFrames; frame++)
            {
                if (TryGet(packet.GadgetItemNetId, out GadgetRoadrunner runner))
                {
                    using (CustomizationSyncScope.Remote())
                    {
                        runner.LengthMeters = Mathf.Clamp(packet.LengthMeters, 0, runner.MaxLength);
                        CountupField?.SetValue(runner, (double)packet.Countup);
                        LastDirectionField?.SetValue(runner, null);
                        HasCompletedProperty?.GetSetMethod(true)?.Invoke(runner, new object[] { packet.HasCompleted });
                    }
                    yield break;
                }
                yield return null;
            }

            Multiplayer.LogWarning($"Roadrunner join dependency did not resolve for gadget item {packet.GadgetItemNetId}");
        }
        finally
        {
            pendingSnapshotApplies = Mathf.Max(0, pendingSnapshotApplies - 1);
        }
    }

    private static void ApplyAction(RoadrunnerSyncPacket packet)
    {
        if (!TryGet(packet.GadgetItemNetId, out GadgetRoadrunner runner))
            return;

        using (CustomizationSyncScope.Remote())
        {
            if (packet.Action == RoadrunnerSyncAction.Start)
                runner.StartMeasure();
            else if (packet.Action == RoadrunnerSyncAction.Acknowledge)
                runner.Acknowledge();
        }
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
        foreach (ServerPlayer recipient in server.ServerPlayers)
        {
            if (recipient.Peer == server.SelfPeer || recipient.Peer == excludePeer ||
                recipient.LoadingState < PlayerLoadingState.ReadyForCustomizers)
                continue;
            server.SendExternalSerializablePacketToPlayer(packet, recipient.Peer, true);
        }
    }
}
