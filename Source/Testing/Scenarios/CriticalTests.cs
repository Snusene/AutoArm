using AutoArm.Caching;
using AutoArm.Definitions;
using AutoArm.Helpers;
using AutoArm.Jobs;
using AutoArm.Logging;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace AutoArm.Testing.Scenarios
{
    /// <summary>
    /// HIGH PRIORITY: Test race conditions when multiple pawns target the same weapon
    /// </summary>
    public class RaceConditionTest : ITestScenario
    {
        public string Name => "Race Condition";
        private List<Pawn> testPawns = new List<Pawn>();
        private ThingWithComps targetWeapon;

        public void Setup(Map map)
        {
            if (map == null) return;

            var existingWeapons = map.listerThings.ThingsInGroup(ThingRequestGroup.Weapon)
                .OfType<ThingWithComps>().ToList();
            AutoArm.Testing.Framework.TestCleanupHelper.DestroyWeapons(existingWeapons);

            WeaponCacheManager.ClearScoreCache();

            JobGiver_PickUpBetterWeapon.EnableTestMode(true);

            if (map.reservationManager != null)
            {
                map.reservationManager.ReleaseAllClaimedBy(null);
            }

            var center = map.Center;
            for (int i = 0; i < 3; i++)
            {
                var pawn = TestHelpers.CreateTestPawn(map, new TestHelpers.TestPawnConfig
                {
                    Name = $"RacePawn{i}",
                    SpawnPosition = center + new IntVec3(i * 2, 0, 0),
                    EnsureViolenceCapable = true
                });

                if (pawn != null)
                {
                    pawn.equipment?.DestroyAllEquipment();
                    pawn.inventory?.innerContainer?.Clear();
                    testPawns.Add(pawn);

                    AutoArm.Testing.TestRunnerFix.PreparePawnForTest(pawn);
                }
            }

            var weaponPos = center + new IntVec3(3, 0, 3);
            targetWeapon = TestHelpers.CreateWeapon(map, AutoArmDefOf.Gun_AssaultRifle,
                weaponPos, QualityCategory.Legendary);

            if (targetWeapon != null)
            {
                if (targetWeapon.holdingOwner != null)
                {
                    targetWeapon.holdingOwner.Remove(targetWeapon);
                }

                targetWeapon.SetForbidden(false, false);

                WeaponCacheManager.AddWeaponToCache(targetWeapon);

                WeaponCacheManager.ForceRebuildAllOutfitCaches(map);

                var weaponsInCache = WeaponCacheManager.GetAllWeapons(map)?.ToList() ?? new List<ThingWithComps>();
                var weaponsOnMap = map.listerThings.ThingsInGroup(ThingRequestGroup.Weapon).Count();

                AutoArmLogger.Debug(() => $"[TEST] RaceConditionTest: Setup complete - {weaponsOnMap} weapons on map, {weaponsInCache.Count} in cache");

                if (!weaponsInCache.Contains(targetWeapon))
                {
                    AutoArmLogger.Error($"[TEST] RaceConditionTest: Target weapon not in cache! Retrying cache rebuild...");
                    WeaponCacheManager.AddWeaponToCache(targetWeapon);

                    weaponsInCache = WeaponCacheManager.GetAllWeapons(map)?.ToList() ?? new List<ThingWithComps>();
                    AutoArmLogger.Debug(() => $"[TEST] RaceConditionTest: After retry - {weaponsInCache.Count} weapons in cache, contains target: {weaponsInCache.Contains(targetWeapon)}");
                }
                else
                {
                    AutoArmLogger.Debug(() => $"[TEST] RaceConditionTest: Target weapon successfully cached: {targetWeapon.Label} at {targetWeapon.Position}");
                }
            }
        }

        public TestResult Run()
        {
            if (testPawns.Count < 2 || targetWeapon == null)
                return TestResult.Failure("Test setup failed");

            var result = new TestResult { Success = true };
            var jobGiver = new JobGiver_PickUpBetterWeapon();
            var jobs = new List<Job>();

            AutoArmLogger.Debug(() => $"[TEST] RaceCondition: Starting test with {testPawns.Count} pawns");

            var weaponsInCache = WeaponCacheManager.GetAllWeapons(targetWeapon.Map)?.Count() ?? 0;
            var weaponsOnMap = targetWeapon.Map.listerThings.ThingsInGroup(ThingRequestGroup.Weapon).Count();
            AutoArmLogger.Debug(() => $"[TEST] RaceCondition: Pre-test state - {weaponsOnMap} weapons on map, {weaponsInCache} in cache");

            if (weaponsInCache == 0)
            {
                return TestResult.Failure($"No weapons found in cache before test execution! {weaponsOnMap} weapons on map but 0 in cache.");
            }

            JobGiver_PickUpBetterWeapon.ResetForTesting();

            JobGiver_PickUpBetterWeapon.EnableTestMode(true);

            if (targetWeapon.Map?.reservationManager != null)
            {
                targetWeapon.Map.reservationManager.ReleaseAllForTarget(targetWeapon);
            }

            foreach (var pawn in testPawns)
            {
                try
                {
                    AutoArm.Testing.TestRunnerFix.PreparePawnForTest(pawn);

                    AutoArmLogger.Debug(() => $"[TEST] RaceCondition: Testing pawn {pawn.Name}, unarmed={pawn.equipment?.Primary == null}, weapon spawned={targetWeapon.Spawned}, weapon pos={targetWeapon.Position}");

                    var job = jobGiver.TestTryGiveJob(pawn);
                    if (job == null && pawn.jobs?.curJob != null)
                    {
                        var cur = pawn.jobs.curJob;
                        if (cur.def == JobDefOf.Equip || (cur.def?.defName == "EquipSecondary"))
                        {
                            job = cur;
                        }
                    }
                    if (job != null)
                    {
                        jobs.Add(job);
                        result.Data[$"Pawn_{pawn.Name}_Job"] = job.targetA.Thing == targetWeapon;
                        AutoArmLogger.Debug(() => $"[TEST] RaceCondition: Job created for {pawn.Name}, target={job.targetA.Thing?.Label}");
                    }
                    else
                    {
                        AutoArmLogger.Debug(() => $"[TEST] RaceCondition: No job created for {pawn.Name} - violence capable: {!pawn.WorkTagIsDisabled(WorkTags.Violent)}, drafted: {pawn.Drafted}, downed: {pawn.Downed}");
                    }
                }
                catch (Exception e)
                {
                    result.Success = false;
                    result.Data[$"Pawn_{pawn.Name}_Error"] = e.Message;
                    AutoArmLogger.Error($"[TEST] RaceCondition: Exception for {pawn.Name}: {e.Message}");
                }
            }

            result.Data["JobsCreated"] = jobs.Count;
            result.Data["AllTargetSameWeapon"] = jobs.All(j => j.targetA.Thing == targetWeapon);

            if (jobs.Count < 1)
            {
                result.Success = false;
                result.FailureReason = $"Race not exercised - expected at least 1 job but got {jobs.Count}";
                result.Data["Error"] = result.FailureReason;
                result.Data["JobsCreatedCount"] = jobs.Count;
                result.Data["PawnsCount"] = testPawns.Count;
                result.Data["WeaponsInCache"] = weaponsInCache;
                result.Data["WeaponsOnMap"] = weaponsOnMap;

                foreach (var pawn in testPawns)
                {
                    result.Data[$"{pawn.Name}_CanUseViolence"] = !pawn.WorkTagIsDisabled(WorkTags.Violent);
                    result.Data[$"{pawn.Name}_IsUnarmed"] = pawn.equipment?.Primary == null;
                    result.Data[$"{pawn.Name}_IsDrafted"] = pawn.Drafted;
                }

                return result;
            }

            if (jobs.Count > 0)
            {
                var firstPawn = testPawns[0];
                var firstJob = jobs[0];

                bool canReserve = firstPawn.Reserve(targetWeapon, firstJob);
                result.Data["FirstPawnReserved"] = canReserve;

                if (!canReserve)
                {
                    result.Success = false;
                    result.FailureReason = "First pawn could not reserve the weapon";
                    result.Data["ErrorReserve"] = result.FailureReason;
                    return result;
                }

                for (int i = 1; i < testPawns.Count && i < jobs.Count; i++)
                {
                    bool othersCanReserve = testPawns[i].CanReserve(targetWeapon);
                    result.Data[$"Pawn{i}_CanReserve_After"] = othersCanReserve;

                    if (othersCanReserve)
                    {
                        result.Success = false;
                        result.FailureReason = "Multiple pawns can reserve same weapon!";
                        result.Data["ErrorMultiReserve"] = result.FailureReason;
                        break;
                    }
                }
            }

            if (jobs.Count > 0 && !targetWeapon.Destroyed)
            {
                var firstJob = jobs[0];

                TestHelpers.SafeDestroyWeapon(targetWeapon);

                result.Data["WeaponDestroyedAfterJob"] = true;
                result.Data["JobTargetStillValid"] = firstJob.targetA.Thing != null && !firstJob.targetA.Thing.Destroyed;

                try
                {
                    if (testPawns[0].jobs != null)
                    {
                        testPawns[0].jobs.StartJob(firstJob, JobCondition.InterruptOptional);
                        result.Data["JobStartedWithDestroyedTarget"] = true;
                    }
                }
                catch (Exception e)
                {
                    result.Data["JobStartException"] = e.Message;
                }
            }

            return result;
        }

        public void Cleanup()
        {
            JobGiver_PickUpBetterWeapon.EnableTestMode(false);

            foreach (var pawn in testPawns)
            {
                TestHelpers.SafeDestroyPawn(pawn);
            }
            testPawns.Clear();

            TestHelpers.SafeDestroyWeapon(targetWeapon);
        }
    }

    /// <summary>
    /// HIGH PRIORITY: Test weapon destruction during evaluation
    /// </summary>
    public class WeaponDestructionMidJobTest : ITestScenario
    {
        public string Name => "Weapon Destruction During Evaluation";
        private Pawn testPawn;
        private List<ThingWithComps> testWeapons = new List<ThingWithComps>();

        public void Setup(Map map)
        {
            if (map == null) return;

            testPawn = TestHelpers.CreateTestPawn(map);
            if (testPawn != null)
            {
                testPawn.equipment?.DestroyAllEquipment();

                for (int i = 0; i < 5; i++)
                {
                    var pos = testPawn.Position + new IntVec3((i + 1) * 2, 0, 0);
                    var weapon = TestHelpers.CreateWeapon(map, AutoArmDefOf.Gun_Autopistol, pos);
                    if (weapon != null)
                    {
                        testWeapons.Add(weapon);
                        WeaponCacheManager.AddWeaponToCache(weapon);
                    }
                }
            }
        }

        public TestResult Run()
        {
            if (testPawn == null || testWeapons.Count == 0)
                return TestResult.Failure("Test setup failed");

            var result = new TestResult { Success = true };
            var jobGiver = new JobGiver_PickUpBetterWeapon();

            var firstWeapon = testWeapons[0];
            TestHelpers.SafeDestroyWeapon(firstWeapon);

            var job1 = jobGiver.TestTryGiveJob(testPawn);
            result.Data["JobAfterFirstDestroyed"] = job1 != null;

            if (job1 != null && job1.targetA.Thing == firstWeapon)
            {
                result.Success = false;
                result.Data["Error1"] = "Job created for destroyed weapon!";
            }

            if (testWeapons.Count > 1)
            {
                var secondWeapon = testWeapons[1];

                var weapons = WeaponCacheManager.GetAllWeapons(testPawn.Map).ToList();

                TestHelpers.SafeDestroyWeapon(secondWeapon);

                try
                {
                    var job2 = jobGiver.TestTryGiveJob(testPawn);
                    result.Data["JobAfterCacheDestruction"] = job2 != null;

                    if (job2 != null && job2.targetA.Thing == secondWeapon)
                    {
                        result.Success = false;
                        result.Data["Error2"] = "Job created for weapon destroyed during cache query!";
                    }
                }
                catch (Exception e)
                {
                    result.Success = false;
                    result.Data["CacheException"] = e.Message;
                }
            }

            if (testWeapons.Count > 2)
            {
                var thirdWeapon = testWeapons[2];
                if (thirdWeapon.Spawned)
                {
                    thirdWeapon.DeSpawn();
                    result.Data["WeaponDespawned"] = !thirdWeapon.Spawned;
                    result.Data["WeaponDestroyed"] = thirdWeapon.Destroyed;

                    var job3 = jobGiver.TestTryGiveJob(testPawn);
                    if (job3 != null && job3.targetA.Thing == thirdWeapon)
                    {
                        result.Success = false;
                        result.Data["Error3"] = "Job created for despawned weapon!";
                    }
                }
            }

            if (testWeapons.Count > 3)
            {
                var fourthWeapon = testWeapons[3];
                if (fourthWeapon.Spawned)
                {
                    fourthWeapon.DeSpawn();
                    if (testPawn.inventory?.innerContainer?.TryAdd(fourthWeapon) == true)
                    {
                        result.Data["WeaponInInventory"] = true;

                        var job4 = jobGiver.TestTryGiveJob(testPawn);
                        if (job4 != null && job4.targetA.Thing == fourthWeapon)
                        {
                            result.Success = false;
                            result.Data["Error4"] = "Job created for weapon in inventory!";
                        }
                    }
                }
            }

            return result;
        }

        public void Cleanup()
        {
            TestHelpers.SafeDestroyPawn(testPawn);
            TestHelpers.CleanupWeapons(testWeapons);
        }
    }


    /// <summary>
    /// HIGH PRIORITY: Test infinite loop prevention
    /// </summary>
    public class InfiniteLoopTest : ITestScenario
    {
        public string Name => "Infinite Loop Prevention";
        private Pawn testPawn;
        private ThingWithComps weaponA;
        private ThingWithComps weaponB;

        public void Setup(Map map)
        {
            if (map == null) return;

            JobGiver_PickUpBetterWeapon.EnableTestMode(true);

            testPawn = TestHelpers.CreateTestPawn(map, new TestHelpers.TestPawnConfig
            {
                Name = "LoopTestPawn",
                EnsureViolenceCapable = true
            });

            if (testPawn != null)
            {
                testPawn.equipment?.DestroyAllEquipment();
                testPawn.inventory?.innerContainer?.Clear();

                weaponA = TestHelpers.CreateWeapon(map, AutoArmDefOf.Gun_Autopistol,
                    testPawn.Position + new IntVec3(2, 0, 0), QualityCategory.Normal);
                weaponB = TestHelpers.CreateWeapon(map, AutoArmDefOf.Gun_AssaultRifle,
                    testPawn.Position + new IntVec3(-2, 0, 0), QualityCategory.Good);

                if (weaponA != null)
                {
                    if (weaponA.holdingOwner != null)
                    {
                        weaponA.holdingOwner.Remove(weaponA);
                    }
                    WeaponCacheManager.AddWeaponToCache(weaponA);
                }

                if (weaponB != null)
                {
                    if (weaponB.holdingOwner != null)
                    {
                        weaponB.holdingOwner.Remove(weaponB);
                    }
                    WeaponCacheManager.AddWeaponToCache(weaponB);

                    var weaponsInCache = WeaponCacheManager.GetAllWeapons(testPawn.Map).ToList();
                    if (!weaponsInCache.Contains(weaponA) || !weaponsInCache.Contains(weaponB))
                    {
                        AutoArmLogger.Error($"[TEST] InfiniteLoopTest: Weapons not in cache! Cache has {weaponsInCache.Count} weapons");
                    }

                    if (weaponA != null && !weaponA.Spawned)
                    {
                        AutoArmLogger.Error($"[TEST] InfiniteLoopTest: WeaponA not spawned!");
                    }
                    if (weaponB != null && !weaponB.Spawned)
                    {
                        AutoArmLogger.Error($"[TEST] InfiniteLoopTest: WeaponB not spawned!");
                    }
                }
            }
        }

        public TestResult Run()
        {
            if (testPawn == null || weaponA == null || weaponB == null)
                return TestResult.Failure("Test setup failed");

            var result = new TestResult { Success = true };
            var jobGiver = new JobGiver_PickUpBetterWeapon();
            var jobHistory = new List<ThingWithComps>();
            const int maxIterations = 10;

            for (int i = 0; i < maxIterations; i++)
            {
                var job = jobGiver.TestTryGiveJob(testPawn);
                if (job != null && job.targetA.Thing is ThingWithComps weapon)
                {
                    jobHistory.Add(weapon);

                    if (weapon.Spawned) weapon.DeSpawn();
                    testPawn.equipment?.DestroyAllEquipment();
                    testPawn.equipment?.AddEquipment(weapon);

                    var otherWeapon = weapon == weaponA ? weaponB : weaponA;
                    if (!otherWeapon.Spawned && !otherWeapon.Destroyed)
                    {
                        GenSpawn.Spawn(otherWeapon, testPawn.Position + new IntVec3(2, 0, 0), testPawn.Map);
                        WeaponCacheManager.AddWeaponToCache(otherWeapon);
                    }
                }
                else
                {
                    break;
                }
            }

            if (jobHistory.Count == 0)
            {
                return TestResult.Failure("No jobs created for pawn with available weapons");
            }

            result.Data["JobsCreated"] = jobHistory.Count;
            result.Data["ReachedMaxIterations"] = jobHistory.Count >= maxIterations;

            bool hasFlipFlop = false;
            for (int i = 2; i < jobHistory.Count; i++)
            {
                if (jobHistory[i] == jobHistory[i - 2])
                {
                    hasFlipFlop = true;
                    break;
                }
            }
            result.Data["HasFlipFlop"] = hasFlipFlop;

            if (jobHistory.Count >= maxIterations)
            {
                result.Success = false;
                result.Data["Error"] = "Potential infinite loop - max iterations reached";
            }

            if (testPawn.equipment?.Primary != null)
            {
                var currentWeapon = testPawn.equipment.Primary;

                testPawn.equipment.TryDropEquipment(currentWeapon, out var dropped, testPawn.Position);
                if (dropped != null)
                {
                    DroppedItemTracker.MarkAsDropped(dropped, 600);

                    var job = jobGiver.TestTryGiveJob(testPawn);
                    if (job != null && job.targetA.Thing == dropped)
                    {
                        result.Success = false;
                        result.Data["Error2"] = "Immediately re-picking up dropped weapon!";
                    }
                    else
                    {
                        result.Data["DroppedItemPrevention"] = "Working";
                    }
                }
            }

            if (weaponA != null && weaponB != null)
            {
                float scoreA = jobGiver.GetWeaponScore(testPawn, weaponA);
                float scoreB = jobGiver.GetWeaponScore(testPawn, weaponB);
                float threshold = AutoArmMod.settings?.weaponUpgradeThreshold ?? 1.15f;

                result.Data["ScoreA"] = scoreA;
                result.Data["ScoreB"] = scoreB;
                result.Data["Threshold"] = threshold;
                result.Data["DifferenceSignificant"] = Math.Abs(scoreA - scoreB) / Math.Min(scoreA, scoreB) > (threshold - 1f);
            }

            return result;
        }

        public void Cleanup()
        {
            JobGiver_PickUpBetterWeapon.EnableTestMode(false);

            TestHelpers.SafeDestroyPawn(testPawn);
            TestHelpers.SafeDestroyWeapon(weaponA);
            TestHelpers.SafeDestroyWeapon(weaponB);
        }
    }
}
