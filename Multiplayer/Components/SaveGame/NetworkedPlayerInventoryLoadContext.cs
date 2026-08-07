namespace Multiplayer.Components.SaveGame;

internal static class NetworkedPlayerInventoryLoadContext
{
    public static bool ShouldAddBasicStartingItems { get; private set; } =
        true;

    public static void Begin(bool hasSavedInventory)
    {
        ShouldAddBasicStartingItems = !hasSavedInventory;
    }

    public static void Reset()
    {
        ShouldAddBasicStartingItems = true;
    }
}
