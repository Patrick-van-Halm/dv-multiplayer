using Multiplayer.Components.Networking.World.Items;
using Xunit;

namespace Multiplayer.Tests;

public class FlashlightBatteryAuthorityTests
{
    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, true, true)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    public void BatterySimulation_RunsOnlyOutsideDedicatedClient(
        bool isClientRunning,
        bool isServerRunning,
        bool expected)
    {
        Assert.Equal(
            expected,
            FlashlightStateRegistration.ShouldSimulateBattery(
                isClientRunning,
                isServerRunning));
    }
}
