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

        // Track pawns who just became unarmed for urgent checks
        private static HashSet<Pawn> recentlyUnarmedPawns = new HashSet<Pawn>();

        // Critical jobs that should never be interrupted
        private static readonly HashSet<JobDef> AlwaysCriticalJobs = new HashSet<JobDef>
        {
            // Medical/Emergency
            JobDefOf.TendPatient,
            JobDefOf.Rescue,
            JobDefOf.ExtinguishSelf,
            JobDefOf.BeatFire,
            JobDefOf.GotoSafeTemperature,     // Added - Fleeing deadly temperatures
    
            // Combat
            JobDefOf.AttackMelee,
            JobDefOf.AttackStatic,
            JobDefOf.Hunt,
            JobDefOf.ManTurret,
            JobDefOf.Wait_Combat,
            JobDefOf.FleeAndCower,
            JobDefOf.Reload,
    
            // Critical personal needs
            JobDefOf.Vomit,
            JobDefOf.LayDown,
            JobDefOf.Lovin,
            JobDefOf.Ingest,
    
            // Transportation
            JobDefOf.EnterTransporter,
            JobDefOf.EnterCryptosleepCasket,  // Added - Emergency medical
    
            // Prison/Security
            JobDefOf.Arrest,
            JobDefOf.Capture,
            JobDefOf.EscortPrisonerToBed,
            JobDefOf.TakeWoundedPrisonerToBed,
            JobDefOf.ReleasePrisoner,
            JobDefOf.Kidnap,
            JobDefOf.CarryDownedPawnToExit,
            JobDefOf.CarryToCryptosleepCasket, // Added - Emergency medical transport
    
            // Trading
            JobDefOf.TradeWithPawn,
    
            // Communications
            JobDefOf.UseCommsConsole,
    
            // Equipment handling
            JobDefOf.DropEquipment             // Added - Prevent interrupt loops
        };

        // Jobs that are only critical for pawns who already have sidearms
        private static readonly HashSet<JobDef> ConditionalCriticalJobs = new HashSet<JobDef>
        {
            JobDefOf.Sow,
            JobDefOf.Harvest,
            JobDefOf.HaulToCell,
            JobDefOf.HaulToContainer,
            JobDefOf.DoBill,
            JobDefOf.FinishFrame,
            JobDefOf.SmoothFloor,
            JobDefOf.Mine,
            JobDefOf.Refuel,
            JobDefOf.Research
        };

        // Safe jobs that can always be interrupted
        private static readonly HashSet<JobDef> AlwaysSafeToInterrupt = new HashSet<JobDef>
        {
            JobDefOf.Wait,
            JobDefOf.Wait_Wander,
            JobDefOf.GotoWander
        };

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

            if (__instance.equipment?.Primary == null && __instance.ageTracker.AgeBiologicalTicks < 100)
            {
                CheckMainWeaponUpgrade(__instance);
            }

            // Check unarmed pawns more frequently
            if (__instance.equipment?.Primary == null)
            {
                // Unarmed check every 60 ticks (1 second) with minimal staggering
                if (__instance.IsHashIntervalTick(60 + __instance.thingIDNumber % 20))
                {
                    CheckMainWeaponUpgrade(__instance);
                }
            }
            else
            {
                // Armed pawns - check frequently for upgrades
                int colonistCount = __instance.Map.mapPawns.FreeColonistsCount;
                int baseInterval = colonistCount < 20 ? 300 : 600;  // 5s or 10s

                if (__instance.IsHashIntervalTick(baseInterval + __instance.thingIDNumber % 100))
                {
                    CheckMainWeaponUpgrade(__instance);  // Skip ShouldCheckMainWeapon
                }
            }

            // Sidearms check
            if (SimpleSidearmsCompat.IsLoaded() &&
                AutoArmMod.settings?.autoEquipSidearms == true &&
                __instance.IsHashIntervalTick(3000 + __instance.thingIDNumber % 500))
            {
                if (ShouldCheckSidearms(__instance))
                {
                    CheckSidearmUpgrade(__instance);
                }
            }

            // Cleanup occasionally
            if (Find.TickManager.TicksGame % 10000 == 0 && __instance.IsHashIntervalTick(100))
            {
                CleanupOldEntries();
            }
        }

        private static bool ShouldCheckMainWeapon(Pawn pawn)
        {
            var currentWeapon = pawn.equipment?.Primary;

            // Recently unarmed gets immediate check
            if (recentlyUnarmedPawns.Contains(pawn))
            {
                recentlyUnarmedPawns.Remove(pawn);
                lastWeaponCheckTick[pawn] = Find.TickManager.TicksGame;
                return true;
            }

            // Unarmed pawns always pass through (they're already limited by tick interval)
            if (currentWeapon == null)
            {
                lastWeaponCheckTick[pawn] = Find.TickManager.TicksGame;
                return true;  // Always return true for unarmed
            }

            // Armed pawns use cooldown
            if (lastWeaponCheckTick.TryGetValue(pawn, out int lastTick))
            {
                if (Find.TickManager.TicksGame - lastTick < 300)  // 5 seconds
                    return false;
            }

            // If think tree injection failed, check more frequently
            if (AutoArmMod.settings?.thinkTreeInjectionFailed == true)
            {
                lastWeaponCheckTick[pawn] = Find.TickManager.TicksGame;
                return pawn.IsHashIntervalTick(2000 + pawn.thingIDNumber % 500);
            }

            // Check outfit filter (only for armed pawns)
            if (pawn.outfits?.CurrentApparelPolicy?.filter != null)
            {
                var filter = pawn.outfits.CurrentApparelPolicy.filter;

                // Check if current weapon is disallowed
                if (!filter.Allows(currentWeapon.def))
                {
                    // Immediate drop for disallowed weapons
                    if (pawn.jobs?.curJob?.def != JobDefOf.DropEquipment)
                    {
                        ForcedWeaponTracker.ClearForced(pawn);
                        var dropJob = new Job(JobDefOf.DropEquipment, currentWeapon);
                        pawn.jobs.TryTakeOrderedJob(dropJob, JobTag.Misc);

                        if (AutoArmMod.settings.debugLogging)
                        {
                            Log.Message($"[AutoArm] {pawn.Name}: Dropping disallowed weapon {currentWeapon.Label}");
                        }
                        return false;
                    }
                }
            }

            // Mark check time and allow check
            lastWeaponCheckTick[pawn] = Find.TickManager.TicksGame;
            return true;
        }

        private static bool ShouldCheckSidearms(Pawn pawn)
        {
            // Check cooldown
            if (lastSidearmCheckTick.TryGetValue(pawn, out int lastTick))
            {
                if (Find.TickManager.TicksGame - lastTick < 500)
                    return false;
            }

            // Check if pawn needs sidearms
            int maxSidearms = SimpleSidearmsCompat.GetMaxSidearmsForPawn(pawn);
            int currentSidearms = SimpleSidearmsCompat.GetCurrentSidearmCount(pawn);
            bool needsSidearms = currentSidearms < maxSidearms;

            if (needsSidearms)
            {
                return pawn.IsHashIntervalTick(250 + pawn.thingIDNumber % 50);
            }
            else
            {
                // At max - scale with colony size
                int pawnCount = pawn.Map.mapPawns.FreeColonistsCount;
                int checkInterval = Math.Min(6000, Math.Max(1500, pawnCount * 50));
                return pawn.IsHashIntervalTick(checkInterval + pawn.thingIDNumber % 500);
            }
        }

        private static bool HasNoSidearms(Pawn pawn)
        {
            // Quick check if pawn has any weapons in inventory
            if (pawn.inventory?.innerContainer == null || pawn.inventory.innerContainer.Count == 0)
                return true;

            // Check if any weapons in inventory
            return !pawn.inventory.innerContainer.Any(t => t.def.IsWeapon);
        }

        private static bool IsCriticalJob(Pawn pawn, bool hasNoSidearms = false)
        {
            if (pawn.CurJob == null)
                return false;

            var job = pawn.CurJob;

            // Always check player-forced
            if (job.playerForced)
                return true;

            // Check always-critical jobs
            if (AlwaysCriticalJobs.Contains(job.def))
                return true;

            // Check conditional jobs (only critical if pawn has sidearms)
            if (!hasNoSidearms && ConditionalCriticalJobs.Contains(job.def))
                return true;

            // Check string-based patterns for DLC/modded content
            var defName = job.def.defName;
            if (defName.Contains("Ritual") ||
                defName.Contains("Surgery") ||
                defName.Contains("Operate") ||
                defName.Contains("Prisoner") ||
                defName.Contains("Mental") ||
                defName.Contains("PrepareCaravan"))
                return true;

            return false;
        }

        private static bool IsSafeToInterrupt(JobDef jobDef)
        {
            if (AlwaysSafeToInterrupt.Contains(jobDef))
                return true;

            var defName = jobDef.defName;
            return defName.StartsWith("Joy") ||
                   defName.Contains("Social");
        }

        private static void CheckMainWeaponUpgrade(Pawn pawn)
        {
            // Try to give a weapon job using our job giver
            var jobGiver = new JobGiver_PickUpBetterWeapon();
            var job = jobGiver.TestTryGiveJob(pawn);

            if (job != null && pawn.jobs != null)
            {
                // Check if we should interrupt current job
                bool shouldInterrupt = false;
                var currentWeapon = pawn.equipment?.Primary;
                bool isUrgent = currentWeapon == null;

                if (pawn.CurJob == null)
                {
                    shouldInterrupt = true;
                }
                else if (isUrgent) // Unarmed pawns get priority
                {
                    // Don't interrupt critical jobs even for unarmed pawns
                    shouldInterrupt = !IsCriticalJob(pawn, hasNoSidearms: true);
                }
                else
                {
                    // Armed pawns interrupt safe jobs OR if upgrade is significant
                    shouldInterrupt = IsSafeToInterrupt(pawn.CurJob.def);

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
            }
        }

        private static void CheckSidearmUpgrade(Pawn pawn)
        {
            SimpleSidearmsCompat.CheckPendingSidearmRegistrations(pawn);

            // Only upgrade sidearms when pawn is doing unimportant work
            if (pawn.CurJob != null && !IsSafeToInterrupt(pawn.CurJob.def))
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
            // Remove entries for dead/despawned pawns
            var toRemove = lastInterruptionTick.Keys
                .Where(p => p.DestroyedOrNull() || p.Dead || !p.Spawned)
                .ToList();

            foreach (var pawn in toRemove)
            {
                lastInterruptionTick.Remove(pawn);
                lastSidearmCheckTick.Remove(pawn);
                lastWeaponCheckTick.Remove(pawn);
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

            SimpleSidearmsCompat.CleanupPendingRegistrations();
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