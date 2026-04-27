using HarmonyLib;
using Source.Util;

namespace VGSilos.Patches;

/// <summary>
/// Globally rewrites the translation lookup for the silo category key
/// (<c>@ItemCategoryUnusedMissionItem</c> → "Silos"). Every UI in the
/// game eventually calls <see cref="Translation.TranslateOnly"/> when
/// resolving translation keys, so a single postfix here gives silos a
/// proper category label across the Forge UI, inventory tab, shops,
/// tooltips, search filters, and anything else that renders the
/// category name.
///
/// We use <see cref="Source.Item.ItemCategory.UnusedMissionItem"/> for
/// the silo itemCategory because vanilla has zero references to that
/// enum value — it's a pure orphan we claim for our own use. No risk of
/// the rewrite affecting any vanilla item, since no vanilla item ever
/// resolves to this key.
/// </summary>
[HarmonyPatch(typeof(Translation), nameof(Translation.TranslateOnly))]
internal static class SiloTranslationPatches
{
    private const string SiloCategoryKey = "@ItemCategoryUnusedMissionItem";
    private const string SiloCategoryLabel = "Silos";

    // ReSharper disable once UnusedMember.Local
    [HarmonyPostfix]
    private static void Postfix(string text, ref string __result)
    {
        if (text == SiloCategoryKey)
            __result = SiloCategoryLabel;
    }
}
