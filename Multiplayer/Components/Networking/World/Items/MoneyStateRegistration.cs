using DV.CabControls;
using DV.Interaction;
using Multiplayer.Components.Networking;
using Multiplayer.Components.Networking.World;
using Multiplayer.Utils;
using UnityEngine;

namespace Multiplayer.Components.Networking.World.Items;

internal static class MoneyStateRegistration
{
    public static void Register(Money __instance)
    {
        NetworkedItem item = __instance.gameObject.GetOrAddComponent<NetworkedItem>();
        item.Initialize(__instance);
        item.RegisterTrackedValue(
            "money.amount",
            () => __instance.Amount,
            value => __instance.Amount = value,
            serverAuthoritative: true);
        item.RegisterTrackedValue(
            "money.jobPayment",
            () => item.IsJobPayment,
            value => item.IsJobPayment = value,
            serverAuthoritative: true);
        item.FinaliseTrackedValues();
    }
}

internal static class NetworkedJobPaymentInteractions
{
    public static void MarkSpawnedPayment(
        MoneyPrinter __instance,
        GameObject __result)
    {
        if (!NetworkLifecycle.Instance.IsHost() ||
            __instance is not MoneyPrinterJobValidator ||
            __result == null ||
            !__result.TryGetComponent(out ItemBase itemBase) ||
            !NetworkedItem.TryGetNetworkedItem(itemBase, out NetworkedItem item))
        {
            return;
        }

        item.SetLastOwner(NetworkLifecycle.Instance.Client?.PlayerId ?? 0);
        NetworkedItemManager.Instance.MarkJobPayment(item);
    }
    public static bool TryHandleUse(
        MoneyUse __instance,
        ItemUseTarget target,
        ref bool __result)
    {
        if (target == null ||
            !target.TryGetComponent(out IMoney targetMoney))
        {
            return true;
        }

        Banknotes banknotes = __instance.money as Banknotes ?? targetMoney as Banknotes;
        if (banknotes == null ||
            !banknotes.TryGetComponent(out ItemBase itemBase) ||
            !NetworkedItem.TryGetNetworkedItem(itemBase, out NetworkedItem item) ||
            !item.IsJobPayment)
        {
            return true;
        }

        __instance.item?.ForceEndInteraction();
        if (NetworkLifecycle.Instance.IsHost())
            NetworkedItemManager.Instance.TryClaimJobPayment(item.NetId, null);
        else
            NetworkLifecycle.Instance.Client.SendJobPaymentClaim(item.NetId);

        __result = true;
        return false;
    }
}
