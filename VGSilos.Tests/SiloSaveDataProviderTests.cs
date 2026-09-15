using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx.Logging;
using Source.Item;
using VGModAPI;
using VGSilos.Domain;
using Xunit;

namespace VGSilos.Tests;

public sealed class SiloSaveDataProviderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "silos-coord-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    private static SessionSnapshot LoadSession(string savePath) =>
        new(Guid.NewGuid(), SessionPhase.PlayerReady, SessionOrigin.SaveLoad, savePath);
    private static SessionSnapshot NewGameSession() =>
        new(Guid.NewGuid(), SessionPhase.PlayerReady, SessionOrigin.NewGame, null);

    private static SiloRegistry Registry() => new(new ManualLogSource("silos-tests"), 0.6f);

    private static void Seed(SiloRegistry registry)
    {
        registry.Clear();
        registry.FirstDockedStationGuid = "starter-guid";
        registry.LoadStationsRaw(new Dictionary<string, StationSilos>
        {
            ["starter-guid"] = new StationSilos
            {
                MaxMounts = 4,
                Installed = { InstalledSilo.Universal(1), InstalledSilo.Specialized(RefinedMaterial.Iridium, 2) },
            },
            ["other-guid"] = new StationSilos { MaxMounts = 2, Installed = { InstalledSilo.Universal(2) } },
        });
    }

    private static void AssertSeed(SiloRegistry registry)
    {
        Assert.Equal("starter-guid", registry.FirstDockedStationGuid);
        Assert.Equal(2, registry.SnapshotStations().Count);
        Assert.Equal(4, registry.SnapshotStations()["starter-guid"].MaxMounts);
        Assert.Equal(
            SiloConstants.BaseCapPerMaterial
            + SiloConstants.UniversalContributionEach(1) + SiloConstants.UniversalContributionEach(2)
            + SiloConstants.SpecializedContribution(2),
            registry.GetCap(RefinedMaterial.Iridium));
        Assert.Equal(
            SiloConstants.BaseCapPerMaterial
            + SiloConstants.UniversalContributionEach(1) + SiloConstants.UniversalContributionEach(2),
            registry.GetCap(RefinedMaterial.Platinum));
    }

    private SiloSaveDataProvider Provider(SiloRegistry registry, bool importLegacy = true, Action<string>? warn = null) =>
        new(registry, importLegacy, warn ?? (_ => { }),
            saveName => Path.Combine(_root, $"vgsilos.{saveName}.json"));

    [Fact]
    public void CaptureValidateRestoreRoundTripsThroughApiPayloads()
    {
        var source = Registry(); Seed(source);
        var payload = Provider(source).Capture();
        Assert.True(SiloSaveDataProvider.Validate(payload));

        var target = Registry();
        target.FirstDockedStationGuid = "stale-state-must-not-survive";
        Provider(target).Restore(LoadSession("/saves/Save3.save"), payload);

        AssertSeed(target);
        // Stable encoding: recapture equals the API-stored payload byte-for-byte.
        Assert.Equal(payload, Provider(target).Capture());
    }

    [Fact]
    public void ApiPayloadAlwaysClearsPriorSessionStateEvenWhenEmpty()
    {
        var registry = Registry(); Seed(registry);
        var empty = Provider(Registry()).Capture();
        Provider(registry).Restore(NewGameSession(), empty);
        Assert.Null(registry.FirstDockedStationGuid);
        Assert.Empty(registry.SnapshotStations());
    }

    [Fact]
    public void NewGameWithoutPayloadStartsFreshAndNeverTouchesLegacyStorage()
    {
        var registry = Registry(); Seed(registry);
        var provider = new SiloSaveDataProvider(registry, true, _ => { },
            _ => throw new InvalidOperationException("legacy path must not be consulted for a new game"),
            _ => throw new InvalidOperationException("legacy existence must not be probed for a new game"),
            _ => throw new InvalidOperationException("legacy read must not happen for a new game"));
        provider.Restore(NewGameSession(), null);
        Assert.Null(registry.FirstDockedStationGuid);
        Assert.Empty(registry.SnapshotStations());
        Assert.Null(registry.CurrentSaveName);
    }

    [Fact]
    public void LoadedSaveWithoutApiDataImportsLegacySidecarReadOnly()
    {
        Directory.CreateDirectory(_root);
        var save = Path.Combine(_root, "Save7.save");
        File.WriteAllText(save, "vanilla-save-bytes");
        var sidecar = Path.Combine(_root, "vgsilos.Save7.json");
        var seeded = Registry(); Seed(seeded);
        File.WriteAllBytes(sidecar, SiloSidecarCodec.Encode("starter-guid", seeded.SnapshotStations()));
        var sidecarBytes = File.ReadAllBytes(sidecar);

        var registry = Registry();
        var warnings = new List<string>();
        Provider(registry, warn: warnings.Add).Restore(LoadSession(save), null);

        AssertSeed(registry);
        Assert.Equal("Save7", registry.CurrentSaveName);
        Assert.Contains(warnings, w => w.Contains("Imported legacy silo sidecar") && w.Contains("untouched"));
        Assert.Equal(sidecarBytes, File.ReadAllBytes(sidecar));
        Assert.Equal("vanilla-save-bytes", File.ReadAllText(save));
        // Provider never publishes files: capture after import adds none.
        _ = Provider(registry).Capture();
        Assert.Equal(2, Directory.GetFiles(_root).Length);
    }

    [Fact]
    public void LoadedSaveWithoutApiDataOrSidecarStartsFresh()
    {
        Directory.CreateDirectory(_root);
        var save = Path.Combine(_root, "Save9.save");
        File.WriteAllText(save, "vanilla-save-bytes");
        var registry = Registry(); Seed(registry);
        var warnings = new List<string>();
        Provider(registry, warn: warnings.Add).Restore(LoadSession(save), null);
        Assert.Null(registry.FirstDockedStationGuid);
        Assert.Empty(registry.SnapshotStations());
        Assert.Equal("Save9", registry.CurrentSaveName);
        Assert.Contains(warnings, w => w.Contains("starting fresh"));
    }

    [Fact]
    public void ImportDisabledRefusesInsteadOfSilentlyWipingExistingProgression()
    {
        Directory.CreateDirectory(_root);
        var save = Path.Combine(_root, "Save1.save");
        File.WriteAllText(save, "vanilla");
        var sidecar = Path.Combine(_root, "vgsilos.Save1.json");
        var seeded = Registry(); Seed(seeded);
        var bytes = SiloSidecarCodec.Encode("starter-guid", seeded.SnapshotStations());
        File.WriteAllBytes(sidecar, bytes);

        var registry = Registry();
        var ex = Assert.Throws<InvalidDataException>(() =>
            Provider(registry, importLegacy: false).Restore(LoadSession(save), null));
        Assert.Contains("ImportLegacySidecars", ex.Message);
        Assert.Contains("delete the sidecar", ex.Message);
        Assert.Equal(bytes, File.ReadAllBytes(sidecar));
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("{\"version\":42,\"firstDockedStationGuid\":null,\"stations\":{}}")]
    [InlineData("garbage-not-json")]
    public void CorruptLegacySidecarRefusesAndIsPreserved(string raw)
    {
        Directory.CreateDirectory(_root);
        var save = Path.Combine(_root, "Save2.save");
        File.WriteAllText(save, "vanilla");
        var sidecar = Path.Combine(_root, "vgsilos.Save2.json");
        File.WriteAllText(sidecar, raw);

        var registry = Registry();
        Assert.Throws<InvalidDataException>(() =>
            Provider(registry).Restore(LoadSession(save), null));
        Assert.Equal(raw, File.ReadAllText(sidecar));
    }

    [Fact]
    public void OversizedLegacySidecarRefusesAndIsPreserved()
    {
        Directory.CreateDirectory(_root);
        var save = Path.Combine(_root, "Save3.save");
        File.WriteAllText(save, "vanilla");
        var sidecar = Path.Combine(_root, "vgsilos.Save3.json");
        File.WriteAllBytes(sidecar, new byte[SiloSidecarCodec.MaxPayloadBytes + 1]);

        Assert.Throws<InvalidDataException>(() =>
            Provider(Registry()).Restore(LoadSession(save), null));
        Assert.Equal(SiloSidecarCodec.MaxPayloadBytes + 1, new FileInfo(sidecar).Length);
    }

    [Fact]
    public void ApiPayloadOnSaveLoadShortCircuitsBeforeAnyLegacyProbe()
    {
        // Review nit: an API payload must never consult legacy storage, even
        // on the SaveLoad path where import would otherwise be considered.
        var source = Registry(); Seed(source);
        var payload = Provider(source).Capture();
        var registry = Registry();
        var provider = new SiloSaveDataProvider(registry, true, _ => { },
            _ => throw new InvalidOperationException("legacy path must not be probed with an API payload"),
            _ => throw new InvalidOperationException("legacy existence must not be checked with an API payload"),
            _ => throw new InvalidOperationException("legacy must not be read with an API payload"));
        provider.Restore(LoadSession("/saves/Save4.save"), payload);
        AssertSeed(registry);
        Assert.Equal("Save4", registry.CurrentSaveName);
    }

    [Fact]
    public void ApiPayloadRestoresSaveNameSoItNeverGoesStaleAcrossSessions()
    {
        var registry = Registry();
        var first = Provider(registry);
        first.Restore(LoadSession("/saves/SaveB.save"), null); // no legacy on disk ⇒ fresh
        Assert.Equal("SaveB", registry.CurrentSaveName);
        var secondPayload = Provider(Registry()).Capture();
        Provider(registry).Restore(LoadSession("/saves/SaveA.save"), secondPayload);
        Assert.Equal("SaveA", registry.CurrentSaveName);
        Provider(registry).Restore(NewGameSession(), secondPayload);
        Assert.Null(registry.CurrentSaveName);
    }

    [Fact]
    public void RunawayCaptureWarnsBeforeHittingTheHardLimit()
    {
        var registry = Registry();
        var many = new Dictionary<string, StationSilos>();
        for (var i = 0; i < 2000; i++)
            many["guid-" + i] = new StationSilos
            {
                MaxMounts = 3,
                Installed = { InstalledSilo.Universal(3), InstalledSilo.Specialized(RefinedMaterial.Astatine, 3) },
            };
        registry.LoadStationsRaw(many);
        var warnings = new List<string>();
        var bytes = Provider(registry, warn: warnings.Add).Capture();
        Assert.True(bytes.Length > SiloSidecarCodec.MaxPayloadBytes / 2);
        Assert.Contains(warnings, w => w.Contains("approaching the") && w.Contains("byte limit"));
    }

    [Fact]
    public void ProviderDeclaresCanonicalOwnerAndCurrentSchema()
    {
        var provider = Provider(Registry()).BuildProvider();
        Assert.Equal("vgsilos", provider.Owner);
        Assert.Equal(SiloConstants.SidecarSchemaVersion, provider.SchemaVersion);
        Assert.Empty(provider.Migrations);
    }
}
