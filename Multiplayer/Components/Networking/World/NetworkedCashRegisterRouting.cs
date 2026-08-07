using DV.CashRegister;
using DV.InventorySystem;
using DV.Shops;
using Multiplayer.Networking.Packets.Common;
using Multiplayer.Utils;

namespace Multiplayer.Components.Networking.World;

internal static class NetworkedCashRegisterRouting
{
    public static bool BeforeModulesDisable(
        CashRegisterWithModules register)
    {
        if (register == null)
            return true;

        register.StopAllCoroutines();
        register.textController?.Clear();
        register.SetupListeners(false);
        return NetworkLifecycle.Instance.IsHost();
    }

    public static bool ShouldRunBaseEnable(
        CashRegisterBase register)
    {
        return register is not CashRegisterWithModules ||
               NetworkLifecycle.Instance.IsHost();
    }

    public static bool BeforeBaseDisable(
        CashRegisterBase register)
    {
        if (register is not CashRegisterWithModules)
            return true;

        register.StopAllCoroutines();
        return NetworkLifecycle.Instance.IsHost();
    }

    public static bool RouteAddCash(
        CashRegisterBase cashRegister,
        double amount)
    {
        if (cashRegister is not CashRegisterWithModules register ||
            !TryGet(register, out NetworkedCashRegisterWithModules networked))
        {
            return true;
        }

        Inventory.Instance.AddMoney(amount);
        CoroutineManager.Instance.StartCoroutine(
            networked.AddCash(amount));
        return false;
    }

    public static bool RouteBuy(CashRegisterWithModules register)
    {
        if (!TryGet(register, out NetworkedCashRegisterWithModules networked))
            return false;

        if (NetworkLifecycle.Instance.IsHost() &&
            !networked.IsShopRegister)
        {
            return true;
        }

        CoroutineManager.Instance.StartCoroutine(networked.Buy());
        return false;
    }

    public static void AfterBuy(CashRegisterWithModules register)
    {
        if (!NetworkLifecycle.Instance.IsHost() ||
            !TryGet(register, out NetworkedCashRegisterWithModules networked) ||
            networked.IsShopRegister)
        {
            return;
        }

        NetworkLifecycle.Instance.Server.SendCashRegisterAction(
            new CommonCashRegisterWithModulesActionPacket
            {
                NetId = networked.NetId,
                Action = CashRegisterAction.Buy,
                Amount = register.DepositedCash,
            });
    }

    public static bool RouteCancel(CashRegisterWithModules register)
    {
        if (!TryGet(register, out NetworkedCashRegisterWithModules networked))
            return false;

        if (networked.IsProcessingServerAction ||
            NetworkLifecycle.Instance.IsHost() &&
            !networked.IsShopRegister)
        {
            return true;
        }

        CoroutineManager.Instance.StartCoroutine(networked.Cancel());
        return false;
    }

    public static void AfterCancel(CashRegisterWithModules register)
    {
        if (!NetworkLifecycle.Instance.IsHost() ||
            !TryGet(register, out NetworkedCashRegisterWithModules networked) ||
            networked.IsShopRegister ||
            networked.IsProcessingServerAction)
        {
            return;
        }

        NetworkLifecycle.Instance.Server.SendCashRegisterAction(
            new CommonCashRegisterWithModulesActionPacket
            {
                NetId = networked.NetId,
                Action = CashRegisterAction.Cancel,
                Amount = register.DepositedCash,
            });
    }

    public static void OnShopItemAdded(
        ScanItemCashRegisterModule module,
        bool added)
    {
        if (!added ||
            module?.shop?.cashRegister == null ||
            !TryGet(
                module.shop.cashRegister,
                out NetworkedCashRegisterWithModules networked))
        {
            return;
        }

        networked.SendCartUpdate(module);
    }

    private static bool TryGet(
        CashRegisterWithModules register,
        out NetworkedCashRegisterWithModules networked)
    {
        networked = null;
        if (register == null)
            return false;

        if (NetworkedCashRegisterWithModules.TryGet(
                register,
                out networked))
        {
            return true;
        }

        Multiplayer.LogWarning(
            $"Networked cash register not found for " +
            $"{register.GetObjectPath()}.");
        return false;
    }
}
