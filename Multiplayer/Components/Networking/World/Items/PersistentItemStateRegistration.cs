using DV;
using DV.CabControls;
using DV.Customization.Gadgets;
using DV.Customization.Gadgets.Implementations;
using DV.Interaction;
using DV.Items;
using DV.Items.Brick;
using DV.TimeKeeping;
using DV.Utils;
using Multiplayer.Components.Networking;
using Multiplayer.Components.Networking.World;
using Multiplayer.Components.Networking.World.Items;
using Multiplayer.Utils;
using System;
using System.Collections;
using UnityEngine;

namespace Multiplayer.Components.Networking.World.Items;

internal static class PageBookStateRegistration
{
    public static void Register(PageBook __instance)
    {
        NetworkedItem item = ItemStateTracking.For(__instance);
        item.RegisterTrackedValue(
            "page.current",
            () => __instance.currentPage,
            value =>
            {
                if (__instance.PagesGenerated)
                    __instance.ForceCurrentPage(value);
                else
                    __instance.currentPage = value;
            });
        item.FinaliseTrackedValues();
    }
}

internal static class LabelableItemStateRegistration
{
    public static void Register(LabelableItem __instance)
    {
        NetworkedItem item = ItemStateTracking.For(__instance);
        item.RegisterTrackedValue(
            "label.text",
            () => __instance.Text ?? string.Empty,
            __instance.UpdateText);
        item.FinaliseTrackedValues();
    }
}

internal static class DrillToolStateRegistration
{
    public static void Register(DrillTool __instance)
    {
        NetworkedItem item = ItemStateTracking.For(__instance);
        RemoteDrillPoseState pose =
            __instance.gameObject.GetOrAddComponent<RemoteDrillPoseState>();
        pose.Initialize(__instance, item);

        item.RegisterTrackedValue(
            "drill.pressed",
            () => __instance.IsPressed,
            value => SetPressed(__instance, value));
        item.RegisterTrackedValue(
            "drill.targetValid",
            () => __instance.TargetIsValid,
            value => __instance.TargetIsValid = value);
        item.RegisterTrackedValue(
            "drill.progress",
            () => __instance.ProcessingProgress,
            value => __instance.ProcessingProgress = value,
            (current, last) => Math.Abs(current - last) >= 0.02f);
        item.RegisterTrackedValue(
            "drill.poseActive",
            pose.GetActive,
            pose.SetActive);
        item.RegisterTrackedValue(
            "drill.targetPosition",
            pose.GetPosition,
            pose.SetPosition,
            (current, last) => (current - last).sqrMagnitude >= 0.0001f);
        item.RegisterTrackedValue(
            "drill.targetRotation",
            pose.GetRotation,
            pose.SetRotation,
            (current, last) => Quaternion.Angle(current, last) >= 0.1f);
        item.FinaliseTrackedValues();
    }

    private static void SetPressed(DrillTool drill, bool pressed)
    {
        drill.IsPressed = pressed;
        if (!pressed || !drill.TryGetComponent(out DrillEffects effects))
            return;

        PlayIfStopped(effects.sourceMotor);
        PlayIfStopped(effects.sourceDrill);
        PlayIfStopped(effects.sourceAirflow);
    }

    private static void PlayIfStopped(AudioSource source)
    {
        if (source != null && !source.isPlaying)
            source.Play();
    }
}

internal static class CassetteStateRegistration
{
    public static void Register(Cassette __instance)
    {
        MagazineAmmoTracking.Register(__instance);
        NetworkedItem item = ItemStateTracking.For(__instance);
        item.RegisterTrackedValue(
            "cassette.track",
            () => __instance.lastPlayedPlaylistEntry,
            value => __instance.lastPlayedPlaylistEntry = value);
        item.FinaliseTrackedValues();
    }
}

internal static class PaintCanStateRegistration
{
    public static void Register(PaintCan __instance) => MagazineAmmoTracking.Register(__instance);
}

internal static class SolderingResourceStateRegistration
{
    public static void Register(GadgetSolderingResource __instance) => MagazineAmmoTracking.Register(__instance);
}

internal static class AnalogAlarmClockStateRegistration
{
    public static void Register(AnalogAlarmClock __instance)
    {
        __instance.StartCoroutine(
            NetworkedRegistrationWait.Until(
                __instance,
                () => __instance.initialized,
                () => RegisterValues(__instance),
                $"analog alarm clock '{__instance.name}'"));
    }

    private static void RegisterValues(AnalogAlarmClock __instance)
    {
        NetworkedItem item = ItemStateTracking.For(__instance);
        item.RegisterTrackedValue(
            "alarm.minutes",
            () => __instance.alarmTimeInMinutes,
            value => SetMinutes(__instance, value));
        item.RegisterTrackedValue(
            "alarm.armed",
            () => __instance.alarmSet,
            value => SetArmed(__instance, value));
        item.FinaliseTrackedValues();
    }

    private static void SetMinutes(AnalogAlarmClock clock, int value)
    {
        clock.alarmTimeInMinutes = value;
        clock.ClampAlarmTime();
        clock.SetAlarmTime();
    }

    private static void SetArmed(AnalogAlarmClock clock, bool value)
    {
        if (clock.alarmSet != value)
            clock.OnButtonPressed();
    }
}

internal static class BoomboxStateRegistration
{
    public static void Register(BoomboxInteractionController __instance)
    {
        __instance.StartCoroutine(
            NetworkedRegistrationWait.Until(
                __instance,
                () => __instance.initialized,
                () => RegisterValues(__instance),
                $"boombox '{__instance.name}'"));
    }

    private static void RegisterValues(
        BoomboxInteractionController __instance)
    {
        NetworkedItem item = ItemStateTracking.For(__instance);
        item.RegisterTrackedValue("boombox.power", () => __instance.IsPoweredOn, __instance.SetPower);
        item.RegisterTrackedValue("boombox.radioMode", () => __instance.IsInRadioMode, __instance.SetMode);
        item.RegisterTrackedValue(
            "boombox.volume",
            () => __instance.volumeAndInterferenceController.GetVolume(),
            __instance.SetVolume,
            (current, last) => Math.Abs(current - last) >= 0.01f);
        item.RegisterTrackedValue(
            "boombox.antenna",
            () => __instance.AntennaPosition,
            __instance.SetAntenna,
            (current, last) => Math.Abs(current - last) >= 0.01f);
        item.RegisterTrackedValue(
            "boombox.station",
            () => __instance.radioController.lastPlayedStationIndex,
            __instance.OverrideLastPlayedStationIndex);
        item.RegisterTrackedValue(
            "boombox.door",
            () => __instance.HasDoorOpen,
            value => SetDoor(__instance, value));
        item.RegisterTrackedValue(
            "boombox.track",
            () => __instance.cassetteController.CurrentPlaylistIndex,
            value =>
            {
                Cassette cassette = __instance.cassetteInteractionAreaScript.GetInsertedCassette();
                if (cassette == null)
                    return;

                cassette.lastPlayedPlaylistEntry = value;
                __instance.cassetteController.player?.SetSeekPosition(value, 0L);
            });
        item.RegisterTrackedValue(
            "boombox.playing",
            () => __instance.cassetteController.IsPlaying,
            value => SetPlaying(__instance, value));
        item.FinaliseTrackedValues();
    }

    private static void SetDoor(BoomboxInteractionController boombox, bool open)
    {
        if (open)
            boombox.cassetteInteractionAreaScript.OpenDoor();
        else
            boombox.CassetteDoorClose();
    }

    private static void SetPlaying(BoomboxInteractionController boombox, bool playing)
    {
        if (playing)
        {
            if (boombox.HasCassetteInserted)
                boombox.CassettePlay();
            else
                boombox.StartCoroutine(PlayWhenCassetteArrives(boombox));
        }
        else if (boombox.cassetteController.IsPlaying)
            boombox.CassettePause();
    }

    private static IEnumerator PlayWhenCassetteArrives(BoomboxInteractionController boombox)
    {
        const int maxFrames = 120;
        for (int frame = 0; frame < maxFrames && !boombox.HasCassetteInserted; frame++)
            yield return null;

        if (boombox.HasCassetteInserted && !boombox.cassetteController.IsPlaying)
            boombox.CassettePlay();
    }
}

internal static class LocomotiveRemoteStateRegistration
{
    public static void Register(LocomotiveRemoteController __instance)
    {
        __instance.StartCoroutine(
            NetworkedRegistrationWait.Until(
                __instance,
                () => __instance.initialized,
                () => RegisterValues(__instance),
                $"locomotive remote '{__instance.name}'"));
    }

    private static void RegisterValues(
        LocomotiveRemoteController remote)
    {
        NetworkedItem item = ItemStateTracking.For(remote);
        item.RegisterTrackedValue(
            "locoRemote.power",
            () => remote.isOn,
            value =>
            {
                remote.isOn = value;
                remote.TogglePower(value, false);
            });
        item.RegisterTrackedValue(
            "locoRemote.battery",
            () => remote.battery.CurrentPower,
            value => SetBattery(remote.battery, value),
            (current, last) => Math.Abs(current - last) >= 1f);
        item.RegisterTrackedValue(
            "locoRemote.pairedLoco",
            () => remote.pairedLocomotive is Component ? remote.pairedLocomotive.GetLocoGuid() : string.Empty,
            value => SetPairedLocomotive(remote, value),
            serverAuthoritative: true);
        item.RegisterTrackedValue(
            "locoRemote.coupler",
            () => remote.selectedCoupler,
            value =>
            {
                remote.selectedCoupler = value;
                remote.UpdateCouplerSelection(0);
            });
        item.FinaliseTrackedValues();
    }

    private static void SetBattery(Battery battery, float power)
    {
        float difference = power - battery.CurrentPower;
        if (difference > 0f)
            battery.Charge(difference);
        else if (difference < 0f)
            battery.Drain(-difference);
    }

    private static void SetPairedLocomotive(LocomotiveRemoteController remote, string locoGuid)
    {
        string currentGuid = remote.pairedLocomotive is Component
            ? remote.pairedLocomotive.GetLocoGuid()
            : string.Empty;
        if (currentGuid == locoGuid)
            return;

        if (string.IsNullOrEmpty(locoGuid))
        {
            NetworkedLocomotiveRemotePairing.ApplyAuthoritative(
                remote,
                null);
            return;
        }

        TrainCar car = SingletonBehaviour<TrainCarRegistry>.Instance.GetTrainCarByCarGuid(locoGuid);
        if (car != null)
        {
            NetworkedLocomotiveRemotePairing.ApplyAuthoritative(
                remote,
                car);
        }
        else
            remote.StartCoroutine(RetryPairing(remote, locoGuid));
    }

    private static IEnumerator RetryPairing(LocomotiveRemoteController remote, string locoGuid)
    {
        const int maxFrames = 120;
        for (int frame = 0; frame < maxFrames; frame++)
        {
            yield return null;
            TrainCar car = SingletonBehaviour<TrainCarRegistry>.Instance.GetTrainCarByCarGuid(locoGuid);
            if (car == null)
                continue;

            NetworkedLocomotiveRemotePairing.ApplyAuthoritative(
                remote,
                car);
            yield break;
        }

        Multiplayer.LogWarning($"Timed out resolving paired locomotive '{locoGuid}' for {remote.name}.");
    }
}

internal static class ProximitySensorStateRegistration
{
    public static void Register(ProximitySensor __instance)
    {
        NetworkedItem item = ItemStateTracking.For(__instance);
        item.RegisterTrackedValue(
            "proximity.channel",
            () => __instance.rotaryChannel.Value,
            value => __instance.rotaryChannel.SetValue(value, ControlImplBase.SetValueSource.Default));
        item.RegisterTrackedValue(
            "proximity.range",
            () => __instance.rotaryRange.Value,
            value => __instance.rotaryRange.SetValue(value, ControlImplBase.SetValueSource.Default));
        item.FinaliseTrackedValues();
    }
}

internal static class CommsRadioStateRegistration
{
    public static void Register(CommsRadioController __instance)
    {
        NetworkedItem item = ItemStateTracking.For(__instance);
        item.RegisterTrackedValue(
            "comms.mode",
            () => __instance.activeModeIndex,
            value => SetMode(__instance, value));
        item.FinaliseTrackedValues();
    }

    private static void SetMode(CommsRadioController radio, int index)
    {
        if (radio.allModes == null || index < 0 || index >= radio.allModes.Count)
            return;

        radio.activeModeIndex = index;
        radio.SetMode(radio.allModes[index]);
    }
}

internal static class BrickConsoleStateRegistration
{
    public static void Register(BrickConsole __instance)
    {
        BrickSyncState state = __instance.gameObject.GetOrAddComponent<BrickSyncState>();
        state.HasNetworkBaseline = NetworkLifecycle.Instance.IsHost();
        NetworkedItem item = ItemStateTracking.For(__instance);
        item.RegisterTrackedValue(
            "brick.event",
            () => state.EventValue,
            value => ApplyEvent(__instance, state, value));
        item.FinaliseTrackedValues();
    }

    public static void CaptureInput(
        BrickConsole __instance,
        BrickInput.BrickInputAction brickInputAction)
    {
        BrickSyncState state = __instance.GetComponent<BrickSyncState>();
        if (state == null || state.Applying)
            return;

        state.LastAction = (int)brickInputAction;
        state.Sequence++;
        state.HasNetworkBaseline = true;
    }

    private static void ApplyEvent(
        BrickConsole console,
        BrickSyncState state,
        string serialized)
    {
        if (!BrickSyncEvent.TryParse(
                serialized,
                out BrickSyncEvent value))
            return;

        bool shouldApply =
            state.HasNetworkBaseline &&
            value.Sequence > state.Sequence;
        state.Sequence = Math.Max(state.Sequence, value.Sequence);
        state.LastAction = value.Action;
        state.HasNetworkBaseline = true;
        if (!shouldApply ||
            !Enum.IsDefined(
                typeof(BrickInput.BrickInputAction),
                value.Action))
        {
            return;
        }

        state.Applying = true;
        try
        {
            console.ExecuteInputAction(
                (BrickInput.BrickInputAction)value.Action);
        }
        finally
        {
            state.Applying = false;
        }
    }
}
