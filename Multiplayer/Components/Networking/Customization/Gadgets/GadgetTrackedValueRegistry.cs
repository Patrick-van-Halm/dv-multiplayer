using DV.Customization.Gadgets;
using DV.Customization.Gadgets.Implementations;
using HarmonyLib;
using Multiplayer.Components.Networking.World;
using System;
using System.Reflection;
using TMPro;
using UnityEngine;

namespace Multiplayer.Components.Networking.Customization.Gadgets;

public static class GadgetTrackedValueRegistry
{
    public static void Register(NetworkedItem item, GadgetItem gadgetItem, GadgetBase gadget)
    {
        if (item == null || gadgetItem == null || gadget == null)
            return;

        RegisterSoldering(item, gadget);
        RegisterDrillables(item, gadget);
        RegisterText(item, gadget);
        RegisterConcrete(item, gadget);
    }

    private static void RegisterSoldering(NetworkedItem item, GadgetBase gadget)
    {
        item.RegisterTrackedValue(
            "solder.units",
            () => gadget.SolderingProgressUnits,
            value => gadget.SetSolderingUnits(Mathf.Max(0, value)));
    }

    private static void RegisterDrillables(NetworkedItem item, GadgetBase gadget)
    {
        Drillable[] drillables = gadget.GetComponentsInChildren<Drillable>(true);
        for (int componentIndex = 0; componentIndex < drillables.Length; componentIndex++)
        {
            Drillable drillable = drillables[componentIndex];
            for (int pointIndex = 0; pointIndex < drillable.MountPointCount; pointIndex++)
            {
                int ci = componentIndex;
                int pi = pointIndex;
                item.RegisterTrackedValue(
                    $"drill.{ci}.{pi}.state",
                    () => (int)drillable.GetMountPointState(pi),
                    value => drillable.SetMountPointState(pi, (MountPoint.States)value));

                FieldInfo glassField = AccessTools.Property(typeof(MountPoint), nameof(MountPoint.IsOnGlass))?.GetSetMethod(true) != null
                    ? AccessTools.Field(typeof(MountPoint), "<IsOnGlass>k__BackingField")
                    : null;
                if (glassField != null)
                {
                    item.RegisterTrackedValue(
                        $"drill.{ci}.{pi}.glass",
                        () => drillable.GetMountPoint(pi).IsOnGlass,
                        value => glassField.SetValue(drillable.GetMountPoint(pi), value));
                }
            }
        }
    }

    private static void RegisterText(NetworkedItem item, GadgetBase gadget)
    {
        TextGadget[] textGadgets = gadget.GetComponentsInChildren<TextGadget>(true);
        FieldInfo textMeshField = AccessTools.Field(typeof(TextGadget), "textMesh");
        MethodInfo updateItemText = AccessTools.Method(typeof(TextGadget), "UpdateItemText");
        if (textMeshField == null)
            return;

        for (int componentIndex = 0; componentIndex < textGadgets.Length; componentIndex++)
        {
            int ci = componentIndex;
            TextGadget text = textGadgets[componentIndex];
            item.RegisterTrackedValue(
                $"text.{ci}.value",
                () => ((TextMeshPro)textMeshField.GetValue(text))?.text ?? string.Empty,
                value =>
                {
                    TextMeshPro mesh = (TextMeshPro)textMeshField.GetValue(text);
                    if (mesh != null)
                        mesh.text = value ?? string.Empty;
                    updateItemText?.Invoke(text, null);
                });
        }
    }

    private static void RegisterConcrete(NetworkedItem item, GadgetBase gadget)
    {
        switch (gadget)
        {
            case AlternatingController alternating:
                item.RegisterTrackedValue("gadget.alternating.interval", () => alternating.SelectedInterval,
                    value => alternating.SelectedInterval = Mathf.Clamp(value, 0, Math.Max(0, alternating.IntervalCount - 1)));
                break;
            case GadgetATS ats:
                item.RegisterTrackedValue("gadget.ats.regime", () => ats.Regime,
                    value => ats.SetRegime(Mathf.Clamp(value, 0, Math.Max(0, ats.RegimesCount - 1))));
                break;
            case GadgetAmpLimiter limiter:
                item.RegisterTrackedValue("gadget.amp.mode", () => limiter.ModeIndex,
                    value => limiter.ModeIndex = Mathf.Clamp(value, 0, Math.Max(0, limiter.ModeCount - 1)));
                break;
            case GadgetDPU dpu:
                item.RegisterTrackedValue("gadget.dpu.regime", () => (int)dpu.Regime,
                    value => dpu.Regime = (GadgetDPU.WirelessMode)value);
                item.RegisterTrackedValue("gadget.dpu.reverse", () => dpu.ReverseOrientation,
                    value => dpu.ReverseOrientation = value);
                item.RegisterTrackedValue("gadget.dpu.channel", () => dpu.Channel,
                    value => dpu.Channel = Mathf.Clamp(value, 0, 7));
                break;
            case GadgetOverheatProtection overheat:
                item.RegisterTrackedValue("gadget.overheat.mode", () => overheat.ModeIndex,
                    value => overheat.ModeIndex = Mathf.Clamp(value, 0, Math.Max(0, overheat.ModeCount - 1)));
                RegisterPrivateBool(item, "gadget.overheat.cut-engine", overheat, "cutEngine");
                break;
            case GadgetControlPanel controlPanel:
                RegisterPrivateFloat(item, "gadget.control-panel.tilt", controlPanel, "tilt");
                break;
            case GadgetProximityScreen screen:
                item.RegisterTrackedValue("gadget.proximity.channel", () => screen.CurrentChannel,
                    value => screen.CurrentChannel = Mathf.Clamp(value, 0, GadgetProximityScreen.CHANNEL_COUNT - 1));
                item.RegisterTrackedValue("gadget.proximity.mode", () => screen.CurrentMode,
                    value => screen.CurrentMode = Mathf.Clamp(value, 0, 1));
                break;
            case GadgetSwitchSetter switchSetter:
                item.RegisterTrackedValue("gadget.switch-setter.mode", () => switchSetter.Mode,
                    value => switchSetter.Mode = Mathf.Clamp(value, 0, Math.Max(0, switchSetter.ModeCount - 1)));
                item.RegisterTrackedValue("gadget.switch-setter.side", () => switchSetter.SideCorrectRegime,
                    value => switchSetter.SideCorrectRegime = value);
                item.RegisterTrackedValue("gadget.switch-setter.direction", () => switchSetter.DirectionMode,
                    value => switchSetter.DirectionMode = Mathf.Clamp(value, 0, 2));
                break;
            case GadgetRoadrunner roadRunner:
                item.RegisterTrackedValue("gadget.roadrunner.target", () => roadRunner.LengthMeters,
                    value => roadRunner.LengthMeters = Mathf.Clamp(value, 0, roadRunner.MaxLength));
                break;
        }
    }

    private static void RegisterPrivateBool(NetworkedItem item, string key, object target, string fieldName)
    {
        FieldInfo field = AccessTools.Field(target.GetType(), fieldName);
        if (field?.FieldType == typeof(bool))
            item.RegisterTrackedValue(key, () => (bool)field.GetValue(target), value => field.SetValue(target, value));
    }

    private static void RegisterPrivateFloat(NetworkedItem item, string key, object target, string fieldName)
    {
        FieldInfo field = AccessTools.Field(target.GetType(), fieldName);
        if (field == null)
            return;
        if (field.FieldType == typeof(float))
            item.RegisterTrackedValue(key, () => (float)field.GetValue(target), value => field.SetValue(target, value));
        else if (field.FieldType == typeof(double))
            item.RegisterTrackedValue(key, () => (float)(double)field.GetValue(target), value => field.SetValue(target, (double)value));
    }
}
