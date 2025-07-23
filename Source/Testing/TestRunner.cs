using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;

namespace AutoArm.Testing
{
    public static class TestRunner
    {
        private static bool isRunningTests = false;
        
        public static bool IsRunningTests => isRunningTests;
        
        // Log messages during tests regardless of debug setting
        public static void TestLog(string message)
        {
            if (isRunningTests)
            {
                Log.Message($"[AutoArm TEST] {message}");
            }
        }
        
        public static TestResults RunAllTests(Map map)
        {
            var results = new TestResults();
            var tests = GetAllTests();
            
            // Save current debug logging state and disable it during tests
            bool originalDebugLogging = AutoArmMod.settings?.debugLogging ?? false;
            if (AutoArmMod.settings != null)
                AutoArmMod.settings.debugLogging = false;
            
            isRunningTests = true;

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
                    
                    // Try to cleanup even if test failed
                    try
                    {
                        test.Cleanup();
                    }
                    catch { }
                }
            }
            
            // Final cleanup - clear any cached references that might have been missed
            try
            {
                ImprovedWeaponCacheManager.InvalidateCache(map);
                JobGiver_PickUpBetterWeapon.CleanupCaches();
            }
            catch { }
            
            // Restore debug logging state
            isRunningTests = false;
            if (AutoArmMod.settings != null)
                AutoArmMod.settings.debugLogging = originalDebugLogging;

            return results;
        }

        public static TestResult RunSingleTest(Map map, ITestScenario test)
        {
            // Save current debug logging state and disable it during test
            bool originalDebugLogging = AutoArmMod.settings?.debugLogging ?? false;
            if (AutoArmMod.settings != null)
                AutoArmMod.settings.debugLogging = false;
                
            isRunningTests = true;
            
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
            finally
            {
                // Restore debug logging state
                isRunningTests = false;
                if (AutoArmMod.settings != null)
                    AutoArmMod.settings.debugLogging = originalDebugLogging;
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
                tests.Add(new NobilityTest());
                
                // Mod compatibility tests
                tests.Add(new CombatExtendedAmmoTest());
                tests.Add(new SimpleSidearmsIntegrationTest());
                
                // System tests
                tests.Add(new MapTransitionTest());
                tests.Add(new SaveLoadTest());
                tests.Add(new PerformanceTest());
                tests.Add(new EdgeCaseTest());
                
                // Advanced tests
                tests.Add(new TemporaryColonistTest());
                tests.Add(new StressTest());
                tests.Add(new PrisonerSlaveTest());
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