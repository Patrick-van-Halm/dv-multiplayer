using Multiplayer.Components.Networking.World;
using Multiplayer.Networking.Data.Items;
using System.Collections.Generic;
using UnityEngine;
using Xunit;

namespace Multiplayer.Tests;

public class NetworkedItemSnapshotValidatorTests
{
    [Fact]
    public void TrackedPayload_RejectsNonFiniteValues()
    {
        var states = new Dictionary<string, object>
        {
            ["item.position"] =
                new Vector3(float.NaN, 0f, 0f),
        };

        Assert.False(
            NetworkedItemSnapshotValidator
                .IsSafeTrackedValuePayload(states));
    }

    [Fact]
    public void TrackedPayload_RejectsTooManyValues()
    {
        var states = new Dictionary<string, object>();
        for (int index = 0;
             index <= ItemPacketLimits.MaxTrackedValues;
             index++)
        {
            states[$"item.{index}"] = index;
        }

        Assert.False(
            NetworkedItemSnapshotValidator
                .IsSafeTrackedValuePayload(states));
    }

    [Fact]
    public void DestinationPolicy_RequiresOwnershipReachAndAvailability()
    {
        Assert.True(
            NetworkedItemDestinationAuthority
                .IsDestinationPolicySatisfied(
                    ownsItem: true,
                    targetExists: true,
                    targetReachable: true,
                    targetAvailable: true));
        Assert.False(
            NetworkedItemDestinationAuthority
                .IsDestinationPolicySatisfied(
                    ownsItem: false,
                    targetExists: true,
                    targetReachable: true,
                    targetAvailable: true));
        Assert.False(
            NetworkedItemDestinationAuthority
                .IsDestinationPolicySatisfied(
                    ownsItem: true,
                    targetExists: true,
                    targetReachable: false,
                    targetAvailable: true));
        Assert.False(
            NetworkedItemDestinationAuthority
                .IsDestinationPolicySatisfied(
                    ownsItem: true,
                    targetExists: true,
                    targetReachable: true,
                    targetAvailable: false));
    }

    [Fact]
    public void Rotation_RequiresFiniteNormalizedQuaternion()
    {
        Assert.True(
            NetworkedItemDestinationAuthority.IsValidRotation(
                Quaternion.identity));
        Assert.False(
            NetworkedItemDestinationAuthority.IsValidRotation(
                new Quaternion(0f, 0f, 0f, 0f)));
        Assert.False(
            NetworkedItemDestinationAuthority.IsValidRotation(
                new Quaternion(float.NaN, 0f, 0f, 1f)));
    }

    [Fact]
    public void ThrowDirection_IsBounded()
    {
        Assert.True(
            NetworkedItemDestinationAuthority
                .IsValidThrowDirection(
                    new Vector3(50f, 0f, 0f)));
        Assert.False(
            NetworkedItemDestinationAuthority
                .IsValidThrowDirection(
                    new Vector3(50.1f, 0f, 0f)));
    }
}
