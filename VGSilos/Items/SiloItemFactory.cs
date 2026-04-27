using System.Collections.Generic;
using System.Reflection;
using Behaviour.Item;
using HarmonyLib;
using Source.Item;
using UnityEngine;
using VGSilos.Domain;

namespace VGSilos.Items;

/// <summary>
/// Builds 26 silo <see cref="InventoryItemType"/> GameObjects at runtime
/// and registers them in <see cref="InventoryItemType"/>'s private static
/// <c>allItems</c> dictionary. Vanilla loads items via
/// <c>Resources.LoadAll&lt;InventoryItemType&gt;("Items")</c> which only
/// reads from the game's Resources folder; we extend the catalog by
/// directly mutating the dictionary after vanilla's <c>LoadAll</c>.
///
/// Each silo is a <see cref="GameObject"/> with two components:
///   • <see cref="InventoryItemType"/> (the catalog entry)
///   • <see cref="SiloUsableItem"/> (the install behaviour on Use)
///
/// All 26 GameObjects are marked <see cref="Object.DontDestroyOnLoad"/>
/// and parented under a hidden host so Unity doesn't garbage-collect
/// them when scenes change.
/// </summary>
internal static class SiloItemFactory
{
    private static GameObject? _host;
    private static bool _built;

    // Cached reflection handles into private static catalog of InventoryItemType.
    private static readonly FieldInfo AllItemsField = AccessTools.Field(typeof(InventoryItemType), "allItems")
        ?? throw new System.Exception("InventoryItemType.allItems field not found");

    // Cached reflection handles into private auto-property backing fields.
    // Auto-property backing field name: "<PropertyName>k__BackingField".
    private static readonly FieldInfo IdentifierBacking = AutoBackingField(nameof(InventoryItemType.identifier));
    private static readonly FieldInfo CategoryBacking = AutoBackingField(nameof(InventoryItemType.itemCategory));
    private static readonly FieldInfo StorageOverrideBacking = AutoBackingField(nameof(InventoryItemType.storageOverride));
    private static readonly FieldInfo DisplayNameBacking = AutoBackingField(nameof(InventoryItemType.displayName));
    private static readonly FieldInfo DescriptionBacking = AutoBackingField(nameof(InventoryItemType.description));
    private static readonly FieldInfo IconBacking = AutoBackingField(nameof(InventoryItemType.icon));
    private static readonly FieldInfo M3Backing = AutoBackingField(nameof(InventoryItemType.m3));
    private static readonly FieldInfo BaseCostBacking = AutoBackingField(nameof(InventoryItemType.baseCost));
    private static readonly FieldInfo RarityBacking = AutoBackingField(nameof(InventoryItemType.rarity));
    private static readonly FieldInfo CanJettisonBacking = AutoBackingField(nameof(InventoryItemType.canJettison));
    private static readonly FieldInfo CanSellBacking = AutoBackingField(nameof(InventoryItemType.canSell));

    private static FieldInfo AutoBackingField(string propertyName) =>
        AccessTools.Field(typeof(InventoryItemType), $"<{propertyName}>k__BackingField")
        ?? throw new System.Exception($"InventoryItemType.{propertyName} backing field not found");

    /// <summary>
    /// Lookup by identifier of every silo item we built. Used by the
    /// recipe factory to resolve result GameObjects.
    /// </summary>
    public static readonly Dictionary<string, InventoryItemType> Built = new();

    /// <summary>Resets the built flag and re-runs the build. Called from
    /// the LoadAll postfix when vanilla wipes the catalog.</summary>
    public static void RebuildAll()
    {
        _built = false;
        Built.Clear();
        BuildAll();
    }

    public static void BuildAll()
    {
        if (_built) return;
        _built = true;

        _host = new GameObject("VGSilos_ItemHost");
        Object.DontDestroyOnLoad(_host);
        _host.SetActive(false); // hidden — we never want these GameObjects to "live" in a scene

        // Universal Mk1, Mk2
        for (var tier = 1; tier <= 2; tier++)
            BuildSilo(InstalledSilo.SiloKind.Universal, null, tier);

        // Specialized × 8 materials × 3 tiers
        foreach (var mat in SiloConstants.AllMaterials)
            for (var tier = 1; tier <= 3; tier++)
                BuildSilo(InstalledSilo.SiloKind.Specialized, mat, tier);

        // Inject into vanilla catalog so InventoryItemType.Get(id) works.
        var allItems = (Dictionary<string, InventoryItemType>)AllItemsField.GetValue(null)!;
        foreach (var kv in Built) allItems[kv.Key] = kv.Value;

        Plugin.Log.LogInfo($"SiloItemFactory: registered {Built.Count} silo items.");
    }

    private static void BuildSilo(InstalledSilo.SiloKind kind, RefinedMaterial? material, int tier)
    {
        var identifier = MakeIdentifier(kind, material, tier);
        var go = new GameObject(identifier);
        go.transform.SetParent(_host!.transform, worldPositionStays: false);

        var iit = go.AddComponent<InventoryItemType>();
        var usable = go.AddComponent<SiloUsableItem>();
        usable.Configure(kind, material, tier);

        // Set the auto-property backing fields on InventoryItemType.
        IdentifierBacking.SetValue(iit, identifier);
        // ItemCategory.UnusedMissionItem is a pure orphan in vanilla (zero
        // references in the entire decompiled assembly). We claim it as
        // the silos' private category. The Translation postfix in
        // SiloTranslationPatches rewrites @ItemCategoryUnusedMissionItem
        // to "Silos" globally, so every UI (Forge, inventory, shops,
        // tooltips, search) renders the category as "Silos" without any
        // per-UI relabel patches. Equipment-comparison logic, module-slot
        // pickers, and other category-specific code paths simply don't
        // touch UnusedMissionItem because vanilla never branches on it —
        // which also avoids the NRE that ItemCategory.Module triggered
        // (CompareTooltip → AbstractUnitData.GetEquipedItemsOfType expects
        // an AbstractEquipment component our silos don't have).
        CategoryBacking.SetValue(iit, ItemCategory.UnusedMissionItem);
        // Force armory routing. Without this, the Forge's deposit logic
        // (ForgeJob.AddForgeItemToCargo) silently drops silos when ship
        // cargo can't accept them: it falls back to CanGoInArmory() then
        // CanGoInMaterials(), both of which return false for
        // UnusedMissionItem. StorageOverride.Armory short-circuits both
        // checks so the deposit always has a valid destination.
        StorageOverrideBacking.SetValue(iit, StorageOverride.Armory);
        DisplayNameBacking.SetValue(iit, MakeDisplayName(kind, material, tier));
        DescriptionBacking.SetValue(iit, MakeDescription(kind, material, tier));
        IconBacking.SetValue(iit, ResolveIcon(kind, material));
        M3Backing.SetValue(iit, ResolveM3(kind, tier));
        BaseCostBacking.SetValue(iit, ResolveBaseCost(kind, tier));
        RarityBacking.SetValue(iit, ResolveRarity(kind, tier));
        CanJettisonBacking.SetValue(iit, true);
        CanSellBacking.SetValue(iit, false); // can't sell raw silos — must install or scrap

        // Wire InventoryItemPart.item references (this is what vanilla LoadAll's
        // last loop does for every loaded item).
        iit.InitializeItem();

        Built[identifier] = iit;
    }

    private static string MakeIdentifier(InstalledSilo.SiloKind kind, RefinedMaterial? material, int tier) =>
        kind == InstalledSilo.SiloKind.Universal
            ? $"VGSilos_UniversalSilo_Mk{tier}"
            : $"VGSilos_{material}Silo_Mk{tier}";

    private static string MakeDisplayName(InstalledSilo.SiloKind kind, RefinedMaterial? material, int tier) =>
        kind == InstalledSilo.SiloKind.Universal
            ? $"Universal Silo Mk{tier}"
            : $"{material} Silo Mk{tier}";

    private static string MakeDescription(InstalledSilo.SiloKind kind, RefinedMaterial? material, int tier)
    {
        if (kind == InstalledSilo.SiloKind.Universal)
        {
            var each = SiloConstants.UniversalContributionEach(tier);
            return $"Use at a silo-capable station to install. Adds +{each:F0} capacity to every refined material.";
        }
        var amount = SiloConstants.SpecializedContribution(tier);
        return $"Use at a silo-capable station to install. Adds +{amount:F0} {material} capacity.";
    }

    private static Sprite? ResolveIcon(InstalledSilo.SiloKind kind, RefinedMaterial? material)
    {
        // Specialized silos use their target material's icon.
        // Universal uses Carbon (the structural filler) as a fallback.
        var iconMaterial = kind == InstalledSilo.SiloKind.Specialized && material.HasValue
            ? material.Value
            : RefinedMaterial.Carbon;
        return iconMaterial.GetIcon();
    }

    private static float ResolveM3(InstalledSilo.SiloKind kind, int tier)
    {
        if (kind == InstalledSilo.SiloKind.Universal)
            return tier switch { 1 => 15f, 2 => 40f, _ => 15f };
        return tier switch { 1 => 20f, 2 => 50f, 3 => 100f, _ => 20f };
    }

    private static int ResolveBaseCost(InstalledSilo.SiloKind kind, int tier)
    {
        // ~10% of crafting credits cost. The recipe defines the real cost;
        // baseCost only matters for sell/insurance fallbacks.
        var cost = kind == InstalledSilo.SiloKind.Universal
            ? SiloConstants.UniversalCost(tier).Credits
            : SiloConstants.SpecializedCost(tier).Credits;
        return cost / 10;
    }

    private static Rarity ResolveRarity(InstalledSilo.SiloKind kind, int tier)
    {
        // Tier → rarity. Vanilla rarity ladder is Standard / Enhanced /
        // HighGrade / Exotic / Legendary (in ascending order).
        return tier switch
        {
            1 => Rarity.Standard,
            2 => Rarity.Enhanced,
            3 => Rarity.HighGrade,
            _ => Rarity.Standard,
        };
    }
}
