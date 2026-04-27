using HarmonyLib;
using Source.Item;
using Source.Player;
using UnityEngine;

namespace VGSilos.Patches;

/// <summary>
/// Enforces per-material caps on <see cref="GamePlayer.AddRefinedMaterial"/>
/// — but ONLY when the call originates from refinery production.
///
/// Vanilla calls <c>AddRefinedMaterial</c> from four paths:
///   1. <see cref="Source.Mining.RefineryJob.ProgressJob"/> — production.
///   2. <see cref="Source.Mining.Forge.CancelJob"/> — refund on cancel.
///   3. <see cref="GamePlayer.FromJson"/> — save load.
///   4. <see cref="Behaviour.Item.Usable.RefinedMaterialsItem.OnUse"/> — gift/refund.
///
/// Only path #1 should be capped. Capping refunds/loads/gifts silently
/// destroys materials the player legitimately holds — observed in the
/// wild when cancelling a multi-job craft refunded less than expected.
///
/// The fix: <see cref="RefineryProductionScopePatch"/> sets a flag while
/// <c>RefineryJob.ProgressJob</c> runs. This prefix only clamps when the
/// flag is true; everything else passes through to vanilla unchanged.
///
/// First-install grandfathering: if the player has more than the cap when
/// the mod is installed against an existing save, the extra is preserved
/// (the prefix never reduces existing balance, only blocks new production
/// adds beyond the cap).
/// </summary>
[HarmonyPatch(typeof(GamePlayer), nameof(GamePlayer.AddRefinedMaterial))]
internal static class RefinedMaterialCapPatches
{
    /// <summary>
    /// True while we're inside a <c>RefineryJob.ProgressJob</c> call. Set
    /// by <see cref="RefineryProductionScopePatch"/> via prefix/postfix.
    /// The clamp only engages when this is true; refunds, save loads, and
    /// gift items bypass the cap entirely.
    /// </summary>
    internal static bool InRefineryProduction;

    // ReSharper disable once UnusedMember.Local
    [HarmonyPrefix]
    private static void Prefix(GamePlayer __instance, RefinedMaterial mat, ref float amt)
    {
        if (Plugin.Instance == null || !Plugin.Instance.CfgEnableCaps.Value) return;
        if (amt <= 0f) return;
        // Only cap production. Refunds, save loads, and item gifts bypass.
        if (!InRefineryProduction) return;

        var registry = Plugin.Instance.Registry;

        // Lazy starter-station capture: AddRefinedMaterial only fires at a
        // refinery, so the player is necessarily docked at the station they
        // refined at. First refine after a fresh sidecar = their starter.
        // Works identically on new and existing saves.
        if (registry.FirstDockedStationGuid == null)
        {
            var station = Source.Galaxy.POI.SpaceStation.current;
            if (station != null)
            {
                registry.FirstDockedStationGuid = station.guid;
                Plugin.Log.LogInfo($"Starter station set: {station.name} ({station.guid}).");
            }
        }

        var cap = registry.GetCap(mat);
        var current = __instance.CountRefinedMaterial(mat);

        // Already at or above cap (e.g. grandfathered surplus): drop the entire add.
        if (current >= cap)
        {
            amt = 0f;
            return;
        }

        var headroom = cap - current;
        if (amt <= headroom) return; // fits, no clamp needed

        // Clamp. The dropped portion is silently lost from THIS call —
        // RefineryJobPausePatches prevents jobs from reaching this state.
        var dropped = amt - headroom;
        amt = headroom;
        Plugin.Log.LogDebug(
            $"Cap clamp on {mat}: current={current:F0}, cap={cap:F0}, dropped={dropped:F0}");
    }
}
