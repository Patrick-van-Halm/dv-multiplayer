using DV.CabControls;
using DV.InventorySystem;
using Multiplayer.Components.Networking;
using Multiplayer.Components.Networking.World;
using Multiplayer.Utils;

namespace Multiplayer.Components.Networking.World.Items;

internal static class ItemBaseStateRegistration
{
    public static void Register(ItemBase __instance)
    {
        __instance.GetOrAddComponent<NetworkedItem>();
        __instance.ItemInventoryStateChanged += ItemInventoryStateChanged;
    }

    private static void ItemInventoryStateChanged(
        ItemBase item,
        InventoryActionType actionType,
        InventoryItemState itemState)
    {
        if (item.CabItem == null ||
            !item.TryGetComponent(out NetworkedItem networkedItem))
        {
            return;
        }

        byte localPlayerId =
            NetworkLifecycle.Instance?.Client?.PlayerId ?? 0;
        bool belongsToRemotePlayer =
            localPlayerId != 0 &&
            networkedItem.LastOwnerId != 0 &&
            networkedItem.LastOwnerId != localPlayerId;

        CabItemRigidbodyCompatibility.TrySetAssumeIsPaused(
            item.CabItem,
            belongsToRemotePlayer && itemState.IsInInventory());
    }
}
