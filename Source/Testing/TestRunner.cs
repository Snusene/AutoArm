using AutoArm.Caching;
using AutoArm.Helpers;
using AutoArm.Jobs;
using AutoArm.Testing.Helpers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Verse;

namespace AutoArm.Testing
{
    internal static class TestRunner
    {
        public static bool IsRunningTests { get; internal set; } = false;

        public static void TestLog(string message)
        {
            if (IsRunningTests)
                AutoArmLogger.Debug(() => $"[TEST] {message}");
        }

        public static TestResults RunAllTests(Map map)
        {
            if (map == null)
            {
                AutoArmLogger.Warn("[TEST] No map available for testing");
                return new TestResults();
            }

            var results = new TestResults();
            var tests = GetAllTests();
            var stopwatch = Stopwatch.StartNew();

            RunInSession(map, () =>
            {
                foreach (var test in tests)
                {
                    var testStopwatch = Stopwatch.StartNew();
                    var result = ExecuteScenario(map, test);
                    testStopwatch.Stop();

                    results.AddResult(test.Name, result);
                    results.AddTiming(test.Name, testStopwatch.Elapsed);
                }
            });

            stopwatch.Stop();
            LogSummary(results, stopwatch.Elapsed);
            return results;
        }

        public static TestResult RunSingleTest(Map map, ITestScenario test)
        {
            if (map == null || test == null)
                return TestResult.Failure("Invalid test parameters");

            TestResult result = null;
            RunInSession(map, () =>
            {
                result = ExecuteScenario(map, test);
            });
            return result ?? TestResult.Failure("Session did not run");
        }

        private static void RunInSession(Map map, Action body)
        {
            var settingsSnapshot = AutoArmMod.settings?.Clone();
            bool originalDevMode = Prefs.DevMode;

            try
            {
                if (AutoArmMod.settings != null)
                {
                    AutoArmMod.settings.debugLogging = true;
                    AutoArmMod.settings.modEnabled = true;
                    AutoArmMod.settings.weaponTypePreference = 0f;
                    AutoArmMod.settings.checkCEAmmo = false;
                }
                IsRunningTests = true;
                Prefs.DevMode = true;
                Cleanup.DisableAutoCleanup();
                CleanupTracker.Reset();

                if (map != null)
                    WeaponCache.Initialize(map);

                body();
            }
            finally
            {
                FinalCleanup(map);
                System.GC.Collect();
                System.GC.WaitForPendingFinalizers();
                Cleanup.EnableAutoCleanup();

                IsRunningTests = false;
                Prefs.DevMode = originalDevMode;
                if (AutoArmMod.settings != null && settingsSnapshot != null)
                    AutoArmMod.settings.CopyFrom(settingsSnapshot);
            }
        }

        private static TestResult ExecuteScenario(Map map, ITestScenario test)
        {
            ResetTestState(map);

            try
            {
                JobGiver_PickUpBetterWeapon.EnableTestMode(true);
                Prefs.DevMode = true;

                test.Setup(map);
                PostTestSetupCacheRebuild(map, test);

                return test.Run();
            }
            catch (Exception e)
            {
                return TestResult.Failure($"Exception: {e.Message}");
            }
            finally
            {
                try { test.Cleanup(); }
                catch (Exception cleanupEx) { TestLog($"cleanup error in {test.Name}: {cleanupEx.Message}"); }
            }
        }

        private static void ResetTestState(Map map)
        {
            try
            {
                CleanupTracker.Reset();
                CleanupHelper.ResetMapForTesting(map);

                WeaponCache.ClearScoreCache();
                DroppedItems.ClearAll();
                ForcedWeapons.Cleanup();
                Blacklist.CleanupOldEntries();
                AutoEquipState.Cleanup();
                PawnValidation.ClearCache();

                JobGiver_PickUpBetterWeapon.EnableTestMode(false);
                JobGiver_PickUpBetterWeapon.ResetForTesting();
                JobGiver_PickUpBetterWeapon.CleanupCaches();
            }
            catch (Exception e)
            {
                AutoArmLogger.Warn("Error resetting test state", e);
            }
        }

        private static void PostTestSetupCacheRebuild(Map map, ITestScenario test)
        {
            if (map == null) return;

            try
            {
                int cachedCount = WeaponCache.GetCacheWeaponCount(map);
                var onMapList = map.listerThings.ThingsInGroup(ThingRequestGroup.Weapon)?.OfType<ThingWithComps>().ToList()
                                 ?? new List<ThingWithComps>();

                if (onMapList.Count > 0 && cachedCount == 0)
                {
                    foreach (var w in onMapList)
                    {
                        if (w != null && !w.Destroyed && w.Spawned)
                            WeaponCache.AddWeaponToCache(w);
                    }
                }

                WeaponCache.ClearScoreCache();
                WeaponCache.ValidateCacheIntegrity(map);
            }
            catch (Exception e)
            {
                AutoArmLogger.Warn($"Error in post-test setup cache rebuild for {test.Name}", e);
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
                            map.reservationManager.ReleaseAllForTarget(thing);
                    }

                    var allPawns = map.mapPawns?.AllPawns;
                    if (allPawns != null)
                    {
                        foreach (var pawn in allPawns)
                            map.reservationManager.ReleaseAllClaimedBy(pawn);
                    }
                }

                JobGiver_PickUpBetterWeapon.CleanupCaches();
                JobGiver_PickUpBetterWeapon.ResetForTesting();
                WeaponCache.ClearScoreCache();

                Cleanup.PerformFullCleanup();
            }
            catch (Exception e)
            {
                AutoArmLogger.Warn("Error during final cleanup", e);
            }
        }

        public static List<ITestScenario> GetAllTests()
        {
            var tests = new List<ITestScenario>();

            try
            {
                var scenarioTypes = typeof(TestRunner).Assembly.GetTypes()
                    .Where(t => typeof(ITestScenario).IsAssignableFrom(t)
                             && !t.IsAbstract
                             && !t.IsInterface
                             && t.GetConstructor(Type.EmptyTypes) != null)
                    .OrderBy(t => t.Name);

                foreach (var type in scenarioTypes)
                {
                    try
                    {
                        tests.Add((ITestScenario)Activator.CreateInstance(type));
                    }
                    catch (Exception e)
                    {
                        AutoArmLogger.Warn($"Failed to instantiate test {type.Name}", e);
                    }
                }
            }
            catch (Exception e)
            {
                AutoArmLogger.Warn("Error discovering tests", e);
            }

            return tests;
        }

        private static void LogSummary(TestResults results, TimeSpan totalDuration)
        {
            AutoArmLogger.InfoFileOnly(
                $"Tests ({totalDuration.TotalMilliseconds:F0}ms)   " +
                $"Passed: {results.PassedTests}   " +
                $"Failed: {results.FailedTests}   " +
                $"Skipped: {results.SkippedTests}");

            foreach (var kvp in results.GetFailedTests())
            {
                var timing = results.GetTiming(kvp.Key);
                string timeStr = timing.HasValue ? $" ({timing.Value.TotalMilliseconds:F0}ms)" : "";
                AutoArmLogger.InfoFileOnly($"  FAIL {kvp.Key}{timeStr} - {kvp.Value.FailureReason ?? "(no reason)"}");
            }

            foreach (var kvp in results.GetSkippedTests())
                AutoArmLogger.InfoFileOnly($"  SKIP {kvp.Key} - {kvp.Value.SkipReason ?? "(no reason)"}");
        }
    }
}
