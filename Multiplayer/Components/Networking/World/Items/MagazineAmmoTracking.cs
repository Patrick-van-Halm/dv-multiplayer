using DV.Items;

namespace Multiplayer.Components.Networking.World.Items;

internal static class MagazineAmmoTracking
{
    public static void Register(MagazineAmmo ammo)
    {
        NetworkedItem item = ItemStateTracking.For(ammo);
        item.RegisterTrackedValue(
            "ammo.spent",
            () => ammo.isSpent,
            value => ammo.isSpent = value);
        item.FinaliseTrackedValues();
    }
}
