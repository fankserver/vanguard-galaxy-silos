using System.Collections.Generic;
using BepInEx.Logging;
using Source.Galaxy.POI;
using Source.Item;
using VGSilos.Domain;
using Xunit;

namespace VGSilos.Tests;

/// <summary>
/// Review finding #1 (substantive): while the SaveData registration's live
/// gate is closed — blocked provider (restore refused), save in flight,
/// provider removed — every progression mutation must be refused so no
/// uncapturable state is minted. Plugin wires MutationAllowed to
/// ISaveDataRegistration.CanMutate; these pin the registry-side contract.
/// The refused paths must not touch the (unconstructable off-game) station
/// argument, hence the null! usage below.
/// </summary>
public sealed class RegistryMutationGateTests
{
    private static SiloRegistry Registry() => new(new ManualLogSource("silos-tests"), 0.6f);

    [Fact]
    public void InstallAndUninstallRefuseWhilePersistenceIsNotWritable()
    {
        var registry = Registry();
        var writes = 0;
        registry.MutationAllowed = () => { writes++; return false; };

        Assert.False(registry.TryInstall(null!, InstalledSilo.Universal(1)));
        Assert.Null(registry.TryUninstall(null!, 0));
        Assert.Equal(2, writes);
        Assert.Empty(registry.SnapshotStations());
    }

    [Fact]
    public void GateReopeningRestoresNormalMutationBehavior()
    {
        var registry = Registry();
        registry.MutationAllowed = () => false;
        Assert.False(registry.TryInstall(null!, InstalledSilo.Universal(1)));

        registry.MutationAllowed = () => true;
        // With the gate open the normal path runs: a null station now
        // reaches the station-dependent code (proves the early refusal was
        // the gate, not permanent breakage). Assert via the delegate being
        // consulted again instead of touching Unity objects.
        var consulted = false;
        registry.MutationAllowed = () => { consulted = true; return true; };
        Assert.ThrowsAny<System.Exception>(() => registry.TryInstall(null!, InstalledSilo.Universal(1)));
        Assert.True(consulted);
    }

    [Fact]
    public void SeededRegistrySurvivesGateClosureWithoutLosingReads()
    {
        var registry = Registry();
        registry.LoadStationsRaw(new Dictionary<string, StationSilos>
        {
            ["g1"] = new StationSilos { MaxMounts = 2, Installed = { InstalledSilo.Specialized(RefinedMaterial.Oxide, 3) } },
        });
        registry.MutationAllowed = () => false;

        // Reads must keep working while blocked: caps still clamp production
        // with the loaded (not wiped) state — only mutations stop.
        Assert.True(registry.GetCap(RefinedMaterial.Oxide) >
                    SiloConstants.BaseCapPerMaterial);
        Assert.False(registry.TryInstall(null!, InstalledSilo.Universal(1)));
        Assert.Single(registry.SnapshotStations()["g1"].Installed);
    }
}
