using DV.Common;
using DV.Customization;
using DV.Customization.Gadgets;
using DV.Customization.Gadgets.Implementations;
using DV.Items;
using DV.Player;
using Multiplayer.Components.Networking;
using Multiplayer.Components.Networking.Train;
using Multiplayer.Components.Networking.World;
using Multiplayer.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using UnityEngine;

namespace Multiplayer.Components.Networking.World.Items;

internal sealed class SolderingToolSyncState : MonoBehaviour
{
    private Coroutine pendingApply;

    public void SetRemainingUnits(GadgetSolderingTool tool, int units)
    {
        Apply(tool, units);

        if (pendingApply != null)
            StopCoroutine(pendingApply);
        pendingApply = StartCoroutine(ReapplyAfterSpool(tool, units));
    }

    private IEnumerator ReapplyAfterSpool(GadgetSolderingTool tool, int units)
    {
        // Container snapshots may arrive after the tool snapshot. Loading that
        // spool calls ReloadResource(), so reapply the authoritative remaining
        // amount after magazine membership has settled.
        if (units != 0)
        {
            const int maxFrames = 120;
            for (int frame = 0;
                 frame < maxFrames && tool.magazine[0] == null;
                 frame++)
            {
                yield return null;
            }
        }

        yield return null;
        Apply(tool, units);
        pendingApply = null;
    }

    private static void Apply(GadgetSolderingTool tool, int units)
    {
        tool.remainingUnits = units;
        tool.OnUnitsChanged();
    }
}
