using Behaviour.Crafting;
using Behaviour.Item;
using HarmonyLib;
using VGSilos.Items;

namespace VGSilos.Patches;

/// <summary>
/// Postfixes vanilla's <c>LoadAll()</c> static methods so silo items and
/// recipes are re-injected if the catalogs are ever rebuilt.
///
/// In practice, the canonical injection happens directly in
/// <see cref="Plugin.Awake"/> because vanilla's
/// <c>RuntimeInitializeOnLoadMethod(BeforeSceneLoad)</c> bootstrap fires
/// LoadAll() before BepInEx Plugin.Awake — so a postfix on LoadAll() never
/// sees the vanilla call. These postfixes remain as a safety net for any
/// scenario where LoadAll() runs again later (hot-reload, future changes).
/// </summary>
internal static class ItemLoadPatches
{
    [HarmonyPatch(typeof(InventoryItemType), nameof(InventoryItemType.LoadAll))]
    private static class ItemPostfix
    {
        // ReSharper disable once UnusedMember.Local
        [HarmonyPostfix]
        private static void Postfix()
        {
            try { SiloItemFactory.RebuildAll(); }
            catch (System.Exception ex) { Plugin.Log.LogError($"SiloItemFactory.RebuildAll failed: {ex}"); }
        }
    }

    [HarmonyPatch(typeof(CraftingRecipe), nameof(CraftingRecipe.LoadAll))]
    private static class RecipePostfix
    {
        // ReSharper disable once UnusedMember.Local
        [HarmonyPostfix]
        private static void Postfix()
        {
            try { SiloRecipeFactory.RebuildAll(); }
            catch (System.Exception ex) { Plugin.Log.LogError($"SiloRecipeFactory.RebuildAll failed: {ex}"); }
        }
    }
}
