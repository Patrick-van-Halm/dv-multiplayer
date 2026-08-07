using System;

namespace Multiplayer.Components.Networking.World;

internal static class NetworkedShopCartRules
{
    public static bool TryAddUnit(
        double currentUnits,
        int itemsInStock,
        int unitsInOtherModules,
        out int units)
    {
        units = 0;
        if (!TryValidateUnits(
                currentUnits,
                itemsInStock,
                unitsInOtherModules,
                out int current))
        {
            return false;
        }

        return current < int.MaxValue &&
               TryValidateUnits(
                   current + 1d,
                   itemsInStock,
                   unitsInOtherModules,
                   out units);
    }

    public static bool TryValidateUnits(
        double requestedUnits,
        int itemsInStock,
        int unitsInOtherModules,
        out int units)
    {
        units = 0;
        if (double.IsNaN(requestedUnits) ||
            double.IsInfinity(requestedUnits) ||
            requestedUnits < 0 ||
            requestedUnits != Math.Truncate(requestedUnits) ||
            requestedUnits > int.MaxValue ||
            itemsInStock < 0 ||
            unitsInOtherModules < 0)
        {
            return false;
        }

        units = (int)requestedUnits;
        return units <= itemsInStock - unitsInOtherModules;
    }
}
