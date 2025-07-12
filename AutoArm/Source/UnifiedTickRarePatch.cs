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

            // Dynamic check intervals based on colony size
            int colonistCount = __instance.Map?.mapPawns?.FreeColonistsCount ?? 1;

            if (!hasWeapon)
            {
                // Unarmed - urgent priority, scales with colony size
                // Small colonies (1-10): check every 30-40 ticks
                // Medium colonies (11-20): check every 40-60 ticks  
                // Large colonies (21+): check every 60-100 ticks
                int baseInterval = 30 + Math.Min(colonistCount * 2, 60);
                int variance = __instance.thingIDNumber % Math.Max(10, colonistCount);

                if (__instance.IsHashIntervalTick(baseInterval + variance))
                {
                    checksThisTick++;
                    CheckMainWeaponUpgrade(__instance);
                }
            }
            else
            {
                // Armed - lower priority, more aggressive scaling
                // Small colonies: check every 150-200 ticks
                // Medium colonies: check every 200-350 ticks
                // Large colonies: check every 350-600 ticks
                int baseInterval = 150 + Math.Min(colonistCount * 10, 450);
                int variance = __instance.thingIDNumber % Math.Max(50, colonistCount * 5);

                if (__instance.IsHashIntervalTick(baseInterval + variance))
                {
                    checksThisTick++;
                    CheckMainWeaponUpgrade(__instance);
                }
            }

            if (SimpleSidearmsCompat.IsLoaded() && AutoArmMod.settings?.autoEquipSidearms == true)
            {
                // Sidearm checks - scale with colony size
                // Base 180 ticks + 10 per colonist (up to 500 ticks max)
                int sidearmInterval = 180 + Math.Min(colonistCount * 10, 320);
                int sidearmVariance = __instance.thingIDNumber % Math.Max(60, colonistCount * 3);

                if (__instance.IsHashIntervalTick(sidearmInterval + sidearmVariance))
                {
                    checksThisTick++;
                    CheckSidearmUpgrade(__instance);
                }
            }

            // Cleanup occasionally - REMOVED, now handled by MemoryCleanupManager
            // The cleanup is now centralized in MemoryCleanupManager
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
                else if (isUnarmed) // Unarmed pawns get priority
                {
                    // Don't interrupt critical jobs even for unarmed pawns
                    shouldInterrupt = !JobGiverHelpers.IsCriticalJob(pawn, hasNoSidearms: false);

                    // Even interrupt wander/idle for unarmed
                    if (pawn.CurJob.def == JobDefOf.Wait_Wander ||
                        pawn.CurJob.def == JobDefOf.GotoWander ||
                        pawn.CurJob.def == JobDefOf.Wait)
                    {
                        shouldInterrupt = true;
                        if (AutoArmMod.settings?.debugLogging == true)
                        {
                            Log.Message($"[AutoArm] {pawn.Name} interrupting wander to get weapon!");
                        }
                    }
                }
                else
                {
                    // Armed pawns interrupt safe jobs OR if upgrade is significant
                    shouldInterrupt = JobGiverHelpers.IsSafeToInterrupt(pawn.CurJob.def);

                    // Also interrupt for major upgrades (25%+ improvement)
                    if (!shouldInterrupt && job?.targetA.Thing is ThingWithComps newWeapon)
                    {
                        float currentScore = jobGiver.GetWeaponScore(pawn, currentEquipment as ThingWithComps);
                        float newScore = jobGiver.GetWeaponScore(pawn, newWeapon);
                        if (newScore > currentScore * 1.25f)
                        {
                            shouldInterrupt = true;
                            if (AutoArmMod.settings?.debugLogging == true)
                            {
                                Log.Message($"[AutoArm] {pawn.Name}: Major upgrade available ({currentScore:F0} -> {newScore:F0})");
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
            }
            else if (AutoArmMod.settings?.debugLogging == true)
            {
                string equipped = currentEquipment?.Label ?? "nothing";
                if (currentEquipment != null && !currentEquipment.def.IsWeapon)
                {
                    equipped = $"{currentEquipment.Label} (not a weapon)";
                }
                Log.Message($"[AutoArm] {pawn.Name}: No upgrade found (has {equipped})");
            }
        }

        private static void CheckSidearmUpgrade(Pawn pawn)
        {
            // NULL CHECK
            if (pawn == null || pawn.CurJob == null)
                return;

            // Only upgrade sidearms when pawn is doing unimportant work
            if (!JobGiverHelpers.IsSafeToInterrupt(pawn.CurJob.def))
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

        // REMOVED CleanupOldEntries - now handled by MemoryCleanupManager

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