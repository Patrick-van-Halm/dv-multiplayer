using DV.CabControls;
using HarmonyLib;
using Multiplayer.Components.Networking.World.Items;

namespace Multiplayer.Patches.World.Items;

[HarmonyPatch(typeof(ItemBase), nameof(ItemBase.Awake))]
internal static class ItemBasePatch
{
    [HarmonyPostfix]
    private static void Register(ItemBase __instance)
    {
        ItemBaseStateRegistration.Register(__instance);
    }
}
