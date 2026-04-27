namespace VGSilos.Patches;

/// <summary>
/// Forge recipe injection for the 26 silo crafting recipes.
///
/// CONFIRMED TARGET: <see cref="Behaviour.Crafting.CraftingRecipe.GetAvailable"/>
/// (static, line ~430 in decomp). It iterates <c>allRecipes.Values</c>
/// (a private static <c>Dictionary&lt;string, CraftingRecipe&gt;</c>) and
/// yields entries that pass per-recipe gates (<c>unlockedFromStart</c>,
/// blueprint state, etc.).
///
/// COMPLICATION: <see cref="Behaviour.Crafting.CraftingRecipe"/> is a
/// MonoBehaviour with <c>[SerializeField]</c> ingredient lists, item
/// prefab references in <c>results</c>, and an <c>InitializeRecipe()</c>
/// pass that resolves <c>InventoryItemType</c> components from the result
/// GameObject. Constructing one at runtime is non-trivial:
///   1. Build a synthetic GameObject host for the recipe.
///   2. Build a synthetic GameObject host for each silo's
///      <see cref="Behaviour.Item.InventoryItemType"/> result, with icon,
///      display name, item category (Equipment? new Silo category?),
///      m3, sell value, and a UsableItem subclass that performs install.
///   3. Wire the recipe's serialized lists via reflection (the fields
///      are private; HarmonyLib.AccessTools.Field handles this).
///   4. Register into the <c>allRecipes</c> dictionary AND inject into
///      <see cref="Source.Galaxy.POI.SpaceStation.recipes"/> (which is
///      a virtual property, so we patch its getter instead of mutating
///      the static dictionary).
///
/// v1 STRATEGY: defer recipe injection until items exist. The cleanest
/// path is a separate <c>Items/SiloItemFactory.cs</c> that constructs
/// <see cref="Behaviour.Item.InventoryItemType"/> prefabs at plugin
/// Awake (BepInEx Chainloader is ready by then; Unity scenes aren't,
/// but prefab construction doesn't require an active scene). The recipe
/// factory consumes those item prefabs and produces the 26 recipes.
///
/// Until then, silos can be granted via a debug console command
/// (planned: <c>vgsilos.give &lt;mat?&gt; &lt;tier&gt;</c>) so the cap
/// system and tooltip can be tested end-to-end without crafting wired.
/// </summary>
internal static class ForgeRecipePatches
{
    // No active patches yet. Activation requires the Items/SiloItemFactory
    // milestone which is the next chunk of work after this scaffold.
}
