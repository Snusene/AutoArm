// AutoArm RimWorld 1.5+ mod - automatic weapon management
// This file: Auto-equip job tracking and think node conditional
// Handles: Tracking auto-equipped jobs, cleanup, and think tree conditions
// Uses: TimingHelper for cleanup intervals
// Critical: Maintains AutoEquipTracker to prevent re-pickup loops
// Note: Main equipping logic is in JobGiver_PickUpBetterWeapon.cs

using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;
using AutoArm.Helpers;
using AutoArm.Caching;
using AutoArm.Jobs;
using AutoArm.Logging;
using AutoArm.Weapons;

namespace AutoArm.Weapons
{

    public static class AutoEquipTracker
    {
        private static HashSet<int> autoEquipJobIds = new HashSet<int>();
        private static Dictionary<Pawn, ThingDef> previousWeapons = new Dictionary<Pawn, ThingDef>();

        private static Dictionary<int, int> jobAddedTick = new Dictionary<int, int>();
        private const int JobRetentionTicks = 2500; // ~42 seconds - enough time to walk across most maps

        public static void MarkAsAutoEquip(Job job, Pawn pawn = null)
        {
            MarkAutoEquip(job, pawn);
        }

        public static void MarkAutoEquip(Job job, Pawn pawn = null)
        {
            if (job != null)
            {
                autoEquipJobIds.Add(job.loadID);

                jobAddedTick[job.loadID] = Find.TickManager.TicksGame;

                if (pawn != null && job.def == JobDefOf.Equip && job.targetA.Thing is ThingWithComps weapon)
                {
                    if (pawn.equipment?.Primary != null)
                    {
                        previousWeapons[pawn] = pawn.equipment.Primary.def;
                    }
                    else
                    {
                        previousWeapons.Remove(pawn);
                    }
                }
            }
        }

        public static bool IsAutoEquip(Job job)
        {
            return job != null && autoEquipJobIds.Contains(job.loadID);
        }

        public static void Clear(Job job)
        {
            if (job != null)
            {
                autoEquipJobIds.Remove(job.loadID);
                jobAddedTick.Remove(job.loadID);
            }
        }

        public static void SetPreviousWeapon(Pawn pawn, ThingDef weaponDef)
        {
            if (pawn != null && weaponDef != null)
            {
                previousWeapons[pawn] = weaponDef;
            }
        }

        public static ThingDef GetPreviousWeapon(Pawn pawn)
        {
            previousWeapons.TryGetValue(pawn, out var weapon);
            return weapon;
        }

        public static void ClearPreviousWeapon(Pawn pawn)
        {
            previousWeapons.Remove(pawn);
        }

        public static void CleanupOldJobs()
        {
            if (jobAddedTick.Count == 0)
                return;

            int currentTick = Find.TickManager.TicksGame;
            var toRemove = new List<int>();

            foreach (var kvp in jobAddedTick)
            {
                if (currentTick - kvp.Value > JobRetentionTicks)
                {
                    toRemove.Add(kvp.Key);
                }
            }

            foreach (int jobId in toRemove)
            {
                autoEquipJobIds.Remove(jobId);
                jobAddedTick.Remove(jobId);
            }

            var deadPawns = previousWeapons.Keys.Where(p => p.DestroyedOrNull() || p.Dead).ToList();
            foreach (var pawn in deadPawns)
            {
                previousWeapons.Remove(pawn);
            }

            if ((toRemove.Count > 0 || deadPawns.Count > 0) && AutoArmMod.settings?.debugLogging == true)
            {
                AutoArmLogger.Debug($"Cleaned up {toRemove.Count} old job IDs and {deadPawns.Count} dead pawn records");
            }
        }
    }

    public class ThinkNode_ConditionalWeaponsInOutfit : ThinkNode_Conditional
    {
        protected override bool Satisfied(Pawn pawn)
        {
            try
            {
                if (!ValidationHelper.SafeIsColonist(pawn))
                {
                    if (AutoArmMod.settings?.debugLogging == true)
                    {
                        AutoArmLogger.LogPawn(pawn, "Not colonist");
                    }
                    return false;
                }

                // Safe check for violence capability
                try
                {
                    if (pawn.WorkTagIsDisabled(WorkTags.Violent))
                    {
                        return false;
                    }
                }
                catch
                {
                    // If we can't check work tags, continue
                    if (AutoArmMod.settings?.debugLogging == true)
                    {
                        AutoArmLogger.Debug($"Could not check work tags for {pawn?.Name?.ToStringShort ?? "unknown"} - continuing");
                    }
                }

                if (pawn.Drafted)
                {
                    if (AutoArmMod.settings?.debugLogging == true)
                    {
                        AutoArmLogger.LogPawn(pawn, "Drafted");
                    }
                    return false;
                }

                // Always return true - let the JobGiver run and check actual weapons
                // The outfit filter will be checked per-weapon in ValidationHelper.IsValidWeapon
                // This avoids the issue where quality filters cause false negatives
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"[AutoArm] Error in ThinkNode_ConditionalWeaponsInOutfit.Satisfied for {pawn?.Name?.ToStringShort ?? "unknown"}: {ex.Message}");
                if (ex.InnerException != null)
                    Log.Error($"[AutoArm] Inner: {ex.InnerException.Message}");
                return false; // Safe default - don't check for weapons if we can't evaluate
            }
        }

        public bool TestSatisfied(Pawn pawn)
        {
            return Satisfied(pawn);
        }

        public override float GetPriority(Pawn pawn)
        {
            try
            {
                // Priority 5.4 - check for upgrades before starting work (5.5)
                // This runs after critical needs but before assigned work
                return Satisfied(pawn) ? 5.4f : 0f;
            }
            catch (Exception ex)
            {
                Log.Error($"[AutoArm] Error in ThinkNode_ConditionalWeaponsInOutfit.GetPriority: {ex.Message}");
                return 0f;
            }
        }
    }
}