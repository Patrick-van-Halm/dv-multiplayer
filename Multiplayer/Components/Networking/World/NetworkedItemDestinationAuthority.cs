using DV.Customization;
using DV.Customization.Gadgets;
using DV.InventorySystem;
using DV.Items;
using Multiplayer.Components.Networking.Train;
using Multiplayer.Components.Networking.World.Items;
using Multiplayer.Networking.Data;
using Multiplayer.Networking.Data.Items;
using Multiplayer.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Multiplayer.Components.Networking.World;

internal static class NetworkedItemDestinationAuthority
{
    private const float RotationMagnitudeTolerance = 0.1f;
    private const float MaximumThrowMagnitude = 50f;

    public static bool Validate(
        ItemUpdateData snapshot,
        NetworkedItem item,
        ServerPlayer player,
        bool ownsItem,
        float reachBuffer)
    {
        if (!ValidateTrackedDestination(
                snapshot,
                item,
                player,
                ownsItem,
                reachBuffer))
        {
            return false;
        }

        if (snapshot.UpdateType.HasFlag(
                ItemUpdateData.ItemUpdateType.ItemPosition) &&
            !snapshot.UpdateType.HasFlag(
                ItemUpdateData.ItemUpdateType.ItemState))
        {
            return ownsItem &&
                   snapshot.ItemState == ItemState.InHand &&
                   IsReachablePose(
                       snapshot.ItemPosition,
                       snapshot.ItemRotation,
                       player,
                       reachBuffer);
        }

        if (!snapshot.UpdateType.HasFlag(
                ItemUpdateData.ItemUpdateType.ItemState))
        {
            return true;
        }

        switch (snapshot.ItemState)
        {
            case ItemState.Dropped:
                return ownsItem &&
                       IsReachablePose(
                           snapshot.ItemPosition,
                           snapshot.ItemRotation,
                           player,
                           reachBuffer);
            case ItemState.Thrown:
                return ownsItem &&
                       IsReachablePose(
                           snapshot.ItemPosition,
                           snapshot.ItemRotation,
                           player,
                           reachBuffer) &&
                       IsValidThrowDirection(snapshot.ThrowDirection);
            case ItemState.Attached:
                return ValidateAttachment(
                    snapshot,
                    item,
                    player,
                    ownsItem,
                    reachBuffer);
            case ItemState.InContainer:
                return ValidateContainer(
                    snapshot,
                    item,
                    player,
                    ownsItem,
                    reachBuffer);
            case ItemState.InstalledGadget:
                return ownsItem &&
                       item.TryGetComponent(out GadgetItem _) &&
                       HasValidGadgetPlacement(
                           snapshot,
                           player,
                           reachBuffer);
            default:
                return true;
        }
    }

    internal static bool IsValidRotation(Quaternion rotation)
    {
        if (!rotation.IsFinite())
            return false;

        float magnitudeSquared =
            rotation.x * rotation.x +
            rotation.y * rotation.y +
            rotation.z * rotation.z +
            rotation.w * rotation.w;
        return Math.Abs(magnitudeSquared - 1f) <=
               RotationMagnitudeTolerance;
    }

    internal static bool IsValidThrowDirection(Vector3 direction)
    {
        return direction.IsFinite() &&
               direction.sqrMagnitude <=
                   MaximumThrowMagnitude * MaximumThrowMagnitude;
    }

    internal static bool IsDestinationPolicySatisfied(
        bool ownsItem,
        bool targetExists,
        bool targetReachable,
        bool targetAvailable)
    {
        return ownsItem &&
               targetExists &&
               targetReachable &&
               targetAvailable;
    }

    private static bool IsReachablePose(
        Vector3 networkPosition,
        Quaternion rotation,
        ServerPlayer player,
        float reachBuffer)
    {
        return networkPosition.IsFinite() &&
               IsValidRotation(rotation) &&
               NetworkedItem.ToWorldPosition(networkPosition)
                   .PlayerCanReach(player, reachBuffer);
    }

    private static bool ValidateAttachment(
        ItemUpdateData snapshot,
        NetworkedItem item,
        ServerPlayer player,
        bool ownsItem,
        float reachBuffer)
    {
        TrainCar trainCar = null;
        bool targetExists =
            snapshot.CarNetId != 0 &&
            item.TryGetComponent(out SnappableItem _) &&
            NetworkedTrainCar.TryGet(
                snapshot.CarNetId,
                out trainCar) &&
            trainCar?.physicsLod != null;
        ItemSnapPointCoupler snapPoint = targetExists
            ? trainCar.physicsLod.GetCouplerSnapPoints()
                .FirstOrDefault(
                    point =>
                        point.IsFront == snapshot.AttachedFront)
            : null;

        bool targetAvailable =
            snapPoint != null &&
            (snapPoint.SnappedItem == null ||
             snapPoint.SnappedItem == item.Item);
        return IsDestinationPolicySatisfied(
            ownsItem,
            targetExists && snapPoint != null,
            snapPoint != null &&
                snapPoint.transform.PlayerCanReach(
                    player,
                    reachBuffer),
            targetAvailable);
    }

    private static bool ValidateContainer(
        ItemUpdateData snapshot,
        NetworkedItem item,
        ServerPlayer player,
        bool ownsItem,
        float reachBuffer)
    {
        NetworkedItem containerItem = null;
        ItemContainer container = null;
        bool targetExists =
            NetworkedItem.TryGet(
                snapshot.ContainerNetId,
                out containerItem) &&
            containerItem.TryGetComponent(
                out container) &&
            snapshot.ContainerSlot >= 0 &&
            snapshot.ContainerSlot < container.Capacity;
        GameObject occupyingItem = targetExists
            ? container[snapshot.ContainerSlot]
            : null;
        bool itemAlreadyInOwnedContainer =
            targetExists &&
            player.OwnsItem(snapshot.ContainerNetId) &&
            item.Item.InContainer == container;

        return IsDestinationPolicySatisfied(
            ownsItem || itemAlreadyInOwnedContainer,
            targetExists,
            targetExists &&
                (player.OwnsItem(snapshot.ContainerNetId) ||
                 containerItem.transform.PlayerCanReach(
                     player,
                     reachBuffer)),
            targetExists &&
                (occupyingItem == null ||
                 occupyingItem == item.gameObject));
    }

    private static bool ValidateTrackedDestination(
        ItemUpdateData snapshot,
        NetworkedItem item,
        ServerPlayer player,
        bool ownsItem,
        float reachBuffer)
    {
        if (!snapshot.UpdateType.HasFlag(
                ItemUpdateData.ItemUpdateType.ObjectState) ||
            snapshot.States == null ||
            !snapshot.States.TryGetValue(
                "gadget.placement",
                out object rawPlacement))
        {
            return true;
        }

        if (!ownsItem ||
            !item.TryGetComponent(out GadgetItem _) ||
            rawPlacement is not string serialized)
        {
            return false;
        }

        if (string.IsNullOrEmpty(serialized))
            return snapshot.ItemState != ItemState.InstalledGadget;

        return snapshot.ItemState == ItemState.InstalledGadget &&
               TryValidateGadgetPlacement(
                   serialized,
                   player,
                   reachBuffer);
    }

    private static bool HasValidGadgetPlacement(
        ItemUpdateData snapshot,
        ServerPlayer player,
        float reachBuffer)
    {
        return snapshot.States != null &&
               snapshot.States.TryGetValue(
                   "gadget.placement",
                   out object rawPlacement) &&
               rawPlacement is string serialized &&
               TryValidateGadgetPlacement(
                   serialized,
                   player,
                   reachBuffer);
    }

    private static bool TryValidateGadgetPlacement(
        string serialized,
        ServerPlayer player,
        float reachBuffer)
    {
        if (!GadgetPlacementData.TryParse(
                serialized,
                out GadgetPlacementData placement) ||
            !placement.TryResolveDestination(
                out Customization destination,
                allowUnregisteredTrainFallback: false))
        {
            return false;
        }

        Vector3 worldPosition =
            destination.transform.TransformPoint(
                placement.LocalPosition);
        return worldPosition.IsFinite() &&
               worldPosition.PlayerCanReach(
                   player,
                   reachBuffer);
    }
}
