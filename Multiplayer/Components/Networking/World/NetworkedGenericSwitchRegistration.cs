using DV.CabControls;
using DV.Interaction;
using Multiplayer.Components.Networking.World;
using Multiplayer.Components.Networking.Player;
using Multiplayer.Utils;
using System.Collections;
using UnityEngine;

namespace Multiplayer.Components.Networking.World;

internal static class NetworkedGenericSwitchRegistration
{
    public static void RegisterWhenReady(GenericSwitch genericSwitch)
    {
        Multiplayer.LogDebug(() => $"GenericSwitch.Constructor() persistenceKey: {genericSwitch.persistenceKey}");
        CoroutineManager.Instance.StartCoroutine(
            NetworkedRegistrationWait.Until(
                genericSwitch,
                () =>
                    !string.IsNullOrEmpty(
                        genericSwitch.persistenceKey) &&
                    genericSwitch.controlObject != null,
                () => Register(genericSwitch),
                $"generic switch '{genericSwitch.name}'"));
    }

    private static void Register(GenericSwitch genericSwitch)
    {
        Multiplayer.LogDebug(() => $"WaitForGenericSwitch() persistenceKey: {genericSwitch.persistenceKey}");

        genericSwitch.gameObject.GetOrAddComponent<NetworkedGenericSwitch>();
        ControlImplBase control =
            genericSwitch.controlObject.GetComponent<ControlImplBase>();
        if (control != null)
            ControlIKHandEventRelay.Attach(control);
    }
}
