using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using VGSilos.Domain;
using VGSilos.Items;

namespace VGSilos;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
[BepInProcess("VanguardGalaxy.exe")]
public class Plugin : BaseUnityPlugin
{
    public const string PluginGuid = "vgsilos";
    public const string PluginName = "Vanguard Galaxy Silos";
    public const string PluginVersion = "0.1.0";

    internal static Plugin Instance { get; private set; } = null!;
    internal static ManualLogSource Log { get; private set; } = null!;

    internal SiloRegistry Registry { get; private set; } = null!;

    internal ConfigEntry<bool> CfgEnableCaps = null!;
    internal ConfigEntry<bool> CfgEnableTooltipInfo = null!;
    internal ConfigEntry<bool> CfgEnableSiloRecipes = null!;
    internal ConfigEntry<float> CfgSiloAvailabilityChance = null!;

    private Harmony _harmony = null!;

    private void Awake()
    {
        Instance = this;
        Log = Logger;

        CfgEnableCaps = Config.Bind("Caps", "Enabled", true,
            "Master enable for the per-material refined-storage cap. When false, the cap " +
            "is unenforced and the mod behaves as if every material had infinite capacity " +
            "(matches vanilla behaviour). Disable for save inspection or to compare runs.");
        CfgEnableTooltipInfo = Config.Bind("UI", "ShowTooltipInfo", true,
            "Append a 'Silo Bay (n/m)' line to the map-hover tooltip on stations that have " +
            "a silo bay. Hold Shift while hovering to expand to per-silo detail.");
        CfgEnableSiloRecipes = Config.Bind("Crafting", "EnableSiloRecipes", true,
            "Inject the 26 silo crafting recipes (2 universal × 2 tiers + 8 specialized × 3 " +
            "tiers) into Forge UIs. When false, silos cannot be crafted; existing installed " +
            "silos still grant their cap bonus.");
        CfgSiloAvailabilityChance = Config.Bind("Worldgen", "SiloAvailabilityChance", 0.6f,
            new ConfigDescription(
                "Fraction of refinery-equipped stations that have a silo bay, decided " +
                "deterministically from the world seed + station POI id. 0.6 = 60%. The " +
                "starter station is always silo-capable regardless of this roll.",
                new AcceptableValueRange<float>(0.0f, 1.0f)));

        Registry = new SiloRegistry(Log, CfgSiloAvailabilityChance.Value);

        _harmony = new Harmony(PluginGuid);
        // PatchAll(Assembly) recurses into nested types, picking up the
        // [HarmonyPatch] attributes inside SaveLoadPatches.LoadPatch /
        // SaveLoadPatches.StorePatch / ItemLoadPatches.ItemPostfix /
        // ItemLoadPatches.RecipePostfix. PatchAll(Type) only patches the
        // type passed in.
        _harmony.PatchAll(System.Reflection.Assembly.GetExecutingAssembly());
        Log.LogInfo($"{PluginName} v{PluginVersion} loaded ({_harmony.GetPatchedMethods().Count()} patches)");

        // Vanilla's InventoryItemType.LoadAll() and CraftingRecipe.LoadAll()
        // are decorated with [RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]
        // which fires BEFORE BepInEx plugin Awake. So our LoadAll postfixes
        // never see those calls — by the time we install them, vanilla load
        // is already done. We register our items/recipes directly here.
        // The postfixes in ItemLoadPatches remain as a safety net for any
        // future hot-reload or replay scenario.
        if (CfgEnableSiloRecipes.Value)
        {
            try
            {
                SiloItemFactory.BuildAll();
                SiloRecipeFactory.BuildAll();
            }
            catch (System.Exception ex)
            {
                Log.LogError($"Silo factory bootstrap failed: {ex}");
            }
        }
    }

    private void OnDestroy()
    {
        _harmony?.UnpatchSelf();
    }
}
