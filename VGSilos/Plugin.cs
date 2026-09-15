using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using VGModAPI;
using VGSilos.Domain;
using VGSilos.Items;

namespace VGSilos;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
[BepInProcess("VanguardGalaxy.exe")]
// Hard dependency: SaveData-coordinated persistence is the only save path.
// 0.2.8 is the minimum API release carrying the SaveData provider surface
// (owner-decision pin; the API source version is normalized to match).
[BepInDependency(ModApi.PluginId, "0.2.8")]
public class Plugin : BaseUnityPlugin
{
    public const string PluginGuid = "vgsilos";
    public const string PluginName = "Silos";
    public const string PluginVersion = "0.1.0";

    internal static Plugin Instance { get; private set; } = null!;
    internal static ManualLogSource Log { get; private set; } = null!;

    /// <summary>
    /// False when durable save-data admission failed. Silos then stays fully
    /// disabled for the process — no Harmony patches, no item/recipe
    /// injection — because progression that cannot be persisted durably must
    /// not be handed to the player. Existing on-disk data stays untouched.
    /// </summary>
    internal static bool Enabled { get; private set; }

    internal SiloRegistry Registry { get; private set; } = null!;

    internal ConfigEntry<bool> CfgEnableCaps = null!;
    internal ConfigEntry<bool> CfgEnableTooltipInfo = null!;
    internal ConfigEntry<bool> CfgEnableSiloRecipes = null!;
    internal ConfigEntry<float> CfgSiloAvailabilityChance = null!;
    internal ConfigEntry<bool> CfgImportLegacySidecars = null!;

    private Harmony? _harmony;
    private ISaveDataRegistration? _saveDataRegistration;

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
        CfgImportLegacySidecars = Config.Bind("Persistence", "ImportLegacySidecars", true,
            "One-time migration acknowledgement: adopt an existing vgsilos.{save}.json sidecar " +
            "the first time a save loads with no API-managed silo data. The legacy file is read " +
            "once and left untouched on disk. When false and a sidecar exists, persistence " +
            "refuses (the API blocks this provider) instead of adopting it without consent — " +
            "set it to true to adopt, or delete the sidecar to start that save fresh.");

        Registry = new SiloRegistry(Log, CfgSiloAvailabilityChance.Value);

        // Fail-closed admission. Registration must happen before any session
        // starts (Awake always is) and refusal is an ordinary result — but a
        // mod whose durable state cannot be admitted must not grant
        // progression that will evaporate on the next load. There is no
        // fallback persistence authority: on refusal we stay inert.
        Enabled = false;
        SaveDataRegistrationStatus admissionStatus = SaveDataRegistrationStatus.Unavailable;
        string admissionDetail = "not attempted";
        try
        {
            _saveDataRegistration = SiloSaveDataInstaller.TryRegister(
                ModApi.Services.SaveData, Registry, CfgImportLegacySidecars.Value,
                message => Log.LogWarning(message), out admissionStatus, out admissionDetail);
        }
        catch (System.Exception ex)
        {
            // ModApi.Services itself throws before the API finished startup;
            // the [BepInDependency] ordering makes this a broken-install case.
            _saveDataRegistration = null;
            Log.LogError($"VGModAPI SaveData unreachable: {ex.GetType().Name}: {ex.Message}");
            Log.LogError("Silos is DISABLED for this process — no patches applied, existing data untouched. " +
                         "Check VGModAPI installation and its BepInEx log for persistence-service errors.");
            Registry.MutationAllowed = () => false;
            return;
        }

        if (_saveDataRegistration == null)
        {
            Log.LogError($"VGModAPI SaveData registration refused ({admissionStatus}: {admissionDetail}).");
            Log.LogError("Silos is DISABLED for this process — no patches applied, existing data untouched. " +
                         "Enable the API persistence service (vgmodapi.cfg) and check API errors.");
            Registry.MutationAllowed = () => false;
            return;
        }

        Enabled = true;
        Log.LogInfo($"Silo persistence: VGModAPI SaveData provider '{SiloSaveDataProvider.Owner}' registered.");

        // Route every progression mutation through the API's live action
        // gate: blocked provider (e.g. restore refused), save in flight, or
        // dispatch windows all refuse install/uninstall with a visible
        // reason instead of minting uncapturable progression. StateChanged
        // only surfaces transitions in the log; permission reads CanMutate.
        Registry.MutationAllowed = () => _saveDataRegistration!.CanMutate;
        _saveDataRegistration.StateChanged += state =>
        {
            if (state.Kind == SaveDataStateKind.Blocked)
                Log.LogError($"Silo persistence blocked ({state.Reason}: {state.Detail}) — silo installs/uninstalls are refused until it recovers.");
            else if (state.Kind == SaveDataStateKind.Ready)
                Log.LogInfo("Silo persistence ready.");
        };

        _harmony = new Harmony(PluginGuid);
        // PatchAll(Assembly) recurses into nested types, picking up the
        // [HarmonyPatch] attributes inside ItemLoadPatches.ItemPostfix /
        // ItemLoadPatches.RecipePostfix and the gameplay patches.
        // PatchAll(Type) only patches the type passed in.
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
        _saveDataRegistration?.Dispose();
        _saveDataRegistration = null;
        Enabled = false;
    }
}
