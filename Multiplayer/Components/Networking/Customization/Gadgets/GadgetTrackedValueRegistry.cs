using DV.Customization.Gadgets;
using DV.Customization.Gadgets.Implementations;
using HarmonyLib;
using Multiplayer.Components.Networking.World;
using System.Reflection;
using TMPro;
using UnityEngine;

namespace Multiplayer.Components.Networking.Customization.Gadgets;

public static class GadgetTrackedValueRegistry
{
    private static readonly MethodInfo SetMountPointGlass = AccessTools.PropertySetter(typeof(MountPoint), nameof(MountPoint.IsOnGlass));

    public static void Register(NetworkedItem item, GadgetBase gadget)
    {
        if (item == null || gadget == null)
            return;

        item.RegisterTrackedValue("solder.units", () => gadget.SolderingProgressUnits,
            value => gadget.SetSolderingUnits(Mathf.Max(0, value)));

        RegisterDrillables(item, gadget);
        RegisterText(item, gadget);
        RegisterGadgetState(item, gadget);
    }

    private static void RegisterDrillables(NetworkedItem item, GadgetBase gadget)
    {
        Drillable[] drillables = gadget.GetComponentsInChildren<Drillable>(true);
        for (int componentIndex = 0; componentIndex < drillables.Length; componentIndex++)
        {
            Drillable drillable = drillables[componentIndex];
            for (int pointIndex = 0; pointIndex < drillable.MountPointCount; pointIndex++)
            {
                int pi = pointIndex;
                string prefix = $"drill.{componentIndex}.{pointIndex}";

                item.RegisterTrackedValue($"{prefix}.state", () => (int)drillable.GetMountPointState(pi),
                    value => drillable.SetMountPointState(pi, (MountPoint.States)value));
                item.RegisterTrackedValue($"{prefix}.glass", () => drillable.GetMountPoint(pi).IsOnGlass,
                    value => SetMountPointGlass?.Invoke(drillable.GetMountPoint(pi), new object[] { value }));
            }
        }
    }

    private static void RegisterText(NetworkedItem item, GadgetBase gadget)
    {
        FieldInfo textMeshField = AccessTools.Field(typeof(TextGadget), "textMesh");
        MethodInfo updateItemText = AccessTools.Method(typeof(TextGadget), "UpdateItemText");
        if (textMeshField == null)
            return;

        TextGadget[] textGadgets = gadget.GetComponentsInChildren<TextGadget>(true);
        for (int i = 0; i < textGadgets.Length; i++)
        {
            TextGadget text = textGadgets[i];
            int index = i;
            item.RegisterTrackedValue($"text.{index}.value",
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

    private static void RegisterGadgetState(NetworkedItem item, GadgetBase gadget)
    {
        switch (gadget)
        {
            case AlternatingController alternating:
                item.RegisterTrackedValue("gadget.alternating.interval", () => alternating.SelectedInterval,
                    value => alternating.SelectedInterval = value);
                break;
            case GadgetATS ats:
                item.RegisterTrackedValue("gadget.ats.regime", () => ats.Regime, ats.SetRegime);
                break;
            case GadgetAmpLimiter limiter:
                item.RegisterTrackedValue("gadget.amp.mode", () => limiter.ModeIndex,
                    value => limiter.ModeIndex = value);
                break;
            case GadgetDPU dpu:
                item.RegisterTrackedValue("gadget.dpu.regime", () => (int)dpu.Regime,
                    value => dpu.Regime = (GadgetDPU.WirelessMode)value);
                item.RegisterTrackedValue("gadget.dpu.reverse", () => dpu.ReverseOrientation,
                    value => dpu.ReverseOrientation = value);
                item.RegisterTrackedValue("gadget.dpu.channel", () => dpu.Channel,
                    value => dpu.Channel = value);
                break;
            case GadgetOverheatProtection overheat:
                item.RegisterTrackedValue("gadget.overheat.mode", () => overheat.ModeIndex,
                    value => overheat.ModeIndex = value);
                item.RegisterTrackedValue("gadget.overheat.cut-engine", () => overheat.cutEngine,
                    value => overheat.cutEngine = value);
                break;
            case GadgetControlPanel controlPanel:
                item.RegisterTrackedValue("gadget.control-panel.tilt", () => controlPanel.tilt,
                    value => controlPanel.tilt = value);
                break;
            case GadgetProximityScreen screen:
                item.RegisterTrackedValue("gadget.proximity.channel", () => screen.CurrentChannel,
                    value => screen.CurrentChannel = value);
                item.RegisterTrackedValue("gadget.proximity.mode", () => screen.CurrentMode,
                    value => screen.CurrentMode = value);
                break;
            case GadgetSwitchSetter switchSetter:
                item.RegisterTrackedValue("gadget.switch-setter.mode", () => switchSetter.Mode,
                    value => switchSetter.Mode = value);
                item.RegisterTrackedValue("gadget.switch-setter.side", () => switchSetter.SideCorrectRegime,
                    value => switchSetter.SideCorrectRegime = value);
                item.RegisterTrackedValue("gadget.switch-setter.direction", () => switchSetter.DirectionMode,
                    value => switchSetter.DirectionMode = value);
                break;
            case GadgetRoadrunner roadRunner:
                item.RegisterTrackedValue("gadget.roadrunner.target", () => roadRunner.LengthMeters,
                    value => roadRunner.LengthMeters = value);
                break;
        }
    }
}
