// AutoArm RimWorld 1.5+ mod - automatic weapon management
// This file: Tracks jobs created by AutoArm to distinguish from player actions
// Critical: Prevents auto-equipped weapons from being marked as forced

using AutoArm.Logging;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace AutoArm.Jobs
{
    /// <summary>
    /// Tracks which equip jobs were created by AutoArm
    /// This allows us to distinguish between player-forced equips and auto-equips
    /// </summary>
    public static class AutoEquipTracker
    {
        // Track jobs by their unique ID
        private static HashSet<int> autoEquipJobIds = new HashSet<int>();

        // Track previous weapon for upgrade messages
        private static Dictionary<Pawn, ThingDef> previousWeaponDefs = new Dictionary<Pawn, ThingDef>();

        // Track weapons that should be forced when equipped (for forced weapon upgrades)
        private static Dictionary<Pawn, ThingWithComps> weaponsToForce = new Dictionary<Pawn, ThingWithComps>();

        // Track weapons that can't be moved to inventory (SimpleSidearms limits)
        // These need to be marked as dropped after the equip completes
        private static Dictionary<Pawn, ThingWithComps> weaponsCannotMoveToInventory = new Dictionary<Pawn, ThingWithComps>();

        /// <summary>
        /// Mark a job as created by AutoArm
        /// </summary>
        public static void MarkAsAutoEquip(Job job, Pawn pawn)
        {
            if (job == null || pawn == null)
                return;

            autoEquipJobIds.Add(job.loadID);

            if (AutoArmMod.settings?.debugLogging == true)
            {
                AutoArmLogger.Debug($"[{pawn.LabelShort}] Marked job {job.loadID} as auto-equip");
            }
        }

        /// <summary>
        /// Check if a job was created by AutoArm
        /// </summary>
        public static bool IsAutoEquip(Job job)
        {
            if (job == null)
                return false;

            return autoEquipJobIds.Contains(job.loadID);
        }

        /// <summary>
        /// Clear tracking for a completed job
        /// </summary>
        public static void Clear(Job job)
        {
            if (job == null)
                return;

            autoEquipJobIds.Remove(job.loadID);
        }

        /// <summary>
        /// Set the previous weapon for upgrade messages
        /// </summary>
        public static void SetPreviousWeapon(Pawn pawn, ThingDef weaponDef)
        {
            if (pawn == null)
            {
                return;
            }

            if (weaponDef != null)
            {
                previousWeaponDefs[pawn] = weaponDef;
            }
            else
            {
                previousWeaponDefs.Remove(pawn);
            }
        }

        /// <summary>
        /// Get the previous weapon def for upgrade messages
        /// </summary>
        public static ThingDef GetPreviousWeapon(Pawn pawn)
        {
            if (pawn == null)
                return null;

            previousWeaponDefs.TryGetValue(pawn, out ThingDef def);
            return def;
        }

        /// <summary>
        /// Clear previous weapon tracking
        /// </summary>
        public static void ClearPreviousWeapon(Pawn pawn)
        {
            if (pawn == null)
                return;

            previousWeaponDefs.Remove(pawn);
        }

        /// <summary>
        /// Mark a weapon to be forced when equipped (for forced weapon upgrades)
        /// </summary>
        public static void SetWeaponToForce(Pawn pawn, ThingWithComps weapon)
        {
            if (pawn == null || weapon == null)
                return;

            weaponsToForce[pawn] = weapon;
        }

        /// <summary>
        /// Check if a weapon should be forced when equipped
        /// </summary>
        public static bool ShouldForceWeapon(Pawn pawn, ThingWithComps weapon)
        {
            if (pawn == null || weapon == null)
                return false;

            return weaponsToForce.TryGetValue(pawn, out var weaponToForce) && weaponToForce == weapon;
        }

        /// <summary>
        /// Clear weapon to force tracking
        /// </summary>
        public static void ClearWeaponToForce(Pawn pawn)
        {
            if (pawn == null)
                return;

            weaponsToForce.Remove(pawn);
        }

        /// <summary>
        /// Mark a weapon that can't be moved to inventory (for SimpleSidearms limit handling)
        /// </summary>
        public static void SetWeaponCannotMoveToInventory(Pawn pawn, ThingWithComps weapon)
        {
            if (pawn == null || weapon == null)
                return;

            weaponsCannotMoveToInventory[pawn] = weapon;
        }

        /// <summary>
        /// Get the weapon that can't be moved to inventory
        /// </summary>
        public static ThingWithComps GetWeaponCannotMoveToInventory(Pawn pawn)
        {
            if (pawn == null)
                return null;

            weaponsCannotMoveToInventory.TryGetValue(pawn, out var weapon);
            return weapon;
        }

        /// <summary>
        /// Clear weapon that can't be moved to inventory tracking
        /// </summary>
        public static void ClearWeaponCannotMoveToInventory(Pawn pawn)
        {
            if (pawn == null)
                return;

            weaponsCannotMoveToInventory.Remove(pawn);
        }

        /// <summary>
        /// Cleanup old job IDs and dead pawns
        /// </summary>
        public static void Cleanup()
        {
            // Jobs are short-lived, so we can clear old IDs periodically
            if (autoEquipJobIds.Count > 100)
            {
                autoEquipJobIds.Clear();
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.Debug("Cleared auto-equip job tracking (exceeded 100 entries)");
                }
            }

            // Clean up dead pawns
            var deadPawns = previousWeaponDefs.Keys.Where(p => p.Dead || p.Destroyed).ToList();
            foreach (var pawn in deadPawns)
            {
                previousWeaponDefs.Remove(pawn);
            }

            // Clean up weapons to force
            var deadPawnsForForce = weaponsToForce.Keys.Where(p => p.Dead || p.Destroyed).ToList();
            foreach (var pawn in deadPawnsForForce)
            {
                weaponsToForce.Remove(pawn);
            }

            // Clean up weapons that can't move to inventory
            var deadPawnsForInventory = weaponsCannotMoveToInventory.Keys.Where(p => p.Dead || p.Destroyed).ToList();
            foreach (var pawn in deadPawnsForInventory)
            {
                weaponsCannotMoveToInventory.Remove(pawn);
            }
        }
    }
}