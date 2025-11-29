using AutoArm.Caching;
using AutoArm.Helpers;
using AutoArm.Jobs;
using AutoArm.Logging;
using AutoArm.Weapons;
using System;
using System.Linq;
using Verse;

namespace AutoArm.Testing
{
    /// <summary>
    /// Test isolation
    /// </summary>
    public static class TestRunnerFix
    {
        /// <summary>
        /// Clear pawn cooldowns
        /// </summary>
        public static void ClearAllCooldownsForPawn(Pawn pawn)
        {
            if (pawn == null) return;

            try
            {

                WeaponBlacklist.ClearBlacklist(pawn);

                ForcedWeapons.ClearForced(pawn);

            }
            catch (Exception e)
            {
                AutoArmLogger.Error($"Error clearing cooldowns for pawn {pawn.Name}", e);
            }
        }

        /// <summary>
        /// Clear all
        /// </summary>
        public static void ClearAllGlobalCooldowns()
        {
            try
            {
                WeaponCacheManager.ClearAllCaches();
                DroppedItemTracker.ClearAll();
            }
            catch (Exception e)
            {
                AutoArmLogger.Error("Error clearing global cooldowns", e);
            }
        }

        /// <summary>
        /// Reset all systems
        /// </summary>
        public static void ResetAllSystems()
        {
            try
            {
                if (AutoArmMod.settings != null)
                {
                    AutoArmMod.settings.modEnabled = true;
                }

                ClearAllWeaponCaches();
                WeaponCacheManager.ClearAllCaches();

                DroppedItemTracker.ClearAll();
                ClearAllForcedWeapons();
                ClearAllWeaponBlacklists();

                ClearAllGlobalCooldowns();

                ClearJobGiverPerTickTracking();

                Cleanup.PerformFullCleanup();

            }
            catch (Exception e)
            {
                AutoArmLogger.Error("Error resetting AutoArm systems", e);
            }
        }

        /// <summary>
        /// Prepare pawn
        /// </summary>
        public static void PreparePawnForTest(Pawn pawn)
        {
            if (pawn == null) return;

            try
            {
                ClearAllCooldownsForPawn(pawn);

                ClearJobGiverPerTickTracking();

                pawn.jobs?.StopAll();


                if (pawn.Drafted)
                {
                    pawn.drafter.Drafted = false;
                }

                if (pawn.Downed && pawn.health != null)
                {
                    var hediffsToRemove = pawn.health.hediffSet.hediffs
                        .Where(h => h.def.stages?.Any(s => s.capMods?.Any() == true) == true)
                        .ToList();

                    foreach (var hediff in hediffsToRemove)
                    {
                        pawn.health.RemoveHediff(hediff);
                    }
                }


            }
            catch (Exception e)
            {
                AutoArmLogger.Error($"Error preparing pawn {pawn.Name} for test", e);
            }
        }


        private static void ClearAllForcedWeapons()
        {
            try
            {
                var allPawns = Find.Maps?.SelectMany(m => m.mapPawns?.AllPawns ?? Enumerable.Empty<Pawn>())
                             ?? Enumerable.Empty<Pawn>();

                foreach (var pawn in allPawns)
                {
                    ForcedWeapons.ClearForced(pawn);
                }

                ForcedWeapons.Cleanup();

            }
            catch (Exception e)
            {
                AutoArmLogger.Error("Error clearing forced weapons", e);
            }
        }


        private static void ClearAllWeaponBlacklists()
        {
            try
            {
                var allPawns = Find.Maps?.SelectMany(m => m.mapPawns?.AllPawns ?? Enumerable.Empty<Pawn>())
                             ?? Enumerable.Empty<Pawn>();

                foreach (var pawn in allPawns)
                {
                    WeaponBlacklist.ClearBlacklist(pawn);
                }

                WeaponBlacklist.CleanupOldEntries();

            }
            catch (Exception e)
            {
                AutoArmLogger.Error("Error clearing weapon blacklists", e);
            }
        }


        private static void ClearAllWeaponCaches()
        {
            try
            {
                WeaponCacheManager.ClearAllCaches();

                WeaponCacheManager.CleanupDestroyedMaps();

            }
            catch (Exception e)
            {
                AutoArmLogger.Error("Error clearing weapon caches", e);
            }
        }

        /// <summary>
        /// Clear per-tick tracking
        /// </summary>
        public static void ClearJobGiverPerTickTracking()
        {
            try
            {
                JobGiver_PickUpBetterWeapon.ResetForTesting();
                JobGiver_PickUpBetterWeapon.CleanupCaches();

            }
            catch (Exception e)
            {
                AutoArmLogger.Error("Error clearing JobGiver per-tick tracking", e);
            }
        }

        /// <summary>
        /// Verify environment
        /// </summary>
        public static bool VerifyTestEnvironment(Map map)
        {
            if (map == null)
            {
                AutoArmLogger.Error("[TEST] No map available for testing");
                return false;
            }

            if (AutoArmMod.settings == null)
            {
                AutoArmLogger.Error("[TEST] AutoArm settings not initialized");
                return false;
            }

            if (!AutoArmMod.settings.modEnabled)
            {
                AutoArmLogger.Warn("[TEST] AutoArm mod is disabled, enabling for tests");
                AutoArmMod.settings.modEnabled = true;
            }

            return true;
        }
    }
}
