using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;

namespace AutoArm.Testing
{
    public static class TestRunner
    {
        public static TestResults RunAllTests(Map map)
        {
            var results = new TestResults();
            var tests = GetAllTests();

            foreach (var test in tests)
            {
                try
                {
                    test.Setup(map);
                    var result = test.Run();
                    results.AddResult(test.Name, result);
                    test.Cleanup();
                }
                catch (Exception e)
                {
                    results.AddResult(test.Name, TestResult.Failure($"Exception: {e.Message}"));
                    Log.Error($"[AutoArm] Test {test.Name} threw exception: {e}");
                }
            }

            return results;
        }

        public static TestResult RunSingleTest(Map map, ITestScenario test)
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
                Log.Error($"[AutoArm] Test {test.Name} threw exception: {e}");
                return TestResult.Failure($"Exception: {e.Message}");
            }
        }

        public static List<ITestScenario> GetAllTests()
        {
            var tests = new List<ITestScenario>();

            try
            {
                // Core functionality tests
                tests.Add(new UnarmedPawnTest());
                tests.Add(new WeaponUpgradeTest());
                
                // Trait and skill based tests
                tests.Add(new BrawlerTest());
                tests.Add(new HunterTest());
                
                // Policy and restriction tests
                tests.Add(new OutfitFilterTest());
                tests.Add(new ForcedWeaponTest());
                tests.Add(new ChildColonistTest());
                tests.Add(new NobilityTest());
                
                // Mod compatibility tests
                tests.Add(new CombatExtendedAmmoTest());
                tests.Add(new SimpleSidearmsIntegrationTest());
                
                // System tests
                tests.Add(new MapTransitionTest());
                tests.Add(new SaveLoadTest());
                tests.Add(new PerformanceTest());
                tests.Add(new EdgeCaseTest());
            }
            catch (Exception e)
            {
                Log.Error($"[AutoArm] Error creating test list: {e}");
            }

            return tests;
        }

        public static void LogTestResults(TestResults results)
        {
            Log.Message($"[AutoArm] === Test Results ===");
            Log.Message($"[AutoArm] Total: {results.TotalTests}");
            Log.Message($"[AutoArm] Passed: {results.PassedTests}");
            Log.Message($"[AutoArm] Failed: {results.FailedTests}");
            Log.Message($"[AutoArm] Success Rate: {results.SuccessRate:P0}");

            var failedTests = results.GetFailedTests();
            if (failedTests.Any())
            {
                Log.Message($"[AutoArm] Failed tests:");
                foreach (var kvp in failedTests)
                {
                    Log.Message($"[AutoArm]   - {kvp.Key}: {kvp.Value.FailureReason}");
                }
            }
        }
    }
}