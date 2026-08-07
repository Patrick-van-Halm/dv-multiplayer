using Multiplayer.Components.Networking.World;
using System.Collections.Generic;
using Xunit;

namespace Multiplayer.Tests;

public class NetworkedItemTrackedStateTests
{
    [Fact]
    public void DirtyValues_AreCollectedAndMarkedClean()
    {
        int current = 1;
        var state = new NetworkedItemTrackedState();
        state.Register(
            "item.count",
            () => current,
            value => current = value,
            null,
            false,
            true,
            "test item");

        current = 2;

        Assert.True(state.HasDirtyValues(true));
        Assert.Equal(2, state.GetDirtyValues(true)["item.count"]);

        state.MarkClean();

        Assert.False(state.HasDirtyValues(true));
    }

    [Fact]
    public void ClientValues_RejectUnknownAndServerAuthoritativeKeys()
    {
        int current = 1;
        var state = new NetworkedItemTrackedState();
        state.Register(
            "server.count",
            () => current,
            value => current = value,
            null,
            true,
            true,
            "test item");

        Assert.False(
            state.AcceptsClientValues(
                new Dictionary<string, object>
                {
                    ["server.count"] = 2,
                }));
        Assert.False(
            state.AcceptsClientValues(
                new Dictionary<string, object>
                {
                    ["unknown"] = 2,
                }));
    }
}
