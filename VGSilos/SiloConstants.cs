using Source.Item;

namespace VGSilos;

/// <summary>
/// All numeric tuning for the silo system in one place. Cap values, recipe costs,
/// and worldgen probabilities. Tweak here, rebuild — no other file should hardcode
/// these numbers.
/// </summary>
internal static class SiloConstants
{
    // -----------------------------------------------------------------------
    // CAP VALUES
    // -----------------------------------------------------------------------

    /// <summary>Per-material cap floor when the player has installed zero silos.</summary>
    public const float BaseCapPerMaterial = 500f;

    /// <summary>Capacity each Universal Silo Mk1 adds to all 8 materials.</summary>
    public const float UniversalMk1ContributionEach = 250f;

    /// <summary>Capacity each Universal Silo Mk2 adds to all 8 materials.</summary>
    public const float UniversalMk2ContributionEach = 1_000f;

    /// <summary>Capacity each Specialized Mk1 adds to its target material.</summary>
    public const float SpecializedMk1Contribution = 1_000f;

    /// <summary>Capacity each Specialized Mk2 adds to its target material.</summary>
    public const float SpecializedMk2Contribution = 5_000f;

    /// <summary>Capacity each Specialized Mk3 adds to its target material.</summary>
    public const float SpecializedMk3Contribution = 25_000f;

    // -----------------------------------------------------------------------
    // WORLDGEN
    // -----------------------------------------------------------------------

    /// <summary>Default fraction of refinery-stations that have a silo bay (overridable in config).</summary>
    public const float DefaultSiloAvailabilityChance = 0.6f;

    /// <summary>Mount-point distribution thresholds (cumulative %, rolled on silo-capable stations).</summary>
    /// <remarks>Roll &lt; 50: 1 mount; &lt; 85: 2 mounts; else 3 mounts.</remarks>
    public const int OneMountThreshold = 50;
    public const int TwoMountThreshold = 85;

    /// <summary>Forced mount count on the player's first-docked station, regardless of seed.</summary>
    public const int StarterStationMountCount = 2;

    // -----------------------------------------------------------------------
    // RECIPE COSTS
    // -----------------------------------------------------------------------
    // Costs are in refined-material units. All silos use Carbon as filler
    // (cheapest material) so silo-crafting is always *some* sink for what
    // the player has the most of.

    public readonly struct RecipeCost
    {
        public readonly int Carbon;
        public readonly int Titanium;
        public readonly int Silicon;
        public readonly int Tungsten;
        public readonly int Iridium;
        public readonly int Astatine;
        public readonly int OwnMaterial;
        public readonly int Credits;

        public RecipeCost(int carbon = 0, int titanium = 0, int silicon = 0, int tungsten = 0,
                          int iridium = 0, int astatine = 0, int ownMaterial = 0, int credits = 0)
        {
            Carbon = carbon;
            Titanium = titanium;
            Silicon = silicon;
            Tungsten = tungsten;
            Iridium = iridium;
            Astatine = astatine;
            OwnMaterial = ownMaterial;
            Credits = credits;
        }
    }

    public static readonly RecipeCost UniversalMk1Cost =
        new(carbon: 200, titanium: 100, silicon: 50, credits: 5_000);

    public static readonly RecipeCost UniversalMk2Cost =
        new(carbon: 500, titanium: 300, silicon: 200, tungsten: 100, credits: 25_000);

    public static readonly RecipeCost SpecializedMk1Cost =
        new(carbon: 300, titanium: 200, silicon: 100, credits: 8_000);

    public static readonly RecipeCost SpecializedMk2Cost =
        new(carbon: 800, titanium: 500, tungsten: 200, ownMaterial: 300, credits: 40_000);

    public static readonly RecipeCost SpecializedMk3Cost =
        new(carbon: 1_500, titanium: 1_000, ownMaterial: 800, iridium: 500, astatine: 200, credits: 200_000);

    // -----------------------------------------------------------------------
    // SIDECAR / VERSIONING
    // -----------------------------------------------------------------------

    /// <summary>Sidecar JSON schema version. Bump on breaking schema changes.</summary>
    public const int SidecarSchemaVersion = 1;

    /// <summary>Filename prefix for the sidecar JSON. Full name: "{prefix}.{saveName}.json".</summary>
    public const string SidecarFilenamePrefix = "vgsilos";

    // -----------------------------------------------------------------------
    // HELPERS
    // -----------------------------------------------------------------------

    /// <summary>How much capacity a specialized silo of the given tier contributes.</summary>
    public static float SpecializedContribution(int tier) => tier switch
    {
        1 => SpecializedMk1Contribution,
        2 => SpecializedMk2Contribution,
        3 => SpecializedMk3Contribution,
        _ => 0f,
    };

    /// <summary>How much capacity a universal silo of the given tier contributes (per material).</summary>
    public static float UniversalContributionEach(int tier) => tier switch
    {
        1 => UniversalMk1ContributionEach,
        2 => UniversalMk2ContributionEach,
        _ => 0f,
    };

    /// <summary>The cost to craft a specialized silo of the given tier.</summary>
    public static RecipeCost SpecializedCost(int tier) => tier switch
    {
        1 => SpecializedMk1Cost,
        2 => SpecializedMk2Cost,
        3 => SpecializedMk3Cost,
        _ => default,
    };

    /// <summary>The cost to craft a universal silo of the given tier.</summary>
    public static RecipeCost UniversalCost(int tier) => tier switch
    {
        1 => UniversalMk1Cost,
        2 => UniversalMk2Cost,
        _ => default,
    };

    /// <summary>All 8 materials in enum order — convenience for iteration.</summary>
    public static readonly RefinedMaterial[] AllMaterials =
    {
        RefinedMaterial.Titanium,
        RefinedMaterial.Oxide,
        RefinedMaterial.Silicon,
        RefinedMaterial.Tungsten,
        RefinedMaterial.Carbon,
        RefinedMaterial.Iridium,
        RefinedMaterial.Platinum,
        RefinedMaterial.Astatine,
    };
}
