// AutoArm RimWorld 1.5+ mod - automatic weapon management
// This file: Job creation logic
// Handles: Creating equip jobs for weapons and sidearms
// Note: Interrupt logic removed - think tree priorities handle when colonists look for weapons

using AutoArm.Logging;
using RimWorld;
using System.Linq;
using Verse;
using Verse.AI;

namespace AutoArm.Jobs
{
    /// <summary>
    /// Centralized job creation
    /// </summary>
    public static class JobHelper
    {
        // Cached EquipSecondary job def for SimpleSidearms
        private static readonly JobDef equipSecondaryJobDef = SimpleSidearmsCompat.IsLoaded() ? 
            DefDatabase<JobDef>.GetNamedSilentFail("EquipSecondary") : null;

        /// <summary>
        /// Create an equip job for a weapon - uses smart swap when replacing existing weapon
        /// </summary>
        public static Job CreateEquipJob(ThingWithComps weapon, bool isSidearm = false, Pawn pawn = null)
        {
            if (weapon == null)
            {
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.Debug("CreateEquipJob called with null weapon");
                }
                return null;
            }

            // SMART SWAP LOGIC: If SimpleSidearms is loaded and pawn has a weapon to replace,
            // use our custom swap job to avoid drop space issues
            if (SimpleSidearmsCompat.IsLoaded() && !SimpleSidearmsCompat.ReflectionFailed && pawn != null)
            {
                var currentPrimary = pawn.equipment?.Primary;
                
                // Check if this is an upgrade of the primary weapon
                if (currentPrimary != null && currentPrimary.def == weapon.def)
                {
                    if (AutoArmMod.settings?.debugLogging == true)
                    {
                        AutoArmLogger.Debug($"Creating primary swap job for upgrade: {currentPrimary.Label} -> {weapon.Label}");
                    }
                    return SimpleSidearmsCompat.CreateSwapPrimaryJob(pawn, weapon, currentPrimary);
                }
                
                // Check if this is replacing a sidearm in inventory
                if (pawn.inventory?.innerContainer != null)
                {
                    var existingSidearm = pawn.inventory.innerContainer
                        .OfType<ThingWithComps>()
                        .FirstOrDefault(w => w.def == weapon.def && w.def.IsWeapon);
                    
                    if (existingSidearm != null)
                    {
                        if (AutoArmMod.settings?.debugLogging == true)
                        {
                            AutoArmLogger.Debug($"Creating sidearm swap job for upgrade: {existingSidearm.Label} -> {weapon.Label}");
                        }
                        return SimpleSidearmsCompat.CreateSwapSidearmJob(pawn, weapon, existingSidearm);
                    }
                }
            }

            // Use SimpleSidearms EquipSecondary job if available and this is a sidearm
            if (isSidearm && equipSecondaryJobDef != null)
            {
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.Debug($"Creating EquipSecondary job for {weapon.Label} (new sidearm)");
                }
                return JobMaker.MakeJob(equipSecondaryJobDef, weapon);
            }

            // Default to vanilla equip job (for new weapons or when SS not loaded)
            if (AutoArmMod.settings?.debugLogging == true)
            {
                AutoArmLogger.Debug($"Creating vanilla Equip job for {weapon.Label}{(isSidearm ? " (marked as sidearm)" : "")}");
            }
            var job = JobMaker.MakeJob(JobDefOf.Equip, weapon);
            job.count = 1;
            return job;
        }

        // Note: IsCriticalJob and IsLowPriorityWork methods removed
        // The think tree system with priorities 5.6 (armed) and 6.9 (unarmed) 
        // naturally ensures colonists only look for weapons between jobs,
        // not during work or critical tasks.
    }
}