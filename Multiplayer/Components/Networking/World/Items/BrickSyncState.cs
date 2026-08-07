using UnityEngine;

namespace Multiplayer.Components.Networking.World.Items;

internal sealed class BrickSyncState : MonoBehaviour
{
    public int Sequence;
    public int LastAction;
    public bool Applying;
    public bool HasNetworkBaseline;

    public string EventValue =>
        new BrickSyncEvent(Sequence, LastAction).Serialize();
}
