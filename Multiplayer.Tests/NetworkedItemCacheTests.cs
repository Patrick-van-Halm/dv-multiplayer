using DV.Logic.Job;
using Multiplayer.Components.Networking.World;
using Xunit;

namespace Multiplayer.Tests;

public class NetworkedItemPrefabCatalogTests
{
    [Theory]
    [InlineData(typeof(JobOverview))]
    [InlineData(typeof(JobBooklet))]
    [InlineData(typeof(JobReport))]
    [InlineData(typeof(JobExpiredReport))]
    [InlineData(typeof(JobMissingLicenseReport))]
    public void ShouldSkipAutoCreate_RecognizesSpecialItemTypes(
        System.Type itemType)
    {
        Assert.True(
            NetworkedItemPrefabCatalog.ShouldSkipAutoCreate(itemType));
    }

    [Fact]
    public void ShouldSkipAutoCreate_AllowsOrdinaryItems()
    {
        Assert.False(
            NetworkedItemPrefabCatalog.ShouldSkipAutoCreate(
                typeof(NetworkedItemPrefabCatalogTests)));
    }
}
