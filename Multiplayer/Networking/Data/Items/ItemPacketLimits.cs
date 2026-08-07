namespace Multiplayer.Networking.Data.Items;

public static class ItemPacketLimits
{
    public const int MaxSnapshots = 128;
    public const int MaxTrackedValues = 64;
    public const int MaxTrackedValueKeyLength = 128;
    public const int MaxTrackedStringLength = 4096;
    public const int MaxPrefabNameLength = 256;
    public const int MaxInventorySlotIndex = 35;
    public const int MaxCompressedPayloadBytes = 1024 * 1024;
    public const int MaxDecompressedPayloadBytes = 8 * 1024 * 1024;
}
