// AutoArm RimWorld 1.5+ mod - automatic weapon management
// This file: Main test runner and test suite coordinator
// Runs all tests and manages test execution

using AutoArm.Testing.Scenarios;
using AutoArm.Testing.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using AutoArm.Caching; using AutoArm.Logging;
using AutoArm.Jobs;

namespace AutoArm.Testing
{
    public static class TestRunner
    {
        public static bool isRunningTests = false;

        public static bool IsRunningTests => isRunningTests;

        // Log messages during tests regardless of debug setting
        public static void TestLog(string message)
        {
            if (isRunningTests)
            {
                AutoArmLogger.Debug($"[TEST] {message}");
            }
        }

        public static TestResults RunAllTests(Map map)
        {
            var results = new TestResults();
            var tests = GetAllTests();

            // Use the new TestModEnabler to ensure mod is enabled
            using (var modEnabler = TestModEnabler.ForceEnableForTesting())
            {
                isRunningTests = true;
                
                // Use test execution context to suppress expected warnings
                using (var testContext = new TestExecutionContext())
                {
                    foreach (var test in tests)
                    {
                        try
                        {
                            // Clear destroyed tracking between tests
                            SafeTestCleanup.ClearDestroyedTracking();
                            
                            // Force garbage collection before test to clean up any lingering references
                            GC.Collect();
                            GC.WaitForPendingFinalizers();
                            
                            test.Setup(map);
                            var result = test.Run();
                            results.AddResult(test.Name, result);
                            test.Cleanup();
                            
                            // Clear tracking again after cleanup
                            SafeTestCleanup.ClearDestroyedTracking();
                        }
                        catch (Exception e)
                        {
                            results.AddResult(test.Name, TestResult.Failure($"Exception: {e.Message}"));
                            AutoArmLogger.Error($"Test {test.Name} threw exception: {e.Message}", e);

                            // Try to cleanup even if test failed
                            try
                            {
                                test.Cleanup();
                            }
                            catch { }
                        }
                    }
                }

                // Final cleanup - clear any cached references that might have been missed
                try
                {
                    ImprovedWeaponCacheManager.InvalidateCache(map);
                    JobGiver_PickUpBetterWeapon.CleanupCaches();
                }
                catch { }

                isRunningTests = false;
            } // modEnabler.Dispose() will restore original settings

            return results;
        }

        public static TestResult RunSingleTest(Map map, ITestScenario test)
        {
            // Use the new TestModEnabler to ensure mod is enabled
            using (var modEnabler = TestModEnabler.ForceEnableForTesting())
            {
                isRunningTests = true;

                // Use test execution context to suppress expected warnings
                using (var testContext = new TestExecutionContext())
                {
                    try
                    {
                        test.Setup(map);
                        var result = test.Run();
                        test.Cleanup();
                        return result;
                    }
                    catch (Exception e)
                    {
                        AutoArmLogger.Error($"Test {test.Name} threw exception: {e.Message}", e);
                        return TestResult.Failure($"Exception: {e.Message}");
                    }
                    finally
                    {
                        isRunningTests = false;
                    }
                }
            } // modEnabler.Dispose() will restore original settings
        }

        public static List<ITestScenario> GetAllTests()
        {
            var tests = new List<ITestScenario>();

            try
            {
                // Core functionality tests
                tests.Add(new UnarmedPawnTest());
                tests.Add(new WeaponUpgradeTest());
                tests.Add(new DraftedBehaviorTest());
                tests.Add(new WeaponDropTest());

                // Preference tests
                tests.Add(new BrawlerTest());
                tests.Add(new HunterTest());
                tests.Add(new SkillBasedPreferenceTest());

                // Policy and restriction tests
                tests.Add(new OutfitFilterTest());
                tests.Add(new ForcedWeaponTest());
                tests.Add(new JobPriorityTest());

                // DLC and age tests
                tests.Add(new ChildColonistTest());

                // Mod compatibility tests
                tests.Add(new CombatExtendedAmmoTest());
                tests.Add(new SimpleSidearmsIntegrationTest());

                // SimpleSidearms advanced tests
                tests.Add(new SimpleSidearmsWeightLimitTest());
                tests.Add(new SimpleSidearmsSlotLimitTest());
                tests.Add(new SimpleSidearmsForcedWeaponTest());

                // Pick Up and Haul + SimpleSidearms integration tests
                tests.Add(new PickUpAndHaulWeaponHoardingTest());
                tests.Add(new PickUpAndHaulWeaponReplacementTest());
                tests.Add(new ComprehensiveWeaponHoardingFixTest());
                tests.Add(new PacifistWeaponHaulingTest());
                tests.Add(new ForcedWeaponPickUpAndHaulProtectionTest());

                // Pacifist behavior tests
                tests.Add(new PacifistBaseGameTest());
                tests.Add(new PacifistSimpleSidearmsTest());
                tests.Add(new PacifistFullModStackTest());
                
                // Weapon blacklist tests
                tests.Add(new WeaponBlacklistBasicTest());
                tests.Add(new WeaponBlacklistExpirationTest());
                tests.Add(new WeaponBlacklistIntegrationTest());

                // Cache system tests
                tests.Add(new WeaponCacheSpatialIndexTest());

                // Settings tests
                tests.Add(new NotificationSettingTest());
                tests.Add(new ForcedWeaponQualityUpgradeTest());
                tests.Add(new WeaponUpgradeThresholdTest());
                tests.Add(new DisableDuringRaidsTest());
                tests.Add(new RespectWeaponBondsTest());

                // System tests
                tests.Add(new MapTransitionTest());
                tests.Add(new SaveLoadTest());
                tests.Add(new PerformanceTest());
                tests.Add(new EdgeCaseTest());

                // Advanced tests
                tests.Add(new TemporaryColonistTest());
                tests.Add(new StressTest());
                tests.Add(new PrisonerSlaveTest());

                // System and container tests
                tests.Add(new WeaponContainerManagementTest());
                tests.Add(new WeaponDestructionSafetyTest());
                tests.Add(new WeaponMaterialHandlingTest());
                tests.Add(new JobEquipmentTransferTest());

                // Core functionality tests
                tests.Add(new CooldownSystemTest());
                tests.Add(new ThinkTreeInjectionTest());
                tests.Add(new WeaponSwapChainTest());
                tests.Add(new WorkInterruptionTest());
                tests.Add(new ProgressiveSearchTest());

                // Weapon scoring tests (matching web analyzer)
                tests.Add(new WeaponScoringSystemTest());
                tests.Add(new WeaponCachePerformanceTest());
                tests.Add(new TraitAndRoleScoringTest());

                // Diagnostic test
                tests.Add(new DiagnosticTest());
                tests.Add(new WeaponScoringDebugTest());

                // Note: Additional test scenarios can be found in TestScenarios_Fixed.cs
                // Add them here as needed
            }
            catch (Exception e)
            {
                AutoArmLogger.Error($"Error creating test list: {e.Message}", e);
            }

            return tests;
        }

        public static void LogTestResults(TestResults results)
        {
            AutoArmLogger.Debug($"=== Test Results ===");
            AutoArmLogger.Debug($"Total: {results.TotalTests}");
            AutoArmLogger.Debug($"Passed: {results.PassedTests}");
            AutoArmLogger.Debug($"Failed: {results.FailedTests}");
            AutoArmLogger.Debug($"Success Rate: {results.SuccessRate:P0}");

            var failedTests = results.GetFailedTests();
            if (failedTests.Any())
            {
                AutoArmLogger.Debug($"Failed tests:");
                foreach (var kvp in failedTests)
                {
                    AutoArmLogger.Debug($"  - {kvp.Key}: {kvp.Value.FailureReason}");
                }
            }
        }
    }
}
