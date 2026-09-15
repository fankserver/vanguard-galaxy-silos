using System;
using System.Collections.Generic;
using System.IO;
using BepInEx.Logging;
using VGModAPI;
using VGSilos.Domain;
using VGSilos.Tests.Support;
using Xunit;

namespace VGSilos.Tests;

/// <summary>
/// Admission is fail-closed: any non-Registered outcome (or an API that
/// throws on access) must produce a null registration — the signal Plugin
/// uses to stay disabled for the process. There is no fallback authority,
/// so these tests pin the refusal matrix the disabled state keys on.
/// </summary>
public sealed class SiloSaveDataInstallerTests
{
    private static SiloRegistry Registry() => new(new ManualLogSource("silos-tests"), 0.6f);

    [Fact]
    public void SuccessfulRegistrationReturnsProviderAndRegistration()
    {
        var api = new FakeSaveDataService();
        var registration = SiloSaveDataInstaller.TryRegister(
            api, Registry(), true, _ => { }, out var status, out var detail);

        Assert.NotNull(registration);
        Assert.Equal(SaveDataRegistrationStatus.Registered, status);
        Assert.Equal(1, api.RegisterCalls);
        var provider = Assert.Single(api.Providers);
        Assert.Equal("vgsilos", provider.Owner);
        Assert.Equal(SiloConstants.SidecarSchemaVersion, provider.SchemaVersion);
        Assert.NotNull(provider.Capture);
        Assert.NotNull(provider.Restore);
        Assert.NotNull(provider.Validate);
        // The provider instance handed to the API must be self-consistent.
        var payload = provider.Capture();
        Assert.True(provider.Validate(payload));
        provider.Restore(new SessionSnapshot(Guid.NewGuid(), SessionPhase.PlayerReady, SessionOrigin.NewGame, null), payload);
    }

    [Theory]
    [InlineData(SaveDataRegistrationStatus.Unavailable, "save storage unavailable")]
    [InlineData(SaveDataRegistrationStatus.SessionAlreadyStarted, "too late")]
    [InlineData(SaveDataRegistrationStatus.DuplicateProvider, "owner taken")]
    [InlineData(SaveDataRegistrationStatus.LimitExceeded, "owner limit")]
    [InlineData(SaveDataRegistrationStatus.InvalidProvider, "bad provider")]
    public void RefusalIsADisabledStateOutcomeNotAFallback(SaveDataRegistrationStatus status, string detail)
    {
        var api = new FakeSaveDataService { NextStatus = status, NextDetail = detail };
        var registration = SiloSaveDataInstaller.TryRegister(
            api, Registry(), true, _ => { }, out var actual, out var actualDetail);

        Assert.Null(registration);
        Assert.Equal(status, actual);
        Assert.Equal(detail, actualDetail);
        // A refused attempt leaves the plugin gate (registration == null ⇒
        // Enabled == false ⇒ no PatchAll, no factories) keeping the whole
        // mod inert — there is no fallback authority to drift into.
    }

    [Fact]
    public void ThrowingServiceIsReportedAsRefusalNotCrash()
    {
        var api = new FakeSaveDataService { ThrowOnRegister = new InvalidOperationException("api not booted") };
        var registration = SiloSaveDataInstaller.TryRegister(
            api, Registry(), true, _ => { }, out var status, out var detail);
        Assert.Null(registration);
        Assert.Equal(SaveDataRegistrationStatus.Unavailable, status);
        Assert.Contains("InvalidOperationException", detail);
    }

    [Fact]
    public void RegistryMutationsSurviveOnlyUntilProviderRestoreReplacesThem()
    {
        // Guards the fail-closed story: with no admitted provider nothing in
        // Domain silently writes save data on its own — capture reads live
        // registry state and restore replaces it wholesale; no file writes
        // exist in the provider path at all.
        var registry = Registry();
        var files = new List<string>();
        var provider = new SiloSaveDataProvider(registry, true, _ => { },
            saveName => { files.Add("path:" + saveName); return "/nonexistent/vgsilos." + saveName + ".json"; },
            _ => false,
            _ => throw new InvalidOperationException("must not read a missing sidecar"));
        provider.Restore(new SessionSnapshot(Guid.NewGuid(), SessionPhase.PlayerReady, SessionOrigin.SaveLoad, "/saves/Save5.save"), null);
        Assert.Equal("path:Save5", Assert.Single(files));
        Assert.Empty(registry.SnapshotStations());
        Assert.Equal("Save5", registry.CurrentSaveName);
    }
}
