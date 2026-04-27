using System;
using Newtonsoft.Json;
using Source.Item;

namespace VGSilos.Domain;

/// <summary>
/// One installed silo at one station. Immutable value record — to "modify"
/// a silo (e.g. upgrade Mk1 → Mk2), remove and reinstall.
/// </summary>
internal readonly struct InstalledSilo : IEquatable<InstalledSilo>
{
    public enum SiloKind { Universal, Specialized }

    [JsonProperty("kind")]
    public SiloKind Kind { get; }

    /// <summary>
    /// The targeted material for a Specialized silo. Always null for Universal.
    /// </summary>
    [JsonProperty("material")]
    public RefinedMaterial? Material { get; }

    /// <summary>1, 2, or 3. Universal currently caps at 2.</summary>
    [JsonProperty("tier")]
    public int Tier { get; }

    [JsonConstructor]
    public InstalledSilo(SiloKind kind, RefinedMaterial? material, int tier)
    {
        Kind = kind;
        Material = kind == SiloKind.Specialized ? material : null;
        Tier = tier;
    }

    public static InstalledSilo Universal(int tier) =>
        new(SiloKind.Universal, null, tier);

    public static InstalledSilo Specialized(RefinedMaterial material, int tier) =>
        new(SiloKind.Specialized, material, tier);

    /// <summary>
    /// How much capacity this silo contributes to the given material.
    /// Universal contributes the same amount to every material; Specialized
    /// contributes only to its own material.
    /// </summary>
    public float ContributionTo(RefinedMaterial mat) => Kind switch
    {
        SiloKind.Universal => SiloConstants.UniversalContributionEach(Tier),
        SiloKind.Specialized when Material == mat => SiloConstants.SpecializedContribution(Tier),
        _ => 0f,
    };

    public string DisplayName() => Kind switch
    {
        SiloKind.Universal => $"Universal Mk{Tier}",
        SiloKind.Specialized => $"{Material} Mk{Tier}",
        _ => "Silo",
    };

    public bool Equals(InstalledSilo other) =>
        Kind == other.Kind && Material == other.Material && Tier == other.Tier;

    public override bool Equals(object? obj) => obj is InstalledSilo s && Equals(s);

    public override int GetHashCode() =>
        HashCode.Combine((int)Kind, Material, Tier);
}
