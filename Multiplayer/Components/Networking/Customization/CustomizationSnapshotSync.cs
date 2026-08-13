using DV.Customization;
using DV.Customization.Gadgets;
using HarmonyLib;
using Multiplayer.Components.Networking.World;
using Multiplayer.Networking.Data.Customization;
using Multiplayer.Networking.Data.Items;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace Multiplayer.Components.Networking.Customization;

internal static class CustomizationSnapshotSync
{
    private static readonly PropertyInfo GadgetIsOnGlassProperty = AccessTools.Property(typeof(GadgetBase), nameof(GadgetBase.IsOnGlass));
    private static bool customizerStateLoaded;
    private static bool snapshotApplying;

    public static bool CustomizerStateLoaded => customizerStateLoaded;

    public static void BeginJoin()
    {
        customizerStateLoaded = false;
        snapshotApplying = false;
    }

    public static void Receive(ClientboundCustomizationStatePacket packet)
    {
        if (NetworkLifecycle.Instance.IsHost())
        {
            customizerStateLoaded = true;
            return;
        }
        NetworkLifecycle.Instance.StartCoroutine(Apply(packet));
    }

    public static ClientboundCustomizationStatePacket Build()
    {
        ClientboundCustomizationStatePacket packet = new();
        foreach (NetworkedItem networkedItem in NetworkedItem.GetAll().ToArray())
        {
            if (networkedItem == null || networkedItem.NetId == 0 || networkedItem.Item == null)
                continue;
            GadgetItem gadgetItem = networkedItem.Item.GetComponent<GadgetItem>();
            GadgetBase gadget = gadgetItem?.Gadget;
            if (gadget == null || !gadget.IsLinked || gadget.Custom == null || !CustomizationRef.TryFrom(gadget.Custom, out CustomizationRef target))
                continue;
            ItemUpdateData itemState = networkedItem.CreateUpdateData(ItemUpdateData.ItemUpdateType.FullSync);
            if (itemState == null)
                continue;
            itemState.UpdateType = ItemUpdateData.ItemUpdateType.Create;
            itemState.ItemState = ItemState.Dropped;
            itemState.ItemPosition = gadget.transform.position - WorldMover.currentMove;
            itemState.ItemRotation = gadget.transform.rotation;
            packet.Gadgets.Add(new GadgetPlacementState
            {
                Item = itemState,
                Target = target,
                LocalPosition = gadget.transform.localPosition,
                LocalRotation = gadget.transform.localRotation,
                IsOnGlass = gadget.IsOnGlass,
            });
        }

        GadgetMountSync.AppendSnapshot(packet);
        GadgetWireSync.AppendSnapshot(packet);

        foreach (Customization customization in RuntimeCustomizations())
        {
            if (!CustomizationRef.TryFrom(customization, out CustomizationRef target))
                continue;
            foreach (Collider hole in customization.Holes)
            {
                if (hole == null)
                    continue;
                packet.Holes.Add(new CustomizationHoleState
                {
                    Target = target,
                    LocalPosition = hole.transform.localPosition,
                    LocalNormal = hole.transform.localRotation * Vector3.forward,
                });
            }
        }
        return packet;
    }

    public static void SetGlassState(GadgetBase gadget, bool value)
    {
        MethodInfo setter = GadgetIsOnGlassProperty?.GetSetMethod(true);
        setter?.Invoke(gadget, new object[] { value });
    }

    private static IEnumerator Apply(ClientboundCustomizationStatePacket packet)
    {
        if (snapshotApplying)
            yield break;
        snapshotApplying = true;
        customizerStateLoaded = false;
        try
        {
            packet ??= new ClientboundCustomizationStatePacket();
            foreach (GadgetPlacementState placement in packet.Gadgets)
            {
                if (placement?.Item == null || placement.Item.ItemNetId == 0)
                    continue;
                ItemUpdateData create = CloneWithoutStates(placement.Item);
                create.UpdateType = ItemUpdateData.ItemUpdateType.Create;
                create.ItemState = ItemState.Dropped;
                NetworkedItemManager.Instance.ReceiveSnapshots(new List<ItemUpdateData> { create }, null);
            }

            while (packet.Gadgets.Any(p => p?.Item != null && p.Item.ItemNetId != 0 && !TryGetGadget(p.Item.ItemNetId, out _, out _)))
                yield return null;

            using (CustomizationSyncScope.Remote(rootAction: true))
            {
                foreach (GadgetPlacementState placement in packet.Gadgets)
                {
                    if (placement?.Item == null || !placement.Target.TryResolve(out Customization target) || !TryGetGadget(placement.Item.ItemNetId, out GadgetItem gadgetItem, out GadgetBase gadget))
                        continue;
                    if (!gadget.IsLinked)
                        GadgetItem.Place(target, placement.LocalPosition, placement.LocalRotation, gadgetItem, null);
                    else if (gadget.Custom == target)
                    {
                        gadget.transform.SetParent(target.GetParentingTransform(), false);
                        gadget.transform.localPosition = placement.LocalPosition;
                        gadget.transform.localRotation = placement.LocalRotation;
                        gadget.gameObject.SetActive(true);
                    }
                    else
                    {
                        gadget.Remove(false);
                        GadgetItem.Place(target, placement.LocalPosition, placement.LocalRotation, gadgetItem, null);
                    }
                    SetGlassState(gadget, placement.IsOnGlass);
                }

                GadgetMountSync.ApplySnapshot(packet);
                GadgetWireSync.ApplySnapshot(packet);

                foreach (Customization customization in RuntimeCustomizations())
                    customization.ClearHoles();
                foreach (CustomizationHoleState hole in packet.Holes)
                {
                    if (hole != null && hole.Target.TryResolve(out Customization target))
                        target.AddHole(hole.LocalPosition, hole.LocalNormal);
                }
            }

            foreach (GadgetPlacementState placement in packet.Gadgets)
            {
                if (placement?.Item?.States == null || placement.Item.States.Count == 0 || !NetworkedItem.TryGet(placement.Item.ItemNetId, out NetworkedItem networkedItem))
                    continue;
                networkedItem.ReceiveSnapshot(new ItemUpdateData
                {
                    UpdateType = ItemUpdateData.ItemUpdateType.ObjectState,
                    ItemNetId = placement.Item.ItemNetId,
                    States = new Dictionary<string, object>(placement.Item.States),
                });
            }
            customizerStateLoaded = true;
        }
        finally
        {
            snapshotApplying = false;
        }
    }

    private static bool TryGetGadget(ushort itemNetId, out GadgetItem gadgetItem, out GadgetBase gadget)
    {
        gadgetItem = null;
        gadget = null;
        if (!NetworkedItem.TryGet(itemNetId, out NetworkedItem item) || item?.Item == null)
            return false;
        gadgetItem = item.Item.GetComponent<GadgetItem>();
        gadget = gadgetItem?.Gadget;
        return gadgetItem != null && gadget != null;
    }

    private static IEnumerable<Customization> RuntimeCustomizations()
    {
        return Resources.FindObjectsOfTypeAll<Customization>().Where(c => c != null && c.gameObject.scene.IsValid());
    }

    private static ItemUpdateData CloneWithoutStates(ItemUpdateData source)
    {
        return new ItemUpdateData
        {
            UpdateType = source.UpdateType,
            ItemNetId = source.ItemNetId,
            PrefabName = source.PrefabName,
            ItemState = source.ItemState,
            ItemPosition = source.ItemPosition,
            ItemRotation = source.ItemRotation,
            ThrowDirection = source.ThrowDirection,
            Player = source.Player,
            CarNetId = source.CarNetId,
            AttachedFront = source.AttachedFront,
        };
    }
}
