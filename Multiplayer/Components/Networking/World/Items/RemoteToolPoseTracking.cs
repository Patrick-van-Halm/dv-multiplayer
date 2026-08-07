using UnityEngine;

namespace Multiplayer.Components.Networking.World.Items;

internal static class RemoteToolPoseTracking
{
    public static void Register(
        NetworkedItem item,
        RemoteToolAnimationState state,
        string prefix)
    {
        item.RegisterTrackedValue(
            $"{prefix}.poseActive",
            state.GetPoseActive,
            state.SetPoseActive);
        item.RegisterTrackedValue(
            $"{prefix}.targetPosition",
            state.GetTargetPosition,
            state.SetTargetPosition,
            (current, last) =>
                (current - last).sqrMagnitude >= 0.0001f);
        item.RegisterTrackedValue(
            $"{prefix}.targetRotation",
            state.GetTargetRotation,
            state.SetTargetRotation,
            (current, last) =>
                Quaternion.Angle(current, last) >= 0.1f);
    }
}
