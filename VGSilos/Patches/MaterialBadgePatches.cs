using Behaviour.UI.Refinery;
using HarmonyLib;
using Source.Item;
using Source.Player;
using Source.Util;
using TMPro;
using UnityEngine;

namespace VGSilos.Patches;

/// <summary>
/// Surfaces the per-material cap in the in-game UI by extending the
/// vanilla <see cref="RefineryMaterialBadge"/> — the small count widget
/// that the Refinery UI and Forge UI both display, one per refined
/// material.
///
/// Vanilla shows the live count number on the badge already, so we don't
/// duplicate that. We only add what's genuinely new:
///
///   • Color tint on the count text — material color → amber at ≥85% →
///     saturated red at-cap. Single visual cue the player can read at a
///     glance without reading numbers.
///   • Hover tooltip enrichment — adds capacity, headroom, and a
///     per-silo breakdown of where the cap comes from. Detailed numbers
///     live here where text length isn't constrained.
///
/// Earlier iterations also tried to use the badge's vestigial
/// <c>progress</c> RectTransform as a fill bar. The prefab doesn't wire
/// it as a visible Image, so scaling it produced nothing the player
/// could see. Dropped — the color tint covers the at-a-glance signal.
/// </summary>
internal static class MaterialBadgePatches
{
    // Backing-field accessors for the badge's private serialized fields.
    private static readonly AccessTools.FieldRef<RefineryMaterialBadge, RefinedMaterial> MaterialRef =
        AccessTools.FieldRefAccess<RefineryMaterialBadge, RefinedMaterial>("material");
    private static readonly AccessTools.FieldRef<RefineryMaterialBadge, TMP_Text> CountRef =
        AccessTools.FieldRefAccess<RefineryMaterialBadge, TMP_Text>("count");

    [HarmonyPatch(typeof(RefineryMaterialBadge), "UpdateLabel")]
    private static class CountTextPatch
    {
        // ReSharper disable once UnusedMember.Local
        [HarmonyPostfix]
        private static void Postfix(RefineryMaterialBadge __instance)
        {
            if (Plugin.Instance == null || !Plugin.Instance.CfgEnableCaps.Value) return;
            try
            {
                var mat = MaterialRef(__instance);
                var count = CountRef(__instance);
                if (count == null) return;

                var current = GamePlayer.current.CountRefinedMaterial(mat);
                var cap = Plugin.Instance.Registry.GetCap(mat);
                var fill = cap > 0f ? Mathf.Clamp01(current / cap) : 0f;

                count.color = fill >= 1f
                    ? new Color(0.95f, 0.25f, 0.25f)        // red at cap
                    : fill >= 0.85f
                        ? new Color(1.00f, 0.75f, 0.20f)    // amber near cap
                        : mat.GetColor();                   // material color otherwise
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"MaterialBadgePatches.CountTextPatch failed: {ex}");
            }
        }
    }

    [HarmonyPatch(typeof(RefineryMaterialBadge), nameof(RefineryMaterialBadge.GetTooltipText))]
    private static class TooltipTextPatch
    {
        // ReSharper disable once UnusedMember.Local
        [HarmonyPostfix]
        private static void Postfix(RefineryMaterialBadge __instance, ref string __result)
        {
            if (Plugin.Instance == null || !Plugin.Instance.CfgEnableCaps.Value) return;
            try
            {
                var mat = MaterialRef(__instance);
                var registry = Plugin.Instance.Registry;
                var current = GamePlayer.current.CountRefinedMaterial(mat);
                var cap = registry.GetCap(mat);
                var headroom = cap - current;

                var sb = new System.Text.StringBuilder();
                sb.Append('\n');
                if (headroom <= 0f)
                    sb.Append($"<color=#F03030>Storage full ({GameMath.FormatNumber(current)} / {GameMath.FormatNumber(cap)})</color>\nRefinery jobs for this material will not produce until you spend or sell some.\n");
                else
                    sb.Append($"Capacity: {GameMath.FormatNumber(current)} / {GameMath.FormatNumber(cap)} (headroom: {GameMath.FormatNumber(headroom)}).\n");

                // Breakdown of where the cap comes from. Aggregate identical
                // silos into "Mk2 × N" rows so a player with eight Mk1 silos
                // doesn't get an eight-line tooltip.
                sb.Append("\nSources:\n");
                sb.Append($"  Base: {GameMath.FormatNumber(SiloConstants.BaseCapPerMaterial)}\n");

                // Group all contributing silos by display name and sum.
                var groups = new System.Collections.Generic.Dictionary<string, (int count, float total)>();
                foreach (var stationKv in registry.AllStations())
                {
                    foreach (var silo in stationKv.Value.Installed)
                    {
                        var contribution = silo.ContributionTo(mat);
                        if (contribution <= 0f) continue;
                        var name = silo.DisplayName();
                        if (!groups.TryGetValue(name, out var g)) g = (0, 0f);
                        groups[name] = (g.count + 1, g.total + contribution);
                    }
                }
                if (groups.Count == 0)
                {
                    sb.Append("  <color=#909090>No silos installed yet.</color>\n");
                }
                else
                {
                    foreach (var kv in groups)
                    {
                        sb.Append($"  {kv.Key} × {kv.Value.count}: {GameMath.FormatNumber(kv.Value.total)}\n");
                    }
                }

                __result = (__result ?? string.Empty) + sb.ToString().TrimEnd('\n');
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"MaterialBadgePatches.TooltipTextPatch failed: {ex}");
            }
        }
    }
}
