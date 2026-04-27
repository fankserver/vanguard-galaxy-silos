namespace VGSilos.Patches;

/// <summary>
/// Station UI integration for silo install/uninstall.
///
/// PIVOT (decomp finding): adding a new tab to the station UI requires
/// extending <see cref="Source.Galaxy.POI.SpaceStationFacility"/> — the
/// <see cref="Behaviour.UI.Spacestation.SpaceStationInterior"/> tab loop
/// iterates <c>spacestation.GetFacilities()</c> and looks each value up
/// in a <c>Dictionary&lt;SpaceStationFacility, Action&gt;</c> of openers.
/// Enums can't be extended at runtime without IL rewriting (banned in
/// this workspace per CLAUDE.md). So a "Silo Bay" tab is not feasible.
///
/// v1 PATH: silos are <see cref="Behaviour.Item.Usable.UsableItem"/>s
/// with <c>canUseInSpacestation=true</c>. Their <c>OnUse()</c> installs
/// at <see cref="Source.Galaxy.POI.SpaceStation.current"/>:
///   - if not silo-capable → notification "this station has no silo bay"
///   - if bay full → notification "silo bay is full at <station>"
///   - else → registry.TryInstall + consume the item
///
/// This mirrors exactly how <see cref="Behaviour.Item.Usable.RefinedMaterialsItem.OnUse"/>
/// works (push refined material into the station's refinery on use), so
/// the player already understands the affordance: dock, click "use" on
/// the item in inventory, it goes somewhere station-bound.
///
/// Uninstall in v1: a list view in the SidePanel inventory tab via a
/// custom button; or a /command-style debug for the first iteration.
/// Refund policy from the spec (Mk1→Mk1 item, Mk2→Mk1+credits, Mk3→Mk2+credits)
/// is implemented in the registry caller, not here.
///
/// This file is a placeholder — actual UsableItem subclass wiring lives
/// in a future <c>Items/</c> directory once the silo InventoryItemType
/// prefabs exist.
/// </summary>
internal static class StationUIPatches
{
    // No active patches yet. The install/uninstall flow goes through
    // UsableItem.OnUse() once silo items are constructed.
}
