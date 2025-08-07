using AutoArm.Caching;
using AutoArm.Helpers;
using AutoArm.Jobs;
using AutoArm.Logging;
using AutoArm.Testing.Scenarios;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Verse;

namespace AutoArm.Testing
{
    public static class TestRunner
    {
        public static bool isRunningTests = false;

        public static bool IsRunningTests => isRunningTests;

        // Log messages during tests to AutoArm.log
        public static void TestLog(string message)
        {
            if (isRunningTests)
            {
                // Use Debug to write to AutoArm.log instead of Info which goes to player.log
                AutoArmLogger.Debug($"[TEST] {message}");
            }
        }

        public static TestResults RunAllTests(Map map)
        {
            if (map == null)
            {
                AutoArmLogger.Error("[TEST] No map available for testing");
                return new TestResults();
            }

            var results = new TestResults();
            var tests = GetAllTests();
            var stopwatch = Stopwatch.StartNew();

            // Save current debug logging state
            bool originalDebugLogging = AutoArmMod.settings?.debugLogging ?? false;
            bool originalModEnabled = AutoArmMod.settings?.modEnabled ?? true;

            try
            {
                // Configure for testing - enable debug logging to see test output
                if (AutoArmMod.settings != null)
                {
                    AutoArmMod.settings.debugLogging = true;  // Enable so test logs appear in AutoArm.log
                    AutoArmMod.settings.modEnabled = true;
                }

                isRunningTests = true;
                TestLog($"Starting test run with {tests.Count} tests on map {map.uniqueID}");

                int testIndex = 0;
                foreach (var test in tests)
                {
                    testIndex++;
                    TestLog($"Running test {testIndex}/{tests.Count}: {test.Name}");

                    // Reset state between tests
                    ResetTestState(map);

                    try
                    {
                        test.Setup(map);
                        var result = test.Run();
                        results.AddResult(test.Name, result);
                        
                        if (!result.Success)
                        {
                            TestLog($"Test FAILED: {test.Name} - {result.FailureReason}");
                        }
                    }
                    catch (Exception e)
                    {
                        results.AddResult(test.Name, TestResult.Failure($"Exception: {e.Message}"));
                        AutoArmLogger.Error($"Test {test.Name} threw exception", e);
                    }
                    finally
                    {
                        // Always run cleanup
                        try
                        {
                            test.Cleanup();
                        }
                        catch (Exception cleanupEx)
                        {
                            AutoArmLogger.Error($"Cleanup failed for test {test.Name}", cleanupEx);
                        }
                    }
                }
            }
            finally
            {
                // Restore original settings
                isRunningTests = false;
                if (AutoArmMod.settings != null)
                {
                    AutoArmMod.settings.debugLogging = originalDebugLogging;
                    AutoArmMod.settings.modEnabled = originalModEnabled;
                }

                // Final cleanup
                FinalCleanup(map);

                stopwatch.Stop();
                TestLog($"Test run completed in {stopwatch.ElapsedMilliseconds}ms");
            }

            return results;
        }

        public static TestResult RunSingleTest(Map map, ITestScenario test)
        {
            if (map == null || test == null)
            {
                return TestResult.Failure("Invalid test parameters");
            }

            bool originalDebugLogging = AutoArmMod.settings?.debugLogging ?? false;
            bool originalModEnabled = AutoArmMod.settings?.modEnabled ?? true;

            try
            {
                if (AutoArmMod.settings != null)
                {
                    AutoArmMod.settings.debugLogging = true;  // Enable so test logs appear in AutoArm.log
                    AutoArmMod.settings.modEnabled = true;
                }

                isRunningTests = true;
                TestLog($"Running single test: {test.Name}");

                ResetTestState(map);
                test.Setup(map);
                var result = test.Run();
                test.Cleanup();
                
                return result;
            }
            catch (Exception e)
            {
                AutoArmLogger.Error($"Test {test.Name} threw exception", e);
                return TestResult.Failure($"Exception: {e.Message}");
            }
            finally
            {
                isRunningTests = false;
                if (AutoArmMod.settings != null)
                {
                    AutoArmMod.settings.debugLogging = originalDebugLogging;
                    AutoArmMod.settings.modEnabled = originalModEnabled;
                }
                
                FinalCleanup(map);
            }
        }

        private static void ResetTestState(Map map)
        {
            try
            {
                // Clear timing cooldowns between tests
                TimingHelper.ClearAllCooldowns();

                // Clear weapon cache
                if (map != null)
                {
                    ImprovedWeaponCacheManager.InvalidateCache(map);
                }

                // Clear all tracking systems
                DroppedItemTracker.ClearAll();
                ForcedWeaponHelper.Cleanup();
                AutoArm.Weapons.WeaponBlacklist.CleanupOldEntries();
                WeaponScoreCache.ClearAllCaches();
            }
            catch (Exception e)
            {
                AutoArmLogger.Error("Error resetting test state", e);
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
            }
            catch (Exception e)
            {
                AutoArmLogger.Error("Error during final cleanup", e);
            }
        }

        public static List<ITestScenario> GetAllTests()
        {
            var tests = new List<ITestScenario>();

            try
            {
                // HIGH PRIORITY - Critical bug prevention tests
                tests.Add(new RaceConditionTest());
                tests.Add(new WeaponDestructionMidJobTest());
                // tests.Add(new NullSafetyTest()); // REMOVED - Can't test
                tests.Add(new InfiniteLoopTest());

                // MEDIUM PRIORITY - Common gameplay issues
                tests.Add(new MentalBreakTest());
                tests.Add(new TradingTest());
                tests.Add(new SettingsChangeTest());
                tests.Add(new RaidDetectionTest());

                // Core functionality tests - USING FIXED VERSIONS
                tests.Add(new TestFixes.UnarmedPawnTestFixed());  // Fixed version
                tests.Add(new TestFixes.WeaponUpgradeTestFixed());  // Fixed version
                tests.Add(new DraftedBehaviorTest());
                tests.Add(new WeaponDropTest());
                tests.Add(new ThinkTreeInjectionTest());
                tests.Add(new CooldownSystemTest());
                tests.Add(new WeaponSwapChainTest());
                tests.Add(new ProgressiveSearchTest());

                // Preference tests
                tests.Add(new BrawlerTest());
                tests.Add(new HunterTest());
                tests.Add(new SkillBasedPreferenceTest());

                // Policy and restriction tests - USING FIXED VERSIONS
                tests.Add(new TestFixes.OutfitFilterTestFixed());  // Fixed version
                // tests.Add(new TestFixes.ForcedWeaponTestFixed());  // REMOVED - Can't test
                tests.Add(new JobPriorityTest());

                // DLC and age tests
                tests.Add(new ChildColonistTest());
                tests.Add(new NobilityTest());

                // Mod compatibility tests
                tests.Add(new CombatExtendedAmmoTest());
                tests.Add(new SimpleSidearmsIntegrationTest());
                tests.Add(new SimpleSidearmsWeightLimitTest());
                tests.Add(new SimpleSidearmsSlotLimitTest());
                tests.Add(new SimpleSidearmsForcedWeaponTest());

                // Weapon blacklist tests
                tests.Add(new WeaponBlacklistBasicTest());
                tests.Add(new WeaponBlacklistExpirationTest());
                tests.Add(new WeaponBlacklistIntegrationTest());

                // Cache system tests
                tests.Add(new WeaponCacheSpatialIndexTest());

                // System tests - USING FIXED VERSION FOR SAVE/LOAD
                tests.Add(new MapTransitionTest());
                // tests.Add(new TestFixes.SaveLoadTestFixed());  // REMOVED - Can't test
                tests.Add(new PerformanceTest());
                tests.Add(new EdgeCaseTest());

                // Advanced tests
                tests.Add(new TemporaryColonistTest());
                tests.Add(new StressTest());
                tests.Add(new SlaveTest());

                // System and container tests
                tests.Add(new WeaponContainerManagementTest());
                tests.Add(new WeaponDestructionSafetyTest());
                tests.Add(new WeaponMaterialHandlingTest());
                tests.Add(new JobEquipmentTransferTest());
            }
            catch (Exception e)
            {
                AutoArmLogger.Error("Error creating test list", e);
            }

            return tests;
        }

        public static void LogTestResults(TestResults results)
        {
            // Use Debug to write to AutoArm.log
            AutoArmLogger.Debug("=== Test Results ===");
            AutoArmLogger.Debug($"Total: {results.TotalTests}");
            AutoArmLogger.Debug($"Passed: {results.PassedTests}");
            AutoArmLogger.Debug($"Failed: {results.FailedTests}");
            AutoArmLogger.Debug($"Success Rate: {results.SuccessRate:P0}");

            var failedTests = results.GetFailedTests();
            if (failedTests.Count > 0)
            {
                AutoArmLogger.Debug("Failed tests:");
                foreach (var kvp in failedTests)
                {
                    AutoArmLogger.Debug($"  - {kvp.Key}: {kvp.Value.FailureReason}");
                }
            }
        }
    }
}
