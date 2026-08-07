using Multiplayer.Components.Networking.World.Items;
using Xunit;

namespace Multiplayer.Tests;

public class LocomotiveRemotePairingAuthorityTests
{
    [Fact]
    public void OwnedRemote_CanRequestUnpairWithoutTarget()
    {
        Assert.True(
            NetworkedLocomotiveRemotePairing.CanApplyRequest(
                ownsRemote: true,
                isUnpairRequest: true,
                targetExists: false,
                targetSupportsRemote: false,
                targetIsReachable: false));
    }

    [Theory]
    [InlineData(false, true, true, true)]
    [InlineData(true, false, true, true)]
    [InlineData(true, true, false, true)]
    [InlineData(true, true, true, false)]
    public void PairRequest_RequiresAllServerAuthorityChecks(
        bool ownsRemote,
        bool targetExists,
        bool targetSupportsRemote,
        bool targetIsReachable)
    {
        Assert.False(
            NetworkedLocomotiveRemotePairing.CanApplyRequest(
                ownsRemote,
                isUnpairRequest: false,
                targetExists,
                targetSupportsRemote,
                targetIsReachable));
    }

    [Fact]
    public void ValidPairRequest_IsAccepted()
    {
        Assert.True(
            NetworkedLocomotiveRemotePairing.CanApplyRequest(
                ownsRemote: true,
                isUnpairRequest: false,
                targetExists: true,
                targetSupportsRemote: true,
                targetIsReachable: true));
    }
}
