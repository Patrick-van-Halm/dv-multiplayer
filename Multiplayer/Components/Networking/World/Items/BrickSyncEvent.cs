using System;

namespace Multiplayer.Components.Networking.World.Items;

internal readonly struct BrickSyncEvent
{
    public int Sequence { get; }
    public int Action { get; }

    public BrickSyncEvent(int sequence, int action)
    {
        Sequence = sequence;
        Action = action;
    }

    public string Serialize() => $"{Sequence}:{Action}";

    public static bool TryParse(
        string serialized,
        out BrickSyncEvent value)
    {
        value = default;
        if (string.IsNullOrEmpty(serialized))
            return false;

        int separator = serialized.IndexOf(':');
        if (separator <= 0 ||
            separator != serialized.LastIndexOf(':') ||
            !int.TryParse(
                serialized.Substring(0, separator),
                out int sequence) ||
            !int.TryParse(
                serialized.Substring(separator + 1),
                out int action))
        {
            return false;
        }

        value = new BrickSyncEvent(sequence, action);
        return true;
    }
}
