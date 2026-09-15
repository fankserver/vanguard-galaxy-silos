using System.Collections.Generic;
using System.IO;
using System.Text;
using Source.Item;
using VGSilos.Domain;
using Xunit;

namespace VGSilos.Tests;

public sealed class SiloSidecarCodecTests
{
    private static Dictionary<string, StationSilos> SampleStations() => new()
    {
        ["station-a-guid"] = new StationSilos
        {
            MaxMounts = 3,
            Installed = { InstalledSilo.Universal(2), InstalledSilo.Specialized(RefinedMaterial.Titanium, 3) },
        },
        ["station-b-guid"] = new StationSilos
        {
            MaxMounts = 1,
            Installed = { InstalledSilo.Specialized(RefinedMaterial.Astatine, 1) },
        },
    };

    [Fact]
    public void EncodeDecodeRoundTripsFullState()
    {
        var payload = SiloSidecarCodec.Encode("first-guid-1", SampleStations());

        var state = SiloSidecarCodec.Decode(payload);

        Assert.Equal(SiloConstants.SidecarSchemaVersion, state.Version);
        Assert.Equal("first-guid-1", state.FirstDockedStationGuid);
        Assert.Equal(2, state.Stations.Count);
        var a = state.Stations["station-a-guid"];
        Assert.Equal(3, a.MaxMounts);
        Assert.Equal(new[] { InstalledSilo.Universal(2), InstalledSilo.Specialized(RefinedMaterial.Titanium, 3) }, a.Installed);
        Assert.Single(state.Stations["station-b-guid"].Installed);
    }

    [Fact]
    public void PayloadIsJsonAndSchemaTagged()
    {
        var payload = SiloSidecarCodec.Encode(null, new Dictionary<string, StationSilos>());
        var text = Encoding.UTF8.GetString(payload);
        Assert.Contains("\"version\": 1", text);
        Assert.Contains("\"firstDockedStationGuid\": null", text);
        Assert.True(SiloSidecarCodec.IsValid(payload));
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("{\"version\":999,\"firstDockedStationGuid\":null,\"stations\":{}}")]
    [InlineData("{\"version\":1,\"firstDockedStationGuid\":null,\"stations\":null}")]
    public void InvalidPayloadsFailDecodeAndValidation(string raw)
    {
        var bytes = Encoding.UTF8.GetBytes(raw);
        Assert.False(SiloSidecarCodec.IsValid(bytes));
        Assert.Throws<InvalidDataException>(() => SiloSidecarCodec.Decode(bytes));
    }

    [Fact]
    public void OversizedPayloadsAreRefusedAtTheApiBound()
    {
        Assert.Throws<InvalidDataException>(() => SiloSidecarCodec.Decode(new byte[SiloSidecarCodec.MaxPayloadBytes + 1]));
        Assert.False(SiloSidecarCodec.IsValid(new byte[SiloSidecarCodec.MaxPayloadBytes + 1]));
    }
}
