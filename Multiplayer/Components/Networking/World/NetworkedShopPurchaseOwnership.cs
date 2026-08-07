using System.Collections.Generic;

namespace Multiplayer.Components.Networking.World;

internal static class NetworkedShopPurchaseOwnership
{
    private static readonly Dictionary<string, Queue<byte>>
        pendingOwnersByPrefab = [];

    public static void Enqueue(
        string prefabName,
        int count,
        byte playerId)
    {
        if (string.IsNullOrEmpty(prefabName) ||
            count <= 0 ||
            playerId == 0)
        {
            return;
        }

        if (!pendingOwnersByPrefab.TryGetValue(
                prefabName,
                out Queue<byte> owners))
        {
            owners = new Queue<byte>();
            pendingOwnersByPrefab[prefabName] = owners;
        }

        for (int index = 0; index < count; index++)
            owners.Enqueue(playerId);
    }

    public static bool TryTake(
        string prefabName,
        out byte playerId)
    {
        playerId = 0;
        if (string.IsNullOrEmpty(prefabName) ||
            !pendingOwnersByPrefab.TryGetValue(
                prefabName,
                out Queue<byte> owners) ||
            owners.Count == 0)
        {
            return false;
        }

        playerId = owners.Dequeue();
        if (owners.Count == 0)
            pendingOwnersByPrefab.Remove(prefabName);
        return true;
    }

    internal static void Clear()
    {
        pendingOwnersByPrefab.Clear();
    }
}
