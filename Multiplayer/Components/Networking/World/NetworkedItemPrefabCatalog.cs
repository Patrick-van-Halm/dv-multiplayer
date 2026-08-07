using DV;
using DV.InventorySystem;
using DV.Logic.Job;
using System;
using System.Collections.Generic;

namespace Multiplayer.Components.Networking.World;

internal sealed class NetworkedItemPrefabCatalog
{
    private static readonly HashSet<Type> ExcludedItemTypes =
    [
        typeof(JobOverview),
        typeof(JobBooklet),
        typeof(JobReport),
        typeof(JobExpiredReport),
        typeof(JobMissingLicenseReport),
    ];

    private readonly Dictionary<string, InventoryItemSpec> itemPrefabs =
        new(1024);

    public void Build()
    {
        itemPrefabs.Clear();
        foreach (InventoryItemSpec item in Globals.G.Items.items)
        {
            if (item != null &&
                !string.IsNullOrEmpty(item.ItemPrefabName) &&
                !itemPrefabs.ContainsKey(item.ItemPrefabName))
            {
                itemPrefabs.Add(item.ItemPrefabName, item);
            }
        }
    }

    public bool TryGet(
        string prefabName,
        out InventoryItemSpec spec)
    {
        spec = null;
        return !string.IsNullOrEmpty(prefabName) &&
               itemPrefabs.TryGetValue(prefabName, out spec);
    }

    internal static bool ShouldSkipAutoCreate(Type itemType)
    {
        return itemType != null &&
               ExcludedItemTypes.Contains(itemType);
    }
}
