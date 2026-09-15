using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using Source.Galaxy.POI;
using Source.Item;

namespace VGSilos.Domain;

/// <summary>
/// Central in-memory model for all silo state. Single source of truth for:
/// per-material global cap, per-station installation lists, mount counts,
/// and silo-bay availability per station. Persistence is handled by
/// <see cref="SiloSaveDataProvider"/>; this class is pure logic.
/// </summary>
internal sealed class SiloRegistry
{
    private readonly ManualLogSource _log;
    private readonly float _availabilityChance;

    // Keyed by SpaceStation.guid (stable Guid string from MapElement).
    private readonly Dictionary<string, StationSilos> _stations = new();

    // Cached "is this station silo-capable?" decision per guid. Decided once
    // from the station's backgroundSeed; never re-rolls.
    private readonly Dictionary<string, bool> _capabilityCache = new();

    private string? _firstDockedStationGuid;
    private string? _currentSaveName;

    public SiloRegistry(ManualLogSource log, float availabilityChance)
    {
        _log = log;
        _availabilityChance = availabilityChance;
    }

    public string? CurrentSaveName
    {
        get => _currentSaveName;
        set => _currentSaveName = value;
    }

    public string? FirstDockedStationGuid
    {
        get => _firstDockedStationGuid;
        set => _firstDockedStationGuid = value;
    }

    // -----------------------------------------------------------------------
    // CAP COMPUTATION
    // -----------------------------------------------------------------------

    /// <summary>
    /// Per-material global cap. <see cref="SiloConstants.BaseCapPerMaterial"/>
    /// plus the sum of every installed silo's contribution to <paramref name="mat"/>
    /// across every station the player has invested in.
    /// </summary>
    public float GetCap(RefinedMaterial mat)
    {
        var sum = SiloConstants.BaseCapPerMaterial;
        foreach (var station in _stations.Values)
        {
            foreach (var silo in station.Installed)
            {
                sum += silo.ContributionTo(mat);
            }
        }
        return sum;
    }

    /// <summary>
    /// Snapshot of caps for all 8 materials. Allocates — call sparingly,
    /// not from a per-frame hot path.
    /// </summary>
    public IReadOnlyDictionary<RefinedMaterial, float> GetAllCaps()
    {
        var result = new Dictionary<RefinedMaterial, float>(8);
        foreach (var mat in SiloConstants.AllMaterials)
        {
            result[mat] = GetCap(mat);
        }
        return result;
    }

    // -----------------------------------------------------------------------
    // STATION QUERIES
    // -----------------------------------------------------------------------

    public bool IsSiloCapable(SpaceStation station)
    {
        if (!HasRefinery(station))
        {
            LogObservedOnce(station, "no refinery → not silo-capable");
            return false;
        }
        if (station.guid == _firstDockedStationGuid)
        {
            LogObservedOnce(station, "starter override → silo-capable");
            return true;
        }

        if (_capabilityCache.TryGetValue(station.guid, out var cached))
            return cached;

        var roll = SeededRoll(station.guid, salt: 0x510a);
        var capable = roll < (int)(_availabilityChance * 100);
        _capabilityCache[station.guid] = capable;
        LogObservedOnce(station, $"refinery + roll={roll}/100 vs {(int)(_availabilityChance * 100)} → {(capable ? "silo-capable" : "no silo bay")}");
        return capable;
    }

    // First-observation log per station so the BepInEx log shows exactly
    // why each station did or didn't get a silo bay. Kept Info-level
    // because each station only logs once per session.
    private readonly HashSet<string> _logged = new();
    private void LogObservedOnce(SpaceStation s, string msg)
    {
        if (_logged.Add(s.guid))
            _log.LogInfo($"Station {s.name} [{s.guid.Substring(0, 8)}]: {msg}");
    }

    /// <summary>
    /// Mount count for a silo-capable station. Lazy-initialized from the
    /// station's seed on first call, then persisted in the registry.
    /// </summary>
    public int GetMaxMounts(SpaceStation station)
    {
        if (!IsSiloCapable(station)) return 0;
        if (_stations.TryGetValue(station.guid, out var existing))
            return existing.MaxMounts;

        var maxMounts = station.guid == _firstDockedStationGuid
            ? SiloConstants.StarterStationMountCount
            : RollMountCount(station.guid);

        _stations[station.guid] = new StationSilos { MaxMounts = maxMounts };
        return maxMounts;
    }

    public StationSilos? GetStation(SpaceStation station)
    {
        if (!IsSiloCapable(station)) return null;
        // Side effect: lazy-create the StationSilos record on first query.
        _ = GetMaxMounts(station);
        return _stations.GetValueOrDefault(station.guid);
    }

    public IEnumerable<KeyValuePair<string, StationSilos>> AllStations() => _stations;

    // -----------------------------------------------------------------------
    // INSTALL / UNINSTALL
    // -----------------------------------------------------------------------

    public bool TryInstall(SpaceStation station, InstalledSilo silo)
    {
        var record = GetStation(station);
        if (record == null)
        {
            _log.LogWarning($"Install rejected: {station.name} is not silo-capable.");
            return false;
        }
        if (record.IsFull)
        {
            _log.LogWarning($"Install rejected: {station.name} silo bay full ({record.Installed.Count}/{record.MaxMounts}).");
            return false;
        }
        record.Installed.Add(silo);
        _log.LogInfo($"Installed {silo.DisplayName()} at {station.name} ({record.Installed.Count}/{record.MaxMounts}).");
        return true;
    }

    /// <summary>
    /// Removes the silo at the given slot index. Returns the removed silo
    /// (caller decides refund policy) or null if the slot is empty/invalid.
    /// </summary>
    public InstalledSilo? TryUninstall(SpaceStation station, int slotIndex)
    {
        var record = GetStation(station);
        if (record == null || slotIndex < 0 || slotIndex >= record.Installed.Count)
            return null;

        var removed = record.Installed[slotIndex];
        record.Installed.RemoveAt(slotIndex);
        _log.LogInfo($"Uninstalled {removed.DisplayName()} from {station.name} ({record.Installed.Count}/{record.MaxMounts}).");
        return removed;
    }

    // -----------------------------------------------------------------------
    // SEEDED ROLLS
    // -----------------------------------------------------------------------

    /// <summary>
    /// Deterministic 0..99 roll from the station's <see cref="MapElement.guid"/>
    /// (always populated, unique per station, stable across saves) plus a
    /// salt. The salt lets us derive multiple independent rolls from the
    /// same guid (capability check vs. mount count vs. future features).
    ///
    /// Uses guid instead of <c>backgroundSeed</c> because backgroundSeed
    /// can be 0 on existing-save stations that pre-date that field, which
    /// would make the roll degenerate (every station rolls identically).
    /// </summary>
    private static int SeededRoll(string guid, ulong salt)
    {
        // FNV-1a 64-bit over the UTF-16 chars of the guid, XOR'd with salt,
        // then SplitMix64-finalised. Cheap, no allocation, no external dep.
        unchecked
        {
            const ulong fnvOffset = 0xCBF29CE484222325UL;
            const ulong fnvPrime = 0x100000001B3UL;
            var hash = fnvOffset;
            foreach (var c in guid)
            {
                hash ^= c;
                hash *= fnvPrime;
            }
            var z = hash ^ salt;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            z ^= z >> 31;
            return (int)(z % 100);
        }
    }

    private static int RollMountCount(string guid)
    {
        var roll = SeededRoll(guid, salt: 0xb44e);
        if (roll < SiloConstants.OneMountThreshold) return 1;
        if (roll < SiloConstants.TwoMountThreshold) return 2;
        return 3;
    }

    private static bool HasRefinery(SpaceStation station) =>
        station.HasFacility(SpaceStationFacility.Refinery);

    // -----------------------------------------------------------------------
    // RAW STATE ACCESS (for persistence layer)
    // -----------------------------------------------------------------------

    /// <summary>Wipes all in-memory state. Used on save-load or new game.</summary>
    public void Clear()
    {
        _stations.Clear();
        _capabilityCache.Clear();
        _firstDockedStationGuid = null;
    }

    /// <summary>Populates state from decoded sidecar JSON (API payload or one-time legacy import).</summary>
    public void LoadStationsRaw(IReadOnlyDictionary<string, StationSilos> stations)
    {
        _stations.Clear();
        foreach (var kv in stations)
            _stations[kv.Key] = kv.Value;
    }

    /// <summary>Snapshot consumed by the SaveData provider capture.</summary>
    public IReadOnlyDictionary<string, StationSilos> SnapshotStations() =>
        _stations.ToDictionary(kv => kv.Key, kv => kv.Value);
}
