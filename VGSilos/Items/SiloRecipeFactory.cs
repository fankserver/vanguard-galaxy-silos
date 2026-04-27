using System.Collections.Generic;
using System.Reflection;
using Behaviour.Crafting;
using Behaviour.Item;
using HarmonyLib;
using Source.Item;
using UnityEngine;
using VGSilos.Domain;

namespace VGSilos.Items;

/// <summary>
/// Builds 26 silo <see cref="CraftingRecipe"/> MonoBehaviours at runtime
/// and registers them in <see cref="CraftingRecipe"/>'s private static
/// <c>allRecipes</c> dictionary.
///
/// Like <see cref="SiloItemFactory"/>, this extends a Resources-loaded
/// catalog by directly mutating its dictionary after vanilla
/// <c>LoadAll()</c> completes. Each recipe:
///   • Has <c>unlockedFromStart=true</c> so <c>GetAvailable()</c> returns it.
///   • Sets <c>customCost</c> (credits) and <c>customName</c>.
///   • Populates the private <c>materials</c> list (refined-material costs).
///   • Populates the private <c>results</c> list with one row pointing to
///     the silo item GameObject from <see cref="SiloItemFactory"/>.
///   • Calls <c>InitializeRecipe()</c> so vanilla cost/icon resolution works.
/// </summary>
internal static class SiloRecipeFactory
{
    private static GameObject? _host;
    private static bool _built;

    private static readonly FieldInfo AllRecipesField = AccessTools.Field(typeof(CraftingRecipe), "allRecipes")
        ?? throw new System.Exception("CraftingRecipe.allRecipes field not found");

    // Private fields on CraftingRecipe.
    private static readonly FieldInfo CustomNameField = PrivateField("customName");
    private static readonly FieldInfo CustomRecipeCategoryNameField = PrivateField("customRecipeCategoryName");
    private static readonly FieldInfo CustomCostField = PrivateField("customCost");
    private static readonly FieldInfo MaterialsField = PrivateField("materials");
    private static readonly FieldInfo ItemMaterialsField = PrivateField("itemMaterials");
    private static readonly FieldInfo ResultsField = PrivateField("results");
    // craftingTime, craftingRarity have [field: SerializeField] auto-property
    // backing fields, so we use the auto-backing pattern.
    private static readonly FieldInfo CraftingTimeBacking = AutoBackingField("craftingTime");
    private static readonly FieldInfo CraftingRarityBacking = AutoBackingField("craftingRarity");

    private static FieldInfo PrivateField(string name) =>
        AccessTools.Field(typeof(CraftingRecipe), name)
        ?? throw new System.Exception($"CraftingRecipe.{name} field not found");

    private static FieldInfo AutoBackingField(string propertyName) =>
        AccessTools.Field(typeof(CraftingRecipe), $"<{propertyName}>k__BackingField")
        ?? throw new System.Exception($"CraftingRecipe.{propertyName} backing field not found");

    /// <summary>Resets the built flag and re-runs the build. Called from
    /// the LoadAll postfix when vanilla wipes the catalog.</summary>
    public static void RebuildAll()
    {
        _built = false;
        BuildAll();
    }

    public static void BuildAll()
    {
        if (_built) return;
        _built = true;

        if (SiloItemFactory.Built.Count == 0)
        {
            Plugin.Log.LogWarning("SiloRecipeFactory: no silo items built — skipping recipe injection.");
            return;
        }

        _host = new GameObject("VGSilos_RecipeHost");
        Object.DontDestroyOnLoad(_host);
        _host.SetActive(false);

        var allRecipes = (Dictionary<string, CraftingRecipe>)AllRecipesField.GetValue(null)!;
        var registered = 0;

        // Universal recipes (Mk1, Mk2)
        for (var tier = 1; tier <= 2; tier++)
        {
            var itemId = $"VGSilos_UniversalSilo_Mk{tier}";
            if (!SiloItemFactory.Built.TryGetValue(itemId, out var item)) continue;
            var recipe = BuildRecipe(
                identifier: $"VGSilos_Recipe_UniversalSilo_Mk{tier}",
                displayName: $"Universal Silo Mk{tier}",
                resultItem: item,
                cost: SiloConstants.UniversalCost(tier),
                ownMaterial: null);
            allRecipes[recipe.identifier] = recipe;
            registered++;
        }

        // Specialized recipes (8 materials × 3 tiers)
        foreach (var mat in SiloConstants.AllMaterials)
        {
            for (var tier = 1; tier <= 3; tier++)
            {
                var itemId = $"VGSilos_{mat}Silo_Mk{tier}";
                if (!SiloItemFactory.Built.TryGetValue(itemId, out var item)) continue;
                var recipe = BuildRecipe(
                    identifier: $"VGSilos_Recipe_{mat}Silo_Mk{tier}",
                    displayName: $"{mat} Silo Mk{tier}",
                    resultItem: item,
                    cost: SiloConstants.SpecializedCost(tier),
                    ownMaterial: mat);
                allRecipes[recipe.identifier] = recipe;
                registered++;
            }
        }

        Plugin.Log.LogInfo($"SiloRecipeFactory: registered {registered} silo recipes.");
    }

    private static CraftingRecipe BuildRecipe(
        string identifier,
        string displayName,
        InventoryItemType resultItem,
        SiloConstants.RecipeCost cost,
        RefinedMaterial? ownMaterial)
    {
        var go = new GameObject(identifier);
        go.transform.SetParent(_host!.transform, worldPositionStays: false);

        var recipe = go.AddComponent<CraftingRecipe>();

        // Identifier is settable via its own private setter on the property.
        AccessTools.Field(typeof(CraftingRecipe), "<identifier>k__BackingField").SetValue(recipe, identifier);

        CustomNameField.SetValue(recipe, displayName);
        // Leave customRecipeCategoryName null — when non-empty, the Forge UI
        // uses it INSTEAD of displayName for the row label, so setting it to
        // a shared string ("Silos") collapses all 26 rows into the same name.
        CustomRecipeCategoryNameField.SetValue(recipe, null);
        CustomCostField.SetValue(recipe, cost.Credits);
        CraftingTimeBacking.SetValue(recipe, 30f);
        CraftingRarityBacking.SetValue(recipe, Rarity.Standard);

        // unlockedFromStart and blueprintAvailable are public bools.
        recipe.unlockedFromStart = true;
        recipe.blueprintAvailable = true;

        // Materials list — refined-material costs.
        var materials = new List<CraftingRecipe.CraftingRecipeMaterialRow>();
        AddMaterial(materials, RefinedMaterial.Carbon, cost.Carbon);
        AddMaterial(materials, RefinedMaterial.Titanium, cost.Titanium);
        AddMaterial(materials, RefinedMaterial.Silicon, cost.Silicon);
        AddMaterial(materials, RefinedMaterial.Tungsten, cost.Tungsten);
        AddMaterial(materials, RefinedMaterial.Iridium, cost.Iridium);
        AddMaterial(materials, RefinedMaterial.Astatine, cost.Astatine);
        if (ownMaterial.HasValue && cost.OwnMaterial > 0)
            AddMaterial(materials, ownMaterial.Value, cost.OwnMaterial);
        MaterialsField.SetValue(recipe, materials);

        // No item-ingredients (silos are crafted purely from refined material).
        ItemMaterialsField.SetValue(recipe, new List<CraftingRecipe.CraftingRecipeItemRow>());

        // Results — one row per recipe output.
        var resultRow = NewItemRow(resultItem.gameObject, count: 1);
        ResultsField.SetValue(recipe, new List<CraftingRecipe.CraftingRecipeItemRow> { resultRow });

        // Vanilla initialisation pass — resolves displayName/icon/category
        // from the result GameObject's InventoryItemType.
        recipe.InitializeRecipe();
        return recipe;
    }

    private static void AddMaterial(
        List<CraftingRecipe.CraftingRecipeMaterialRow> list,
        RefinedMaterial material,
        int amount)
    {
        if (amount <= 0) return;
        var row = (CraftingRecipe.CraftingRecipeMaterialRow)System.Activator.CreateInstance(
            typeof(CraftingRecipe.CraftingRecipeMaterialRow))!;
        AccessTools.Field(typeof(CraftingRecipe.CraftingRecipeMaterialRow), "<material>k__BackingField")
            .SetValue(row, material);
        AccessTools.Field(typeof(CraftingRecipe.CraftingRecipeMaterialRow), "<amount>k__BackingField")
            .SetValue(row, (float)amount);
        list.Add(row);
    }

    private static CraftingRecipe.CraftingRecipeItemRow NewItemRow(GameObject item, int count)
    {
        var row = (CraftingRecipe.CraftingRecipeItemRow)System.Activator.CreateInstance(
            typeof(CraftingRecipe.CraftingRecipeItemRow))!;
        AccessTools.Field(typeof(CraftingRecipe.CraftingRecipeItemRow), "<item>k__BackingField")
            .SetValue(row, item);
        AccessTools.Field(typeof(CraftingRecipe.CraftingRecipeItemRow), "<count>k__BackingField")
            .SetValue(row, count);
        return row;
    }
}
