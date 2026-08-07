using Multiplayer.Components.Networking.World.Items;
using Xunit;

namespace Multiplayer.Tests;

public class BrickSyncEventTests
{
    [Theory]
    [InlineData("")]
    [InlineData("1")]
    [InlineData("1:2:3")]
    [InlineData("one:2")]
    [InlineData("1:two")]
    public void TryParse_RejectsMalformedValues(string serialized)
    {
        Assert.False(BrickSyncEvent.TryParse(serialized, out _));
    }

    [Fact]
    public void Serialize_RoundTripsSequenceAndActionAtomically()
    {
        var original = new BrickSyncEvent(42, 7);

        bool parsed = BrickSyncEvent.TryParse(
            original.Serialize(),
            out BrickSyncEvent result);

        Assert.True(parsed);
        Assert.Equal(42, result.Sequence);
        Assert.Equal(7, result.Action);
    }
}
