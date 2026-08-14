using DV.Customization.Gadgets;
using DV.Customization.Gadgets.Implementations;
using Multiplayer.Components.Networking.World;
using Multiplayer.Networking.Data.Customization;
using Multiplayer.Networking.Data.Items;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Multiplayer.Components.Networking.Customization;

internal static class CustomizationToolJoinSync
{
    private static bool loaded;

    public static bool Loaded => loaded;

    public static void BeginJoin() => loaded = false;

    public static ClientboundCustomizationToolStatePacket Build()
    {
        ClientboundCustomizationToolStatePacket packet = new();
        HashSet<ushort> added = new();

        foreach (NetworkedItem item in NetworkedItem.GetAll().ToArray())
        {
            if (item?.Item == null || item.NetId == 0)
                continue;

            DuctTape tape = item.Item.GetComponent<DuctTape>();
            GadgetSolderingTool tool = item.Item.GetComponent<GadgetSolderingTool>();
            if (tape == null && tool == null)
                continue;

            AddCurrentItem(packet, added, item);

            if (tool != null)
            {
                uint spoolId = SolderingMagazineSync.GetSpoolNetId(tool);
                if (spoolId != 0 && spoolId <= ushort.MaxValue && NetworkedItem.TryGet((ushort)spoolId, out NetworkedItem spool))
                    AddCurrentItem(packet, added, spool);

                Multiplayer.LogDebug(() => $"[Customization] Soldering tool {item.NetId} remaining={tool.remainingUnits} spool={spoolId}");
            }
            else
            {
                Multiplayer.LogDebug(() => $"[Customization] Duct tape {item.NetId} usesLeft={tape.usesLeft}");
            }
        }

        ClientboundCustomizationStatePacket snapState = new();
        GadgetSnapSync.AppendSnapshot(snapState);
        foreach (GadgetSnapState snap in snapState.Snaps)
        {
            if (snap == null || !NetworkedItem.TryGet(snap.AttachedItemNetId, out NetworkedItem attached))
                continue;

            GadgetBase attachedGadget = attached?.Item?.GetComponent<GadgetItem>()?.Gadget;
            if (attachedGadget?.IsLinked == true)
                continue;

            AddCurrentItem(packet, added, attached);
        }

        return packet;
    }

    public static void Receive(ClientboundCustomizationToolStatePacket packet)
    {
        if (NetworkLifecycle.Instance.IsHost())
        {
            loaded = true;
            return;
        }

        packet ??= new ClientboundCustomizationToolStatePacket();
        if (packet.Items.Count == 0)
        {
            loaded = true;
            return;
        }

        NetworkedItemManager.Instance.ReceiveSnapshots(packet.Items, null);
        NetworkLifecycle.Instance.StartCoroutine(WaitForItems(packet));
    }

    private static IEnumerator WaitForItems(ClientboundCustomizationToolStatePacket packet)
    {
        while (!ItemsAndRelationshipsReady(packet))
            yield return null;

        loaded = true;
    }

    private static bool ItemsAndRelationshipsReady(ClientboundCustomizationToolStatePacket packet)
    {
        foreach (ItemUpdateData itemState in packet.Items)
        {
            if (itemState == null || itemState.ItemNetId == 0)
                continue;
            if (!NetworkedItem.TryGet(itemState.ItemNetId, out NetworkedItem networkedItem) || networkedItem?.Item == null)
                return false;

            if (itemState.States == null || !itemState.States.TryGetValue("soldering.spool", out object spoolValue) ||
                spoolValue is not uint expectedSpool)
                continue;

            GadgetSolderingTool tool = networkedItem.Item.GetComponent<GadgetSolderingTool>();
            if (tool != null && SolderingMagazineSync.GetSpoolNetId(tool) != expectedSpool)
                return false;
        }

        return true;
    }

    private static void AddCurrentItem(ClientboundCustomizationToolStatePacket packet, HashSet<ushort> added, NetworkedItem item)
    {
        if (item == null || item.NetId == 0 || !added.Add(item.NetId))
            return;

        ItemUpdateData snapshot = item.CreateUpdateData(ItemUpdateData.ItemUpdateType.FullSync);
        if (snapshot == null)
            return;

        snapshot.UpdateType = ItemUpdateData.ItemUpdateType.Create;
        packet.Items.Add(snapshot);
    }
}
