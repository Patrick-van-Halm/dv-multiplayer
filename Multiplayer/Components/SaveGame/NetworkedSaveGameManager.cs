using DV.InventorySystem;
using DV.JObjectExtstensions;
using DV.ThingTypes;
using DV.Utils;
using JetBrains.Annotations;
using Multiplayer.Components.Networking;
using Multiplayer.Components.Networking.World;
using Multiplayer.Networking.Data;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;

namespace Multiplayer.Components.SaveGame;

public class NetworkedSaveGameManager : SingletonBehaviour<NetworkedSaveGameManager>
{
    private const string ROOT_KEY = "Multiplayer";
    private const string PLAYERS_KEY = "Players";
    private NetworkedPlayerInventoryStore playerInventories;

    protected override void Awake()
    {
        base.Awake();
        if (!NetworkLifecycle.Instance.IsHost())
            return;
        playerInventories =
            new NetworkedPlayerInventoryStore(transform);
        Inventory.Instance.MoneyChanged += Server_OnMoneyChanged;
        LicenseManager.Instance.LicenseAcquired += Server_OnLicenseAcquired;
        LicenseManager.Instance.JobLicenseAcquired += Server_OnJobLicenseAcquired;
        LicenseManager.Instance.GarageUnlocked += Server_OnGarageUnlocked;
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        if (UnloadWatcher.isUnloading)
            return;
        if (!NetworkLifecycle.Instance.IsHost())
            return;
        playerInventories?.Dispose();
        playerInventories = null;
        Inventory.Instance.MoneyChanged -= Server_OnMoneyChanged;
        LicenseManager.Instance.LicenseAcquired -= Server_OnLicenseAcquired;
        LicenseManager.Instance.JobLicenseAcquired -= Server_OnJobLicenseAcquired;
        LicenseManager.Instance.GarageUnlocked -= Server_OnGarageUnlocked;
    }

    #region Server

    private static void Server_OnMoneyChanged(double oldAmount, double newAmount)
    {
        NetworkLifecycle.Instance.Server.SendMoney((float)newAmount);
    }

    private static void Server_OnLicenseAcquired(GeneralLicenseType_v2 license)
    {
        NetworkLifecycle.Instance.Server.SendLicense(license.id, false);
    }

    private static void Server_OnJobLicenseAcquired(JobLicenseType_v2 license)
    {
        NetworkLifecycle.Instance.Server.SendLicense(license.id, true);
    }

    private static void Server_OnGarageUnlocked(GarageType_v2 garage)
    {
        NetworkLifecycle.Instance.Server.SendGarage(garage.id);
    }

    public void Server_UpdateInternalData(SaveGameData data)
    {
        JObject root = data.GetJObject(ROOT_KEY) ?? [];
        JObject players = root.GetJObject(PLAYERS_KEY) ?? [];

        foreach (ServerPlayer player in NetworkLifecycle.Instance.Server.ServerPlayers)
        {
            if (player.Peer == NetworkLifecycle.Instance.Server.SelfPeer || player.LoadingState != PlayerLoadingState.Complete)
                continue;

            JObject playerData =
                players.GetJObject(player.Guid.ToString()) ?? [];
            playerData.SetVector3(SaveGameKeys.Player_position, player.AbsoluteWorldPosition);
            playerData.SetFloat(SaveGameKeys.Player_rotation, player.WorldRotationY);
            players.SetJObject(player.Guid.ToString(), playerData);
        }

        playerInventories?.Save(players);
        root.SetJObject(PLAYERS_KEY, players);
        data.SetJObject(ROOT_KEY, root);
    }

    internal bool Server_PreparePlayerInventory(
        SaveGameData data,
        ServerPlayer player,
        out IReadOnlyList<StorageItemData> inventory)
    {
        if (player?.Peer ==
            NetworkLifecycle.Instance.Server.SelfPeer)
        {
            inventory = Array.Empty<StorageItemData>();
            return false;
        }

        JObject root = data.GetJObject(ROOT_KEY) ?? [];
        JObject players = root.GetJObject(PLAYERS_KEY) ?? [];
        return playerInventories.TryPreparePlayer(
            player,
            players,
            out inventory);
    }

    internal void Server_TrackPlayerInventoryItem(
        ServerPlayer player,
        NetworkedItem item,
        PlayerInventoryLayout layout)
    {
        if (player?.Peer ==
            NetworkLifecycle.Instance.Server.SelfPeer)
        {
            return;
        }
        playerInventories?.Track(player, item, layout);
    }

    internal void Server_RemovePlayerInventoryItem(
        NetworkedItem item)
    {
        playerInventories?.Remove(item);
    }

    internal IReadOnlyList<NetworkedItem>
        Server_FreezePlayerInventory(ServerPlayer player)
    {
        return playerInventories?.Freeze(player) ??
               Array.Empty<NetworkedItem>();
    }

    internal void Server_MarkPlayerInventoryEstablished(
        ServerPlayer player)
    {
        if (player?.Peer ==
            NetworkLifecycle.Instance.Server.SelfPeer)
        {
            return;
        }
        playerInventories?.MarkEstablished(player);
    }

    public JObject Server_GetPlayerData(SaveGameData data, Guid guid)
    {
        return data?.GetJObject(ROOT_KEY)?.GetJObject(PLAYERS_KEY)?.GetJObject(guid.ToString());
    }

    #endregion

    [UsedImplicitly]
    public new static string AllowAutoCreate()
    {
        return $"[{nameof(NetworkedSaveGameManager)}]";
    }
}
