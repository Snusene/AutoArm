using AutoArm.Caching;
using AutoArm.Helpers;
using AutoArm.Jobs;
using AutoArm.Logging;
using AutoArm.Testing.Framework;
using AutoArm.Testing.Scenarios;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Verse;

namespace AutoArm.Testing
{
    public static class TestRunner
    {
        public static bool IsRunningTests { get; internal set; } = false;

        public static bool isRunningTests
        {
            get { return IsRunningTests; }
            set { IsRunningTests = value; }
        }

        public static void TestLog(string message)
        {
            if (isRunningTests)
            {
                AutoArmLogger.Debug(() => $"[TEST] {message}");
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

            bool originalDebugLogging = AutoArmMod.settings?.debugLogging ?? false;
            bool originalModEnabled = AutoArmMod.settings?.modEnabled ?? true;
            float originalWeaponPref = AutoArmMod.settings?.weaponTypePreference ?? 0f;
            bool originalDevMode = Prefs.DevMode;

            try
            {
                if (AutoArmMod.settings != null)
                {
                    AutoArmMod.settings.debugLogging = true;
                    AutoArmMod.settings.modEnabled = true;
                    AutoArmMod.settings.weaponTypePreference = 0f;
                }
                IsRunningTests = true;
                Prefs.DevMode = true;

                Cleanup.DisableAutoCleanup();


                TestLog($"=== Test Results ===");
                TestLog($"Starting test run with {tests.Count} tests");

                CleanupTracker.Reset();

                if (map != null)
                {
                    WeaponCacheManager.Initialize(map);
                }

                int testIndex = 0;
                foreach (var test in tests)
                {
                    testIndex++;

                    ResetTestState(map);

                    try
                    {
                        JobGiver_PickUpBetterWeapon.EnableTestMode(true);
                        Prefs.DevMode = true;

                        test.Setup(map);

                        PostTestSetupCacheRebuild(map, test);

                        var result = test.Run();
                        results.AddResult(test.Name, result);

                        if (!result.Success)
                        {
                            TestLog($"[FAILED] {test.Name}: {result.FailureReason}");
                        }
                    }
                    catch (Exception e)
                    {
                        results.AddResult(test.Name, TestResult.Failure($"Exception: {e.Message}"));
                        TestLog($"[EXCEPTION] {test.Name}: {e.Message}");
                    }
                    finally
                    {
                        try
                        {
                            test.Cleanup();
                        }
                        catch (Exception cleanupEx)
                        {
                            TestLog($"[CLEANUP ERROR] {test.Name}: {cleanupEx.Message}");
                        }
                    }
                }
            }
            finally
            {
                FinalCleanup(map);

                System.GC.Collect();
                System.GC.WaitForPendingFinalizers();

                stopwatch.Stop();
                LogTestResults(results);
                TestLog($"Completed in {stopwatch.ElapsedMilliseconds}ms");

                Cleanup.EnableAutoCleanup();

                IsRunningTests = false;
                Prefs.DevMode = originalDevMode;
                if (AutoArmMod.settings != null)
                {
                    AutoArmMod.settings.debugLogging = originalDebugLogging;
                    AutoArmMod.settings.modEnabled = originalModEnabled;
                    AutoArmMod.settings.weaponTypePreference = originalWeaponPref;
                }
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
            float originalWeaponPref = AutoArmMod.settings?.weaponTypePreference ?? 0f;
            bool originalDevMode = Prefs.DevMode;

            try
            {
                if (AutoArmMod.settings != null)
                {
                    AutoArmMod.settings.debugLogging = true;
                    AutoArmMod.settings.modEnabled = true;
                    AutoArmMod.settings.weaponTypePreference = 0f;
                }

                IsRunningTests = true;
                Prefs.DevMode = true;

                Cleanup.DisableAutoCleanup();

                TestLog($"Running single test: {test.Name}");

                CleanupTracker.Reset();

                ResetTestState(map);
                try
                {
                    test.Setup(map);

                    PostTestSetupCacheRebuild(map, test);

                    var result = test.Run();
                    return result;
                }
                finally
                {
                    try
                    {
                        test.Cleanup();
                    }
                    catch (Exception cleanupEx)
                    {
                        AutoArmLogger.Error($"Cleanup failed for test {test?.Name}", cleanupEx);
                    }
                }
            }
            catch (Exception e)
            {
                AutoArmLogger.Error($"Test {test.Name} threw exception", e);
                return TestResult.Failure($"Exception: {e.Message}");
            }
            finally
            {
                FinalCleanup(map);

                Cleanup.EnableAutoCleanup();

                IsRunningTests = false;
                Prefs.DevMode = originalDevMode;
                if (AutoArmMod.settings != null)
                {
                    AutoArmMod.settings.debugLogging = originalDebugLogging;
                    AutoArmMod.settings.modEnabled = originalModEnabled;
                    AutoArmMod.settings.weaponTypePreference = originalWeaponPref;
                }
            }
        }

        private static void ResetTestState(Map map)
        {
            try
            {
                CleanupTracker.Reset();

                AutoArm.Testing.Framework.TestCleanupHelper.ResetMapForTesting(map);

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


                TestLog("Reset test state - complete map reset, all caches cleared, fresh state established");
            }
            catch (Exception e)
            {
                AutoArmLogger.Error("Error resetting test state", e);
            }
        }


        private static void PostTestSetupCacheRebuild(Map map, ITestScenario test)
        {
            if (map == null) return;

            try
            {
                int cachedCount = WeaponCacheManager.GetCacheWeaponCount(map);
                var onMapList = map.listerThings.ThingsInGroup(ThingRequestGroup.Weapon)?.OfType<ThingWithComps>().ToList()
                                 ?? new System.Collections.Generic.List<ThingWithComps>();
                int onMapCount = onMapList.Count;

                if (onMapCount > 0 && cachedCount == 0)
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

                int finalCount = WeaponCacheManager.GetCacheWeaponCount(map);
                TestLog($"{test.Name} setup cache state: onMap={onMapCount}, cached={finalCount}");
            }
            catch (Exception e)
            {
                AutoArmLogger.Error($"Error in post-test setup cache rebuild for {test.Name}", e);
            }
        }


        private static void ValidateCacheIntegrity(Map map)
        {
            WeaponCacheManager.ValidateCacheIntegrity(map);
        }

        private static void FinalCleanup(Map map)
        {
            try
            {
                if (map != null && map.reservationManager != null)
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

                JobGiver_PickUpBetterWeapon.CleanupCaches();
                JobGiver_PickUpBetterWeapon.ResetForTesting();
                WeaponCacheManager.ClearScoreCache();

                Cleanup.PerformFullCleanup();
            }
            catch (Exception e)
            {
                AutoArmLogger.Error("Error during final cleanup", e);
            }
        }

        public static void RunDiagnostics()
        {
            if (!Game_FinalizeInit_InjectThinkTree_Patch.ValidateThinkTreeInjection(logWarning: true))
            {
                Messages.Message("AutoArm_ThinkTreeNotInjected".Translate(), MessageTypeDefOf.NegativeEvent, false);
                AutoArmLogger.Debug(() => "[DIAG] Think tree not injected!");
                return;
            }
            else
            {
                Messages.Message("AutoArm_ThinkTreeInjected".Translate(), MessageTypeDefOf.TaskCompletion, false);
            }

            if (Find.CurrentMap != null)
            {
                var colonists = Find.CurrentMap.mapPawns.FreeColonists;
                int unarmedCount = colonists.Count(p => p.equipment?.Primary == null);
                int armedCount = colonists.Count(p => p.equipment?.Primary != null);

                Messages.Message("AutoArm_ColonistStatus".Translate(colonists.Count(), unarmedCount, armedCount),
                    MessageTypeDefOf.TaskCompletion, false);

                if (AutoArmMod.settings?.modEnabled == true)
                {
                    AutoArmLogger.Debug(() => $"[DIAG] Mod enabled, debug={AutoArmMod.settings.debugLogging}");
                }
                else
                {
                    Messages.Message("AutoArm_ModDisabled".Translate(), MessageTypeDefOf.RejectInput, false);
                }
            }
        }

        public static List<ITestScenario> GetAllTests()
        {
            var tests = new List<ITestScenario>();

            try
            {
                tests.Add(new RaceConditionTest());
                tests.Add(new WeaponDestructionMidJobTest());
                tests.Add(new InfiniteLoopTest());
                tests.Add(new MemoryCleanupValidationTest());
                tests.Add(new ForbiddenWeaponHandlingTest());
                tests.Add(new SimpleSidearmsReflectionFixTest());

                tests.Add(new MentalBreakTest());
                tests.Add(new TradingTest());
                tests.Add(new RaidDetectionTest());

                tests.Add(new UnarmedPawnTest());
                tests.Add(new WeaponUpgradeTest());
                tests.Add(new DraftedBehaviorTest());
                tests.Add(new WeaponDropTest());
                tests.Add(new ThinkTreeInjectionTest());
                tests.Add(new WeaponSwapChainTest());
                tests.Add(new ProgressiveSearchTest());

                tests.Add(new BrawlerTest());
                tests.Add(new SkillBasedPreferenceTest());

                tests.Add(new OutfitFilterTest());
                tests.Add(new JobPriorityTest());

                tests.Add(new ChildColonistTest());
                tests.Add(new NobilityTest());

                tests.Add(new CombatExtendedAmmoTest());
                tests.Add(new SimpleSidearmsIntegrationTest());
                tests.Add(new SimpleSidearmsWeightLimitTest());
                tests.Add(new SimpleSidearmsSlotLimitTest());
                tests.Add(new SimpleSidearmsForcedWeaponTest());
                tests.Add(new TestSimpleSidearmsValidation());

                tests.Add(new WeaponBlacklistBasicTest());
                tests.Add(new WeaponBlacklistExpirationTest());
                tests.Add(new WeaponBlacklistIntegrationTest());

                tests.Add(new MapTransitionTest());
                tests.Add(new SaveLoadTest());
                tests.Add(new EdgeCaseTest());

                tests.Add(new TemporaryColonistTest());
                tests.Add(new SlaveTest());

                tests.Add(new WeaponContainerManagementTest());
                tests.Add(new WeaponDestructionSafetyTest());
                tests.Add(new WeaponMaterialHandlingTest());
                tests.Add(new JobEquipmentTransferTest());

                tests.Add(new PersonaWeaponTest());
                tests.Add(new GrenadeHandlingTest());
                tests.Add(new CaravanTest());
                tests.Add(new IncapacitationTest());
                tests.Add(new UIIntegrationTest());
            }
            catch (Exception e)
            {
                AutoArmLogger.Error("Error creating test list", e);
            }

            return tests;
        }

        public static void LogTestResults(TestResults results)
        {
            TestLog("=== Test Results ===");
            TestLog($"Passed: {results.PassedTests}/{results.TotalTests} ({results.SuccessRate:P0})");

            var failedTests = results.GetFailedTests();
            if (failedTests.Count > 0)
            {
                TestLog($"Failed: {results.FailedTests} tests");
                foreach (var kvp in failedTests)
                {
                    TestLog($"  ✗ {kvp.Key}: {kvp.Value.FailureReason}");
                }
            }
            else if (results.TotalTests > 0)
            {
                TestLog("All tests passed!");
            }
        }
    }
}
