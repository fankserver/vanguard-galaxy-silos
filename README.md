# Vanguard Galaxy Silos (VGSilos)

A BepInEx plugin for [Vanguard Galaxy](https://store.steampowered.com/app/3471800/) that adds per-material storage caps to the refined-material system and a station-anchored silo network for growing them. Vanilla refined storage is a single uncapped float per material — this plugin turns that into a progression axis you build out by crafting and installing silos at space stations.

- **Per-material caps** — every refined material (Titanium, Oxide, Silicon, Tungsten, Carbon, Iridium, Platinum, Astatine) has its own cap, derived from a base value plus every silo you've installed. Caps are visible on each material badge in the Refinery and Forge UIs (color shifts material → amber → red as you fill), and the badge tooltip shows the full breakdown of where the cap comes from.
- **Crafted silo items** — 26 forge recipes: 2 universal silos × 2 tiers and 8 material-specific silos × 3 tiers. Universal silos add a small cap to every material; specialized silos add a much larger cap to one. Recipe costs scale with tier (Mk3 needs Astatine).
- **Silo bay stations** — only some refinery stations have silo mounts (seeded per-station, ~60% of refinery stations). Mount counts vary 1–3, also seeded. The map tooltip on a station shows `Silo Bay (n/m)` and the silos installed there.
- **Install via Use** — craft a silo, dock at a silo-capable station, click Use on the silo in your inventory. Same UX as using a refined-material pack.
- **Existing-save safe** — never modifies the vanilla `.save` file. Silo state lives in a sidecar JSON next to the save (`Saves/vgsilos.{name}.json`). Uninstalling the mod leaves vanilla saves intact; reinstalling restores silo state on next load.

## Install

1. **Install BepInEx 5.x** — grab `BepInEx_win_x64_5.4.x.zip` from the [BepInEx releases](https://github.com/BepInEx/BepInEx/releases) and unzip it into your Vanguard Galaxy install folder (next to `VanguardGalaxy.exe`).
2. **Launch the game once** so BepInEx creates its `BepInEx/plugins/` and `BepInEx/config/` subfolders, then close the game.
3. **Download the VGSilos release** zip from [Releases](https://github.com/fank/vanguard-galaxy-silos/releases).
4. **Unzip** into `BepInEx/plugins/`. The zip contains a single `VGSilos/` folder that drops in cleanly:
   ```
   VanguardGalaxy/BepInEx/plugins/
     VGSilos/
       VGSilos.dll
       README.md
   ```
5. **Launch the game.** Open the BepInEx console — you should see a load line ending with the number of Harmony patches applied, e.g.:
   ```
   [Info :Vanguard Galaxy Silos] Vanguard Galaxy Silos v0.1.0 loaded (N patches)
   ```

## Uninstall

Delete the `BepInEx/plugins/VGSilos/` folder. The vanilla `.save` files were never touched, so they keep working unmodified — your refined-material counters revert to the vanilla uncapped behaviour. The mod's sidecar JSONs at `Saves/vgsilos.*.json` can be deleted at your leisure or left in place (a future reinstall picks them back up).

## How to use

After installing the plugin, every refinery station's tooltip on the galaxy map shows whether it has a silo bay (`Silo Bay (n/m)`). Your starter station is guaranteed silo-capable with 2 mounts so you can experiment immediately.

1. **Open the Forge** at any forge-equipped station. Silos appear in their own "Silos" category in the recipe list.
2. **Craft a silo.** Universal Mk1 is the cheapest starter — adds +250 capacity to all 8 materials. Specialized Mk1 adds +1,000 to one chosen material.
3. **Dock at a silo-capable station** (one with a `Silo Bay (n/m)` line in its map tooltip).
4. **Click Use on the silo** in your inventory. You'll see a `Installed [silo name] at [station]` notification, the silo is consumed, and the station's silo bay count goes up by one.
5. **Refine.** Material caps now reflect the installed silos across your entire silo network. The cap is global; each silo just lives at one station.

When a material hits its cap, refinery jobs producing that material won't add the surplus to your storage — drain by spending in crafting recipes, selling, or extracting via the refinery's right-click menu.

## Troubleshooting

**No load line in the BepInEx console**
- Check that `BepInEx/plugins/VGSilos/VGSilos.dll` exists.
- Enable the console: `BepInEx/config/BepInEx.cfg` → `[Logging.Console]` → `Enabled = true`.

**Silo recipes don't appear in the Forge**
- Confirm the load line ends with `loaded (N patches)` where N is non-zero.
- Recipes are unlocked from the start — no blueprint needed. If the Forge UI shows zero silo recipes, search the BepInEx log for `VGSilos:` errors and file an issue with the log.

**I crafted a silo and it's not in my inventory**
- The Forge deposits crafted items to ship cargo first, falling back to your armory (global inventory) when cargo is full or the cargo-toggle is off. Silos always have a valid armory destination, so check both before assuming the craft failed.

**The category header reads weirdly somewhere I haven't tested**
- Silos use `ItemCategory.UnusedMissionItem` (a vanilla orphan with zero in-engine references), and a translation postfix maps `@ItemCategoryUnusedMissionItem` → "Silos" globally. If you find a UI that still shows the raw key or a different category name, open an issue — the postfix should cover every consumer of `Translation.TranslateOnly` but a buggy edge case is possible.

## Build

The repo commits **publicized stubs** of the three game-specific assemblies it references — `Assembly-CSharp.dll`, `UnityEngine.UI.dll`, `Unity.TextMeshPro.dll` — at `VGSilos/lib/`. These are method-signature-only stubs (every IL body replaced with `throw null;` by `assembly-publicizer --strip`), legal to redistribute, and enough to compile against. The real runtime takes over in-game.

The remaining references — BepInEx, HarmonyX, the Unity engine modules, and Newtonsoft.Json — come from NuGet (see `VGSilos/VGSilos.csproj`).

```bash
# Build the DLL
make build      # or: dotnet build VGSilos/VGSilos.csproj -c Debug

# Build + copy into the game's BepInEx/plugins/ folder (WSL/Steam path; edit Makefile if yours differs)
make deploy
```

To regenerate the stubs after a game update, install [`assembly-publicizer`](https://github.com/CabbageCrow/AssemblyPublicizer) and run:

```bash
assembly-publicizer --strip <game>/VanguardGalaxy_Data/Managed/Assembly-CSharp.dll  -o VGSilos/lib/Assembly-CSharp.dll
assembly-publicizer --strip <game>/VanguardGalaxy_Data/Managed/UnityEngine.UI.dll   -o VGSilos/lib/UnityEngine.UI.dll
assembly-publicizer --strip <game>/VanguardGalaxy_Data/Managed/Unity.TextMeshPro.dll -o VGSilos/lib/Unity.TextMeshPro.dll
```

`--strip` is required — without it, the committed DLLs would carry the proprietary IL bodies, which can't be redistributed.

## License

MIT — see [LICENSE](LICENSE).
