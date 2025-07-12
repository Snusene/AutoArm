using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace AutoArm
{
    [HarmonyPatch(typeof(Pawn), "TickRare")]
    public static class Pawn_TickRare_Unified_Patch
    {
        // Changed from private to internal for cleanup access
        internal static Dictionary<Pawn, int> lastInterruptionTick = new Dictionary<Pawn, int>();
        internal static Dictionary<Pawn, int> lastSidearmCheckTick = new Dictionary<Pawn, int>();
        internal static Dictionary<Pawn, int> lastWeaponCheckTick = new Dictionary<Pawn, int>();
        internal static Dictionary<Pawn, int> lastWeaponSearchTick = new Dictionary<Pawn, int>();
        internal static Dictionary<Pawn, Job> cachedWeaponJobs = new Dictionary<Pawn, Job>();
        internal static HashSet<Pawn> recentlyUnarmedPawns = new HashSet<Pawn>();

        private static int checksThisTick = 0;
        private static int lastTickProcessed = -1;

        // Dynamic max checks based on colony size
        private static int GetMaxChecksPerTick(Map map)
        {
            if (map == null) return 3;
            int colonistCount = map.mapPawns.FreeColonistsCount;

            // Scale checks: 1-10 colonists = 3 checks, 11-20 = 5 checks, 21+ = 7 checks
            if (colonistCount <= 10) return 3;
            if (colonistCount <= 20) return 5;
            return 7;
        }

        [HarmonyPostfix]
        public static void Postfix(Pawn __instance)
        {
            // NULL CHECK at the very beginning
            if (__instance == null)
                return;

            if (Find.TickManager.TicksGame != lastTickProcessed)
            {
                checksThisTick = 0;
                lastTickProcessed = Find.TickManager.TicksGame;
            }

            // Skip if too many checks this tick (dynamic based on colony size)
            int maxChecks = GetMaxChecksPerTick(__instance.Map);
            if (checksThisTick >= maxChecks)
                return;

            // Skip if mod is disabled
            if (AutoArmMod.settings?.modEnabled != true)
                return;

            // EARLY EXIT: Check if pawn is actually spawned
            if (!__instance.Spawned || __instance.Map == null)
                return;

            // Skip dead or destroyed pawns
            if (__instance.Dead || __instance.Destroyed)
                return;

            // Skip if in container (cryptosleep, transport pod, etc)
            if (__instance.InContainerEnclosed)
                return;

            // Skip if in caravan or world pawn
            if (__instance.IsCaravanMember() || __instance.IsWorldPawn())
                return;

            // Only check colonists
            if (!__instance.IsColonist || __instance.Downed ||
                __instance.InBed() || __instance.WorkTagIsDisabled(WorkTags.Violent))
                return;

            // Skip drafted pawns
            if (__instance.Drafted)
                return;

            // Check equipment with null safety
            var primary = __instance.equipment?.Primary;
            bool hasWeapon = primary != null && primary.def?.IsWeapon == true;

            // MORE AGGRESSIVE CHECK INTERVALS
            int colonistCount = __instance.Map?.mapPawns?.FreeColonistsCount ?? 1;

            if (!hasWeapon)
            {
                // Unarmed - VERY urgent priority
                int baseInterval = 15; // Very fast checks for unarmed
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
                // Armed - still check frequently
                int baseInterval = 60; // Much faster than before
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
                // Sidearm checks - also more aggressive
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

        private static void CheckMainWeaponUpgrade(Pawn pawn)
        {
            // NULL CHECK
            if (pawn == null || pawn.equipment == null)
                return;

            // Check if pawn has a WEAPON (not just any equipment like beer)
            var currentEquipment = pawn.equipment.Primary;
            bool hasWeapon = currentEquipment != null && currentEquipment.def?.IsWeapon == true;

            // NEW: Check if current weapon is disallowed by outfit
            if (hasWeapon && pawn.IsColonist && pawn.Faction == Faction.OfPlayer)
            {
                var filter = pawn.outfits?.CurrentApparelPolicy?.filter;
                if (filter != null && !filter.Allows(currentEquipment.def))
                {
                    // Weapon is now disallowed - drop it
                    ForcedWeaponTracker.ClearForced(pawn);

                    if (pawn.jobs != null && !pawn.Drafted)
                    {
                        var dropJob = JobMaker.MakeJob(JobDefOf.DropEquipment, currentEquipment);
                        pawn.jobs.StartJob(dropJob, JobCondition.InterruptForced);

                        if (AutoArmMod.settings?.debugLogging == true)
                        {
                            Log.Message($"[AutoArm] {pawn.Name}: Dropping {currentEquipment.Label} - no longer allowed by outfit");
                        }

                        if (PawnUtility.ShouldSendNotificationAbout(pawn))
                        {
                            Messages.Message("AutoArm_DroppingDisallowed".Translate(
                                pawn.LabelShort.CapitalizeFirst(),
                                currentEquipment.Label
                            ), new LookTargets(pawn), MessageTypeDefOf.SilentInput, false);
                        }
                    }
                    return; // Don't check for upgrades if we're dropping
                }
            }

            // Try to give a weapon job using our job giver
            var jobGiver = new JobGiver_PickUpBetterWeapon();
            var job = jobGiver.TestTryGiveJob(pawn);

            if (job == null && hasWeapon && AutoArmMod.settings?.debugLogging == true)
            {
                float currentScore = jobGiver.GetWeaponScore(pawn, currentEquipment as ThingWithComps);
                Log.Message($"[AutoArm DEBUG] {pawn.Name} has {currentEquipment.Label} (score: {currentScore:F1}) but found no upgrade");
            }

            // Update last search time
            lastWeaponSearchTick[pawn] = Find.TickManager.TicksGame;

            if (job != null && pawn.jobs != null)
            {
                // Check if we should interrupt current job
                bool shouldInterrupt = false;
                bool isUnarmed = !hasWeapon; // No weapon (might have beer/wood/etc)

                if (pawn.CurJob == null)
                {
                    shouldInterrupt = true;
                }
                else if (isUnarmed) // Unarmed pawns get ABSOLUTE priority
                {
                    // Interrupt EVERYTHING except the most critical jobs
                    shouldInterrupt = !JobGiverHelpers.IsCriticalJob(pawn, hasNoSidearms: false);

                    if (AutoArmMod.settings?.debugLogging == true && shouldInterrupt)
                    {
                        Log.Message($"[AutoArm] {pawn.Name} is UNARMED - interrupting {pawn.CurJob.def.defName} to get weapon!");
                    }
                }
                else
                {
                    // AGGRESSIVE INTERRUPTION for armed pawns
                    var currentJobDef = pawn.CurJob.def;
                    var currentJobDefName = currentJobDef.defName;

                    // ALWAYS interrupt these vanilla low-priority jobs for ANY upgrade
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
                        if (AutoArmMod.settings?.debugLogging == true)
                        {
                            Log.Message($"[AutoArm] {pawn.Name}: Interrupting low priority {currentJobDef.defName} for weapon upgrade");
                        }
                    }
                    // Check string patterns for modded/DLC jobs
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
                        if (AutoArmMod.settings?.debugLogging == true)
                        {
                            Log.Message($"[AutoArm] {pawn.Name}: Interrupting {currentJobDefName} (matched pattern) for weapon upgrade");
                        }
                    }
                    // Check work priority for other jobs
                    else if (pawn.workSettings != null && pawn.CurJob.workGiverDef?.workType != null)
                    {
                        var workType = pawn.CurJob.workGiverDef.workType;
                        int priority = pawn.workSettings.GetPriority(workType);

                        // Interrupt priority 3 and 4 work for any upgrade
                        if (priority >= 3)
                        {
                            shouldInterrupt = true;
                            if (AutoArmMod.settings?.debugLogging == true)
                            {
                                Log.Message($"[AutoArm] {pawn.Name}: Interrupting priority {priority} work ({currentJobDef.defName}) for weapon upgrade");
                            }
                        }
                    }

                    // For higher priority work, still interrupt for significant upgrades
                    if (!shouldInterrupt && job?.targetA.Thing is ThingWithComps newWeapon && currentEquipment is ThingWithComps currentWeaponComp)
                    {
                        float currentScore = jobGiver.GetWeaponScore(pawn, currentWeaponComp);
                        float newScore = jobGiver.GetWeaponScore(pawn, newWeapon);
                        float upgradePercentage = newScore / currentScore;

                        // Interrupt for 10%+ upgrades (lower threshold than before)
                        if (upgradePercentage >= 1.10f)
                        {
                            shouldInterrupt = !JobGiverHelpers.IsCriticalJob(pawn, hasNoSidearms: false);

                            if (AutoArmMod.settings?.debugLogging == true && shouldInterrupt)
                            {
                                Log.Message($"[AutoArm] {pawn.Name}: {(upgradePercentage - 1f) * 100f:F0}% upgrade available - interrupting {currentJobDef.defName}");
                            }
                        }
                    }
                }

                // ACTUALLY START THE JOB!
                if (shouldInterrupt)
                {
                    pawn.jobs.StartJob(job, JobCondition.InterruptForced);

                    // Update cooldown
                    lastInterruptionTick[pawn] = Find.TickManager.TicksGame;

                    if (AutoArmMod.settings?.debugLogging == true)
                    {
                        Log.Message($"[AutoArm] {pawn.Name}: Interrupting to pick up {job.targetA.Thing?.Label}");
                    }
                }
                else if (AutoArmMod.settings?.debugLogging == true)
                {
                    Log.Message($"[AutoArm] {pawn.Name}: Found upgrade but not interrupting {pawn.CurJob.def.defName} (critical job)");
                }
            }
        }

        private static void CheckSidearmUpgrade(Pawn pawn)
        {
            // NULL CHECK
            if (pawn == null || pawn.CurJob == null)
                return;

            // AGGRESSIVE SIDEARM CHECKING - interrupt most non-critical jobs
            bool shouldCheckSidearms = !JobGiverHelpers.IsCriticalJob(pawn, hasNoSidearms: false);

            if (!shouldCheckSidearms)
                return;

            // Try to find sidearm upgrade
            var job = SimpleSidearmsCompat.TryGetSidearmUpgradeJob(pawn);
            if (job != null && pawn.jobs != null)
            {
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    Log.Message($"[AutoArm] Found sidearm job for {pawn.Name}: {job.def.defName} targeting {job.targetA.Thing?.Label}");
                }

                pawn.jobs.StartJob(job, JobCondition.InterruptForced);

                // Update cooldown
                lastSidearmCheckTick[pawn] = Find.TickManager.TicksGame;

                if (AutoArmMod.settings?.debugLogging == true)
                {
                    Log.Message($"[AutoArm] Started sidearm job for {pawn.Name} (was doing {pawn.CurJob?.def.defName ?? "nothing"})");
                }
            }
        }

        // Public method for marking pawns who just became unarmed
        public static void MarkRecentlyUnarmed(Pawn pawn)
        {
            if (pawn != null && pawn.IsColonist)
            {
                recentlyUnarmedPawns.Add(pawn);

                if (AutoArmMod.settings?.debugLogging == true)
                {
                    Log.Message($"[AutoArm] {pawn.Name} marked as recently unarmed - will check for weapon immediately");
                }
            }
        }
    }
}