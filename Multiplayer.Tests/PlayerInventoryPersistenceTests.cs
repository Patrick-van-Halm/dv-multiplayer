using System;
using Multiplayer.Components.SaveGame;
using Xunit;

namespace Multiplayer.Tests;

public class PlayerInventoryPersistenceTests : IDisposable
{
    public PlayerInventoryPersistenceTests()
    {
        NetworkedPlayerInventoryLoadContext.Reset();
    }

    public void Dispose()
    {
        NetworkedPlayerInventoryLoadContext.Reset();
    }

    [Fact]
    public void PlayerInventoryStorage_UsesReservedIdentity()
    {
        Assert.Equal(
            100000,
            (int)MultiplayerStorageType.PlayerInventory);
        Assert.Equal(
            "MP_Player_Inventory",
            NetworkedPlayerInventoryRecord.StorageId);
    }

    [Fact]
    public void NewPlayer_ReceivesBasicStartingItems()
    {
        NetworkedPlayerInventoryLoadContext.Begin(
            hasSavedInventory: false);

        Assert.True(
            NetworkedPlayerInventoryLoadContext
                .ShouldAddBasicStartingItems);
    }

    [Fact]
    public void ReturningPlayer_DoesNotRegainBasicStartingItems()
    {
        NetworkedPlayerInventoryLoadContext.Begin(
            hasSavedInventory: true);

        Assert.False(
            NetworkedPlayerInventoryLoadContext
                .ShouldAddBasicStartingItems);
    }

    [Fact]
    public void ContainerLayout_PreservesContainerIdentityAndSlot()
    {
        var layout = new PlayerInventoryLayout(
            -1,
            false,
            false,
            false,
            "container-id",
            3);

        Assert.Equal("container-id", layout.ContainerId);
        Assert.Equal(3, layout.ContainerSlot);
        Assert.Equal(-1, layout.Slot);
    }
}
