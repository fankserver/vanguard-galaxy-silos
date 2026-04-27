using HarmonyLib;
using Source.Mining;

namespace VGSilos.Patches;

/// <summary>
/// Scopes the cap clamp to refinery production. Sets
/// <see cref="RefinedMaterialCapPatches.InRefineryProduction"/> to true
/// while <see cref="RefineryJob.ProgressJob"/> runs, then back to false.
///
/// All four vanilla call sites of <c>GamePlayer.AddRefinedMaterial</c>
/// reach our cap-clamp prefix, but only this one (production) should be
/// capped. The flag distinguishes production from refund/load/gift paths
/// without us having to walk a stack trace on every call.
///
/// Uses a finalizer to guarantee the flag is reset even if vanilla throws.
///
/// TODO(silocap-orejob): wire job-pause logic. When a job's destination
/// material is already at cap, return <c>false</c> from this prefix to
/// skip the vanilla tick — that prevents surplus production from being
/// silently destroyed by the clamp. Needs the OreItemData → RefinedMaterial
/// mapping (one decomp grep). Until wired, jobs at-cap still complete and
/// the clamp drops the excess (production-only side effect; refunds and
/// loads are unaffected because of the scope flag).
/// </summary>
[HarmonyPatch(typeof(RefineryJob), nameof(RefineryJob.ProgressJob))]
internal static class RefineryProductionScopePatch
{
    // ReSharper disable once UnusedMember.Local
    [HarmonyPrefix]
    private static void Prefix()
    {
        RefinedMaterialCapPatches.InRefineryProduction = true;
    }

    // ReSharper disable once UnusedMember.Local
    [HarmonyFinalizer]
    private static System.Exception? Finalizer(System.Exception? __exception)
    {
        RefinedMaterialCapPatches.InRefineryProduction = false;
        return __exception; // re-throw any vanilla exception
    }
}
