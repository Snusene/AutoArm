using System.Collections.Generic;
using System.Linq;
using Verse;

namespace AutoArm
{
    /// <summary>
    /// Consolidated forced weapon tracking (fixes #12)
    /// </summary>
    public static class ForcedWeaponHelper
    {
        // Single source of truth for forced weapons
        private static Dictionary<Pawn, HashSet<ThingDef>> forcedWeaponDefs = new Dictionary<Pawn, HashSet<ThingDef>>();

        private static Dictionary<Pawn, ThingWithComps> forcedPrimaryWeapon = new Dictionary<Pawn, ThingWithComps>();

        /// <summary>
        /// Mark a weapon as forced for a pawn
        /// </summary>
        public static void SetForced(Pawn pawn, ThingWithComps weapon)
        {
            if (pawn == null || weapon == null)
                return;

            // Only update the primary weapon reference, don't clear all forced defs
            forcedPrimaryWeapon[pawn] = weapon;

            // Add to forced defs
            if (!forcedWeaponDefs.ContainsKey(pawn))
                forcedWeaponDefs[pawn] = new HashSet<ThingDef>();
            forcedWeaponDefs[pawn].Add(weapon.def);

            AutoArmDebug.LogWeapon(pawn, weapon, "Marked as forced weapon");
        }

        /// <summary>
        /// Clear only the forced primary weapon reference (not all forced defs)
        /// </summary>
        public static void ClearForcedPrimary(Pawn pawn)
        {
            if (pawn == null)
                return;

            // Get the weapon def before removing
            ThingDef weaponDefToCheck = null;
            if (forcedPrimaryWeapon.TryGetValue(pawn, out var forcedWeapon) && forcedWeapon != null)
            {
                weaponDefToCheck = forcedWeapon.def;
            }

            forcedPrimaryWeapon.Remove(pawn);

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
                    AutoArmDebug.LogPawn(pawn, $"Removed forced weapon def {weaponDefToCheck.defName} - pawn no longer has any weapons of this type");
                }
                else
                {
                    AutoArmDebug.LogPawn(pawn, $"Kept forced weapon def {weaponDefToCheck.defName} - pawn still has weapons of this type");
                }
            }
        }

        /// <summary>
        /// Clear all forced weapons for a pawn
        /// </summary>
        public static void ClearForced(Pawn pawn)
        {
            if (pawn == null)
                return;

            if (forcedWeaponDefs.ContainsKey(pawn))
            {
                AutoArmDebug.LogPawn(pawn, $"Clearing {forcedWeaponDefs[pawn].Count} forced weapon type(s)");
                forcedWeaponDefs.Remove(pawn);
            }

            forcedPrimaryWeapon.Remove(pawn);
        }

        /// <summary>
        /// Check if a specific weapon is forced
        /// </summary>
        public static bool IsForced(Pawn pawn, ThingWithComps weapon)
        {
            if (pawn == null || weapon == null)
                return false;

            // Don't consider weapons forced during sidearm upgrades
            if (SimpleSidearmsCompat.PawnHasTemporarySidearmEquipped(pawn))
                return false;

            // Check if the specific weapon instance is forced
            if (forcedPrimaryWeapon.TryGetValue(pawn, out var forced) && forced == weapon)
                return true;

            // Check if the weapon def is forced
            return IsWeaponDefForced(pawn, weapon.def);
        }

        /// <summary>
        /// Check if a weapon def is forced
        /// </summary>
        public static bool IsWeaponDefForced(Pawn pawn, ThingDef weaponDef)
        {
            if (pawn == null || weaponDef == null)
                return false;

            return forcedWeaponDefs.ContainsKey(pawn) && forcedWeaponDefs[pawn].Contains(weaponDef);
        }

        /// <summary>
        /// Check if pawn has any forced weapon
        /// </summary>
        public static bool HasForcedWeapon(Pawn pawn)
        {
            if (pawn == null)
                return false;

            return forcedPrimaryWeapon.ContainsKey(pawn) ||
                   (forcedWeaponDefs.ContainsKey(pawn) && forcedWeaponDefs[pawn].Count > 0);
        }

        /// <summary>
        /// Get the forced primary weapon
        /// </summary>
        public static ThingWithComps GetForcedPrimary(Pawn pawn)
        {
            if (pawn == null)
                return null;

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
        /// Add a forced sidearm def
        /// </summary>
        public static void AddForcedDef(Pawn pawn, ThingDef weaponDef)
        {
            if (pawn == null || weaponDef == null)
                return;

            if (!forcedWeaponDefs.ContainsKey(pawn))
                forcedWeaponDefs[pawn] = new HashSet<ThingDef>();

            forcedWeaponDefs[pawn].Add(weaponDef);

            AutoArmDebug.LogPawn(pawn, $"Added forced weapon def: {weaponDef.defName}");
        }

        /// <summary>
        /// Remove a specific forced def
        /// </summary>
        public static void RemoveForcedDef(Pawn pawn, ThingDef weaponDef)
        {
            if (pawn == null || weaponDef == null)
                return;

            if (forcedWeaponDefs.ContainsKey(pawn))
            {
                forcedWeaponDefs[pawn].Remove(weaponDef);
                if (forcedWeaponDefs[pawn].Count == 0)
                    forcedWeaponDefs.Remove(pawn);

                AutoArmDebug.LogPawn(pawn, $"Removed forced weapon def: {weaponDef.defName}");
            }
        }

        /// <summary>
        /// Cleanup dead/destroyed pawns
        /// </summary>
        public static void Cleanup()
        {
            var invalidPawns = forcedWeaponDefs.Keys
                .Where(p => p == null || p.Destroyed || p.Dead)
                .ToList();

            foreach (var pawn in invalidPawns)
            {
                forcedWeaponDefs.Remove(pawn);
                forcedPrimaryWeapon.Remove(pawn);
            }

            // Also cleanup invalid weapon references
            var invalidWeaponPawns = forcedPrimaryWeapon
                .Where(kvp => kvp.Value == null || kvp.Value.Destroyed)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var pawn in invalidWeaponPawns)
            {
                forcedPrimaryWeapon.Remove(pawn);
            }

            // NEW: Also check for phantom forced weapons (unarmed pawns with forced weapons)
            var phantomForcedPawns = forcedPrimaryWeapon
                .Where(kvp => kvp.Key != null && !kvp.Key.Dead && kvp.Key.Spawned &&
                              kvp.Key.equipment?.Primary != kvp.Value)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var pawn in phantomForcedPawns)
            {
                // Only clear if they've been unarmed for a while (300 ticks = 5 seconds)
                if (pawn.equipment?.Primary == null &&
                    !TimingHelper.IsOnCooldown(pawn, TimingHelper.CooldownType.DroppedWeapon))
                {
                    forcedPrimaryWeapon.Remove(pawn);
                    AutoArmDebug.LogPawn(pawn, "Cleared phantom forced weapon (pawn is unarmed)");
                }
            }
        }

        /// <summary>
        /// Transfer forced status between primary and sidearm
        /// </summary>
        public static void TransferForcedStatus(Pawn pawn, ThingWithComps fromWeapon, ThingWithComps toWeapon)
        {
            if (pawn == null || fromWeapon == null || toWeapon == null)
                return;

            if (IsForced(pawn, fromWeapon) || IsWeaponDefForced(pawn, fromWeapon.def))
            {
                SetForced(pawn, toWeapon);
                AutoArmDebug.LogPawn(pawn, $"Transferred forced status from {fromWeapon.Label} to {toWeapon.Label}");
            }
        }

        // Save/Load support methods
        public static Dictionary<Pawn, ThingDef> GetSaveData()
        {
            var result = new Dictionary<Pawn, ThingDef>();

            // Save the primary forced weapon def for each pawn
            // NOTE: This saves weapon TYPE, not specific instances. If a pawn has multiple
            // weapons of the same type (e.g., two assault rifles of different quality),
            // the forced status will apply to the TYPE after save/load, not the specific weapon.
            // This is a known limitation to avoid complex instance tracking across saves.
            foreach (var kvp in forcedPrimaryWeapon)
            {
                if (kvp.Key != null && kvp.Value != null)
                {
                    result[kvp.Key] = kvp.Value.def;
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

            // Note: We can't restore the actual weapon references, only the defs
            // The primary weapon tracking will be re-established when pawns equip weapons
            // LIMITATION: If a pawn had a specific forced weapon (e.g., excellent assault rifle)
            // and also has other weapons of the same type (e.g., normal assault rifle),
            // the forced status will apply to ANY weapon of that type after loading.
            foreach (var kvp in data)
            {
                if (kvp.Key != null && kvp.Value != null)
                {
                    // Add to forced defs to remember the weapon type was forced
                    if (!forcedWeaponDefs.ContainsKey(kvp.Key))
                        forcedWeaponDefs[kvp.Key] = new HashSet<ThingDef>();
                    forcedWeaponDefs[kvp.Key].Add(kvp.Value);
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
    }
}