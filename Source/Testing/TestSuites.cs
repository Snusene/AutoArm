using AutoArm.Caching;
using AutoArm.Helpers;
using AutoArm.Jobs;
using AutoArm.Logging;
using AutoArm.Testing.Framework;
using AutoArm.Testing.Scenarios;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Verse;

namespace AutoArm.Testing
{
    public static class TestSuites
    {
        public static TestResults RunCriticalTests(Map map)
        {
            Log.Message("[AutoArm] Running Critical Bug Prevention Tests...");
            var tests = new List<ITestScenario>
            {
                new RaceConditionTest(),
                new WeaponDestructionMidJobTest(),
                new InfiniteLoopTest(),
                new SimpleSidearmsReflectionFixTest(),
                new UnarmedOutfitBypassTest(),
                new MemoryCleanupValidationTest(),
                new ForbiddenWeaponHandlingTest()
            };

            return RunTests(tests, map, "Critical");
        }

        public static TestResults RunGameplayTests(Map map)
        {
            Log.Message("[AutoArm] Running Gameplay Tests...");
            var tests = new List<ITestScenario>
            {
                new MentalBreakTest(),
                new TradingTest(),
                new RaidDetectionTest()
            };

            return RunTests(tests, map, "Gameplay");
        }

        public static TestResults RunCoreTests(Map map)
        {
            Log.Message("[AutoArm] Running Core Tests...");
            var tests = new List<ITestScenario>
            {
                new UnarmedPawnTest(),
                new WeaponUpgradeTest(),
                new DraftedBehaviorTest(),
                new ThinkTreeInjectionTest(),
                new WeaponSwapChainTest(),
                new ProgressiveSearchTest(),
                new NewGameDefaultsTest(),
                new WeaponCachePerformanceTest()
            };

            return RunTests(tests, map, "Core");
        }

        public static TestResults RunCompatibilityTests(Map map)
        {
            Log.Message("[AutoArm] Running Compatibility Tests...");
            var tests = new List<ITestScenario>
            {
                new SimpleSidearmsIntegrationTest(),
                new SimpleSidearmsWeightLimitTest(),
                new SimpleSidearmsSlotLimitTest(),
                new SimpleSidearmsForcedWeaponTest(),
                new TestSimpleSidearmsValidation(),
                new SimpleSidearmsReflectionFixTest(),
                new CombatExtendedAmmoTest()
            };

            return RunTests(tests, map, "Compatibility");
        }

        public static TestResults RunPawnBehaviorTests(Map map)
        {
            Log.Message("[AutoArm] Running Pawn Behavior Tests...");
            var tests = new List<ITestScenario>
            {
                new BrawlerTest(),
                new SkillBasedPreferenceTest(),
                new ChildColonistTest(),
                new NobilityTest(),
                new TemporaryColonistTest(),
                new SlaveTest()
            };

            return RunTests(tests, map, "Pawn Behavior");
        }

        public static TestResults RunSystemTests(Map map)
        {
            Log.Message("[AutoArm] Running System Tests...");
            var tests = new List<ITestScenario>
            {
                new WeaponContainerManagementTest(),
                new WeaponDestructionSafetyTest(),
                new WeaponMaterialHandlingTest(),
                new JobEquipmentTransferTest(),
                new SaveLoadTest(),
                new MapTransitionTest(),
                new EdgeCaseTest()
            };

            return RunTests(tests, map, "System");
        }

        /// <summary>
        /// DEPRECATED: Performance tests removed from main test suite.
        /// Kept for backward compatibility if called directly.
        /// </summary>
        public static TestResults RunPerformanceTests(Map map)
        {
            Log.Warning("[AutoArm] Performance tests are deprecated.");
            var tests = new List<ITestScenario>
            {
                new ProgressiveSearchTest(),
            };

            return RunTests(tests, map, "Performance");
        }

        /// <summary>
        /// DEPRECATED: Stress test removed from main test suite.
        /// </summary>
        public static TestResults RunStressTest(Map map)
        {
            Log.Warning("[AutoArm] Stress test is deprecated.");
            var tests = new List<ITestScenario>();

            return RunTests(tests, map, "Stress");
        }

        public static TestResults RunWeaponTests(Map map)
        {
            Log.Message("[AutoArm] Running Weapon Tests...");
            var tests = new List<ITestScenario>
            {
                new WeaponDropTest(),
                new WeaponBlacklistBasicTest(),
                new WeaponBlacklistExpirationTest(),
                new WeaponBlacklistIntegrationTest(),
                new OutfitFilterTest(),
                new JobPriorityTest(),
                new DropCooldownTest(),
                new OutfitBatchOperationsTest()
            };

            return RunTests(tests, map, "Weapon");
        }

        public static TestResults RunQuickTests(Map map)
        {
            Log.Message("[AutoArm] Running Quick Validation Tests...");
            var tests = new List<ITestScenario>
            {
                new UnarmedPawnTest(),
                new ThinkTreeInjectionTest(),
                new SimpleSidearmsIntegrationTest(),
                new RaidDetectionTest(),
                new ForbiddenWeaponHandlingTest(),
                new SimpleSidearmsWeightLimitTest()
            };

            return RunTests(tests, map, "Quick");
        }

        public static TestResults RunAllTestSuites(Map map)
        {
            Log.Message("[AutoArm] Running All Test Suites...");

            var allResults = new TestResults();
            var stopwatch = Stopwatch.StartNew();

            var suites = new Dictionary<string, Func<Map, TestResults>>
            {
                { "Critical", RunCriticalTests },
                { "Core", RunCoreTests },
                { "Gameplay", RunGameplayTests },
                { "Weapon", RunWeaponTests },
                { "PawnBehavior", RunPawnBehaviorTests },
                { "System", RunSystemTests },
                { "Compatibility", RunCompatibilityTests }
            };

            foreach (var suite in suites)
            {
                try
                {
                    Log.Message($"[AutoArm] Starting {suite.Key} suite...");
                    var suiteResults = suite.Value(map);

                    foreach (var result in suiteResults.GetAllResults())
                    {
                        allResults.AddResult($"{suite.Key}.{result.Key}", result.Value);
                    }
                }
                catch (Exception e)
                {
                    Log.Error($"[AutoArm] {suite.Key} suite failed: {e}");
                    allResults.AddResult($"{suite.Key}.Error", TestResult.Failure($"Suite failed: {e.Message}"));
                }
            }

            stopwatch.Stop();
            Log.Message($"[AutoArm] All test suites completed in {stopwatch.Elapsed.TotalSeconds:F2} seconds");
            LogSuiteResults("All Suites", allResults);

            return allResults;
        }

        private static TestResults RunTests(List<ITestScenario> tests, Map map, string suiteName)
        {
            if (map == null)
            {
                Log.Error($"[AutoArm] Cannot run {suiteName} tests - no map available");
                return new TestResults();
            }

            var results = new TestResults();
            var stopwatch = Stopwatch.StartNew();

            bool originalDebugLogging = AutoArmMod.settings?.debugLogging ?? false;
            bool originalModEnabled = AutoArmMod.settings?.modEnabled ?? true;
            bool originalDevMode = Prefs.DevMode;

            try
            {
                if (AutoArmMod.settings != null)
                {
                    AutoArmMod.settings.debugLogging = true;
                    AutoArmMod.settings.modEnabled = true;
                }

                Prefs.DevMode = true;

                Cleanup.DisableAutoCleanup();

                TestRunner.isRunningTests = true;

                CleanupTracker.Reset();

                int testIndex = 0;
                foreach (var test in tests)
                {
                    testIndex++;

                    var testStopwatch = Stopwatch.StartNew();
                    try
                    {
                        ResetTestState(map);

                        JobGiver_PickUpBetterWeapon.EnableTestMode(true);
                        Prefs.DevMode = true;

                        test.Setup(map);

                        int cachedCount = WeaponCacheManager.GetCacheWeaponCount(map);
                        var onMapList = map.listerThings.ThingsInGroup(ThingRequestGroup.Weapon)?.OfType<ThingWithComps>().ToList()
                                         ?? new System.Collections.Generic.List<ThingWithComps>();
                        if (onMapList.Count > 0 && cachedCount == 0)
                        {
                            foreach (var w in onMapList)
                            {
                                if (w != null && !w.Destroyed && w.Spawned)
                                {
                                    WeaponCacheManager.AddWeaponToCache(w);
                                }
                            }
                        }

                        WeaponCacheManager.ClearScoreCache();
                        WeaponCacheManager.ValidateCacheIntegrity(map);

                        var result = test.Run();
                        results.AddResult(test.Name, result);

                        testStopwatch.Stop();

                        if (!result.Success)
                        {
                            AutoArmLogger.Error($"[{suiteName}] Test FAILED: {test.Name} - {result.FailureReason}");
                        }
                    }
                    catch (Exception e)
                    {
                        testStopwatch.Stop();
                        results.AddResult(test.Name, TestResult.Failure($"Exception in {test.Name}: {e.Message}"));
                        Log.Error($"[AutoArm] Test {test.Name} in suite {suiteName} threw exception:\n{e.StackTrace}");
                        AutoArmLogger.Error($"[{suiteName}] Test {test.Name} exception", e);
                    }
                    finally
                    {
                        try
                        {
                            test.Cleanup();
                        }
                        catch (Exception cleanupEx)
                        {
                            Log.Warning($"[AutoArm] Cleanup failed for test {test.Name}: {cleanupEx.Message}");
                            AutoArmLogger.Error($"[{suiteName}] Cleanup failed for {test.Name}", cleanupEx);
                        }
                    }
                }
            }
            finally
            {
                FinalCleanup(map);

                stopwatch.Stop();

                Cleanup.EnableAutoCleanup();

                TestRunner.isRunningTests = false;
                Prefs.DevMode = originalDevMode;
                if (AutoArmMod.settings != null)
                {
                    AutoArmMod.settings.debugLogging = originalDebugLogging;
                    AutoArmMod.settings.modEnabled = originalModEnabled;
                }
            }
            LogSuiteResults(suiteName, results);
            return results;
        }

        private static void ResetTestState(Map map)
        {
            try
            {
                CleanupTracker.Reset();

                TestCleanupHelper.ResetMapForTesting(map);

                WeaponCacheManager.ClearAllCaches();
                WeaponCacheManager.ClearScoreCache();
                WeaponCacheManager.ClearOutfitCachesOnly();

                DroppedItemTracker.ClearAll();
                ForcedWeapons.Cleanup();
                AutoArm.Weapons.WeaponBlacklist.CleanupOldEntries();
                AutoEquipState.Cleanup();

                PawnValidationCache.ClearCache();

                JobGiver_PickUpBetterWeapon.EnableTestMode(false);
                JobGiver_PickUpBetterWeapon.ResetForTesting();
                JobGiver_PickUpBetterWeapon.CleanupCaches();

                TestRunnerFix.ClearJobGiverPerTickTracking();

                if (map != null)
                {
                    WeaponCacheManager.Initialize(map);
                    WeaponCacheManager.ValidateCacheIntegrity(map);
                }

                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
            catch (Exception e)
            {
                Log.Warning($"[AutoArm] Error resetting test state: {e.Message}");
            }
        }

        private static void FinalCleanup(Map map)
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
                    WeaponCacheManager.MarkCacheAsChanged(map);
                }
                JobGiver_PickUpBetterWeapon.CleanupCaches();
                JobGiver_PickUpBetterWeapon.ResetForTesting();
                WeaponCacheManager.ClearScoreCache();
                Cleanup.PerformFullCleanup();

                if (map?.reachability != null)
                {
                    map.reachability.ClearCache();
                }


                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
            catch (Exception e)
            {
                Log.Warning($"[AutoArm] Error during final cleanup: {e.Message}");
                AutoArmLogger.Error("Final cleanup error", e);
            }
        }

        private static void LogSuiteResults(string suiteName, TestResults results)
        {
            Log.Message($"[AutoArm] === {suiteName} Test Suite Results ===");
            Log.Message($"[AutoArm] Total: {results.TotalTests}");
            Log.Message($"[AutoArm] Passed: {results.PassedTests}");
            Log.Message($"[AutoArm] Failed: {results.FailedTests}");
            Log.Message($"[AutoArm] Success Rate: {results.SuccessRate:P0}");

            var failedTests = results.GetFailedTests();
            if (failedTests.Count > 0)
            {
                Log.Message($"[AutoArm] Failed tests in {suiteName} suite:");
                foreach (var kvp in failedTests.OrderBy(x => x.Key))
                {
                    Log.Message($"[AutoArm]   - {kvp.Key}: {kvp.Value.FailureReason}");
                }
            }

            var allResults = results.GetAllResults();
            var testsWithWarnings = allResults
                .Where(r => r.Value.Success && r.Value.Data.ContainsKey("Warning"))
                .ToList();

            if (testsWithWarnings.Count > 0)
            {
                Log.Message($"[AutoArm] Tests with warnings:");
                foreach (var kvp in testsWithWarnings)
                {
                    Log.Message($"[AutoArm]   - {kvp.Key}: {kvp.Value.Data["Warning"]}");
                }
            }
        }

        /// <summary>
        /// Run tests
        /// Only use if tests are truly independent and don't share state
        /// </summary>
        public static TestResults RunAllTestSuitesParallel(Map map)
        {
            Log.Warning("[AutoArm] Parallel test execution is disabled; running suites sequentially.");
            return RunAllTestSuites(map);
        }
    }
}
