using DV.Customization;
using DV.Customization.Gadgets;
using MPAPI.Interfaces;
using MPAPI.Interfaces.Packets;
using Multiplayer.API;
using Multiplayer.Components.Networking.World;
using Multiplayer.Networking.Data;
using Multiplayer.Networking.Data.Customization;
using Multiplayer.Networking.Data.Items;
using Multiplayer.Networking.Managers.Client;
using Multiplayer.Networking.Managers.Server;
using Multiplayer.Networking.TransportLayers;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Multiplayer.Components.Networking.Customization;

internal static class GadgetStructuralSync
{
    private const int DependencyWaitFrames = 120;

    public static void RegisterClient(NetworkClient client)
    {
        client.RegisterExternalSerializablePacket<GadgetPlacePacket>(ApplyPlace);
        client.RegisterExternalSerializablePacket<GadgetRemovePacket>(ApplyRemove);
    }

    public static void RegisterServer(NetworkServer server)
    {
        server.RegisterExternalSerializablePacket<GadgetPlacePacket>((packet, sender) => OnServerPlace(server, packet, sender));
        server.RegisterExternalSerializablePacket<GadgetRemovePacket>((packet, sender) => OnServerRemove(server, packet, sender));
    }

    public static void SendObserved<T>(T packet) where T : class, ISerializablePacket, new()
    {
        NetworkLifecycle lifecycle = NetworkLifecycle.Instance;
        if (lifecycle?.Client?.IsRunning != true || lifecycle.Server?.IsSinglePlayer == true)
            return;

        if (lifecycle.IsHost())
        {
            foreach (ServerPlayer player in lifecycle.Server.ServerPlayers)
            {
                if (player.Peer == lifecycle.Server.SelfPeer || player.LoadingState < PlayerLoadingState.ReadyForCustomizers)
                    continue;
                CustomizationPacketSend.SendJoinState(lifecycle.Server, player.Peer, packet);
            }
        }
        else
        {
            lifecycle.Client.SendExternalSerializablePacketToServer(packet, true);
        }
    }

    public static bool TryGet(ushort itemNetId, out NetworkedItem item, out GadgetItem gadgetItem, out GadgetBase gadget)
    {
        gadgetItem = null;
        gadget = null;
        if (!NetworkedItem.TryGet(itemNetId, out item) || item?.Item == null)
            return false;

        gadgetItem = item.Item.GetComponent<GadgetItem>();
        gadget = gadgetItem?.Gadget;
        return gadgetItem != null && gadget != null;
    }

    public static void ClearServerOwnership(ushort itemNetId)
    {
        NetworkServer server = NetworkLifecycle.Instance?.Server;
        if (server == null)
            return;

        foreach (var player in server.ServerPlayers)
        {
            if (player.OwnsItem(itemNetId))
                player.RemoveOwnedItem(itemNetId);
        }
    }

    private static void OnServerPlace(NetworkServer server, GadgetPlacePacket packet, IPlayer sender)
    {
        if (packet == null || !IsFinite(packet.LocalPosition) || !IsFinite(packet.LocalRotation) ||
            !packet.Target.TryResolve(out Customization target) ||
            !TryGet(packet.GadgetItemNetId, out NetworkedItem item, out GadgetItem gadgetItem, out GadgetBase gadget) || gadget.IsLinked)
            return;

        using (CustomizationSyncScope.Remote(rootAction: true))
        {
            GadgetItem.Place(target, packet.LocalPosition, packet.LocalRotation, gadgetItem, null);
            CustomizationSnapshotSync.SetGlassState(gadget, packet.IsOnGlass);
        }

        ClearServerOwnership(packet.GadgetItemNetId);
        BroadcastPlace(server, packet, sender, item, gadget);
    }

    private static void OnServerRemove(NetworkServer server, GadgetRemovePacket packet, IPlayer sender)
    {
        if (packet == null || !TryGet(packet.GadgetItemNetId, out _, out _, out GadgetBase gadget) || !gadget.IsLinked)
            return;

        using (CustomizationSyncScope.Remote(rootAction: true))
            gadget.Remove(packet.ReparentToTrainCar);

        Broadcast(server, packet, sender);
    }

    private static void ApplyPlace(GadgetPlacePacket packet)
    {
        if (packet == null || !packet.Target.TryResolve(out Customization target))
            return;

        if (!TryGet(packet.GadgetItemNetId, out _, out GadgetItem gadgetItem, out GadgetBase gadget))
        {
            NetworkLifecycle.Instance.StartCoroutine(ApplyPlaceWhenReady(packet));
            return;
        }

        ApplyPlaceResolved(packet, target, gadgetItem, gadget);
    }

    private static IEnumerator ApplyPlaceWhenReady(GadgetPlacePacket packet)
    {
        for (int frame = 0; frame < DependencyWaitFrames; frame++)
        {
            if (packet.Target.TryResolve(out Customization target) &&
                TryGet(packet.GadgetItemNetId, out _, out GadgetItem gadgetItem, out GadgetBase gadget))
            {
                ApplyPlaceResolved(packet, target, gadgetItem, gadget);
                yield break;
            }
            yield return null;
        }

        Multiplayer.LogWarning($"Customization placement dependency did not resolve for gadget item {packet.GadgetItemNetId}");
    }

    private static void ApplyPlaceResolved(GadgetPlacePacket packet, Customization target, GadgetItem gadgetItem, GadgetBase gadget)
    {
        using (CustomizationSyncScope.Remote(rootAction: true))
        {
            if (!gadget.IsLinked)
                GadgetItem.Place(target, packet.LocalPosition, packet.LocalRotation, gadgetItem, null);
            else if (gadget.Custom == target)
            {
                gadget.transform.SetParent(target.GetParentingTransform(), false);
                gadget.transform.localPosition = packet.LocalPosition;
                gadget.transform.localRotation = packet.LocalRotation;
            }

            CustomizationSnapshotSync.SetGlassState(gadget, packet.IsOnGlass);
        }
    }

    private static void ApplyRemove(GadgetRemovePacket packet)
    {
        if (packet == null || !TryGet(packet.GadgetItemNetId, out _, out _, out GadgetBase gadget) || !gadget.IsLinked)
            return;

        using (CustomizationSyncScope.Remote(rootAction: true))
            gadget.Remove(packet.ReparentToTrainCar);
    }

    private static void BroadcastPlace(NetworkServer server, GadgetPlacePacket packet, IPlayer sender, NetworkedItem item, GadgetBase gadget)
    {
        ITransportPeer excludePeer = (sender as ServerPlayerWrapper)?.Peer;
        uint tick = NetworkLifecycle.Instance.Tick;
        foreach (ServerPlayer recipient in server.ServerPlayers)
        {
            if (recipient.Peer == server.SelfPeer || recipient.Peer == excludePeer || recipient.LoadingState < PlayerLoadingState.ReadyForCustomizers)
                continue;

            if (!recipient.KnownItems.ContainsKey(item))
            {
                ItemUpdateData create = item.CreateUpdateData(ItemUpdateData.ItemUpdateType.Create);
                if (create != null)
                {
                    create.ItemState = ItemState.Dropped;
                    create.ItemPosition = gadget.transform.position - WorldMover.currentMove;
                    create.ItemRotation = gadget.transform.rotation;
                    server.SendItemsChangePacket(new List<ItemUpdateData> { create }, recipient);
                    recipient.KnownItems[item] = tick;
                }
            }

            CustomizationPacketSend.SendJoinState(server, recipient.Peer, packet);
        }
    }

    private static void Broadcast<T>(NetworkServer server, T packet, IPlayer sender) where T : class, ISerializablePacket, new()
    {
        ITransportPeer excludePeer = (sender as ServerPlayerWrapper)?.Peer;
        foreach (ServerPlayer recipient in server.ServerPlayers)
        {
            if (recipient.Peer == server.SelfPeer || recipient.Peer == excludePeer || recipient.LoadingState < PlayerLoadingState.ReadyForCustomizers)
                continue;
            CustomizationPacketSend.SendJoinState(server, recipient.Peer, packet);
        }
    }

    private static bool IsFinite(Vector3 value) =>
        !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
        !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
        !float.IsNaN(value.z) && !float.IsInfinity(value.z);

    private static bool IsFinite(Quaternion value) =>
        !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
        !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
        !float.IsNaN(value.z) && !float.IsInfinity(value.z) &&
        !float.IsNaN(value.w) && !float.IsInfinity(value.w) &&
        value.x * value.x + value.y * value.y + value.z * value.z + value.w * value.w > 0.0001f;
}
