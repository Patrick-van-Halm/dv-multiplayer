namespace Multiplayer.Components.Networking.World.Items;

internal static class LanternStateRegistration
{
    public static void InitializeItem(Lantern lantern)
    {
        ItemStateTracking.For(lantern);
    }

    public static void Register(Lantern lantern)
    {
        NetworkedItem item = ItemStateTracking.For(lantern);
        item.RegisterTrackedValue(
            "wickSize",
            () => lantern.wickSize,
            lantern.UpdateWickRelatedLogic);
        item.RegisterTrackedValue(
            "Ignited",
            () => lantern.igniter.enabled,
            value =>
            {
                if (value)
                    lantern.Ignite(1);
                else
                    lantern.OnFlameExtinguished();
            });
        item.FinaliseTrackedValues();
    }
}
