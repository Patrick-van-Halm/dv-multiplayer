using Multiplayer.Components.Networking.World;
using Multiplayer.Networking.Data.Items;
using System;
using Xunit;

namespace Multiplayer.Tests;

public class NetworkedItemRequestTrackerTests
{
    [Fact]
    public void TryConsumeCreationRequest_LimitsAndResetsWindow()
    {
        var tracker = new NetworkedItemRequestTracker();
        Guid player = Guid.NewGuid();

        for (int index = 0; index < 8; index++)
            Assert.True(tracker.TryConsumeCreationRequest(player, 10f));

        Assert.False(tracker.TryConsumeCreationRequest(player, 10.5f));
        Assert.True(tracker.TryConsumeCreationRequest(player, 11f));
    }

    [Fact]
    public void CreationResponse_IsCachedByPlayerAndRequest()
    {
        var tracker = new NetworkedItemRequestTracker();
        Guid player = Guid.NewGuid();
        var expected = new ItemUpdateData { CreationRequestId = 42 };

        tracker.CacheCreationResponse(player, expected);

        Assert.True(
            tracker.TryGetCreationResponse(
                player,
                42,
                out ItemUpdateData actual));
        Assert.Same(expected, actual);
        Assert.False(
            tracker.TryGetCreationResponse(
                Guid.NewGuid(),
                42,
                out _));
    }

    [Fact]
    public void RemovePlayer_ClearsRateAndCachedResponse()
    {
        var tracker = new NetworkedItemRequestTracker();
        Guid player = Guid.NewGuid();
        var response = new ItemUpdateData { CreationRequestId = 42 };

        for (int index = 0; index < 8; index++)
            Assert.True(tracker.TryConsumeCreationRequest(player, 10f));
        tracker.CacheCreationResponse(player, response);

        tracker.RemovePlayer(player);

        Assert.True(tracker.TryConsumeCreationRequest(player, 10f));
        Assert.False(tracker.TryGetCreationResponse(player, 42, out _));
    }

    [Fact]
    public void CreationRetrySchedule_IsRateLimitedAndBounded()
    {
        var schedule = new CreationRetrySchedule();

        Assert.Equal(
            PendingCreationDecision.Attempt,
            schedule.GetDecision(0f));
        Assert.Equal(
            PendingCreationDecision.Waiting,
            schedule.GetDecision(0.5f));
        for (int attempt = 1;
             attempt < NetworkedItemRequestTracker.MaxPendingCreationAttempts;
             attempt++)
        {
            Assert.Equal(
                PendingCreationDecision.Attempt,
                schedule.GetDecision(attempt));
        }

        Assert.Equal(
            PendingCreationDecision.Exhausted,
            schedule.GetDecision(100f));
    }
}
