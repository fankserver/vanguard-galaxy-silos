using HarmonyLib;
using LightJson;
using Source.Util;
using VGSilos.Domain;

namespace VGSilos.Patches;

/// <summary>
/// Wires <see cref="SiloPersistence"/> into the vanilla save flow:
///   • postfix on <see cref="SaveGameFile.LoadSaveGame"/> — fires after
///     vanilla load completes, with the save's Name available on the
///     instance. We use that as the sidecar key.
///   • postfix on <see cref="SaveGame.Store"/> — fires after vanilla
///     save completes, with the destination saveName as a parameter.
///
/// Both writes are best-effort: any exception in our sidecar I/O is
/// logged and swallowed, never propagated up to the vanilla save/load
/// path. A broken sidecar must never block the vanilla game from loading
/// or saving.
///
/// Existing-save support: this hook makes the mod work transparently on
/// pre-existing saves. Vanilla never reads or writes the sidecar; the
/// vanilla .save file is untouched. First load on a save that has no
/// sidecar yields a fresh registry, which is exactly what we want — the
/// player starts the silo progression from scratch on existing saves.
/// </summary>
internal static class SaveLoadPatches
{
    [HarmonyPatch(typeof(SaveGameFile), nameof(SaveGameFile.LoadSaveGame))]
    private static class LoadPatch
    {
        // ReSharper disable once UnusedMember.Local
        [HarmonyPostfix]
        private static void Postfix(SaveGameFile __instance)
        {
            if (Plugin.Instance == null) return;
            try
            {
                SiloPersistence.Load(Plugin.Instance.Registry, __instance.Name, Plugin.Log);
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"VGSilos load hook failed: {ex}");
            }
        }
    }

    [HarmonyPatch(typeof(SaveGame), nameof(SaveGame.Store))]
    private static class StorePatch
    {
        // ReSharper disable once UnusedMember.Local
        [HarmonyPostfix]
        private static void Postfix(JsonObject data, string saveName)
        {
            if (Plugin.Instance == null) return;
            try
            {
                Plugin.Instance.Registry.CurrentSaveName = saveName;
                SiloPersistence.Save(Plugin.Instance.Registry, Plugin.Log);
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"VGSilos save hook failed: {ex}");
            }
        }
    }
}
