using AutoArm.Caching;
using AutoArm.Helpers;
using AutoArm.Jobs;
using AutoArm.Logging;
using System;
using Verse;

namespace AutoArm.Testing.Framework
{
    /// <summary>
    /// Centralized test environment management to handle state reset and cleanup across all tests
    /// </summary>
    public static class TestEnvironment
    {
        /// <summary>
        /// Reset the entire test state for a map
        /// </summary>
        public static void ResetTestState(Map map)
        {
            try
            {
                if (map?.reservationManager != null)
                {
                    map.reservationManager.ReleaseAllForTarget(null);
                }

                if (map != null)
                {
                    WeaponCacheManager.ClearAllCaches();
                }

                DroppedItemTracker.ClearAll();
                ForcedWeapons.Cleanup();
                AutoArm.Weapons.WeaponBlacklist.CleanupOldEntries();
                AutoEquipState.Cleanup();

                WeaponCacheManager.ClearAllCaches();
                WeaponCacheManager.ClearScoreCache();

                JobGiver_PickUpBetterWeapon.ResetForTesting();
                JobGiver_PickUpBetterWeapon.CleanupCaches();

                if (map != null)
                {
                    WeaponCacheManager.Initialize(map);
                    WeaponCacheManager.ValidateCacheIntegrity(map);
                }
            }
            catch (Exception e)
            {
                AutoArmLogger.Error("Error resetting test state", e);
                throw;
            }
        }

        /// <summary>
        /// Perform final cleanup after test run
        /// </summary>
        public static void FinalCleanup(Map map)
        {
            try
            {
                if (map?.reservationManager != null)
                {
                    var allThings = map.listerThings?.AllThings;
                    if (allThings != null)
                    {
                        foreach (var thing in allThings)
                        {
                            map.reservationManager.ReleaseAllForTarget(thing);
                        }
                    }

                    var allPawns = map.mapPawns?.AllPawns;
                    if (allPawns != null)
                    {
                        foreach (var pawn in allPawns)
                        {
                            map.reservationManager.ReleaseAllClaimedBy(pawn);
                        }
                    }
                }

                if (map != null)
                {
                    WeaponCacheManager.ClearAllCaches();
                }

                JobGiver_PickUpBetterWeapon.CleanupCaches();
                JobGiver_PickUpBetterWeapon.ResetForTesting();
                WeaponCacheManager.ClearAllCaches();
                WeaponCacheManager.ClearScoreCache();

                Cleanup.PerformFullCleanup();

                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
            catch (Exception e)
            {
                AutoArmLogger.Error("Error during final cleanup", e);
                throw;
            }
        }

        /// <summary>
        /// Log test results in a consistent format
        /// </summary>
        public static void LogTestResults(TestResults results, string suiteName = "Test")
        {
            AutoArmLogger.Debug(() => $"=== {suiteName} Test Results ===");
            AutoArmLogger.Debug(() => $"Total: {results.TotalTests}");
            AutoArmLogger.Debug(() => $"Passed: {results.PassedTests}");
            AutoArmLogger.Debug(() => $"Failed: {results.FailedTests}");
            AutoArmLogger.Debug(() => $"Success Rate: {results.SuccessRate:P0}");

            var failedTests = results.GetFailedTests();
            if (failedTests.Count > 0)
            {
                AutoArmLogger.Debug(() => "Failed tests:");
                foreach (var kvp in failedTests)
                {
                    AutoArmLogger.Debug(() => $"  - {kvp.Key}: {kvp.Value.FailureReason}");
                }
            }
        }
    }
}
