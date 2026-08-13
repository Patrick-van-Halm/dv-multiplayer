using HarmonyLib;
using Multiplayer.Components.Networking;
using Multiplayer.Components.Networking.Customization;
using Multiplayer.Networking.Data;
using Multiplayer.Networking.Managers.Client;
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
    private static void Subscribe(NetworkClient __instance) => CustomizationNetworkSync.RegisterClient(__instance);

    [HarmonyPrefix]
    [HarmonyPatch(typeof(NetworkClient), "SendLoadStateUpdate")]
    private static bool BeforeLoadState(NetworkClient __instance, [HarmonyArgument(0)] PlayerLoadingState newState)
    {
        if (newState != PlayerLoadingState.ReadyForItems || allowItems || NetworkLifecycle.Instance.IsHost())
            return true;

        CustomizationNetworkSync.BeginCustomizerJoin();
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
                while (!CustomizationNetworkSync.CustomizerStateLoaded)
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
