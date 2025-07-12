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
        // Add these missing fields
        private static Dictionary<Pawn, int> lastInterruptionTick = new Dictionary<Pawn, int>();
        private static Dictionary<Pawn, int> lastSidearmCheckTick = new Dictionary<Pawn, int>();
        private static Dictionary<Pawn, int> lastWeaponCheckTick = new Dictionary<Pawn, int>();
        internal static Dictionary<Pawn, int> lastWeaponSearchTick = new Dictionary<Pawn, int>();
        internal static Dictionary<Pawn, Job> cachedWeaponJobs = new Dictionary<Pawn, Job>();
        private static HashSet<Pawn> recentlyUnarmedPawns = new HashSet<Pawn>();

        private static int checksThisTick = 0;
        private static int lastTickProcessed = -1;
        private const int MaxChecksPerTick = 1;

        [HarmonyPostfix]
        public static void Postfix(Pawn __instance)
        {
            if (Find.TickManager.TicksGame != lastTickProcessed)
            {
                checksThisTick = 0;
                lastTickProcessed = Find.TickManager.TicksGame;
            }

            // Skip if too many checks this tick
            if (checksThisTick >= MaxChecksPerTick)
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
                __instance.InBed() || __instance.WorkTagIsDisabled(WorkTags.Violent))  // Fixed syntax
                return;

            // Skip drafted pawns
            if (__instance.Drafted)
                return;

            // Rest of your existing code...
            var primary = __instance.equipment?.Primary;
            bool hasWeapon = primary != null && primary.def.IsWeapon;

            if (!hasWeapon)
            {
                // Unarmed - check every 30-60 ticks (0.5-1 second) for faster response
                if (__instance.IsHashIntervalTick(31 + __instance.thingIDNumber % 37))
                {
                    checksThisTick++;  // ADD THIS!
                    CheckMainWeaponUpgrade(__instance);
                }
            }
            else
            {
                // Armed - check based on colony size
                int colonistCount = __instance.Map.mapPawns.FreeColonistsCount;
                int baseInterval = colonistCount < 20 ? 150 : 300;
                if (__instance.IsHashIntervalTick(baseInterval + __instance.thingIDNumber % 100))
                {
                    checksThisTick++;  // ADD THIS!
                    CheckMainWeaponUpgrade(__instance);
                }
            }

            if (SimpleSidearmsCompat.IsLoaded() && AutoArmMod.settings?.autoEquipSidearms == true)
            {
                // Check more often - every 3-5 seconds
                if (__instance.IsHashIntervalTick(180 + __instance.thingIDNumber % 120))
                {
                    checksThisTick++;
                    CheckSidearmUpgrade(__instance);
                }
            }

            // Cleanup occasionally
            if (Find.TickManager.TicksGame % 2500 == 0 && __instance.IsHashIntervalTick(100))
            {
                CleanupOldEntries();
            }
        }

        private static void CheckMainWeaponUpgrade(Pawn pawn)
        {
            // Check if pawn has a WEAPON (not just any equipment like beer)
            var currentEquipment = pawn.equipment?.Primary;
            bool hasWeapon = currentEquipment != null && currentEquipment.def.IsWeapon;

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
                            if (AutoArmMod.settings.debugLogging)
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
            // Only upgrade sidearms when pawn is doing unimportant work
            if (pawn.CurJob != null && !JobGiverHelpers.IsSafeToInterrupt(pawn.CurJob.def))
                return;

            // Try to find sidearm upgrade
            var job = SimpleSidearmsCompat.TryGetSidearmUpgradeJob(pawn);
            if (job != null)
            {
                if (AutoArmMod.settings.debugLogging)
                {
                    Log.Message($"[AutoArm] Found sidearm job for {pawn.Name}: {job.def.defName} targeting {job.targetA.Thing?.Label}");
                }

                pawn.jobs.StartJob(job, JobCondition.InterruptForced);

                // Update cooldown
                lastSidearmCheckTick[pawn] = Find.TickManager.TicksGame;

                if (AutoArmMod.settings.debugLogging)
                {
                    Log.Message($"[AutoArm] Started sidearm job for {pawn.Name} (was doing {pawn.CurJob?.def.defName ?? "nothing"})");
                }
            }
        }

        private static void CleanupOldEntries()
        {
            var invalidJobPawns = cachedWeaponJobs
                .Where(kvp => kvp.Value != null &&
                       (kvp.Value.targetA.Thing?.DestroyedOrNull() ?? true))
                .Select(kvp => kvp.Key)
                .ToList();

            // Remove entries for dead/despawned pawns
            var toRemove = lastInterruptionTick.Keys
                .Where(p => p.DestroyedOrNull() || p.Dead || !p.Spawned)
                .ToList();

            foreach (var pawn in toRemove)
            {
                lastInterruptionTick.Remove(pawn);
                lastSidearmCheckTick.Remove(pawn);
                lastWeaponCheckTick.Remove(pawn);
                lastWeaponSearchTick.Remove(pawn);
                cachedWeaponJobs.Remove(pawn);
                recentlyUnarmedPawns.Remove(pawn);
            }

            // Clean up weapon cache for null or destroyed maps
            var cacheMapsToRemove = JobGiver_PickUpBetterWeapon.weaponCache.Keys
                .Where(m => m == null || m.Tile < 0 || !Find.Maps.Contains(m))
                .ToList();
            foreach (var map in cacheMapsToRemove)
            {
                JobGiver_PickUpBetterWeapon.weaponCache.Remove(map);
                JobGiver_PickUpBetterWeapon.weaponCacheAge.Remove(map);
            }

            // Limit recently unarmed pawns set size
            if (recentlyUnarmedPawns.Count > 20)
            {
                recentlyUnarmedPawns.Clear();

                if (AutoArmMod.settings?.debugLogging == true)
                {
                    Log.Message("[AutoArm] Cleared recentlyUnarmedPawns set - was getting too large");
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