using System.Linq;
using Behaviour.UI;
using HarmonyLib;
using Source.Galaxy.POI;
using Source.Util;

namespace VGSilos.Patches;

/// <summary>
/// Appends a "Silo Bay (n/m)" line to the map-hover tooltip on silo-capable
/// stations, after vanilla's facility list.
///
/// Vanilla target: <see cref="SpaceStation.AddTooltipInfo(UITooltip)"/>.
/// The vanilla method writes a "Facilities:" header followed by a
/// <c>tooltip.AddTextLine(...)</c> call per present facility, then if
/// nothing was added emits "(none)". We append our line after that —
/// always rendered when the station is silo-capable, even if "(none)"
/// was just shown for vanilla facilities (shouldn't happen since silo
/// requires Refinery, but defensive).
///
/// Hold-Shift expansion to per-silo detail is a v2 polish — UITooltip's
/// line-add API doesn't trivially support a single-tooltip rebuild on
/// modifier-key change. v1 always shows just the count.
/// </summary>
[HarmonyPatch(typeof(SpaceStation), nameof(SpaceStation.AddTooltipInfo))]
internal static class StationTooltipPatches
{
    // ReSharper disable once UnusedMember.Local
    [HarmonyPostfix]
    private static void Postfix(SpaceStation __instance, UITooltip tooltip)
    {
        if (Plugin.Instance == null || !Plugin.Instance.CfgEnableTooltipInfo.Value) return;

        // Diagnostic: log every tooltip build for a station so we can
        // confirm the patch is firing and whether IsSiloCapable returned
        // true. Drop to LogDebug once the system is verified working.
        Plugin.Log.LogInfo($"Tooltip postfix fired for {__instance.name} [{__instance.guid.Substring(0, 8)}]");

        var registry = Plugin.Instance.Registry;
        if (!registry.IsSiloCapable(__instance)) return;

        var record = registry.GetStation(__instance);
        if (record == null) return;

        var line = $"Silo Bay ({record.Installed.Count}/{record.MaxMounts})";
        tooltip.AddTextLine(line).Text.color = ColorHelper.detailsColor;

        // Compact per-silo summary on the same line cluster — group by
        // display name so a stack of 3× Carbon Mk1 reads "Carbon Mk1 ×3".
        if (record.Installed.Count == 0) return;
        var groups = record.Installed
            .GroupBy(s => s.DisplayName())
            .Select(g => g.Count() > 1 ? $"{g.Key} ×{g.Count()}" : g.Key);
        foreach (var g in groups)
        {
            tooltip.AddTextLine("  " + g).Text.color = ColorHelper.boringGrey;
        }
    }
}
