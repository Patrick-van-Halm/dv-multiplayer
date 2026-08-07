using Multiplayer.Components.Networking.Train;
using Multiplayer.Networking.Data.Train;
using Xunit;

namespace Multiplayer.Tests;

public class CouplerInteractionRulesTests
{
    [Theory]
    [InlineData(CouplerInteractionType.Start)]
    [InlineData(CouplerInteractionType.CouplerDrop)]
    [InlineData(CouplerInteractionType.DragPoseUpdate)]
    [InlineData(CouplerInteractionType.CoupleViaUI)]
    [InlineData(
        CouplerInteractionType.CoupleViaUI |
        CouplerInteractionType.HoseConnect |
        CouplerInteractionType.CockOpen)]
    [InlineData(
        CouplerInteractionType.Start |
        CouplerInteractionType.CoupleViaRemote)]
    public void IsValid_AcceptsIntentionalProtocolActions(
        CouplerInteractionType interaction)
    {
        Assert.True(CouplerInteractionRules.IsValid(interaction));
    }

    [Theory]
    [InlineData(CouplerInteractionType.NoAction)]
    [InlineData(CouplerInteractionType.HoseConnect)]
    [InlineData(
        CouplerInteractionType.Start |
        CouplerInteractionType.DragPoseUpdate)]
    [InlineData(
        CouplerInteractionType.CoupleViaUI |
        CouplerInteractionType.HoseDisconnect)]
    [InlineData((CouplerInteractionType)ushort.MaxValue)]
    public void IsValid_RejectsUnknownOrAmbiguousCombinations(
        CouplerInteractionType interaction)
    {
        Assert.False(CouplerInteractionRules.IsValid(interaction));
    }

    [Fact]
    public void RequiresOtherCoupler_OnlyForCouplingActions()
    {
        Assert.True(CouplerInteractionRules.RequiresOtherCoupler(
            CouplerInteractionType.CouplerCouple));
        Assert.True(CouplerInteractionRules.RequiresOtherCoupler(
            CouplerInteractionType.CoupleViaUI |
            CouplerInteractionType.HoseConnect |
            CouplerInteractionType.CockOpen));
        Assert.False(CouplerInteractionRules.RequiresOtherCoupler(
            CouplerInteractionType.UncoupleViaUI));
    }

    [Theory]
    [InlineData(false, true, true, true, true, true, false)]
    [InlineData(true, false, true, true, true, true, false)]
    [InlineData(true, true, false, true, true, true, false)]
    [InlineData(true, true, true, true, false, true, false)]
    [InlineData(true, true, true, true, true, false, true)]
    [InlineData(true, true, true, false, false, true, true)]
    [InlineData(true, true, true, false, true, false, false)]
    public void RemoteAuthority_RequiresOwnedActivePairedRemoteAndScope(
        bool ownsRemote,
        bool inControl,
        bool hasPair,
        bool isCouple,
        bool targetsPair,
        bool sharesTrainset,
        bool expected)
    {
        Assert.Equal(
            expected,
            RemoteCouplerAuthorityRules.IsAuthorized(
                ownsRemote,
                inControl,
                hasPair,
                isCouple,
                targetsPair,
                sharesTrainset));
    }

    [Theory]
    [InlineData(false, true, true, true, false)]
    [InlineData(true, false, true, true, false)]
    [InlineData(true, true, false, true, false)]
    [InlineData(true, true, true, false, false)]
    [InlineData(true, true, true, true, true)]
    public void RequestedCoupler_MustBeExactGameTarget(
        bool hasSource,
        bool hasRequested,
        bool isDifferent,
        bool matchesGameTarget,
        bool expected)
    {
        Assert.Equal(
            expected,
            NetworkedCouplerAuthority.IsExactRequestedTarget(
                hasSource,
                hasRequested,
                isDifferent,
                matchesGameTarget));
    }
}
