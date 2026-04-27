using System.Collections.Generic;
using System.IO;
using BepInEx.Logging;
using Newtonsoft.Json;
using UnityEngine;

namespace VGSilos.Domain;

/// <summary>
/// Sidecar JSON persistence for the silo registry. One file per save:
/// <c>{persistentDataPath}/Saves/vgsilos.{saveName}.json</c>.
///
/// Lives next to the vanilla save (.save) but is fully independent:
/// uninstalling the mod leaves the vanilla save untouched, reinstalling
/// picks the sidecar back up.
/// </summary>
internal static class SiloPersistence
{
    private sealed class SidecarDocument
    {
        [JsonProperty("version")]
        public int Version { get; set; } = SiloConstants.SidecarSchemaVersion;

        [JsonProperty("firstDockedStationGuid")]
        public string? FirstDockedStationGuid { get; set; }

        [JsonProperty("stations")]
        public Dictionary<string, StationSilos> Stations { get; set; } = new();
    }

    private static string SidecarPath(string saveName) =>
        Path.Combine(Application.persistentDataPath, "Saves",
            $"{SiloConstants.SidecarFilenamePrefix}.{saveName}.json");

    public static void Load(SiloRegistry registry, string saveName, ManualLogSource log)
    {
        registry.Clear();
        registry.CurrentSaveName = saveName;

        var path = SidecarPath(saveName);
        if (!File.Exists(path))
        {
            log.LogInfo($"No sidecar at {path} — starting fresh state.");
            return;
        }

        try
        {
            var json = File.ReadAllText(path);
            var doc = JsonConvert.DeserializeObject<SidecarDocument>(json);
            if (doc == null)
            {
                log.LogWarning($"Sidecar at {path} parsed as null — starting fresh.");
                return;
            }
            if (doc.Version != SiloConstants.SidecarSchemaVersion)
            {
                log.LogWarning($"Sidecar version mismatch (file={doc.Version}, expected={SiloConstants.SidecarSchemaVersion}) — starting fresh.");
                return;
            }

            registry.FirstDockedStationGuid = doc.FirstDockedStationGuid;
            registry.LoadStationsRaw(doc.Stations);
            log.LogInfo($"Loaded sidecar from {path}: {doc.Stations.Count} stations.");
        }
        catch (System.Exception ex)
        {
            log.LogError($"Failed to load sidecar at {path}: {ex}");
        }
    }

    public static void Save(SiloRegistry registry, ManualLogSource log)
    {
        if (registry.CurrentSaveName == null)
        {
            log.LogWarning("Skipping sidecar save: no current save name.");
            return;
        }

        var path = SidecarPath(registry.CurrentSaveName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var doc = new SidecarDocument
        {
            Version = SiloConstants.SidecarSchemaVersion,
            FirstDockedStationGuid = registry.FirstDockedStationGuid,
            Stations = new Dictionary<string, StationSilos>(registry.SnapshotStations()),
        };

        try
        {
            var json = JsonConvert.SerializeObject(doc, Formatting.Indented);
            File.WriteAllText(path, json);
            log.LogInfo($"Saved sidecar to {path}: {doc.Stations.Count} stations.");
        }
        catch (System.Exception ex)
        {
            log.LogError($"Failed to save sidecar at {path}: {ex}");
        }
    }
}
