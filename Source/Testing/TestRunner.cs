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

            var problems = RunInSession(map, () =>
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
            results.AddResult("Session hygiene", problems.Count == 0
                ? TestResult.Pass()
                : TestResult.Failure(string.Join("  |  ", problems)));

            stopwatch.Stop();
            LogSummary(results, stopwatch.Elapsed);
            return results;
        }

        public static TestResult RunSingleTest(Map map, ITestScenario test)
        {
            if (map == null || test == null)
                return TestResult.Failure("Invalid test parameters");

            TestResult result = null;
            var problems = RunInSession(map, () =>
            {
                result = ExecuteScenario(map, test);
            });
            if (result == null)
                return TestResult.Failure("Session did not run");
            if (problems.Count > 0 && result.Success)
                return TestResult.Failure("Passed, but " + string.Join("  |  ", problems));
            return result;
        }

        private static Dictionary<string, string> Snapshot(Map map)
        {
            var snap = new Dictionary<string, string>();
            if (map == null)
                return snap;

            int weapons = 0;
            foreach (var thing in map.listerThings.ThingsInGroup(ThingRequestGroup.Weapon))
            {
                if (Validation.IsWeapon(thing))
                    weapons++;
            }
            int drafted = 0;
            foreach (var pawn in map.mapPawns.FreeColonistsSpawned)
            {
                if (pawn.Drafted)
                    drafted++;
                if (pawn.equipment?.Primary != null)
                    weapons++;
                foreach (var thing in pawn.inventory.innerContainer)
                {
                    if (Validation.IsWeapon(thing))
                        weapons++;
                }
                snap[$"{pawn.LabelShort} primary"] = pawn.equipment?.Primary?.ThingID ?? "none";
            }

            foreach (var pawn in map.mapPawns.SpawnedPawnsInFaction(RimWorld.Faction.OfPlayer))
            {
                if (pawn.RaceProps.Humanlike)
                    snap[$"{pawn.LabelShort} ({pawn.ThingID})"] = "present";
            }

            snap["weapons"] = weapons.ToString();
            snap["drafted colonists"] = drafted.ToString();
            snap["forced primaries"] = ForcedWeapons.GetAllForcedWeapons().Count.ToString();
            snap["forced sidearm pawns"] = ForcedWeapons.GetSidearmSaveData().Count.ToString();
            snap["apparel policies"] = Current.Game.outfitDatabase.AllOutfits.Count.ToString();
            snap["zones"] = map.zoneManager.AllZones.Count.ToString();
            snap["areas"] = map.areaManager.AllAreas.Count.ToString();
            return snap;
        }

        private static Dictionary<LogMessage, int> ErrorSnapshot()
        {
            var snap = new Dictionary<LogMessage, int>();
            foreach (var message in Log.Messages)
            {
                if (message.type == LogMessageType.Error)
                    snap[message] = message.repeats;
            }
            return snap;
        }

        private static List<string> SessionProblems(Map map, Dictionary<string, string> before, Dictionary<LogMessage, int> errorsBefore)
        {
            var problems = new List<string>();

            int errors = 0;
            string first = null;
            foreach (var message in Log.Messages)
            {
                if (message.type != LogMessageType.Error)
                    continue;
                errorsBefore.TryGetValue(message, out int seen);
                if (message.repeats <= seen)
                    continue;
                errors += message.repeats - seen;
                if (first == null)
                    first = message.text.Split('\n')[0];
            }
            if (errors > 0)
                problems.Add($"{errors} error(s) logged during the run, first: {(first.Length > 160 ? first.Substring(0, 160) : first)}");

            var after = Snapshot(map);
            foreach (var kvp in before)
            {
                if (!after.TryGetValue(kvp.Key, out string now))
                    problems.Add($"{kvp.Key} missing");
                else if (now != kvp.Value)
                    problems.Add($"{kvp.Key} changed from {kvp.Value} to {now}");
            }
            foreach (var key in after.Keys)
            {
                if (!before.ContainsKey(key))
                    problems.Add($"{key} appeared");
            }
            return problems;
        }

        private static List<string> RunInSession(Map map, Action body)
        {
            var errorsBefore = ErrorSnapshot();
            var before = Snapshot(map);
            var settingsSnapshot = AutoArmMod.settings?.Clone();
            bool originalDevMode = Prefs.DevMode;
            var despised = new List<(RimWorld.Precept_Weapon precept, WeaponClassDef def)>();
            var limits = SidearmLimits.Lift();

            try
            {
                if (ModsConfig.IdeologyActive && Find.IdeoManager != null)
                {
                    foreach (var ideo in Find.IdeoManager.IdeosListForReading)
                    {
                        foreach (var precept in ideo.PreceptsListForReading)
                        {
                            if (precept is RimWorld.Precept_Weapon weaponPrecept && weaponPrecept.despised != null)
                            {
                                despised.Add((weaponPrecept, weaponPrecept.despised));
                                IdeoTestHelper.SetDespised(weaponPrecept, null);
                            }
                        }
                    }
                }

                if (AutoArmMod.settings != null)
                {
                    AutoArmMod.settings.ApplyDefaults();
                    AutoArmMod.settings.debugLogging = true;
                    AutoArmMod.settings.modEnabled = true;
                    AutoArmMod.settings.weaponTypePreference = 0f;
                    AutoArmMod.settings.checkCEAmmo = false;
                }
                WeaponCache.ClearScoreCache();
                EquipEligibility.Clear();
                IsRunningTests = true;
                Prefs.DevMode = true;
                Cleanup.DisableAutoCleanup();
                CleanupTracker.Reset();

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
                foreach (var (precept, def) in despised)
                    IdeoTestHelper.SetDespised(precept, def);
                SidearmLimits.Restore(limits);
                if (AutoArmMod.settings != null && settingsSnapshot != null)
                    AutoArmMod.settings.CopyFrom(settingsSnapshot);
                WeaponCache.ClearScoreCache();
                EquipEligibility.Clear();
            }
            return SessionProblems(map, before, errorsBefore);
        }

        internal static TestResult ExecuteScenario(Map map, ITestScenario test)
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
            WeaponCache.ClearScoreCache();
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
                    .OrderBy(t => t.Name)
                    .ToList();

                var grouped = new HashSet<Type>();
                foreach (var type in scenarioTypes)
                {
                    if (!typeof(TestGroup).IsAssignableFrom(type))
                        continue;
                    try
                    {
                        var group = (TestGroup)Activator.CreateInstance(type);
                        tests.Add(group);
                        foreach (var part in group.Parts)
                            grouped.Add(part);
                    }
                    catch (Exception e)
                    {
                        AutoArmLogger.Warn($"Failed to instantiate test group {type.Name}", e);
                    }
                }

                foreach (var type in scenarioTypes)
                {
                    if (typeof(TestGroup).IsAssignableFrom(type) || grouped.Contains(type))
                        continue;
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

            foreach (var kvp in results.GetPassedTests())
                foreach (var part in kvp.Value.Data)
                    if (part.Value is string s && s.StartsWith("skipped ("))
                        AutoArmLogger.InfoFileOnly($"  SKIP {kvp.Key} / {part.Key} - {s}");
        }
    }

    internal sealed class CorePipelineGroup : TestGroup
    {
        public override string Name => "Core pipeline";
        public override Type[] Parts => new[]
        {
            typeof(Scenarios.ThinkTreeInjectionTest),
            typeof(Scenarios.NewGameDefaultsTest),
            typeof(Scenarios.UnarmedPawnTest),
            typeof(Scenarios.WeaponUpgradeTest),
            typeof(Scenarios.WeaponSwapChainTest),
        };
    }

    internal sealed class StabilityGroup : TestGroup
    {
        public override string Name => "Stability and safety";
        public override Type[] Parts => new[]
        {
            typeof(Scenarios.RaceConditionTest),
            typeof(Scenarios.WeaponDestructionMidJobTest),
            typeof(Scenarios.InfiniteLoopTest),
            typeof(Scenarios.NullInputSafetyTest),
            typeof(Scenarios.WeaponDestructionSafetyTest),
            typeof(Scenarios.FactionChangeInvalidatesScoreCacheTest),
            typeof(Scenarios.KnownPawnEnumerationTest),
        };
    }

    internal sealed class OutfitPolicyGroup : TestGroup
    {
        public override string Name => "Outfit policies";
        public override Type[] Parts => new[]
        {
            typeof(Scenarios.OutfitFilterTest),
            typeof(Scenarios.OutfitFilterPenaltyTest),
            typeof(Scenarios.UnarmedOutfitBypassTest),
            typeof(Scenarios.ForbiddenWeaponHandlingTest),
            typeof(Scenarios.PolicyDisallowedPrimaryDropsTest),
        };
    }

    internal sealed class ForcedWeaponsGroup : TestGroup
    {
        public override string Name => "Forced weapons";
        public override Type[] Parts => new[]
        {
            typeof(Scenarios.ForcedWeaponPersistenceTest),
            typeof(Scenarios.ForcedWeaponSaveLoadTest),
            typeof(Scenarios.LegacyFormatSaveLoadTest),
            typeof(Scenarios.OrderedEquipMarksForcedTest),
            typeof(Scenarios.UnfinishedOrderKeepsForcedPrimaryTest),
            typeof(Scenarios.ClearForcedPrimaryPreservesSidearmsTest),
            typeof(Scenarios.ForcedIncineratorTest),
            typeof(Scenarios.ForcedPrimarySameDefUpgradeTest),
            typeof(Scenarios.ForcedPrimaryCrossDefBlockedTest),
            typeof(Scenarios.ForcedSidearmSettingOffTest),
            typeof(Scenarios.ForcedSidearmSameDefUpgradeTest),
            typeof(Scenarios.ForcedSidearmCrossDefBlockedTest),
            typeof(Scenarios.ForcedSidearmAllowsPrimaryUpgradeTest),
            typeof(Scenarios.ForcedSidearmUpgradeStaysForcedTest),
            typeof(Scenarios.ForcedDefPickedByPrimarySearchStaysForcedTest),
            typeof(Scenarios.SecondForcedSidearmUpgradesTest),
            typeof(Scenarios.ForcedPrimaryStillGetsSidearmTest),
            typeof(Scenarios.EqualQualityBetterStuffSidearmSwapTest),
            typeof(Scenarios.SSForcedUnarmedStaysUnarmedTest),
            typeof(Scenarios.CargoWeaponKeptByPolicyTest),
            typeof(Scenarios.RememberedSidearmDroppedByPolicyTest),
            typeof(Scenarios.SameDefLowerQualityBlockedTest),
        };
    }

    internal sealed class SimpleSidearmsGroup : TestGroup
    {
        public override string Name => "SimpleSidearms compatibility";
        public override Type[] Parts => new[]
        {
            typeof(Scenarios.SimpleSidearmsReflectionFixTest),
            typeof(Scenarios.SimpleSidearmsIntegrationTest),
            typeof(Scenarios.SimpleSidearmsSlotLimitTest),
            typeof(Scenarios.SimpleSidearmsForcedWeaponTest),
            typeof(Scenarios.SimpleSidearmsCrossTypePrimarySwapTest),
            typeof(Scenarios.UnarmedWithSameDefSidearmTest),
            typeof(Scenarios.PrimaryBlocksSameDefSidearmTest),
            typeof(Scenarios.SidearmBlocksSameDefPrimaryUpgradeTest),
            typeof(Scenarios.RetrievalDupeBlockSelfHealsTest),
        };
    }

    internal sealed class ScoringGroup : TestGroup
    {
        public override string Name => "Scoring and selection";
        public override Type[] Parts => new[]
        {
            typeof(Scenarios.PersonaWeaponBonusTest),
            typeof(Scenarios.GrenadeHandlingTest),
            typeof(Scenarios.PistolDetectedByRangedLightTest),
            typeof(Scenarios.ShieldBeltBlocksRangedTest),
            typeof(Scenarios.BrawlerTest),
            typeof(Scenarios.SkillBasedPreferenceTest),
            typeof(Scenarios.ShooterTraitFitTest),
            typeof(Scenarios.MeleeGeneRoleTest),
            typeof(Scenarios.AreaRestrictedPicksAllowedWeaponTest),
            typeof(Scenarios.UniqueWeaponTraitsScoredTest),
            typeof(Scenarios.IdeologyDespisedWeaponTest),
            typeof(Scenarios.WeaponBlacklistBasicTest),
            typeof(Scenarios.WeaponBlacklistIntegrationTest),
        };
    }

    internal sealed class LiveBehaviorGroup : TestGroup
    {
        public override string Name => "Live behavior (ticked)";
        protected override string Unavailable(Map map)
            => map != null && RimWorld.GenHostility.AnyHostileActiveThreatToPlayer(map) ? "Hostiles on the map" : null;
        public override Type[] Parts => new[]
        {
            typeof(Scenarios.LiveArmsIdleColonistTest),
            typeof(Scenarios.LivePrimarySwapTest),
            typeof(Scenarios.LiveForcedPrimaryUpgradeTest),
            typeof(Scenarios.LiveSidearmSwapTest),
            typeof(Scenarios.LiveNoFlipFlopTest),
            typeof(Scenarios.LiveForcedGraceTest),
            typeof(Scenarios.LivePolicyReplaceTest),
            typeof(Scenarios.LiveAreaTest),
            typeof(Scenarios.LiveForcedPrimarySidearmTest),
            typeof(Scenarios.LiveMinigunUpgradeTest),
        };
    }

    internal sealed class SettingsGroup : TestGroup
    {
        public override string Name => "Settings";
        public override Type[] Parts => new[]
        {
            typeof(Scenarios.BondedWeaponAutoForcedTest),
            typeof(Scenarios.StorageOnlyTest),
            typeof(Scenarios.UpgradeEquippedOffTest),
            typeof(Scenarios.UpgradeThresholdTest),
            typeof(Scenarios.SkilledShooterQualityUpgradeTest),
            typeof(Scenarios.WeaponTypePreferenceTest),
            typeof(Scenarios.ForcedLabelTest),
        };
    }

    internal sealed class PocketSandGroup : TestGroup
    {
        public override string Name => "Pocket Sand compatibility";
        public override Type[] Parts => new[]
        {
            typeof(Scenarios.PocketSandSwapNotForcedTest),
            typeof(Scenarios.PocketSandUnarmedRespectedTest),
            typeof(Scenarios.PocketSandBeerNotWeaponTest),
        };
    }

    internal sealed class PawnEligibilityGroup : TestGroup
    {
        public override string Name => "Pawn eligibility";
        public override Type[] Parts => new[]
        {
            typeof(Scenarios.TemporaryColonistTest),
            typeof(Scenarios.FormerLodgerIsPermanentTest),
            typeof(Scenarios.BorrowedSoldierKeepsLockedWeaponTest),
            typeof(Scenarios.BiocodedPrimaryStillManagedTest),
            typeof(Scenarios.NearsightedRangeFitTest),
            typeof(Scenarios.ChildColonistTest),
            typeof(Scenarios.SlaveTest),
            typeof(Scenarios.RaidDetectionTest),
            typeof(Scenarios.CaravanTest),
        };
    }
}
