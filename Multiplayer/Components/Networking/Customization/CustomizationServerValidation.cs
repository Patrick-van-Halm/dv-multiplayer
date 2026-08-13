using MPAPI.Interfaces;
using Multiplayer.API;
using Multiplayer.Components.Networking.World;
using Multiplayer.Networking.Data;
using UnityEngine;

namespace Multiplayer.Components.Networking.Customization;

internal static class CustomizationServerValidation
{
    public static bool TryGetPlayer(IPlayer sender, out ServerPlayer player)
    {
        player = (sender as ServerPlayerWrapper)?._serverPlayer;
        return player != null;
    }

    public static bool OwnsItem(IPlayer sender, ushort itemNetId, out ServerPlayer player)
    {
        return TryGetPlayer(sender, out player) && itemNetId != 0 && player.OwnsItem(itemNetId);
    }

    public static bool IsInReach(ServerPlayer player, Vector3 worldPosition)
    {
        if (player == null || NetworkedItemManager.Instance == null)
            return false;

        float maxReach = NetworkedItemManager.Instance.MAX_REACH_DISTANCE;
        return (player.WorldPosition - worldPosition).sqrMagnitude <= maxReach * maxReach;
    }

    public static bool IsReady(ServerPlayer player)
    {
        return player != null && player.LoadingState >= PlayerLoadingState.ReadyForCustomizers;
    }
}
