using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace VGSilos.Domain;

/// <summary>Decoded sidecar state, independent of any storage backend.</summary>
internal sealed class SiloSidecarState
{
    public int Version { get; set; }
    public string? FirstDockedStationGuid { get; set; }
    public Dictionary<string, StationSilos> Stations { get; set; } = new();
}

/// <summary>
/// The single sidecar JSON document codec. One schema, two storages: the
/// bytes the VGModAPI SaveData provider (<see cref="SiloSaveDataProvider"/>)
/// stores are exactly what older versions wrote into
/// <c>vgsilos.{saveName}.json</c>, so the one-time legacy import (and a
/// downgrade back to those versions) stays compatible. The legacy file is
/// read-only history; nothing writes it anymore.
///
/// Payloads are bounded by <see cref="MaxPayloadBytes"/>, which matches the
/// API's owner-payload limit; oversized captures are refused before the API
/// wraps them in its envelope.
/// </summary>
internal static class SiloSidecarCodec
{
    /// <summary>Matches the VGModAPI persistence owner-payload bound (1 MiB).</summary>
    public const int MaxPayloadBytes = 1024 * 1024;

    private sealed class SidecarDocument
    {
        [JsonProperty("version")]
        public int Version { get; set; } = SiloConstants.SidecarSchemaVersion;

        [JsonProperty("firstDockedStationGuid")]
        public string? FirstDockedStationGuid { get; set; }

        [JsonProperty("stations")]
        public Dictionary<string, StationSilos> Stations { get; set; } = new();
    }

    /// <summary>Serializes registry state to UTF-8 JSON bytes (indented, as legacy files were).</summary>
    public static byte[] Encode(string? firstDockedStationGuid, IReadOnlyDictionary<string, StationSilos> stations)
    {
        var doc = new SidecarDocument
        {
            Version = SiloConstants.SidecarSchemaVersion,
            FirstDockedStationGuid = firstDockedStationGuid,
            Stations = new Dictionary<string, StationSilos>(stations),
        };
        var bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(doc, Formatting.Indented));
        if (bytes.Length > MaxPayloadBytes)
            throw new InvalidOperationException(
                $"Silo sidecar payload is {bytes.Length} bytes, above the {MaxPayloadBytes}-byte limit.");
        return bytes;
    }

    /// <summary>
    /// Parses payload bytes, throwing <see cref="InvalidDataException"/> on
    /// anything that is not a current-schema document (bad JSON, wrong
    /// version, missing sections). Never returns partial state.
    /// </summary>
    public static SiloSidecarState Decode(byte[] payload)
    {
        if (payload == null) throw new InvalidDataException("Missing sidecar payload.");
        if (payload.Length > MaxPayloadBytes) throw new InvalidDataException("Sidecar payload exceeds the size limit.");
        SidecarDocument? doc;
        try
        {
            doc = JsonConvert.DeserializeObject<SidecarDocument>(Encoding.UTF8.GetString(payload));
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Sidecar payload is not valid JSON.", ex);
        }
        if (doc == null) throw new InvalidDataException("Sidecar payload parsed as null.");
        if (doc.Version != SiloConstants.SidecarSchemaVersion)
            throw new InvalidDataException(
                $"Sidecar schema version mismatch (file={doc.Version}, expected={SiloConstants.SidecarSchemaVersion}).");
        if (doc.Stations == null) throw new InvalidDataException("Sidecar payload has no stations section.");
        return new SiloSidecarState
        {
            Version = doc.Version,
            FirstDockedStationGuid = doc.FirstDockedStationGuid,
            Stations = doc.Stations,
        };
    }

    /// <summary>Non-throwing check that a payload is decodable current-schema state.</summary>
    public static bool IsValid(byte[] payload)
    {
        try { _ = Decode(payload); return true; }
        catch (Exception) { return false; }
    }
}
