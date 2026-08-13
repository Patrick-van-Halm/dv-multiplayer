using HarmonyLib;
using Multiplayer.Components.Networking;
using Multiplayer.Components.Networking.Customization;
using Multiplayer.Components.Networking.World;
using Multiplayer.Networking.Data;
using Multiplayer.Networking.Data.Customization;
using Multiplayer.Networking.Managers.Client;
using Multiplayer.Networking.Managers.Server;
using Multiplayer.Networking.Packets.Serverbound;
using Multiplayer.Networking.TransportLayers;
using System.Collections;
using System.Reflection;

namespace Multiplayer.Patches.Customization;

[HarmonyPatch]
internal static class CustomizationClientJoinPatch
{
    private static readonly MethodInfo SendLoadStateUpdate = AccessTools.Method(typeof(NetworkClient), "SendLoadStateUpdate");
    private static bool waiting;
    private static bool allowItems;

    [HarmonyPostfix]
    [HarmonyPatch(typeof(NetworkClient), "Subscribe")]
    private static void SubscribeClient(NetworkClient __instance)
    {
        __instance.RegisterExternalSerializablePacket<ClientboundCustomizationStatePacket>(CustomizationSnapshotSync.Receive);
        GadgetStructuralSync.RegisterClient(__instance);
        GadgetMountSync.RegisterClient(__instance);
        GadgetWireSync.RegisterClient(__instance);
        CustomizationHoleSync.RegisterClient(__instance);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(NetworkServer), "Subscribe")]
    private static void SubscribeServer(NetworkServer __instance)
    {
        GadgetStructuralSync.RegisterServer(__instance);
        GadgetMountSync.RegisterServer(__instance);
        GadgetWireSync.RegisterServer(__instance);
        CustomizationHoleSync.RegisterServer(__instance);
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(NetworkClient), "SendLoadStateUpdate")]
    private static bool BeforeLoadState(NetworkClient __instance, [HarmonyArgument(0)] PlayerLoadingState newState)
    {
        if (newState != PlayerLoadingState.ReadyForItems || allowItems || NetworkLifecycle.Instance.IsHost())
            return true;
        CustomizationSnapshotSync.BeginJoin();
        waiting = true;
        SendLoadStateUpdate.Invoke(__instance, new object[] { PlayerLoadingState.ReadyForCustomizers });
        return false;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(NetworkClient), "SyncWorldState")]
    private static void Wrap(NetworkClient __instance, ref IEnumerator __result)
    {
        __result = Wait(__instance, __result);
    }

    private static IEnumerator Wait(NetworkClient client, IEnumerator original)
    {
        while (original.MoveNext())
        {
            object current = original.Current;
            if (waiting && !NetworkLifecycle.Instance.IsHost())
            {
                client.Log("Waiting for customization state");
                while (!CustomizationSnapshotSync.CustomizerStateLoaded)
                    yield return null;
                allowItems = true;
                try
                {
                    SendLoadStateUpdate.Invoke(client, new object[] { PlayerLoadingState.ReadyForItems });
                }
                finally
                {
                    allowItems = false;
                    waiting = false;
                }
            }
            yield return current;
        }
    }
}

[HarmonyPatch(typeof(NetworkServer), "OnServerboundLoadStateUpdatePacket")]
internal static class CustomizationServerJoinPatch
{
    [HarmonyPrefix]
    private static bool Prefix(NetworkServer __instance, ServerboundLoadStateUpdatePacket packet, ITransportPeer peer)
    {
        if (packet.LoadState != PlayerLoadingState.ReadyForCustomizers)
            return true;
        if (!__instance.TryGetServerPlayer(peer, out ServerPlayer player))
            return false;
        if (player.LoadingState != PlayerLoadingState.ReadyForTrainSets)
        {
            __instance.LogWarning($"Ignoring ReadyForCustomizers from {player.Username} while at {player.LoadingState}");
            return false;
        }

        var snapshot = CustomizationSnapshotSync.Build();
        uint tick = NetworkLifecycle.Instance.Tick;
        foreach (GadgetPlacementState placement in snapshot.Gadgets)
        {
            if (placement?.Item != null && NetworkedItem.TryGet(placement.Item.ItemNetId, out NetworkedItem item))
                player.KnownItems[item] = tick;
        }

        __instance.Log($"Sending customization state to {player.Username}: {snapshot.Gadgets.Count} gadgets, {snapshot.Mounts.Count} mounts, {snapshot.Wires.Count} wires, {snapshot.Holes.Count} free holes");
        CustomizationPacketSend.SendJoinState(__instance, peer, snapshot);
        player.LoadingState = PlayerLoadingState.ReadyForCustomizers;
        return false;
    }
}
