using System;
using Behaviour.Item.Usable;
using Behaviour.UI.NotificationAlert;
using Behaviour.UI.Spacestation;
using Behaviour.Util;
using LightJson;
using Source.Galaxy.POI;
using Source.Item;
using Source.Util;
using VGSilos.Domain;

namespace VGSilos.Items;

/// <summary>
/// Component attached to every silo InventoryItemType. When the player
/// clicks "Use" on a silo from inventory while docked at a silo-capable
/// station, this fires, calls <see cref="SiloRegistry.TryInstall"/>,
/// and (on success) returns true so the vanilla item-consumption path
/// removes the silo from inventory.
///
/// Mirrors <see cref="RefinedMaterialsItem"/> — same shape, same lifecycle.
/// Internal accessibility is intentional: Unity's <c>AddComponent</c> uses
/// reflection so visibility doesn't matter to the runtime, and keeping it
/// internal aligns with the rest of the mod's domain types.
/// </summary>
internal class SiloUsableItem : UsableItem
{
    public override bool canUseInSpacestation => true;

    public InstalledSilo.SiloKind Kind { get; private set; }
    public RefinedMaterial? Material { get; private set; }
    public int Tier { get; private set; }

    public void Configure(InstalledSilo.SiloKind kind, RefinedMaterial? material, int tier)
    {
        Kind = kind;
        Material = kind == InstalledSilo.SiloKind.Specialized ? material : null;
        Tier = tier;
    }

    public override bool OnUse()
    {
        if (!SpaceStationInterior.instance)
        {
            Notify("@SiloMustBeDocked", "Dock at a station to install a silo.");
            return false;
        }
        var station = SpaceStation.current;
        if (station == null)
        {
            Notify("@SiloNoStation", "No station found to install at.");
            return false;
        }

        var registry = Plugin.Instance?.Registry;
        if (registry == null) return false;

        // Fail-closed UX: while durable persistence is blocked (restore
        // refused, provider removed) or a save is in flight, refuse the
        // install with a visible reason — item stays unconsumed — instead of
        // granting progression that can never be captured.
        if (!registry.MutationAllowed())
        {
            Notify("@SiloPersistBlocked", "Silo persistence unavailable — installs are disabled until saving recovers (see the BepInEx log).");
            return false;
        }

        if (!registry.IsSiloCapable(station))
        {
            Notify("@SiloNotCapable", $"{station.name} has no silo bay.");
            return false;
        }

        var stationRecord = registry.GetStation(station);
        if (stationRecord == null || stationRecord.IsFull)
        {
            var max = stationRecord?.MaxMounts ?? 0;
            Notify("@SiloBayFull", $"Silo bay full at {station.name} ({max}/{max}).");
            return false;
        }

        var silo = new InstalledSilo(Kind, Material, Tier);
        if (!registry.TryInstall(station, silo))
        {
            Notify("@SiloInstallFailed", "Silo install failed.");
            return false;
        }

        Notify("@SiloInstalled", $"Installed {silo.DisplayName()} at {station.name}.");
        return true;
    }

    public override void DataToJson(JsonObject data)
    {
        data["kind"] = Kind.ToString();
        if (Material.HasValue)
            data["material"] = Material.Value.ToString();
        data["tier"] = Tier;
    }

    public override void DataFromJson(JsonObject data)
    {
        Kind = Enum.Parse<InstalledSilo.SiloKind>(data["kind"]);
        Material = data.ContainsKey("material")
            ? Enum.Parse<RefinedMaterial>(data["material"])
            : null;
        Tier = data["tier"];
    }

    private static void Notify(string translationKey, string fallback)
    {
        var msg = Translation.Translate(translationKey);
        if (string.IsNullOrEmpty(msg) || msg == translationKey) msg = fallback;
        Singleton<NotificationManager>.Instance
            .CreateNotification(msg)
            .WithColor(ColorHelper.detailsColor)
            .Show();
    }
}
