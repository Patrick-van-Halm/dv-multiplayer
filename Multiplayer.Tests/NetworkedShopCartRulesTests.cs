using Multiplayer.Components.Networking.World;
using Xunit;

namespace Multiplayer.Tests;

public class NetworkedShopCartRulesTests
{
    [Theory]
    [InlineData(0, 5, 0, 1)]
    [InlineData(3, 5, 0, 4)]
    [InlineData(1, 5, 3, 2)]
    public void AddUnit_IncrementsAuthoritativeQuantity(
        double current,
        int stock,
        int otherUnits,
        int expected)
    {
        Assert.True(
            NetworkedShopCartRules.TryAddUnit(
                current,
                stock,
                otherUnits,
                out int units));
        Assert.Equal(expected, units);
    }

    [Theory]
    [InlineData(5, 5, 0)]
    [InlineData(2, 5, 3)]
    [InlineData(-1, 5, 0)]
    [InlineData(1.5, 5, 0)]
    [InlineData(double.NaN, 5, 0)]
    public void AddUnit_RejectsInvalidOrExhaustedCart(
        double current,
        int stock,
        int otherUnits)
    {
        Assert.False(
            NetworkedShopCartRules.TryAddUnit(
                current,
                stock,
                otherUnits,
                out _));
    }

    [Theory]
    [InlineData(0, 5, 0, 0)]
    [InlineData(3, 5, 0, 3)]
    [InlineData(2, 5, 3, 2)]
    public void Units_AcceptsIntegralQuantityWithinRemainingStock(
        double requested,
        int stock,
        int otherUnits,
        int expected)
    {
        Assert.True(
            NetworkedShopCartRules.TryValidateUnits(
                requested,
                stock,
                otherUnits,
                out int units));
        Assert.Equal(expected, units);
    }

    [Theory]
    [InlineData(-1, 5, 0)]
    [InlineData(1.5, 5, 0)]
    [InlineData(6, 5, 0)]
    [InlineData(3, 5, 3)]
    [InlineData(double.NaN, 5, 0)]
    [InlineData(double.PositiveInfinity, 5, 0)]
    public void Units_RejectsInvalidOrOversoldQuantity(
        double requested,
        int stock,
        int otherUnits)
    {
        Assert.False(
            NetworkedShopCartRules.TryValidateUnits(
                requested,
                stock,
                otherUnits,
                out _));
    }
}
