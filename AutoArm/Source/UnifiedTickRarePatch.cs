using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace AutoArm
{
    // Consolidated TickRare patch to avoid multiple patches on the same method
    [HarmonyPatch(typeof(Pawn), "TickRare")]
    [HarmonyPriority(Priority.Low)] // Run after other mods
    public static class Pawn_TickRare_Unified_Patch
    {
        // Track last interruption time for cooldowns
        private static Dictionary<Pawn, int> lastInterruptionTick = new Dictionary<Pawn, int>();
        private static Dictionary<Pawn, int> lastSidearmCheckTick = new Dictionary<Pawn, int>();
        private static Dictionary<Pawn, int> lastWeaponCheckTick = new Dictionary<Pawn, int>();

        internal static Dictionary<Pawn, int> lastWeaponSearchTick = new Dictionary<Pawn, int>();
        internal static Dictionary<Pawn, Job> cachedWeaponJobs = new Dictionary<Pawn, Job>();

        // Track pawns who just became unarmed for urgent checks
        private static HashSet<Pawn> recentlyUnarmedPawns = new HashSet<Pawn>();

        [HarmonyPostfix]
        public static void Postfix(Pawn __instance)
        {
            // Skip if mod is disabled
            if (AutoArmMod.settings?.modEnabled != true)
                return;

            // Only check colonists
            if (!__instance.IsColonist || __instance.Dead || __instance.Downed ||
                __instance.InBed() || __instance.WorkTagIsDisabled(WorkTags.Violent))
                return;

            // Skip drafted pawns
            if (__instance.Drafted)
                return;

            // Main weapon checks
            if (__instance.equipment?.Primary == null)
            {
                // Unarmed - check frequently
                if (__instance.IsHashIntervalTick(60 + __instance.thingIDNumber % 20))
                {
                    CheckMainWeaponUpgrade(__instance);
                }
            }
            else
            {
                // Armed - check based on colony size
                int colonistCount = __instance.Map.mapPawns.FreeColonistsCount;
                int baseInterval = colonistCount < 20 ? 300 : 600;

                if (__instance.IsHashIntervalTick(baseInterval + __instance.thingIDNumber % 100))
                {
                    CheckMainWeaponUpgrade(__instance);
                }
            }

            // Sidearms check - normal priority only
            if (SimpleSidearmsCompat.IsLoaded() && AutoArmMod.settings?.autoEquipSidearms == true)
            {
                if (__instance.IsHashIntervalTick(1500 + __instance.thingIDNumber % 500))
                {
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
            // Check cache first (skip for unarmed - they need immediate checks)
            if (pawn.equipment?.Primary != null)
            {
                if (lastWeaponSearchTick.TryGetValue(pawn, out int lastSearch))
                {
                    if (Find.TickManager.TicksGame - lastSearch < 600) // 10 seconds
                    {
                        return; // Skip expensive search for armed pawns
                    }
                }
            }

            // Try to give a weapon job using our job giver
            var jobGiver = new JobGiver_PickUpBetterWeapon();
            var job = jobGiver.TestTryGiveJob(pawn);

            // Update last search time
            lastWeaponSearchTick[pawn] = Find.TickManager.TicksGame;

            if (job != null && pawn.jobs != null)
            {
                // Check if we should interrupt current job
                bool shouldInterrupt = false;
                var currentWeapon = pawn.equipment?.Primary;
                bool isUnarmed = currentWeapon == null;

                if (pawn.CurJob == null)
                {
                    shouldInterrupt = true;
                }
                else if (isUnarmed) // Unarmed pawns get priority
                {
                    // Don't interrupt critical jobs even for unarmed pawns
                    shouldInterrupt = !JobGiverHelpers.IsCriticalJob(pawn, hasNoSidearms: false);
                }
                else
                {
                    // Armed pawns interrupt safe jobs OR if upgrade is significant
                    shouldInterrupt = JobGiverHelpers.IsSafeToInterrupt(pawn.CurJob.def);

                    // Also interrupt for major upgrades (25%+ improvement)
                    if (!shouldInterrupt && job?.targetA.Thing is ThingWithComps newWeapon)
                    {
                        float currentScore = jobGiver.GetWeaponScore(pawn, currentWeapon);
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
                Log.Message($"[AutoArm] {pawn.Name}: No upgrade found (has {pawn.equipment?.Primary?.Label ?? "nothing"})");
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

            // Limit recently unarmed pawns set size to prevent accumulation
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