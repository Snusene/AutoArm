// AutoArm RimWorld 1.5+ mod - automatic weapon management
// This file: Fallback tick processing when ThinkTree injection fails
// Provides weapon management through TickRare when think tree integration is unavailable

using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace AutoArm.Patches
{
    using AutoArm.Helpers;
    using AutoArm.Jobs;
    using AutoArm.Weapons;
    using AutoArm.Caching;
    using AutoArm.Logging;
using AutoArm.Patches;
    [HarmonyPatch(typeof(Pawn), "TickRare")]
    public static class Pawn_TickRare_Unified_Patch
    {
        internal static Dictionary<Pawn, int> lastInterruptionTick = new Dictionary<Pawn, int>();
        internal static Dictionary<Pawn, int> lastSidearmCheckTick = new Dictionary<Pawn, int>();
        internal static Dictionary<Pawn, int> lastWeaponCheckTick = new Dictionary<Pawn, int>();
        internal static Dictionary<Pawn, int> lastWeaponSearchTick = new Dictionary<Pawn, int>();
        internal static Dictionary<Pawn, Job> cachedWeaponJobs = new Dictionary<Pawn, Job>();
        internal static HashSet<Pawn> recentlyUnarmedPawns = new HashSet<Pawn>();

        private static int checksThisTick = 0;
        private static int lastTickProcessed = -1;

        private static int GetMaxChecksPerTick(Map map)
        {
            if (map == null) return 3;
            int colonistCount = map.mapPawns.FreeColonistsCount;

            if (colonistCount <= 10) return 3;
            if (colonistCount <= 20) return 5;
            return 7;
        }

        private static int lastCleanupTick = 0;
        private const int CLEANUP_INTERVAL = 6000; // Every ~100 seconds

        [HarmonyPostfix]
        public static void Postfix(Pawn __instance)
        {
            if (__instance == null)
                return;

            if (AutoArmMod.settings?.modEnabled != true)
                return;

            // Periodic cleanup of dead pawns
            if (Find.TickManager.TicksGame - lastCleanupTick > CLEANUP_INTERVAL)
            {
                CleanupDeadPawns();
                lastCleanupTick = Find.TickManager.TicksGame;
            }

            if (!__instance.Spawned || __instance.Map == null || __instance.Dead ||
                __instance.Destroyed || !__instance.IsColonist || __instance.Drafted)
            {
                // Clean up dead/destroyed/unspawned pawns from dictionaries
                if (__instance.Dead || __instance.Destroyed || __instance.IsWorldPawn() ||
                    (!__instance.Spawned && !__instance.IsCaravanMember()))
                {
                    CleanupPawnFromDictionaries(__instance);
                }
                return;
            }

            if (!AutoArmMod.settings.thinkTreeInjectionFailed)
            {
                if (__instance.IsHashIntervalTick(500))
                {
                    CheckOutfitPolicyOnly(__instance);
                }

                // Additional periodic cleanup for sidearms as a safety net
                if (__instance.IsHashIntervalTick(2500)) // Every ~40 seconds
                {
                    if (SimpleSidearmsCompat.IsLoaded())
                    {
                        Pawn_OutfitTracker_CurrentApparelPolicy_Setter_Patch.CheckAndDropDisallowedSidearms(__instance);
                    }
                }

                return;
            }

            if (Find.TickManager.TicksGame != lastTickProcessed)
            {
                checksThisTick = 0;
                lastTickProcessed = Find.TickManager.TicksGame;
            }

            int maxChecks = GetMaxChecksPerTick(__instance.Map);
            if (checksThisTick >= maxChecks)
                return;

            if (__instance.Downed || __instance.InBed() ||
                __instance.WorkTagIsDisabled(WorkTags.Violent) ||
                __instance.InContainerEnclosed ||
                __instance.IsCaravanMember() || __instance.IsWorldPawn())
                return;

            var primary = __instance.equipment?.Primary;
            bool hasWeapon = primary != null && primary.def?.IsWeapon == true;

            int colonistCount = __instance.Map?.mapPawns?.FreeColonistsCount ?? 1;

            if (!hasWeapon)
            {
                int baseInterval = 15;
                baseInterval += Math.Min(colonistCount, 30);
                int variance = __instance.thingIDNumber % Math.Max(5, colonistCount);

                if (__instance.IsHashIntervalTick(baseInterval + variance))
                {
                    checksThisTick++;
                    CheckMainWeaponUpgrade(__instance);
                }
            }
            else
            {
                int baseInterval = 60;
                baseInterval += Math.Min(colonistCount * 5, 200);
                int variance = __instance.thingIDNumber % Math.Max(20, colonistCount * 2);

                if (__instance.IsHashIntervalTick(baseInterval + variance))
                {
                    checksThisTick++;
                    CheckMainWeaponUpgrade(__instance);
                }
            }

            if (SimpleSidearmsCompat.IsLoaded() && AutoArmMod.settings?.autoEquipSidearms == true)
            {
                int sidearmInterval = 120;
                sidearmInterval += Math.Min(colonistCount * 8, 250);
                int sidearmVariance = __instance.thingIDNumber % Math.Max(40, colonistCount * 2);

                if (__instance.IsHashIntervalTick(sidearmInterval + sidearmVariance))
                {
                    checksThisTick++;
                    CheckSidearmUpgrade(__instance);
                }
            }
        }

        private static void CheckOutfitPolicyOnly(Pawn pawn)
        {
            // Check primary weapon
            if (pawn.equipment?.Primary != null && pawn.jobs != null)
            {
                var currentWeapon = pawn.equipment.Primary;

                if (!WeaponValidation.IsProperWeapon(currentWeapon))
                    return;

                // Don't interfere with sidearm upgrades in progress
                if (SimpleSidearmsCompat.IsLoaded() && AutoArmMod.settings?.autoEquipSidearms == true)
                {
                    if (DroppedItemTracker.IsSimpleSidearmsSwapInProgress(pawn))
                    {
                        // Sidearm upgrade in progress
                        return;
                    }

                    // Also skip if the current primary is a remembered sidearm (SS might have equipped it temporarily)
                    if (SimpleSidearmsCompat.PrimaryIsRememberedSidearm(pawn))
                    {
                        return;
                    }
                }

                // Don't force temporary colonists (quest pawns) to drop their weapons
                if (JobGiverHelpers.IsTemporaryColonist(pawn))
                {
                    // Temporary colonist
                    return;
                }

                var filter = pawn.outfits?.CurrentApparelPolicy?.filter;
                if (filter != null)
                {
                    bool weaponAllowed = filter.Allows(currentWeapon);
                    // Removed spammy TickRare outfit check log - this happens every 15 seconds per pawn

                    if (!weaponAllowed)
                    {
                        // Check if this weapon is forced - if so, keep it equipped
                        if (ForcedWeaponHelper.IsForced(pawn, currentWeapon))
                        {
                            // Keeping forced weapon
                            return; // Don't drop forced weapons
                        }

                        ForcedWeaponHelper.ClearForced(pawn);

                        // Mark weapon as dropped to prevent immediate re-pickup
                        DroppedItemTracker.MarkAsDropped(currentWeapon, 1200); // 20 second cooldown

                        var dropJob = JobMaker.MakeJob(JobDefOf.DropEquipment, currentWeapon);
                        pawn.jobs.StartJob(dropJob, JobCondition.InterruptForced);

                        if (AutoArmMod.settings?.debugLogging == true)
                        {
                            AutoArmLogger.Debug($"{pawn.LabelShort}: Dropping {currentWeapon.Label} - not allowed by outfit");
                        }
                    }
                }
            }

            // Also check sidearms
            Pawn_OutfitTracker_CurrentApparelPolicy_Setter_Patch.CheckAndDropDisallowedSidearms(pawn);
        }

        private static void CheckMainWeaponUpgrade(Pawn pawn)
        {
            try
            {
                if (pawn == null || pawn.equipment == null)
                    return;

                // Skip if pawn has a temporary sidearm equipped for upgrading
                if (SimpleSidearmsCompat.IsLoaded() && AutoArmMod.settings?.autoEquipSidearms == true)
                {
                    if (DroppedItemTracker.IsSimpleSidearmsSwapInProgress(pawn))
                    {
                        // Sidearm upgrade in progress
                        return;
                    }

                    // Also skip if the current primary is a remembered sidearm (SS might have equipped it temporarily)
                    if (SimpleSidearmsCompat.PrimaryIsRememberedSidearm(pawn))
                    {
                        // Primary is sidearm
                        return;
                    }
                }

                // Skip if pawn has a forced weapon or is manually equipping
                if (ForcedWeaponHelper.HasForcedWeapon(pawn))
                {
                    // Has forced weapon
                    return;
                }

                if (pawn.CurJob?.def == JobDefOf.Equip && pawn.CurJob.playerForced)
                {
                    // Manual equipping
                    return;
                }

                var currentEquipment = pawn.equipment.Primary;

                // Validate equipment is actually equipped and is a proper weapon
                if (currentEquipment != null && currentEquipment.ParentHolder != pawn.equipment)
                {
                    // Orphaned equipment reference
                    return;
                }

                bool hasWeapon = currentEquipment != null && IsProperWeapon(currentEquipment);

                if (hasWeapon && pawn.IsColonist && pawn.Faction == Faction.OfPlayer)
                {
                    // Don't force temporary colonists (quest pawns) to drop their weapons
                    if (JobGiverHelpers.IsTemporaryColonist(pawn))
                    {
                        // Temporary colonist
                        return;
                    }

                    var filter = pawn.outfits?.CurrentApparelPolicy?.filter;
                    if (filter != null && !filter.Allows(currentEquipment))
                    {
                        // Check if this weapon is forced - if so, keep it equipped
                        if (ForcedWeaponHelper.IsForced(pawn, currentEquipment))
                        {
                            // Keeping forced weapon
                            return; // Don't drop forced weapons
                        }

                        ForcedWeaponHelper.ClearForced(pawn);

                        // Mark weapon as dropped to prevent immediate re-pickup
                        DroppedItemTracker.MarkAsDropped(currentEquipment, 1200); // 20 second cooldown

                        if (pawn.jobs != null && !pawn.Drafted)
                        {
                            var dropJob = JobMaker.MakeJob(JobDefOf.DropEquipment, currentEquipment);
                            pawn.jobs.StartJob(dropJob, JobCondition.InterruptForced);

                            if (AutoArmMod.settings?.debugLogging == true)
                            {
                                AutoArmLogger.Debug($"{pawn.LabelShort}: Dropping {currentEquipment.Label} - not allowed by outfit");
                            }
                        }
                        return;
                    }
                }

                var jobGiver = new JobGiver_PickUpBetterWeapon();
                var job = jobGiver.TestTryGiveJob(pawn);

                if (job == null && hasWeapon)
                {
                    float currentScore = WeaponScoreCache.GetCachedScore(pawn, currentEquipment as ThingWithComps);
                    // No upgrade found
                }

                lastWeaponSearchTick[pawn] = Find.TickManager.TicksGame;

                if (job != null && pawn.jobs != null)
                {
                    bool shouldInterrupt = false;
                    bool isUnarmed = !hasWeapon;

                    if (pawn.CurJob == null)
                    {
                        shouldInterrupt = true;
                    }
                    else if (isUnarmed)
                    {
                        shouldInterrupt = !JobGiverHelpers.IsCriticalJob(pawn, hasNoSidearms: false);

                        if (shouldInterrupt)
                        {
                            if (AutoArmMod.settings?.debugLogging == true)
                            {
                                AutoArmLogger.Debug($"{pawn.LabelShort}: UNARMED - interrupting {pawn.CurJob.def.defName} to get weapon");
                            }
                        }
                    }
                    else
                    {
                        var currentJobDef = pawn.CurJob.def;
                        var currentJobDefName = currentJobDef.defName;

                        if (currentJobDef == JobDefOf.Wait ||
                            currentJobDef == JobDefOf.Wait_Wander ||
                            currentJobDef == JobDefOf.GotoWander ||
                            currentJobDef == JobDefOf.Goto ||
                            currentJobDef == JobDefOf.Clean ||
                            currentJobDef == JobDefOf.ClearSnow ||
                            currentJobDef == JobDefOf.HaulToCell ||
                            currentJobDef == JobDefOf.HaulToContainer ||
                            currentJobDef == JobDefOf.Research ||
                            currentJobDef == JobDefOf.SmoothFloor ||
                            currentJobDef == JobDefOf.SmoothWall ||
                            currentJobDef == JobDefOf.RemoveFloor ||
                            currentJobDef == JobDefOf.Sow ||
                            currentJobDef == JobDefOf.CutPlant ||
                            currentJobDef == JobDefOf.Harvest ||
                            currentJobDef == JobDefOf.HarvestDesignated ||
                            currentJobDef == JobDefOf.PlantSeed ||
                            currentJobDef == JobDefOf.Deconstruct ||
                            currentJobDef == JobDefOf.Uninstall ||
                            currentJobDef == JobDefOf.Repair ||
                            currentJobDef == JobDefOf.FixBrokenDownBuilding ||
                            currentJobDef == JobDefOf.Tame ||
                            currentJobDef == JobDefOf.Train ||
                            currentJobDef == JobDefOf.Milk ||
                            currentJobDef == JobDefOf.Shear ||
                            currentJobDef == JobDefOf.Slaughter ||
                            currentJobDef == JobDefOf.Mine ||
                            currentJobDef == JobDefOf.OperateScanner ||
                            currentJobDef == JobDefOf.OperateDeepDrill ||
                            currentJobDef == JobDefOf.Refuel ||
                            currentJobDef == JobDefOf.RearmTurret ||
                            currentJobDef == JobDefOf.FillFermentingBarrel ||
                            currentJobDef == JobDefOf.TakeBeerOutOfFermentingBarrel ||
                            currentJobDef == JobDefOf.UnloadInventory ||
                            currentJobDef == JobDefOf.UnloadYourInventory ||
                            currentJobDef == JobDefOf.Open ||
                            currentJobDef == JobDefOf.Flick ||
                            currentJobDef == JobDefOf.DoBill ||
                            currentJobDef == JobDefOf.TakeInventory ||
                            currentJobDef == JobDefOf.GiveToPackAnimal ||
                            currentJobDef == JobDefOf.LayDown ||
                            currentJobDef == JobDefOf.LayDownResting)
                        {
                            shouldInterrupt = true;
                            // Interrupting low priority job
                        }
                        else if (currentJobDefName.StartsWith("Joy") ||
                                 currentJobDefName.StartsWith("Play") ||
                                 currentJobDefName.Contains("Social") ||
                                 currentJobDefName.Contains("Relax") ||
                                 currentJobDefName.Contains("Clean") ||
                                 currentJobDefName.Contains("Idle") ||
                                 currentJobDefName.Contains("Wander") ||
                                 currentJobDefName.Contains("Wait") ||
                                 currentJobDefName.Contains("Skygaze") ||
                                 currentJobDefName.Contains("Meditate") ||
                                 currentJobDefName.Contains("Pray") ||
                                 currentJobDefName.Contains("CloudWatch") ||
                                 currentJobDefName.Contains("StandAndBeSociallyActive") ||
                                 currentJobDefName.Contains("ViewArt") ||
                                 currentJobDefName.Contains("VisitGrave") ||
                                 currentJobDefName.Contains("BuildSnowman"))
                        {
                            shouldInterrupt = true;
                            // Interrupting job by pattern
                        }
                        else if (pawn.workSettings != null && pawn.CurJob.workGiverDef?.workType != null)
                        {
                            var workType = pawn.CurJob.workGiverDef.workType;
                            int priority = pawn.workSettings.GetPriority(workType);

                            if (priority >= 3)
                            {
                                shouldInterrupt = true;
                                // Interrupting low priority work
                            }
                        }

                        if (!shouldInterrupt && job?.targetA.Thing is ThingWithComps newWeapon && currentEquipment is ThingWithComps currentWeaponComp)
                        {
                            float currentScore = WeaponScoreCache.GetCachedScore(pawn, currentWeaponComp);
                            float newScore = WeaponScoreCache.GetCachedScore(pawn, newWeapon);
                            float upgradePercentage = newScore / currentScore;

                            if (upgradePercentage >= 1.10f)
                            {
                                shouldInterrupt = !JobGiverHelpers.IsCriticalJob(pawn, hasNoSidearms: false);

                                if (shouldInterrupt)
                                {
                                    // Major upgrade available
                                }
                            }
                        }
                    }

                    if (shouldInterrupt)
                    {
                        pawn.jobs.StartJob(job, JobCondition.InterruptForced);

                        lastInterruptionTick[pawn] = Find.TickManager.TicksGame;

                        // Job started
                    }
                    else
                    {
                        // Critical job - not interrupting
                    }
                }
            }
            catch (Exception ex)
            {
                AutoArmLogger.Log($"ERROR in CheckMainWeaponUpgrade for {pawn?.Name?.ToStringShort ?? "unknown pawn"}: {ex.Message}\nStack trace: {ex.StackTrace}");
                // Continue without weapon upgrade - don't break the pawn's normal behavior
            }
        }

        private static void CheckSidearmUpgrade(Pawn pawn)
        {
            if (pawn == null || pawn.CurJob == null)
                return;

            // Check if temporary colonists are allowed to equip sidearms
            if (AutoArmMod.settings?.allowTemporaryColonists != true && JobGiverHelpers.IsTemporaryColonist(pawn))
            {
                // Temporary colonist
                return;
            }

            bool shouldCheckSidearms = !JobGiverHelpers.IsCriticalJob(pawn, hasNoSidearms: false);

            if (!shouldCheckSidearms)
                return;

            var job = SimpleSidearmsCompat.TryGetSidearmUpgradeJob(pawn);
            if (job != null && pawn.jobs != null)
            {
                // Found sidearm job

                pawn.jobs.StartJob(job, JobCondition.InterruptForced);

                lastSidearmCheckTick[pawn] = Find.TickManager.TicksGame;

                // Sidearm job started
            }
        }

        public static void MarkRecentlyUnarmed(Pawn pawn)
        {
            if (pawn != null && pawn.IsColonist)
            {
                recentlyUnarmedPawns.Add(pawn);

                // Marked as recently unarmed
            }
        }

        private static bool IsProperWeapon(ThingWithComps thing)
        {
            return WeaponValidation.IsProperWeapon(thing);
        }
        
        /// <summary>
        /// Clean up pawn references from all dictionaries to prevent memory leaks
        /// </summary>
        private static void CleanupPawnFromDictionaries(Pawn pawn)
        {
            if (pawn == null) return;
            
            lastInterruptionTick.Remove(pawn);
            lastSidearmCheckTick.Remove(pawn);
            lastWeaponCheckTick.Remove(pawn);
            lastWeaponSearchTick.Remove(pawn);
            cachedWeaponJobs.Remove(pawn);
            recentlyUnarmedPawns.Remove(pawn);
        }
        
        /// <summary>
        /// Periodic cleanup of dead pawns that might have been missed
        /// </summary>
        public static int CleanupDeadPawns()
        {
            int removed = 0;
            
            // Clean up lastInterruptionTick
            var pawnsToRemove = new List<Pawn>();
            foreach (var kvp in lastInterruptionTick)
            {
                if (kvp.Key == null || kvp.Key.Destroyed || kvp.Key.Dead || !kvp.Key.Spawned)
                    pawnsToRemove.Add(kvp.Key);
            }
            foreach (var pawn in pawnsToRemove)
            {
                lastInterruptionTick.Remove(pawn);
                removed++;
            }
            
            // Clean up lastSidearmCheckTick
            pawnsToRemove.Clear();
            foreach (var kvp in lastSidearmCheckTick)
            {
                if (kvp.Key == null || kvp.Key.Destroyed || kvp.Key.Dead || !kvp.Key.Spawned)
                    pawnsToRemove.Add(kvp.Key);
            }
            foreach (var pawn in pawnsToRemove)
            {
                lastSidearmCheckTick.Remove(pawn);
                removed++;
            }
                
            // Clean up lastWeaponCheckTick
            pawnsToRemove.Clear();
            foreach (var kvp in lastWeaponCheckTick)
            {
                if (kvp.Key == null || kvp.Key.Destroyed || kvp.Key.Dead || !kvp.Key.Spawned)
                    pawnsToRemove.Add(kvp.Key);
            }
            foreach (var pawn in pawnsToRemove)
            {
                lastWeaponCheckTick.Remove(pawn);
                removed++;
            }
                
            // Clean up lastWeaponSearchTick
            pawnsToRemove.Clear();
            foreach (var kvp in lastWeaponSearchTick)
            {
                if (kvp.Key == null || kvp.Key.Destroyed || kvp.Key.Dead || !kvp.Key.Spawned)
                    pawnsToRemove.Add(kvp.Key);
            }
            foreach (var pawn in pawnsToRemove)
            {
                lastWeaponSearchTick.Remove(pawn);
                removed++;
            }
                
            // Clean up cachedWeaponJobs
            pawnsToRemove.Clear();
            foreach (var kvp in cachedWeaponJobs)
            {
                if (kvp.Key == null || kvp.Key.Destroyed || kvp.Key.Dead || !kvp.Key.Spawned)
                    pawnsToRemove.Add(kvp.Key);
            }
            foreach (var pawn in pawnsToRemove)
            {
                cachedWeaponJobs.Remove(pawn);
                removed++;
            }
                
            // Clean up recentlyUnarmedPawns
            int removedFromHashSet = recentlyUnarmedPawns.RemoveWhere(p => p == null || p.Destroyed || p.Dead || !p.Spawned);
            removed += removedFromHashSet;
            
            return removed;
        }
    }
}