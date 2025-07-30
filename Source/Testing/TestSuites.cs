using AutoArm.Testing.Scenarios;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace AutoArm.Testing
{
    public static class TestSuites
    {
        public static TestResults RunCoreTests(Map map)
        {
            Log.Message("[AutoArm] Running Core Tests...");
            var tests = new List<ITestScenario>
            {
                new UnarmedPawnTest(),
                new WeaponUpgradeTest(),
                new DraftedBehaviorTest(),
                new ForcedWeaponTest(),
                new ThinkTreeInjectionTest(),
                new CooldownSystemTest(),
                new WeaponSwapChainTest()
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
                new PrisonerSlaveTest()
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
                new StressTest(),
                new WeaponCacheSpatialIndexTest(),
                new ProgressiveSearchTest()
            };

            return RunTests(tests, map, "Performance");
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
                new WorkInterruptionTest()
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
                new WeaponContainerManagementTest(),
                new CooldownSystemTest()
            };

            return RunTests(tests, map, "Quick");
        }

        private static TestResults RunTests(List<ITestScenario> tests, Map map, string suiteName)
        {
            var results = new TestResults();
            bool originalDebugLogging = AutoArmMod.settings?.debugLogging ?? false;

            try
            {
                if (AutoArmMod.settings != null)
                    AutoArmMod.settings.debugLogging = false;

                TestRunner.isRunningTests = true;

                foreach (var test in tests)
                {
                    try
                    {
                        test.Setup(map);
                        var result = test.Run();
                        results.AddResult(test.Name, result);
                        test.Cleanup();
                    }
                    catch (System.Exception e)
                    {
                        results.AddResult(test.Name, TestResult.Failure($"Exception: {e.Message}"));
                        Log.Error($"[AutoArm] Test {test.Name} in suite {suiteName} threw exception: {e}");

                        try
                        {
                            test.Cleanup();
                        }
                        catch { }
                    }
                }
            }
            finally
            {
                TestRunner.isRunningTests = false;
                if (AutoArmMod.settings != null)
                    AutoArmMod.settings.debugLogging = originalDebugLogging;

                // Clean up caches
                try
                {
                    ImprovedWeaponCacheManager.InvalidateCache(map);
                    JobGiver_PickUpBetterWeapon.CleanupCaches();
                }
                catch { }
            }

            LogSuiteResults(suiteName, results);
            return results;
        }

        private static void LogSuiteResults(string suiteName, TestResults results)
        {
            Log.Message($"[AutoArm] === {suiteName} Test Suite Results ===");
            Log.Message($"[AutoArm] Total: {results.TotalTests}");
            Log.Message($"[AutoArm] Passed: {results.PassedTests}");
            Log.Message($"[AutoArm] Failed: {results.FailedTests}");
            Log.Message($"[AutoArm] Success Rate: {results.SuccessRate:P0}");

            var failedTests = results.GetFailedTests();
            if (failedTests.Any())
            {
                Log.Message($"[AutoArm] Failed tests in {suiteName} suite:");
                foreach (var kvp in failedTests)
                {
                    Log.Message($"[AutoArm]   - {kvp.Key}: {kvp.Value.FailureReason}");
                }
            }
        }
    }
}