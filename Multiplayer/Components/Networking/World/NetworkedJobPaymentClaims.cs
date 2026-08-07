using DV;
using DV.InventorySystem;
using DV.Items;
using Multiplayer.Networking.Data;
using Multiplayer.Utils;
using System.Collections.Generic;
using UnityEngine;

namespace Multiplayer.Components.Networking.World;

internal sealed class NetworkedJobPaymentClaims
{
    private readonly HashSet<ushort> claimedItemIds = [];

    public bool Mark(NetworkedItem item)
    {
        if (!NetworkLifecycle.Instance.IsHost() ||
            item == null ||
            item.NetId == 0)
        {
            return false;
        }

        item.IsJobPayment = true;
        claimedItemIds.Remove(item.NetId);
        item.MarkTransformDirty();
        return true;
    }

    public bool TryClaim(
        ushort itemNetId,
        ServerPlayer player,
        float reachDistanceBuffer)
    {
        if (!NetworkLifecycle.Instance.IsHost() ||
            itemNetId == 0 ||
            claimedItemIds.Contains(itemNetId) ||
            !NetworkedItem.TryGet(itemNetId, out NetworkedItem item) ||
            item == null ||
            !item.IsJobPayment ||
            !item.TryGetComponent(out Banknotes banknotes))
        {
            return false;
        }

        if (player != null &&
            !item.transform.PlayerCanReach(player, reachDistanceBuffer))
        {
            NetworkLifecycle.Instance.Server.LogWarning(
                $"Rejected out-of-reach job payment claim for item {itemNetId} from {player.Username}.");
            return false;
        }

        double amount = banknotes.Amount;
        if (double.IsNaN(amount) ||
            double.IsInfinity(amount) ||
            amount <= 0.0)
        {
            return false;
        }

        claimedItemIds.Add(itemNetId);
        Inventory.Instance.AddMoney(amount);
        Object.Destroy(item.gameObject);
        return true;
    }
}
