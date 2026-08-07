using Multiplayer.Utils;
using System;
using UnityEngine;

namespace Multiplayer.Components.Networking.World.Items;

internal static class FlashlightStateRegistration
{
    public static void Register(FlashlightItem flashlight)
    {
        if (flashlight == null)
            return;

        NetworkedItem networkedItem =
            flashlight.gameObject.GetOrAddComponent<NetworkedItem>();
        networkedItem.Initialize(flashlight);

        networkedItem.RegisterTrackedValue(
            "originalLightIntensity",
            () => flashlight.originalLightIntensity,
            value => flashlight.originalLightIntensity = value,
            serverAuthoritative: true);
        networkedItem.RegisterTrackedValue(
            "originalBeamColour",
            () => flashlight.originalBeamColor.ColorToUInt32(),
            value =>
                flashlight.originalBeamColor =
                    value.UInt32ToColor(),
            serverAuthoritative: true);
        networkedItem.RegisterTrackedValue(
            "beamColour",
            () => flashlight.beamController
                .GetBeamColor()
                .ColorToUInt32(),
            value => SetBeamColour(flashlight, value),
            serverAuthoritative: true);
        networkedItem.RegisterTrackedValue(
            "batteryPower",
            () => flashlight.battery.currentPower,
            value => SetBatteryPower(flashlight.battery, value),
            (current, last) =>
                Math.Abs(current - last) >= 1f,
            serverAuthoritative: true);
        networkedItem.RegisterTrackedValue(
            "buttonState",
            () => flashlight.button.Value > 0f,
            value => SetButtonState(flashlight, value));
        networkedItem.FinaliseTrackedValues();

        EnforceBatteryAuthority(flashlight);
    }

    public static void OnFlashlightToggled(
        FlashlightItem flashlight)
    {
        EnforceBatteryAuthority(flashlight);
    }

    internal static bool ShouldSimulateBattery(
        bool isClientRunning,
        bool isServerRunning)
    {
        return !isClientRunning || isServerRunning;
    }

    private static void EnforceBatteryAuthority(
        FlashlightItem flashlight)
    {
        NetworkLifecycle lifecycle = NetworkLifecycle.Instance;
        if (flashlight?.batteryConsumer == null ||
            ShouldSimulateBattery(
                lifecycle?.IsClientRunning ?? false,
                lifecycle?.IsServerRunning ?? false))
        {
            return;
        }

        // BatteryConsumer derives drain from client weather time. Small
        // backwards time corrections look like a midnight wrap to the game
        // and consume almost a full day. Clients display replicated battery
        // state; only the host advances it.
        flashlight.batteryConsumer.TogglePowerConsumption(false);
    }

    private static void SetBeamColour(
        FlashlightItem flashlight,
        uint packedColour)
    {
        Color colour = packedColour.UInt32ToColor();
        flashlight.beamController.SetBeamColor(colour);
        flashlight.spotlight.color = colour;
    }

    private static void SetBatteryPower(
        Battery battery,
        float power)
    {
        battery.currentPower = power;
        battery.UpdatePower(0f);
    }

    private static void SetButtonState(
        FlashlightItem flashlight,
        bool enabled)
    {
        flashlight.button.SetValue(enabled ? 1f : 0f);
        flashlight.ToggleFlashlight(enabled);
    }
}
