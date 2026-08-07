using DV.CabControls;
using HarmonyLib;
using System;

namespace Multiplayer.Components.Networking.World.Items;

internal static class CabItemRigidbodyCompatibility
{
    private static readonly AccessTools.FieldRef<
        CabItemRigidbody,
        bool> AssumeIsPaused = Bind();
    private static bool runtimeFailureLogged;

    public static bool TrySetAssumeIsPaused(
        CabItemRigidbody body,
        bool value)
    {
        if (body == null || AssumeIsPaused == null)
            return false;

        try
        {
            AssumeIsPaused(body) = value;
            return true;
        }
        catch (Exception exception)
        {
            if (!runtimeFailureLogged)
            {
                runtimeFailureLogged = true;
                Multiplayer.LogWarning(
                    "Disabling remote inventory pause compatibility: " +
                    exception.Message);
            }
            return false;
        }
    }

    private static AccessTools.FieldRef<CabItemRigidbody, bool>
        Bind()
    {
        try
        {
            return AccessTools.FieldRefAccess<
                CabItemRigidbody,
                bool>("assumeIsPaused");
        }
        catch (Exception exception)
        {
            Multiplayer.LogWarning(
                "Could not bind CabItemRigidbody.assumeIsPaused; " +
                "remote inventory pause compatibility is disabled. " +
                exception.Message);
            return null;
        }
    }
}
