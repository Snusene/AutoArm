// AutoArm RimWorld 1.5+ mod - automatic weapon management
// This file: Tracks player-forced weapon assignments
// Respects player decisions when manually equipping weapons
// Uses: AutoArmGameComponent for save/load persistence
// Critical: Prevents mod from overriding player's manual weapon choices
//
// IMPORTANT: When SimpleSidearms is loaded, it becomes the COMPLETE authority
// on forced weapons. AutoArm does NOT automatically sync with SS anymore.
// Players must use SimpleSidearms' own UI (right-click menu) to force weapons.
// Exception: Bonded weapons are still auto-synced to SS when "Respect weapon bonds" is enabled.
//
// When SS is NOT loaded:
// This system tracks SPECIFIC weapon instances by their unique ID,
// not weapon types. When a colonist forces a specific obsidian knife,
// only THAT knife is forced, not all obsidian knives.

using AutoArm.Logging;
using AutoArm.Weapons;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace AutoArm.Helpers
{
    /// <summary>
    /// Consolidated forced weapon tracking (fixes #12)
    /// Tracks specific weapon instances, not just types
    /// </summary>
    public static class ForcedWeaponHelper
    {
        // Track specific forced weapon instances by their unique ID
        private static Dictionary<Pawn, HashSet<int>> forcedWeaponIds = new Dictionary<Pawn, HashSet<int>>();

        // Track weapon types for save/load
        private static Dictionary<Pawn, HashSet<ThingDef>> forcedWeaponDefs = new Dictionary<Pawn, HashSet<ThingDef>>();

        // Track the primary forced weapon reference
        private static Dictionary<Pawn, ThingWithComps> forcedPrimaryWeapon = new Dictionary<Pawn, ThingWithComps>();

        /// <summary>
        /// Force a weapon in SimpleSidearms - only used for bonded weapons
        /// </summary>
        public static void ForceBondedWeaponInSimpleSidearms(Pawn pawn, ThingWithComps weapon)
        {
            if (pawn == null || weapon == null)
                return;

            if (!SimpleSidearmsCompat.IsLoaded())
                return;

            // Only sync bonded weapons
            var biocomp = weapon.TryGetComp<CompBiocodable>();
            if (biocomp?.CodedPawn != pawn)
                return;

            SimpleSidearmsCompat.SetWeaponAsForced(pawn, weapon);

            if (AutoArmMod.settings?.debugLogging == true)
            {
                AutoArmLogger.Debug($"[{pawn.Name?.ToStringShort ?? "Unknown"}] Bonded weapon {weapon.Label} synced to SimpleSidearms as forced");
            }
        }

        /// <summary>
        /// Mark a specific weapon instance as forced for a pawn
        /// When SimpleSidearms is loaded, this ONLY tracks locally - doesn't sync to SS
        /// Players must use SS's own UI to force weapons in SS
        /// </summary>
        public static void SetForced(Pawn pawn, ThingWithComps weapon)
        {
            if (pawn == null || weapon == null)
                return;

            // Don't track non-weapons as forced
            if (!WeaponValidation.IsProperWeapon(weapon))
            {
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.Debug($"[{pawn.Name?.ToStringShort ?? "Unknown"}] Ignoring SetForced for non-weapon: {weapon.Label}");
                }
                return;
            }

            // When SimpleSidearms is loaded, we no longer automatically sync
            // Players must use SS's UI to force weapons
            if (SimpleSidearmsCompat.IsLoaded())
            {
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.Debug($"[{pawn.Name?.ToStringShort ?? "Unknown"}] SimpleSidearms is loaded - use SS UI to force weapons");
                }
                // Don't sync to SS automatically anymore
                // Just return without tracking locally since SS manages forcing
                return;
            }

            // SimpleSidearms NOT loaded - use AutoArm's tracking

            // Track the specific weapon instance
            forcedPrimaryWeapon[pawn] = weapon;

            // Add the specific weapon ID
            if (!forcedWeaponIds.ContainsKey(pawn))
                forcedWeaponIds[pawn] = new HashSet<int>();
            forcedWeaponIds[pawn].Add(weapon.thingIDNumber);

            // Also track def for backwards compatibility
            if (!forcedWeaponDefs.ContainsKey(pawn))
                forcedWeaponDefs[pawn] = new HashSet<ThingDef>();
            forcedWeaponDefs[pawn].Add(weapon.def);

            if (AutoArmMod.settings?.debugLogging == true)
            {
                AutoArmLogger.LogWeapon(pawn, weapon, $"Marked as forced weapon (ID: {weapon.thingIDNumber})");
            }
        }

        /// <summary>
        /// Clear only the forced primary weapon reference (not all forced weapons)
        /// </summary>
        public static void ClearForcedPrimary(Pawn pawn)
        {
            if (pawn == null)
                return;

            // Get the weapon before removing
            ThingWithComps weaponToRemove = null;
            ThingDef weaponDefToCheck = null;
            if (forcedPrimaryWeapon.TryGetValue(pawn, out var forcedWeapon) && forcedWeapon != null)
            {
                weaponToRemove = forcedWeapon;
                weaponDefToCheck = forcedWeapon.def;
            }

            forcedPrimaryWeapon.Remove(pawn);

            // Remove the specific weapon ID if we have it
            if (weaponToRemove != null && forcedWeaponIds.ContainsKey(pawn))
            {
                forcedWeaponIds[pawn].Remove(weaponToRemove.thingIDNumber);
                if (forcedWeaponIds[pawn].Count == 0)
                    forcedWeaponIds.Remove(pawn);
            }

            // Check if we should also remove the def from forcedWeaponDefs
            if (weaponDefToCheck != null && forcedWeaponDefs.ContainsKey(pawn))
            {
                // Check if the pawn still has any weapons of this type
                bool stillHasWeaponOfType = false;

                // Check current primary
                if (pawn.equipment?.Primary?.def == weaponDefToCheck)
                {
                    stillHasWeaponOfType = true;
                }

                // Check inventory
                if (!stillHasWeaponOfType && pawn.inventory?.innerContainer != null)
                {
                    foreach (var item in pawn.inventory.innerContainer)
                    {
                        if (item is ThingWithComps weaponInInventory && weaponInInventory.def == weaponDefToCheck)
                        {
                            stillHasWeaponOfType = true;
                            break;
                        }
                    }
                }

                // If pawn no longer has any weapons of this type, remove it from forced defs
                if (!stillHasWeaponOfType)
                {
                    forcedWeaponDefs[pawn].Remove(weaponDefToCheck);
                    if (forcedWeaponDefs[pawn].Count == 0)
                    {
                        forcedWeaponDefs.Remove(pawn);
                    }
                    if (AutoArmMod.settings?.debugLogging == true)
                    {
                        AutoArmLogger.Debug($"[{pawn.Name?.ToStringShort ?? "Unknown"}] Removed forced weapon def {weaponDefToCheck.defName} - no longer has any");
                    }
                }
            }
        }

        /// <summary>
        /// Clear all forced weapons for a pawn
        /// When SimpleSidearms is loaded, this only clears local tracking (SS maintains its own)
        /// </summary>
        public static void ClearForced(Pawn pawn)
        {
            if (pawn == null)
                return;

            // Note: When SS is loaded, we don't clear SS's forced status
            // That can only be done through SS's UI
            // We only clear AutoArm's local tracking (which isn't used when SS is loaded anyway)

            int count = 0;
            if (forcedWeaponIds.ContainsKey(pawn))
            {
                count = forcedWeaponIds[pawn].Count;
                forcedWeaponIds.Remove(pawn);
            }

            if (forcedWeaponDefs.ContainsKey(pawn))
            {
                forcedWeaponDefs.Remove(pawn);
            }

            forcedPrimaryWeapon.Remove(pawn);

            if (count > 0 && !SimpleSidearmsCompat.IsLoaded())
            {
                // Only log if SS is not loaded (when SS is loaded, this is just cleanup)
                AutoArmLogger.Debug($"[{pawn.Name?.ToStringShort ?? "Unknown"}] Cleared {count} forced weapon(s)");
            }
        }

        /// <summary>
        /// Check if a specific weapon instance is forced
        /// When SimpleSidearms is loaded, it becomes the complete authority
        /// </summary>
        public static bool IsForced(Pawn pawn, ThingWithComps weapon)
        {
            if (pawn == null || weapon == null)
                return false;

            // When SimpleSidearms is loaded, it's the ONLY authority on forced weapons
            if (SimpleSidearmsCompat.IsLoaded())
            {
                // Check SS's forced/preferred status for this weapon type
                bool isForced, isPreferred;
                if (SimpleSidearmsCompat.IsWeaponTypeForced(pawn, weapon.def, out isForced, out isPreferred))
                {
                    return true; // SS says this weapon type is forced/preferred
                }
                return false; // SS doesn't have this weapon forced
            }

            // SimpleSidearms NOT loaded - use AutoArm's own tracking

            // Don't consider weapons forced during SimpleSidearms operations
            // (This check is redundant when SS is loaded, but kept for when SS is not loaded)
            if (DroppedItemTracker.IsSimpleSidearmsSwapInProgress(pawn))
                return false;

            // Check if the specific weapon instance is forced by ID
            if (forcedWeaponIds.ContainsKey(pawn) && forcedWeaponIds[pawn].Contains(weapon.thingIDNumber))
                return true;

            // Check if this is the current forced primary
            if (forcedPrimaryWeapon.TryGetValue(pawn, out var forced) && forced == weapon)
                return true;

            // DO NOT fall back to checking weapon def - that's the bug!
            return false;
        }

        /// <summary>
        /// Check if pawn has any forced weapon
        /// When SimpleSidearms is loaded, check SS's forced status
        /// </summary>
        public static bool HasForcedWeapon(Pawn pawn)
        {
            if (pawn == null)
                return false;

            // When SimpleSidearms is loaded, check if any weapon type is forced in SS
            if (SimpleSidearmsCompat.IsLoaded())
            {
                // Check primary weapon
                if (pawn.equipment?.Primary != null)
                {
                    bool isForced, isPreferred;
                    if (SimpleSidearmsCompat.IsWeaponTypeForced(pawn, pawn.equipment.Primary.def, out isForced, out isPreferred))
                        return true;
                }

                // Check inventory weapons
                if (pawn.inventory?.innerContainer != null)
                {
                    foreach (var item in pawn.inventory.innerContainer)
                    {
                        if (item is ThingWithComps weapon && weapon.def.IsWeapon)
                        {
                            bool isForced, isPreferred;
                            if (SimpleSidearmsCompat.IsWeaponTypeForced(pawn, weapon.def, out isForced, out isPreferred))
                                return true;
                        }
                    }
                }

                return false;
            }

            // SimpleSidearms NOT loaded - use AutoArm's tracking
            return forcedPrimaryWeapon.ContainsKey(pawn) ||
                   (forcedWeaponDefs.ContainsKey(pawn) && forcedWeaponDefs[pawn].Count > 0);
        }

        /// <summary>
        /// Get the forced primary weapon
        /// When SimpleSidearms is loaded, returns primary if it's forced in SS
        /// </summary>
        public static ThingWithComps GetForcedPrimary(Pawn pawn)
        {
            if (pawn == null)
                return null;

            // When SimpleSidearms is loaded, check if current primary is forced in SS
            if (SimpleSidearmsCompat.IsLoaded())
            {
                var primary = pawn.equipment?.Primary;
                if (primary != null)
                {
                    bool isForced, isPreferred;
                    if (SimpleSidearmsCompat.IsWeaponTypeForced(pawn, primary.def, out isForced, out isPreferred))
                        return primary;
                }
                return null;
            }

            // SimpleSidearms NOT loaded - use AutoArm's tracking
            forcedPrimaryWeapon.TryGetValue(pawn, out var weapon);
            return weapon;
        }

        /// <summary>
        /// Get all forced weapon defs for a pawn
        /// </summary>
        public static HashSet<ThingDef> GetForcedWeaponDefs(Pawn pawn)
        {
            if (pawn == null)
                return new HashSet<ThingDef>();

            return forcedWeaponDefs.ContainsKey(pawn) ?
                new HashSet<ThingDef>(forcedWeaponDefs[pawn]) :
                new HashSet<ThingDef>();
        }

        /// <summary>
        /// Force a specific sidearm instance
        /// </summary>
        public static void AddForcedSidearm(Pawn pawn, ThingWithComps weapon)
        {
            if (pawn == null || weapon == null)
                return;

            // Don't track non-weapons as forced
            if (!WeaponValidation.IsProperWeapon(weapon))
            {
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.Debug($"[{pawn.Name?.ToStringShort ?? "Unknown"}] Ignoring AddForcedSidearm for non-weapon: {weapon.Label}");
                }
                return;
            }

            // Track the specific weapon ID
            if (!forcedWeaponIds.ContainsKey(pawn))
                forcedWeaponIds[pawn] = new HashSet<int>();
            forcedWeaponIds[pawn].Add(weapon.thingIDNumber);

            // Also track def for compatibility
            if (!forcedWeaponDefs.ContainsKey(pawn))
                forcedWeaponDefs[pawn] = new HashSet<ThingDef>();
            forcedWeaponDefs[pawn].Add(weapon.def);

            if (AutoArmMod.settings?.debugLogging == true)
            {
                AutoArmLogger.Debug($"[{pawn.Name?.ToStringShort ?? "Unknown"}] Added forced sidearm: {weapon.Label} (ID: {weapon.thingIDNumber})");
            }
        }

        /// <summary>
        /// Remove a specific forced weapon instance
        /// </summary>
        public static void RemoveForcedWeapon(Pawn pawn, ThingWithComps weapon)
        {
            if (pawn == null || weapon == null)
                return;

            // Remove from weapon ID tracking
            if (forcedWeaponIds.ContainsKey(pawn))
            {
                forcedWeaponIds[pawn].Remove(weapon.thingIDNumber);
                if (forcedWeaponIds[pawn].Count == 0)
                    forcedWeaponIds.Remove(pawn);
            }

            // Remove from primary if it matches
            if (forcedPrimaryWeapon.TryGetValue(pawn, out var primary) && primary == weapon)
            {
                forcedPrimaryWeapon.Remove(pawn);
            }

            // Check if we should also remove the def
            if (forcedWeaponDefs.ContainsKey(pawn))
            {
                // Only remove the def if no other forced weapons of this type exist
                bool hasOtherForcedOfSameType = false;
                if (forcedWeaponIds.ContainsKey(pawn))
                {
                    // Check all forced weapon IDs to see if any match this def
                    foreach (var id in forcedWeaponIds[pawn])
                    {
                        // Try to find the weapon by ID
                        var otherWeapon = pawn.equipment?.Primary;
                        if (otherWeapon?.thingIDNumber == id && otherWeapon.def == weapon.def)
                        {
                            hasOtherForcedOfSameType = true;
                            break;
                        }

                        // Check inventory
                        if (!hasOtherForcedOfSameType && pawn.inventory?.innerContainer != null)
                        {
                            foreach (var item in pawn.inventory.innerContainer)
                            {
                                if (item is ThingWithComps invWeapon && invWeapon.thingIDNumber == id && invWeapon.def == weapon.def)
                                {
                                    hasOtherForcedOfSameType = true;
                                    break;
                                }
                            }
                        }
                    }
                }

                if (!hasOtherForcedOfSameType)
                {
                    forcedWeaponDefs[pawn].Remove(weapon.def);
                    if (forcedWeaponDefs[pawn].Count == 0)
                        forcedWeaponDefs.Remove(pawn);
                }
            }

            if (AutoArmMod.settings?.debugLogging == true)
            {
                AutoArmLogger.Debug($"[{pawn.Name?.ToStringShort ?? "Unknown"}] Removed forced weapon: {weapon.Label} (ID: {weapon.thingIDNumber})");
            }
        }

        /// <summary>
        /// Cleanup dead/destroyed pawns and invalid weapon references
        /// </summary>
        public static int Cleanup()
        {
            int removed = 0;

            // Cleanup weapon IDs
            var invalidIdPawns = forcedWeaponIds.Keys
                .Where(p => p == null || p.Destroyed || p.Dead)
                .ToList();

            foreach (var pawn in invalidIdPawns)
            {
                forcedWeaponIds.Remove(pawn);
                removed++;
            }

            // Cleanup weapon defs
            var invalidPawns = forcedWeaponDefs.Keys
                .Where(p => p == null || p.Destroyed || p.Dead)
                .ToList();

            foreach (var pawn in invalidPawns)
            {
                forcedWeaponDefs.Remove(pawn);
                forcedPrimaryWeapon.Remove(pawn);
                removed++;
            }

            // Also cleanup invalid weapon references
            var invalidWeaponPawns = forcedPrimaryWeapon
                .Where(kvp => kvp.Value == null || kvp.Value.Destroyed)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var pawn in invalidWeaponPawns)
            {
                forcedPrimaryWeapon.Remove(pawn);
                removed++;
            }

            // NEW: Also check for phantom forced weapons (unarmed pawns with forced weapons)
            var phantomForcedPawns = forcedPrimaryWeapon
                .Where(kvp => kvp.Key != null && !kvp.Key.Dead && kvp.Key.Spawned &&
                              kvp.Key.equipment?.Primary != kvp.Value)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var pawn in phantomForcedPawns)
            {
                // Clear phantom forced weapons immediately
                if (pawn.equipment?.Primary == null)
                {
                    forcedPrimaryWeapon.Remove(pawn);
                    if (AutoArmMod.settings?.debugLogging == true)
                    {
                        AutoArmLogger.Debug($"[{pawn.Name?.ToStringShort ?? "Unknown"}] Cleared phantom forced weapon (pawn is unarmed)");
                    }
                    removed++;
                }
            }

            return removed;
        }

        /// <summary>
        /// Transfer forced status between primary and sidearm
        /// </summary>
        public static void TransferForcedStatus(Pawn pawn, ThingWithComps fromWeapon, ThingWithComps toWeapon)
        {
            if (pawn == null || fromWeapon == null || toWeapon == null)
                return;

            if (IsForced(pawn, fromWeapon))
            {
                SetForced(pawn, toWeapon);
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.Debug($"[{pawn.Name?.ToStringShort ?? "Unknown"}] Transferred forced status from {fromWeapon.Label} to {toWeapon.Label}");
                }
            }
        }

        // Save/Load support methods
        public static Dictionary<Pawn, ThingDef> GetSaveData()
        {
            var result = new Dictionary<Pawn, ThingDef>();

            // Legacy save format - only used for backwards compatibility
            foreach (var kvp in forcedPrimaryWeapon)
            {
                if (kvp.Key != null && kvp.Value != null)
                {
                    result[kvp.Key] = kvp.Value.def;
                }
            }

            return result;
        }

        /// <summary>
        /// Get forced weapon IDs for saving
        /// </summary>
        public static Dictionary<Pawn, List<int>> GetForcedWeaponIds()
        {
            var result = new Dictionary<Pawn, List<int>>();

            foreach (var kvp in forcedWeaponIds)
            {
                if (kvp.Key != null && kvp.Value != null && kvp.Value.Count > 0)
                {
                    result[kvp.Key] = kvp.Value.ToList();
                }
            }

            return result;
        }

        public static Dictionary<Pawn, List<ThingDef>> GetSidearmSaveData()
        {
            var result = new Dictionary<Pawn, List<ThingDef>>();

            // Save all forced weapon defs for each pawn
            foreach (var kvp in forcedWeaponDefs)
            {
                if (kvp.Key != null && kvp.Value != null && kvp.Value.Count > 0)
                {
                    result[kvp.Key] = kvp.Value.ToList();
                }
            }

            return result;
        }

        public static void LoadSaveData(Dictionary<Pawn, ThingDef> data)
        {
            if (data == null)
                return;

            forcedPrimaryWeapon.Clear();

            // Legacy loading - only for old saves
            foreach (var kvp in data)
            {
                if (kvp.Key != null && kvp.Value != null)
                {
                    // Add to forced defs for backwards compatibility
                    if (!forcedWeaponDefs.ContainsKey(kvp.Key))
                        forcedWeaponDefs[kvp.Key] = new HashSet<ThingDef>();
                    forcedWeaponDefs[kvp.Key].Add(kvp.Value);
                }
            }
        }

        /// <summary>
        /// Load forced weapon IDs from save
        /// </summary>
        public static void LoadForcedWeaponIds(Dictionary<Pawn, List<int>> data)
        {
            if (data == null)
                return;

            forcedWeaponIds.Clear();

            foreach (var kvp in data)
            {
                if (kvp.Key != null && kvp.Value != null && kvp.Value.Count > 0)
                {
                    forcedWeaponIds[kvp.Key] = new HashSet<int>(kvp.Value);
                }
            }
        }

        public static void LoadSidearmSaveData(Dictionary<Pawn, HashSet<ThingDef>> data)
        {
            if (data == null)
                return;

            forcedWeaponDefs.Clear();

            foreach (var kvp in data)
            {
                if (kvp.Key != null && kvp.Value != null && kvp.Value.Count > 0)
                {
                    forcedWeaponDefs[kvp.Key] = new HashSet<ThingDef>(kvp.Value);
                }
            }
        }

        // ========== Test-specific method overloads ==========

        /// <summary>
        /// Test method for checking forced weapon defs - for internal tests only
        /// </summary>
        internal static bool HasForcedWeaponDef(Pawn pawn, ThingDef weaponDef)
        {
            if (pawn == null || weaponDef == null)
                return false;

            return forcedWeaponDefs.ContainsKey(pawn) && forcedWeaponDefs[pawn].Contains(weaponDef);
        }
    }
}