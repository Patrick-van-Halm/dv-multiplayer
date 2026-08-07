using Multiplayer.Networking.Data.Items;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Multiplayer.Components.Networking.World;

internal sealed class NetworkedItemTrackedState
{
    private readonly List<ITrackedValue> values = [];
    private readonly Dictionary<string, object> pendingValues = [];

    public void Register<T>(
        string key,
        Func<T> getter,
        Action<T> setter,
        Func<T, T, bool> thresholdComparer,
        bool serverAuthoritative,
        bool isHost,
        string itemDescription)
    {
        if (values.Any(value => value.Key == key))
        {
            Multiplayer.LogWarning(
                $"Duplicate tracked item key '{key}' ignored for {itemDescription}.");
            return;
        }

        var trackedValue = new TrackedValue<T>(
            key,
            getter,
            setter,
            thresholdComparer,
            serverAuthoritative);
        values.Add(trackedValue);

        if (!pendingValues.TryGetValue(key, out object pendingValue))
            return;

        if (!isHost || !serverAuthoritative)
            trackedValue.SetValueFromObject(pendingValue);
        pendingValues.Remove(key);
    }

    public bool HasDirtyValues(bool isHost)
    {
        return values.Any(
            value =>
                value.IsDirty &&
                (isHost || !value.ServerAuthoritative));
    }

    public Dictionary<string, object> GetDirtyValues(bool isHost)
    {
        return values
            .Where(
                value =>
                    value.IsDirty &&
                    (isHost || !value.ServerAuthoritative))
            .ToDictionary(
                value => value.Key,
                value => value.GetValueAsObject());
    }

    public Dictionary<string, object> GetAllValues()
    {
        return values.ToDictionary(
            value => value.Key,
            value => value.GetValueAsObject());
    }

    public bool AcceptsClientValues(
        Dictionary<string, object> newValues)
    {
        if (newValues == null)
            return true;

        foreach (KeyValuePair<string, object> newValue in newValues)
        {
            ITrackedValue trackedValue =
                values.Find(value => value.Key == newValue.Key);
            if (trackedValue == null ||
                trackedValue.ServerAuthoritative ||
                !trackedValue.AcceptsValue(newValue.Value))
            {
                return false;
            }
        }

        return true;
    }

    public void Apply(
        Dictionary<string, object> newValues,
        bool isHost,
        string itemDescription)
    {
        if (newValues == null || newValues.Count == 0)
            return;

        foreach (KeyValuePair<string, object> newValue in newValues)
        {
            ITrackedValue trackedValue =
                values.Find(value => value.Key == newValue.Key);
            if (trackedValue == null)
            {
                pendingValues[newValue.Key] = newValue.Value;
                Multiplayer.LogDebug(
                    () =>
                        $"Tracked value '{newValue.Key}' queued until its item adapter registers on {itemDescription}.");
                continue;
            }

            if (isHost && trackedValue.ServerAuthoritative)
            {
                Multiplayer.LogWarning(
                    $"Skipped server-authoritative value update from client: {newValue.Key} on {itemDescription}.");
                continue;
            }

            try
            {
                trackedValue.SetValueFromObject(newValue.Value);
            }
            catch (Exception exception)
            {
                Multiplayer.LogError(
                    $"Error updating tracked value {newValue.Key} on {itemDescription}: {exception.Message}");
            }
        }
    }

    public void MarkClean()
    {
        foreach (ITrackedValue value in values)
            value.MarkClean();
    }

    public string GetDirtyDebugString(string itemDescription)
    {
        List<ITrackedValue> dirtyValues =
            values.Where(value => value.IsDirty).ToList();
        if (dirtyValues.Count == 0)
            return "No dirty values";

        var builder = new StringBuilder();
        builder.AppendLine(
            $"Dirty values for NetworkedItem: {itemDescription}");
        foreach (ITrackedValue value in dirtyValues)
            builder.AppendLine(value.GetDebugString());
        return builder.ToString();
    }
}
