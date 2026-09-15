using System;
using System.IO;
using VGModAPI;

namespace VGSilos.Domain;

/// <summary>
/// VGModAPI SaveData provider for the silo registry — the sole persistence
/// authority. The API owns when bytes travel: it calls <see cref="Capture"/>
/// when a save begins and <see cref="Restore"/> once per session at
/// player-readiness, keyed to the vanilla save generation's hash. Silos never
/// reads or writes the vanilla <c>.save</c> file; the payload is stored in
/// the API's own generation store.
///
/// Legacy import is a ONE-TIME migration path, not a storage backend: when
/// the API has no payload for a loaded save, the pre-existing file sidecar
/// <c>{persistentDataPath}/Saves/vgsilos.{saveName}.json</c> (named from the
/// loaded save file, matching the pre-API hooks' key) is read once and left
/// untouched. There is deliberately no code path that writes legacy files —
/// dual persistence cannot be re-activated. Missing legacy file means a
/// fresh progression; corrupt/invalid legacy data throws so the API blocks
/// this provider rather than let an empty capture publish over progression
/// the player still has on disk. With the acknowledgement
/// (<c>[Persistence] ImportLegacySidecars</c>) off and a sidecar present,
/// restore refuses for the same reason: adoption without consent.
///
/// All callbacks run on the game's main thread, dispatched by the API.
/// </summary>
internal sealed class SiloSaveDataProvider
{
    /// <summary>Canonical API owner namespace for this plugin.</summary>
    public const string Owner = "vgsilos";

    private readonly SiloRegistry _registry;
    private readonly Func<string, string> _legacySidecarPath;
    private readonly Func<string, bool> _legacySidecarExists;
    private readonly Func<string, byte[]> _legacySidecarRead;
    private readonly bool _importLegacy;
    private readonly Action<string> _warn;

    /// <summary>Legacy (pre-API) sidecar location for one save name. Read-only forever.</summary>
    public static string LegacySidecarPath(string saveName) =>
        Path.Combine(UnityEngine.Application.persistentDataPath, "Saves",
            $"{SiloConstants.SidecarFilenamePrefix}.{saveName}.json");

    public SiloSaveDataProvider(
        SiloRegistry registry,
        bool importLegacy,
        Action<string> warn,
        Func<string, string>? legacySidecarPath = null,
        Func<string, bool>? legacySidecarExists = null,
        Func<string, byte[]>? legacySidecarRead = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _warn = warn ?? throw new ArgumentNullException(nameof(warn));
        _importLegacy = importLegacy;
        _legacySidecarPath = legacySidecarPath ?? LegacySidecarPath;
        _legacySidecarExists = legacySidecarExists ?? File.Exists;
        _legacySidecarRead = legacySidecarRead ?? File.ReadAllBytes;
    }

    public PersistenceProvider BuildProvider() =>
        new(Owner, SiloConstants.SidecarSchemaVersion, Capture, Restore, Validate);

    // Called by the API at save time. Any fault surfaces as an API-side
    // capture refusal (blocked provider), never as a vanilla save crash.
    internal byte[] Capture()
    {
        var bytes = SiloSidecarCodec.Encode(_registry.FirstDockedStationGuid, _registry.SnapshotStations());
        // Soft early-warning: an oversized capture hard-blocks publication
        // for the whole save via the API; notice a runaway state beforehand.
        if (bytes.Length > SiloSidecarCodec.MaxPayloadBytes / 2)
            _warn($"Silo capture is {bytes.Length} bytes — approaching the {SiloSidecarCodec.MaxPayloadBytes}-byte limit; further growth blocks saves.");
        return bytes;
    }

    internal static bool Validate(byte[] payload) => SiloSidecarCodec.IsValid(payload);

    internal void Restore(SessionSnapshot session, byte[]? payload)
    {
        if (session == null) throw new ArgumentNullException(nameof(session));
        _registry.Clear();

        // Save-name binding for the whole restore: derived from the loaded
        // path so it never goes stale across sessions; null for new games.
        var saveName = session.Origin == SessionOrigin.SaveLoad && session.SavePath != null
            ? Path.GetFileNameWithoutExtension(session.SavePath)
            : null;
        _registry.CurrentSaveName = saveName;

        if (payload != null)
        {
            var stored = SiloSidecarCodec.Decode(payload);
            _registry.FirstDockedStationGuid = stored.FirstDockedStationGuid;
            _registry.LoadStationsRaw(stored.Stations);
            return;
        }

        if (saveName == null)
        {
            // New game: fresh progression, nothing to migrate.
            return;
        }

        var path = _legacySidecarPath(saveName);
        if (!_legacySidecarExists(path))
        {
            _warn($"No API-managed silo data and no legacy sidecar for '{saveName}' — starting fresh.");
            return;
        }

        if (!_importLegacy)
            throw new InvalidDataException(
                $"Legacy silo sidecar '{path}' exists but no API-managed data was found. " +
                "Set [Persistence] ImportLegacySidecars = true in the Silos config to adopt it " +
                "(the file stays untouched), or delete the sidecar to start this save fresh.");

        var bytes = _legacySidecarRead(path);
        var state = SiloSidecarCodec.Decode(bytes); // throws → provider blocked by the API; file untouched
        _registry.FirstDockedStationGuid = state.FirstDockedStationGuid;
        _registry.LoadStationsRaw(state.Stations);
        _warn($"Imported legacy silo sidecar '{path}' ({state.Stations.Count} stations); " +
              "source file remains untouched on disk.");
    }
}

/// <summary>
/// Registers <see cref="SiloSaveDataProvider"/> with the API. Kept thin so
/// the admission decision is one testable call against an
/// <see cref="ISaveDataService"/>. Refusal — never an exception — is the
/// signal the plugin uses to stay disabled; there is no alternate
/// persistence authority.
/// </summary>
internal static class SiloSaveDataInstaller
{
    public static ISaveDataRegistration? TryRegister(
        ISaveDataService saveData,
        SiloRegistry registry,
        bool importLegacy,
        Action<string> warn,
        out SaveDataRegistrationStatus status,
        out string detail)
    {
        status = SaveDataRegistrationStatus.Unavailable;
        detail = string.Empty;
        try
        {
            var provider = new SiloSaveDataProvider(registry, importLegacy, warn);
            var result = saveData.Register(provider.BuildProvider());
            status = result.Status;
            detail = result.Detail;
            return result.Succeeded ? result.Registration : null;
        }
        catch (Exception ex)
        {
            detail = "Registration threw " + ex.GetType().Name;
            return null;
        }
    }
}
