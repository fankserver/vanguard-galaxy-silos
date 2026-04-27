using System.Collections.Generic;
using Newtonsoft.Json;

namespace VGSilos.Domain;

/// <summary>
/// Per-station bookkeeping: how many mounts the station has and which silos
/// are currently installed in them. Mount count is decided once (seeded from
/// the station's own backgroundSeed) and cached here so re-rolls can't shift
/// it after a player has invested.
/// </summary>
internal sealed class StationSilos
{
    [JsonProperty("maxMounts")]
    public int MaxMounts { get; set; }

    [JsonProperty("installed")]
    public List<InstalledSilo> Installed { get; set; } = new();

    public int FreeMounts => MaxMounts - Installed.Count;

    public bool IsFull => Installed.Count >= MaxMounts;
}
