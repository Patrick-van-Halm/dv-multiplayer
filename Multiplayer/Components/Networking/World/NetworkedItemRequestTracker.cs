using Multiplayer.Networking.Data.Items;
using System;
using System.Collections.Generic;

namespace Multiplayer.Components.Networking.World;

internal sealed class NetworkedItemRequestTracker
{
    private const int MaxCreationRequestsPerSecond = 8;
    private const int MaxCachedCreationResponses = 1024;
    internal const int MaxPendingCreationAttempts = 8;
    internal const float PendingCreationRetryInterval = 1f;

    private readonly Dictionary<(Guid PlayerGuid, uint RequestId), ItemUpdateData>
        creationResponses = [];
    private readonly Queue<(Guid PlayerGuid, uint RequestId)>
        creationResponseOrder = new();
    private readonly Dictionary<Guid, CreationRateState> creationRates = [];
    private readonly Dictionary<uint, PendingCreation> pendingClientCreations = [];
    private readonly Dictionary<uint, NetworkedItem> expiredClientCreations = [];

    private sealed class PendingCreation
    {
        public NetworkedItem Item;
        public readonly CreationRetrySchedule Retry = new();
    }

    private sealed class CreationRateState
    {
        public float WindowStart;
        public int Count;
    }

    public bool TryGetCreationResponse(
        Guid playerGuid,
        uint requestId,
        out ItemUpdateData response)
    {
        return creationResponses.TryGetValue(
            (playerGuid, requestId),
            out response);
    }

    public bool TryConsumeCreationRequest(Guid playerGuid, float now)
    {
        if (!creationRates.TryGetValue(
                playerGuid,
                out CreationRateState rate))
        {
            rate = new CreationRateState { WindowStart = now };
            creationRates[playerGuid] = rate;
        }

        if (now - rate.WindowStart >= 1f)
        {
            rate.WindowStart = now;
            rate.Count = 0;
        }

        if (rate.Count >= MaxCreationRequestsPerSecond)
            return false;

        rate.Count++;
        return true;
    }

    public void CacheCreationResponse(
        Guid playerGuid,
        ItemUpdateData response)
    {
        var key = (playerGuid, response.CreationRequestId);
        creationResponses[key] = response;
        creationResponseOrder.Enqueue(key);

        while (creationResponseOrder.Count >
               MaxCachedCreationResponses)
        {
            creationResponses.Remove(creationResponseOrder.Dequeue());
        }
    }

    public PendingCreationDecision TrackPendingCreation(
        uint requestId,
        NetworkedItem item,
        float now)
    {
        if (!pendingClientCreations.TryGetValue(
                requestId,
                out PendingCreation pending) ||
            pending.Item != item)
        {
            pending = new PendingCreation { Item = item };
            pendingClientCreations[requestId] = pending;
            expiredClientCreations.Remove(requestId);
        }

        PendingCreationDecision decision =
            pending.Retry.GetDecision(now);
        if (decision == PendingCreationDecision.Exhausted)
        {
            pendingClientCreations.Remove(requestId);
            if (!expiredClientCreations.ContainsKey(requestId) &&
                expiredClientCreations.Count >=
                MaxCachedCreationResponses)
            {
                uint expiredRequestToRemove = 0;
                foreach (uint expiredRequest in
                         expiredClientCreations.Keys)
                {
                    expiredRequestToRemove = expiredRequest;
                    break;
                }
                expiredClientCreations.Remove(
                    expiredRequestToRemove);
            }
            expiredClientCreations[requestId] = pending.Item;
        }

        return decision;
    }

    public bool TryTakePendingCreation(
        uint requestId,
        out NetworkedItem item)
    {
        if (pendingClientCreations.TryGetValue(
                requestId,
                out PendingCreation pending))
        {
            pendingClientCreations.Remove(requestId);
            item = pending.Item;
            return true;
        }

        if (expiredClientCreations.TryGetValue(
                requestId,
                out item))
        {
            expiredClientCreations.Remove(requestId);
            return item != null;
        }

        item = null;
        return false;
    }

    public void PruneUnavailablePendingCreations()
    {
        var unavailableRequests = new List<uint>();
        foreach (var pending in pendingClientCreations)
        {
            if (pending.Value.Item == null)
                unavailableRequests.Add(pending.Key);
        }

        foreach (uint requestId in unavailableRequests)
            pendingClientCreations.Remove(requestId);

        unavailableRequests.Clear();
        foreach (var expired in expiredClientCreations)
        {
            if (expired.Value == null)
                unavailableRequests.Add(expired.Key);
        }
        foreach (uint requestId in unavailableRequests)
            expiredClientCreations.Remove(requestId);
    }

    public void RemovePlayer(Guid playerGuid)
    {
        creationRates.Remove(playerGuid);

        var playerResponses =
            new List<(Guid PlayerGuid, uint RequestId)>();
        foreach (var response in creationResponses)
        {
            if (response.Key.PlayerGuid == playerGuid)
                playerResponses.Add(response.Key);
        }

        foreach (var key in playerResponses)
            creationResponses.Remove(key);
    }
}

internal sealed class CreationRetrySchedule
{
    private int attempts;
    private float nextAttempt;

    public PendingCreationDecision GetDecision(float now)
    {
        if (now < nextAttempt)
            return PendingCreationDecision.Waiting;
        if (attempts >=
            NetworkedItemRequestTracker.MaxPendingCreationAttempts)
            return PendingCreationDecision.Exhausted;

        attempts++;
        nextAttempt =
            now + NetworkedItemRequestTracker.PendingCreationRetryInterval;
        return PendingCreationDecision.Attempt;
    }
}

internal enum PendingCreationDecision
{
    Waiting,
    Attempt,
    Exhausted,
}
