using Multiplayer.Components.Networking.World;
using Xunit;

namespace Multiplayer.Tests;

public class NetworkedShopPurchaseOwnershipTests
{
    public NetworkedShopPurchaseOwnershipTests()
    {
        NetworkedShopPurchaseOwnership.Clear();
    }

    [Fact]
    public void PurchasedItems_RetainBuyerOrderPerPrefab()
    {
        NetworkedShopPurchaseOwnership.Enqueue(
            "Flashlight",
            count: 2,
            playerId: 3);
        NetworkedShopPurchaseOwnership.Enqueue(
            "Flashlight",
            count: 1,
            playerId: 7);

        Assert.True(
            NetworkedShopPurchaseOwnership.TryTake(
                "Flashlight",
                out byte first));
        Assert.True(
            NetworkedShopPurchaseOwnership.TryTake(
                "Flashlight",
                out byte second));
        Assert.True(
            NetworkedShopPurchaseOwnership.TryTake(
                "Flashlight",
                out byte third));
        Assert.Equal((byte)3, first);
        Assert.Equal((byte)3, second);
        Assert.Equal((byte)7, third);
        Assert.False(
            NetworkedShopPurchaseOwnership.TryTake(
                "Flashlight",
                out _));
    }

    [Fact]
    public void PurchasedItems_DoNotCrossPrefabQueues()
    {
        NetworkedShopPurchaseOwnership.Enqueue(
            "Lantern",
            count: 1,
            playerId: 4);

        Assert.False(
            NetworkedShopPurchaseOwnership.TryTake(
                "Shovel",
                out _));
        Assert.True(
            NetworkedShopPurchaseOwnership.TryTake(
                "Lantern",
                out byte owner));
        Assert.Equal((byte)4, owner);
    }
}
