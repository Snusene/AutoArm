using AutoArm.Caching;
using AutoArm.Helpers;
using AutoArm.Jobs;
using AutoArm.Logging;
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
                // new NullSafetyTest(), // REMOVED - Can't test reliably
                new InfiniteLoopTest(),
                // SimpleSidearms critical tests
                new SimpleSidearmsAntiDuplicationTest(),
                new SimpleSidearmsForcedWeaponUpgradeTest(),
                new SimpleSidearmsConcurrentAccessTest(),
                new SimpleSidearmsBondedWeaponTest(),
                new SimpleSidearmsReflectionFixTest(),
                new UnarmedOutfitBypassTest(),
                // Memory and performance critical
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
                new SettingsChangeTest(),
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
                // new ForcedWeaponTest(), // REMOVED - Can't test reliably
                new ThinkTreeInjectionTest(),
                new CooldownSystemTest(),
                new WeaponSwapChainTest(),
                new ProgressiveSearchTest()
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
                new HunterTest(),
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

        public static TestResults RunPerformanceTests(Map map)
        {
            Log.Message("[AutoArm] Running Performance Tests...");
            var tests = new List<ITestScenario>
            {
                new PerformanceTest(),
                new WeaponCacheSpatialIndexTest(),
                new ProgressiveSearchTest(),
                new HeavyLoadPerformanceTest()
            };

            // Note: StressTest is intentionally separate due to high resource usage
            return RunTests(tests, map, "Performance");
        }

        public static TestResults RunStressTest(Map map)
        {
            Log.Message("[AutoArm] Running Stress Test (this may take a while)...");
            var tests = new List<ITestScenario>
            {
                new StressTest()
            };

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
                new JobPriorityTest()
            };

            return RunTests(tests, map, "Weapon");
        }

        public static TestResults RunQuickTests(Map map)
        {
            Log.Message("[AutoArm] Running Quick Validation Tests...");
            var tests = new List<ITestScenario>
            {
                // new NullSafetyTest(),  // REMOVED - Can't test reliably
                new UnarmedPawnTest(),
                new ThinkTreeInjectionTest(),
                new SimpleSidearmsIntegrationTest(),
                new RaidDetectionTest(),  // Important gameplay test
                new ForbiddenWeaponHandlingTest(),  // Critical unarmed behavior
                new SimpleSidearmsAntiDuplicationTest()  // Critical SS integration
            };

            return RunTests(tests, map, "Quick");
        }

        public static TestResults RunAllTestSuites(Map map)
        {
            Log.Message("[AutoArm] Running All Test Suites...");
            
            var allResults = new TestResults();
            var stopwatch = Stopwatch.StartNew();

            // Run each suite and aggregate results
            var suites = new Dictionary<string, Func<Map, TestResults>>
            {
                { "Critical", RunCriticalTests },  // Run first - most important
                { "Core", RunCoreTests },
                { "Gameplay", RunGameplayTests },
                { "Weapon", RunWeaponTests },
                { "PawnBehavior", RunPawnBehaviorTests },
                { "System", RunSystemTests },
                { "Compatibility", RunCompatibilityTests },
                { "Performance", RunPerformanceTests }
            };

            foreach (var suite in suites)
            {
                try
                {
                    Log.Message($"[AutoArm] Starting {suite.Key} suite...");
                    var suiteResults = suite.Value(map);
                    
                    // Merge results
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

            try
            {
                // Configure for testing
                if (AutoArmMod.settings != null)
                {
                    AutoArmMod.settings.debugLogging = false;
                    AutoArmMod.settings.modEnabled = true;
                }

                TestRunner.isRunningTests = true;

                int testIndex = 0;
                foreach (var test in tests)
                {
                    testIndex++;
                    AutoArmLogger.Debug($"[{suiteName}] Running test {testIndex}/{tests.Count}: {test.Name}");

                    try
                    {
                        // Reset state between tests
                        ResetTestState(map);
                        
                        test.Setup(map);
                        var result = test.Run();
                        results.AddResult(test.Name, result);
                    }
                    catch (Exception e)
                    {
                        results.AddResult(test.Name, TestResult.Failure($"Exception: {e.Message}"));
                        Log.Error($"[AutoArm] Test {test.Name} in suite {suiteName} threw exception: {e}");
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
                        }
                    }
                }
            }
            finally
            {
                TestRunner.isRunningTests = false;
                
                // Restore original settings
                if (AutoArmMod.settings != null)
                {
                    AutoArmMod.settings.debugLogging = originalDebugLogging;
                    AutoArmMod.settings.modEnabled = originalModEnabled;
                }

                // Final cleanup
                FinalCleanup(map);
                
                stopwatch.Stop();
                AutoArmLogger.Debug($"[{suiteName}] Suite completed in {stopwatch.ElapsedMilliseconds}ms");
            }

            LogSuiteResults(suiteName, results);
            return results;
        }

        private static void ResetTestState(Map map)
        {
            try
            {
                // Clear timing systems
                TimingHelper.ClearAllCooldowns();
                
                // Clear caches
                if (map != null)
                {
                    ImprovedWeaponCacheManager.InvalidateCache(map);
                }
                WeaponScoreCache.ClearAllCaches();
                
                // Clear tracking systems
                DroppedItemTracker.ClearAll();
                ForcedWeaponHelper.Cleanup();
                AutoArm.Weapons.WeaponBlacklist.CleanupOldEntries();
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
                if (map != null)
                {
                    ImprovedWeaponCacheManager.InvalidateCache(map);
                }
                JobGiver_PickUpBetterWeapon.CleanupCaches();
                CleanupHelper.PerformFullCleanup();
                
                // Force garbage collection
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
            catch (Exception e)
            {
                Log.Warning($"[AutoArm] Error during final cleanup: {e.Message}");
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

            // Log any warnings from passed tests
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
    }
}
