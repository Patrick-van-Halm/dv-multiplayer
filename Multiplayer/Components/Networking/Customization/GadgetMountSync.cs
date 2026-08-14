using DV.Customization.Gadgets;
using MPAPI.Interfaces;
using Multiplayer.API;
using Multiplayer.Components.Networking.World;
using Multiplayer.Networking.Data;
using Multiplayer.Networking.Data.Customization;
using Multiplayer.Networking.Managers.Client;
using Multiplayer.Networking.Managers.Server;
using Multiplayer.Networking.TransportLayers;
using System;
using System.Linq;

namespace Multiplayer.Components.Networking.Customization;

internal static class GadgetMountSync
{
    public static void RegisterClient(NetworkClient client)
    {
        client.RegisterExternalSerializablePacket<GadgetMountPacket>(ApplyMount);
        client.RegisterExternalSerializablePacket<GadgetUnmountPacket>(ApplyUnmount);
    }

    public static void RegisterServer(NetworkServer server)
    {
        server.RegisterExternalSerializablePacket<GadgetMountPacket>((packet, sender) => OnServerMount(server, packet, sender));
        server.RegisterExternalSerializablePacket<GadgetUnmountPacket>((packet, sender) => OnServerUnmount(server, packet, sender));
    }

    public static void AppendSnapshot(ClientboundCustomizationStatePacket packet)
    {
        foreach (NetworkedItem networkedItem in NetworkedItem.GetAll().ToArray())
        {
            GadgetBase owner = networkedItem?.Item?.GetComponent<GadgetItem>()?.Gadget;
            if (owner == null || !owner.IsLinked)
                continue;

            Mount[] mounts = owner.GetComponentsInChildren<Mount>(true);
            for (int index = 0; index < mounts.Length; index++)
            {
                GadgetBase mounted = mounts[index].MountedGadget;
                if (mounted?.GadgetItem?.Item == null || !NetworkedItem.TryGetNetworkedItem(mounted.GadgetItem.Item, out NetworkedItem mountedItem) || mountedItem.NetId == 0)
                    continue;

                packet.Mounts.Add(new GadgetMountState
                {
                    MountOwnerItemNetId = networkedItem.NetId,
                    MountedItemNetId = mountedItem.NetId,
                    MountIndex = index,
                });
            }
        }
    }

    public static void ApplySnapshot(ClientboundCustomizationStatePacket packet)
    {
        foreach (NetworkedItem item in NetworkedItem.GetAll().ToArray())
        {
            GadgetBase gadget = item?.Item?.GetComponent<GadgetItem>()?.Gadget;
            if (gadget == null || !gadget.IsLinked)
                continue;
            foreach (Mount mount in gadget.GetComponentsInChildren<Mount>(true))
            {
                if (mount.MountedGadget != null)
                    mount.UnmountGadget();
            }
        }

        foreach (GadgetMountState state in packet.Mounts)
        {
            if (state == null)
                continue;
            ApplyMount(new GadgetMountPacket
            {
                MountOwnerItemNetId = state.MountOwnerItemNetId,
                MountedItemNetId = state.MountedItemNetId,
                MountIndex = state.MountIndex,
            });
        }
    }

    public static bool TryDescribe(Mount mount, out ushort ownerItemNetId, out int mountIndex)
    {
        ownerItemNetId = 0;
        mountIndex = -1;
        GadgetBase owner = mount?.GetComponent<GadgetBase>() ?? mount?.GetComponentInParent<GadgetBase>();
        if (owner?.GadgetItem?.Item == null || !NetworkedItem.TryGetNetworkedItem(owner.GadgetItem.Item, out NetworkedItem ownerItem) || ownerItem.NetId == 0)
            return false;

        mountIndex = Array.IndexOf(owner.GetComponentsInChildren<Mount>(true), mount);
        if (mountIndex < 0)
            return false;

        ownerItemNetId = ownerItem.NetId;
        return true;
    }

    private static void OnServerMount(NetworkServer server, GadgetMountPacket packet, IPlayer sender)
    {
        if (packet == null || !TryResolveMount(packet.MountOwnerItemNetId, packet.MountIndex, out Mount mount) ||
            !GadgetStructuralSync.TryGet(packet.MountedItemNetId, out _, out _, out GadgetBase mounted) ||
            mount.MountedGadget != null || mounted.MountedOn != null || !mount.Accepts(mounted))
            return;

        using (CustomizationSyncScope.Remote())
            mount.MountGadget(mounted);
        Broadcast(server, packet, sender);
    }

    private static void OnServerUnmount(NetworkServer server, GadgetUnmountPacket packet, IPlayer sender)
    {
        if (packet == null || !TryResolveMount(packet.MountOwnerItemNetId, packet.MountIndex, out Mount mount) || mount.MountedGadget == null)
            return;

        using (CustomizationSyncScope.Remote())
            mount.UnmountGadget();
        Broadcast(server, packet, sender);
    }

    private static void ApplyMount(GadgetMountPacket packet)
    {
        if (packet == null || !TryResolveMount(packet.MountOwnerItemNetId, packet.MountIndex, out Mount mount) ||
            !GadgetStructuralSync.TryGet(packet.MountedItemNetId, out _, out _, out GadgetBase mounted))
            return;

        using (CustomizationSyncScope.Remote())
        {
            if (mount.MountedGadget == mounted)
                return;
            if (mount.MountedGadget != null)
                mount.UnmountGadget();
            if (mounted.MountedOn != null)
                mounted.MountedOn.UnmountGadget();
            mount.MountGadget(mounted);
        }
    }

    private static void ApplyUnmount(GadgetUnmountPacket packet)
    {
        if (packet == null || !TryResolveMount(packet.MountOwnerItemNetId, packet.MountIndex, out Mount mount))
            return;
        using (CustomizationSyncScope.Remote())
            mount.UnmountGadget();
    }

    private static bool TryResolveMount(ushort ownerItemNetId, int mountIndex, out Mount mount)
    {
        mount = null;
        if (mountIndex < 0 || !GadgetStructuralSync.TryGet(ownerItemNetId, out _, out _, out GadgetBase owner))
            return false;
        Mount[] mounts = owner.GetComponentsInChildren<Mount>(true);
        if (mountIndex >= mounts.Length)
            return false;
        mount = mounts[mountIndex];
        return mount != null;
    }

    private static void Broadcast<T>(NetworkServer server, T packet, IPlayer sender) where T : class, MPAPI.Interfaces.Packets.ISerializablePacket, new()
    {
        ITransportPeer excludePeer = (sender as ServerPlayerWrapper)?.Peer;
        foreach (ServerPlayer player in server.ServerPlayers)
        {
            if (player.Peer == server.SelfPeer || player.Peer == excludePeer || player.LoadingState < PlayerLoadingState.ReadyForCustomizers)
                continue;
            server.SendExternalSerializablePacketToPlayer(packet, player.Peer, true);
        }
    }
}
