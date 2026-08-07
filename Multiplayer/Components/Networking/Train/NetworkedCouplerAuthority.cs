using Multiplayer.Networking.Data.Train;
using Multiplayer.Networking.Packets.Common.Train;

namespace Multiplayer.Components.Networking.Train;

internal static class NetworkedCouplerAuthority
{
    public static bool ValidateRequestedTarget(
        CommonCouplerInteractionPacket packet,
        CouplerInteractionType interaction,
        Coupler source)
    {
        if (!CouplerInteractionRules.RequiresOtherCoupler(interaction))
            return packet.OtherNetId == 0;

        if (source == null ||
            packet.OtherNetId == 0 ||
            !NetworkedTrainCar.TryGet(
                packet.OtherNetId,
                out TrainCar otherCar))
        {
            return false;
        }

        Coupler requested = packet.IsFrontOtherCoupler
            ? otherCar?.frontCoupler
            : otherCar?.rearCoupler;
        return IsRequestedTarget(
            source,
            requested,
            source.GetFirstCouplerInRange());
    }

    internal static bool IsRequestedTarget(
        Coupler source,
        Coupler requested,
        Coupler gameTarget)
    {
        return IsExactRequestedTarget(
            source != null,
            requested != null,
            requested != source,
            requested == gameTarget);
    }

    internal static bool IsExactRequestedTarget(
        bool hasSource,
        bool hasRequestedTarget,
        bool isDifferentCoupler,
        bool matchesGameTarget)
    {
        return hasSource &&
               hasRequestedTarget &&
               isDifferentCoupler &&
               matchesGameTarget;
    }
}
