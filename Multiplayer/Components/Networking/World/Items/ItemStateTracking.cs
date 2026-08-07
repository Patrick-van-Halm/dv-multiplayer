using DV.Items;
using Multiplayer.Utils;
using UnityEngine;

namespace Multiplayer.Components.Networking.World.Items;

internal static class ItemStateTracking
{
    public static NetworkedItem For(Component component)
    {
        NetworkedItem item =
            component.gameObject.GetOrAddComponent<NetworkedItem>();
        item.Initialize(component);
        return item;
    }
}
