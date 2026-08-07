using DV.InventorySystem;
using DV.Items;
using Multiplayer.Networking.Data;
using Multiplayer.Networking.Data.Items;
using Multiplayer.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Multiplayer.Components.Networking.World;

internal static class NetworkedItemSnapshotValidator
{
    public static bool IsSafeTrackedValuePayload(
        Dictionary<string, object> states)
    {
        if (states == null)
            return true;
        if (states.Count > ItemPacketLimits.MaxTrackedValues)
            return false;

        foreach (KeyValuePair<string, object> state in states)
        {
            if (string.IsNullOrEmpty(state.Key) ||
                state.Key.Length >
                    ItemPacketLimits.MaxTrackedValueKeyLength ||
                state.Value == null ||
                (state.Value is string value &&
                 value.Length >
                    ItemPacketLimits.MaxTrackedStringLength) ||
                (state.Value is float floatValue &&
                 !floatValue.IsFinite()) ||
                (state.Value is double doubleValue &&
                 (double.IsNaN(doubleValue) ||
                  double.IsInfinity(doubleValue))) ||
                (state.Value is Vector3 vector && !vector.IsFinite()) ||
                (state.Value is Quaternion rotation &&
                 !rotation.IsFinite()))
            {
                return false;
            }
        }

        return true;
    }

    public static bool ValidateClientAction(
        ItemUpdateData snapshot,
        ServerPlayer player,
        IEnumerable<ServerPlayer> serverPlayers,
        float reachBuffer)
    {
        const ItemUpdateData.ItemUpdateType clientUpdateTypes =
            ItemUpdateData.ItemUpdateType.ItemState |
            ItemUpdateData.ItemUpdateType.ItemPosition |
            ItemUpdateData.ItemUpdateType.ObjectState;

        if (snapshot == null ||
            player == null ||
            snapshot.ItemNetId == 0 ||
            snapshot.UpdateType == ItemUpdateData.ItemUpdateType.None ||
            (snapshot.UpdateType & ~clientUpdateTypes) != 0 ||
            !Enum.IsDefined(typeof(ItemState), snapshot.ItemState) ||
            snapshot.UpdateType.HasFlag(
                ItemUpdateData.ItemUpdateType.ItemState) &&
            !snapshot.HasValidInventoryLayout() ||
            !IsSafeTrackedValuePayload(snapshot.States) ||
            !NetworkedItem.TryGet(
                snapshot.ItemNetId,
                out NetworkedItem networkedItem))
        {
            return false;
        }

        bool ownsItem = player.OwnsItem(snapshot.ItemNetId);
        bool isWithinReach =
            networkedItem.transform.PlayerCanReach(player, reachBuffer);

        if (snapshot.UpdateType.HasFlag(
                ItemUpdateData.ItemUpdateType.ObjectState) &&
            (!networkedItem.ValidateClientTrackedValues(snapshot.States) ||
             (!ownsItem && !isWithinReach)))
        {
            return false;
        }

        if (!NetworkedItemDestinationAuthority.Validate(
                snapshot,
                networkedItem,
                player,
                ownsItem,
                reachBuffer))
        {
            return false;
        }

        if (!snapshot.UpdateType.HasFlag(
                ItemUpdateData.ItemUpdateType.ItemState))
        {
            return true;
        }

        switch (snapshot.ItemState)
        {
            case ItemState.InHand:
            case ItemState.InInventory:
                ServerPlayer currentOwner = serverPlayers.FirstOrDefault(
                    candidate =>
                        candidate.OwnsItem(snapshot.ItemNetId));
                return (currentOwner == null ||
                        currentOwner == player) &&
                       (ownsItem || isWithinReach);

            case ItemState.Dropped:
            case ItemState.Thrown:
            case ItemState.Attached:
            case ItemState.InstalledGadget:
                return ownsItem;

            case ItemState.InContainer:
                return ownsItem ||
                       IsInOwnedContainer(
                           snapshot,
                           networkedItem,
                           player);

            default:
                return false;
        }
    }

    private static bool IsInOwnedContainer(
        ItemUpdateData snapshot,
        NetworkedItem item,
        ServerPlayer player)
    {
        return player.OwnsItem(snapshot.ContainerNetId) &&
               NetworkedItem.TryGet(
                   snapshot.ContainerNetId,
                   out NetworkedItem containerItem) &&
               containerItem.TryGetComponent(
                   out ItemContainer container) &&
               item.Item.InContainer == container;
    }
}
