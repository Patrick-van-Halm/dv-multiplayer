using HarmonyLib;
using Multiplayer.Components.SaveGame;

namespace Multiplayer.Patches.SaveGame;

[HarmonyPatch(
    typeof(StartingItemsController),
    "StartingItemsSafeguard")]
internal static class StartingItemsControllerPatch
{
    [HarmonyPrefix]
    private static bool AddBasicStartingItems() =>
        NetworkedPlayerInventoryLoadContext
            .ShouldAddBasicStartingItems;
}
