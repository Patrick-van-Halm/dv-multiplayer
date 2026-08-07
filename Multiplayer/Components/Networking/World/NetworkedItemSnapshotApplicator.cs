using DV.Interaction;
using DV.InventorySystem;
using DV.Items;
using Multiplayer.Components.Networking.Player;
using Multiplayer.Components.Networking.Train;
using Multiplayer.Networking.Data;
using Multiplayer.Networking.Data.Items;
using Multiplayer.Networking.Managers.Client;
using System;
using System.Collections;
using System.Linq;
using UnityEngine;

namespace Multiplayer.Components.Networking.World;

internal sealed class NetworkedItemSnapshotApplicator
{
    private readonly NetworkedItem owner;
    private Coroutine remoteHeldStabilization;

    public NetworkedItemSnapshotApplicator(NetworkedItem owner)
    {
        this.owner = owner ??
            throw new ArgumentNullException(nameof(owner));
    }

    public void Apply(ItemUpdateData snapshot)
    {
        if (snapshot.Player != 0)
            owner.SetLastOwner(snapshot.Player, false);

        if (HasItemState(snapshot.UpdateType))
            ApplyItemState(snapshot);

        if ((snapshot.UpdateType.HasFlag(
                 ItemUpdateData.ItemUpdateType.Create) ||
             snapshot.UpdateType.HasFlag(
                 ItemUpdateData.ItemUpdateType.ObjectState)) &&
            snapshot.States != null)
        {
            owner.ApplyTrackedValues(snapshot.States);
        }

        if ((snapshot.UpdateType.HasFlag(
                 ItemUpdateData.ItemUpdateType.Create) ||
             snapshot.UpdateType.HasFlag(
                 ItemUpdateData.ItemUpdateType.ItemPosition)) &&
            snapshot.ItemState == ItemState.InHand)
        {
            ApplyRemoteVrHeldPose(snapshot);
        }

        RefreshRemoteHeldItem(snapshot);
    }

    public void Dispose()
    {
        DetachFromRemotePlayer();
    }

    private static bool HasItemState(
        ItemUpdateData.ItemUpdateType updateType)
    {
        return updateType.HasFlag(
                   ItemUpdateData.ItemUpdateType.ItemState) ||
               updateType.HasFlag(
                   ItemUpdateData.ItemUpdateType.FullSync) ||
               updateType.HasFlag(
                   ItemUpdateData.ItemUpdateType.Create);
    }

    private void ApplyItemState(ItemUpdateData snapshot)
    {
        switch (snapshot.ItemState)
        {
            case ItemState.Dropped:
            case ItemState.Thrown:
                ApplyDroppedOrThrown(snapshot);
                break;
            case ItemState.InHand:
            case ItemState.InInventory:
                ApplyInventoryOrHand(snapshot);
                break;
            case ItemState.Attached:
                ApplyAttachment(snapshot);
                break;
            case ItemState.InContainer:
                AddServerOwnership(snapshot.Player);
                if (!TryApplyContainer(snapshot))
                {
                    owner.StartCoroutine(
                        RetryContainer(snapshot));
                }
                break;
            case ItemState.InstalledGadget:
                // Gadget state adapters apply placement atomically.
                break;
            default:
                throw new InvalidOperationException(
                    $"Unsupported item state: {snapshot.ItemState}.");
        }
    }

    private void ApplyDroppedOrThrown(ItemUpdateData snapshot)
    {
        DetachFromRemotePlayer();
        if (owner.Item.IsSnapped)
            owner.Item.SnappableItem.SnappedTo.UnsnapItem(false);

        RemoveServerOwnership(snapshot.Player);
        StorageController.RemoveItemFromCurrentStorageAndAddToWorld(
            owner.Item,
            NetworkedItem.ToWorldPosition(snapshot.ItemPosition),
            snapshot.ItemRotation);

        if (snapshot.ItemState != ItemState.Thrown)
            return;

        owner.MarkRemoteThrow();
        owner.TryGetComponent(out GrabHandlerItem grabHandler);
        grabHandler?.Throw(snapshot.ThrowDirection);
    }

    private void ApplyAttachment(ItemUpdateData snapshot)
    {
        RemoveServerOwnership(snapshot.Player);
        owner.gameObject.SetActive(true);

        if (!NetworkedTrainCar.TryGet(
                snapshot.CarNetId,
                out TrainCar trainCar))
        {
            Multiplayer.LogWarning(
                $"Car {snapshot.CarNetId} not found while attaching item " +
                $"{snapshot.ItemNetId}.");
            return;
        }

        ItemSnapPointCoupler snapPoint =
            trainCar?.physicsLod?.GetCouplerSnapPoints()
                .FirstOrDefault(
                    point =>
                        point.IsFront == snapshot.AttachedFront);
        if (snapPoint == null)
        {
            Multiplayer.LogWarning(
                $"No coupler snap point found for item " +
                $"{snapshot.ItemNetId} on car {snapshot.CarNetId}.");
            return;
        }

        owner.Item.ItemRigidbody.isKinematic = false;
        if (!snapPoint.SnapItem(owner.Item, false))
        {
            Multiplayer.LogWarning(
                $"Failed to attach item {snapshot.ItemNetId} to car " +
                $"{snapshot.CarNetId}.");
        }
    }

    private void ApplyInventoryOrHand(ItemUpdateData snapshot)
    {
        if (owner.Item.IsSnapped)
            owner.Item.SnappableItem.SnappedTo.UnsnapItem(false);

        AddServerOwnership(snapshot.Player);
        if (snapshot.ItemState == ItemState.InHand &&
            TryGetRemotePlayer(snapshot.Player, out NetworkedPlayer player))
        {
            if (player.RightHandItemGO != owner.gameObject)
            {
                if (player.RightHandItemGO != null)
                    player.DropItem();

                owner.gameObject.SetActive(true);
                player.HoldItem(owner.gameObject);
            }

            RestartHeldStabilization(player);
            return;
        }

        DetachFromRemotePlayer();
        owner.gameObject.SetActive(false);
    }

    private void RefreshRemoteHeldItem(ItemUpdateData snapshot)
    {
        if (snapshot.ItemState == ItemState.InHand &&
            TryGetRemotePlayer(
                snapshot.Player,
                out NetworkedPlayer remotePlayer))
        {
            remotePlayer.RefreshHeldItem(owner.gameObject);
        }
    }

    private bool TryGetRemotePlayer(
        byte playerId,
        out NetworkedPlayer player)
    {
        player = null;
        return NetworkLifecycle.Instance.Client != null &&
               playerId !=
               NetworkLifecycle.Instance.Client.PlayerId &&
               NetworkLifecycle.Instance.Client.ClientPlayerManager
                   .TryGetPlayer(playerId, out player);
    }

    private void RestartHeldStabilization(NetworkedPlayer player)
    {
        if (remoteHeldStabilization != null)
            owner.StopCoroutine(remoteHeldStabilization);

        remoteHeldStabilization =
            owner.StartCoroutine(StabilizeHeldItem(player));
    }

    private IEnumerator StabilizeHeldItem(NetworkedPlayer player)
    {
        yield return null;
        yield return new WaitForEndOfFrame();
        player?.RefreshHeldItem(owner.gameObject);
        yield return null;
        player?.RefreshHeldItem(owner.gameObject);
        remoteHeldStabilization = null;
    }

    private void ApplyRemoteVrHeldPose(ItemUpdateData snapshot)
    {
        if (!TryGetRemotePlayer(
                snapshot.Player,
                out NetworkedPlayer player) ||
            !player.IsVR ||
            player.RightHandItemGO != owner.gameObject)
        {
            return;
        }

        Vector3 position =
            NetworkedItem.ToWorldPosition(snapshot.ItemPosition);
        owner.transform.SetPositionAndRotation(
            position,
            snapshot.ItemRotation);
        owner.GetComponent<IRemoteItemPoseOverride>()?.SetIdlePose(
            position,
            snapshot.ItemRotation);
    }

    private void DetachFromRemotePlayer()
    {
        if (remoteHeldStabilization != null)
        {
            owner.StopCoroutine(remoteHeldStabilization);
            remoteHeldStabilization = null;
        }

        ClientPlayerManager players =
            NetworkLifecycle.Instance.Client?.ClientPlayerManager;
        if (players == null)
            return;

        foreach (NetworkedPlayer player in players.Players)
        {
            if (player.RightHandItemGO == owner.gameObject)
                player.DropItem();
        }
    }

    private bool TryApplyContainer(ItemUpdateData snapshot)
    {
        if (!NetworkedItem.TryGet(
                snapshot.ContainerNetId,
                out NetworkedItem containerItem) ||
            containerItem == null ||
            !containerItem.TryGetComponent(
                out ItemContainer container) ||
            snapshot.ContainerSlot < 0 ||
            snapshot.ContainerSlot >= container.Capacity)
        {
            return false;
        }

        if (owner.Item.InContainer == container &&
            Array.IndexOf(
                container.GetItemsArray(false),
                owner.Item.gameObject) == snapshot.ContainerSlot)
        {
            return true;
        }

        if (owner.Item.InContainer != null)
        {
            owner.Item.InContainer.RemoveItem(
                owner.Item.gameObject,
                false,
                false);
        }

        GameObject occupyingItem = container[snapshot.ContainerSlot];
        if (occupyingItem != null &&
            occupyingItem != owner.Item.gameObject)
        {
            container.RemoveItem(
                snapshot.ContainerSlot,
                false,
                true);
        }

        return container.AddItem(
            owner.Item.gameObject,
            snapshot.ContainerSlot);
    }

    private IEnumerator RetryContainer(ItemUpdateData snapshot)
    {
        const int maxFrames = 120;
        for (int frame = 0; frame < maxFrames; frame++)
        {
            yield return null;
            if (TryApplyContainer(snapshot))
                yield break;
        }

        Multiplayer.LogWarning(
            $"Timed out attaching item {owner.NetId} to container " +
            $"{snapshot.ContainerNetId} slot {snapshot.ContainerSlot}.");
    }

    private void AddServerOwnership(byte playerId)
    {
        if (NetworkLifecycle.Instance.IsHost() &&
            NetworkLifecycle.Instance.Server.TryGetServerPlayer(
                playerId,
                out ServerPlayer player) &&
            !player.OwnsItem(owner.NetId))
        {
            player.AddOwnedItem(owner.NetId);
        }
    }

    private void RemoveServerOwnership(byte playerId)
    {
        if (NetworkLifecycle.Instance.IsHost() &&
            NetworkLifecycle.Instance.Server.TryGetServerPlayer(
                playerId,
                out ServerPlayer player) &&
            player.OwnsItem(owner.NetId))
        {
            player.RemoveOwnedItem(owner.NetId);
        }
    }
}
