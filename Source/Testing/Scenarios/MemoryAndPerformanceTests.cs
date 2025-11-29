using AutoArm.Caching;
using AutoArm.Compatibility;
using AutoArm.Definitions;
using AutoArm.Helpers;
using AutoArm.Jobs;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;
using Verse.AI;

namespace AutoArm.Testing.Scenarios
{
    /// <summary>
    /// CRITICAL: Test memory cleanup to prevent memory leaks
    /// </summary>
    public class MemoryCleanupValidationTest : ITestScenario
    {
        public string Name => "Memory Cleanup Validation";
        private List<Pawn> testPawns = new List<Pawn>();
        private List<ThingWithComps> testWeapons = new List<ThingWithComps>();
        private Map testMap;

        public void Setup(Map map)
        {
            if (map == null) return;
            testMap = map;

            for (int i = 0; i < 20; i++)
            {
                var pawn = TestHelpers.CreateTestPawn(map, new TestHelpers.TestPawnConfig
                {
                    Name = $"MemTestPawn{i}",
                    SpawnPosition = map.Center + new IntVec3((i % 10) * 3, 0, (i / 10) * 3)
                });

                if (pawn != null)
                {
                    testPawns.Add(pawn);

                    var weapon = TestHelpers.CreateWeapon(map, AutoArmDefOf.Gun_Autopistol,
                        pawn.Position + new IntVec3(1, 0, 0));
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
            var result = new TestResult { Success = true };

            int initialCacheSize = GetWeaponCacheSize();
            int initialScoreCacheSize = GetScoreCacheSize();
            result.Data["InitialWeaponCache"] = initialCacheSize;
            result.Data["InitialScoreCache"] = initialScoreCacheSize;

            foreach (var pawn in testPawns)
            {
                foreach (var weapon in testWeapons.Take(5))
                {
                    WeaponCacheManager.GetCachedScore(pawn, weapon);
                }
            }

            int afterScoringCache = GetScoreCacheSize();
            result.Data["ScoreCacheAfterScoring"] = afterScoringCache;

            int pawnsToDestroy = testPawns.Count / 2;
            for (int i = 0; i < pawnsToDestroy; i++)
            {
                var pawn = testPawns[i];
                TestHelpers.SafeDestroyPawn(pawn);
            }

            AutoArm.Helpers.Cleanup.PerformFullCleanup();

            int afterCleanupScoreCache = GetScoreCacheSize();
            result.Data["ScoreCacheAfterCleanup"] = afterCleanupScoreCache;

            if (afterCleanupScoreCache >= afterScoringCache)
            {
                result.Success = false;
                result.Data["ERROR1"] = "Score cache not cleaned after pawn destruction!";
            }

            foreach (var weapon in testWeapons.Take(10))
            {
                TestHelpers.SafeDestroyWeapon(weapon);
            }

            WeaponCacheManager.InvalidateCache(testMap);
            int afterWeaponDestruction = GetWeaponCacheSize();
            result.Data["WeaponCacheAfterDestruction"] = afterWeaponDestruction;

            var orphanedRefs = CheckForOrphanedReferences();
            result.Data["OrphanedReferences"] = orphanedRefs.Count;

            if (orphanedRefs.Count > 0)
            {
                result.Success = false;
                result.Data["ERROR2"] = $"Found {orphanedRefs.Count} orphaned references!";
                foreach (var orphan in orphanedRefs.Take(5))
                {
                    result.Data[$"Orphan_{orphan.Key}"] = orphan.Value;
                }
            }

            var tracker = GetAutoEquipTrackerData();
            result.Data["AutoEquipTrackerEntries"] = tracker.Count;

            var droppedItems = GetDroppedItemTrackerData();
            result.Data["DroppedItemTrackerEntries"] = droppedItems.Count;

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            int finalCacheSize = GetWeaponCacheSize();
            int finalScoreCache = GetScoreCacheSize();
            result.Data["FinalWeaponCache"] = finalCacheSize;
            result.Data["FinalScoreCache"] = finalScoreCache;

            return result;
        }

        private int GetWeaponCacheSize()
        {
            try
            {
                var cacheField = typeof(WeaponCacheManager)
                    .GetField("weaponsByMap", BindingFlags.NonPublic | BindingFlags.Static);
                if (cacheField != null)
                {
                    var cache = cacheField.GetValue(null) as System.Collections.IDictionary;
                    if (cache != null && cache.Contains(testMap))
                    {
                        var mapCache = cache[testMap];
                        var countProp = mapCache.GetType().GetProperty("Count");
                        if (countProp != null)
                            return (int)countProp.GetValue(mapCache);
                    }
                }
            }
            catch { }
            return -1;
        }

        private int GetScoreCacheSize()
        {
            try
            {
                var cacheField = typeof(WeaponCacheManager)
                    .GetField("scoreCache", BindingFlags.NonPublic | BindingFlags.Static);
                if (cacheField != null)
                {
                    var cache = cacheField.GetValue(null) as System.Collections.IDictionary;
                    return cache?.Count ?? 0;
                }
            }
            catch { }
            return -1;
        }

        private List<KeyValuePair<string, string>> CheckForOrphanedReferences()
        {
            var orphans = new List<KeyValuePair<string, string>>();

            try
            {
                var cacheField = typeof(WeaponCacheManager)
                    .GetField("scoreCache", BindingFlags.NonPublic | BindingFlags.Static);
                if (cacheField != null)
                {
                    var cache = cacheField.GetValue(null) as System.Collections.IDictionary;
                    if (cache != null)
                    {
                        foreach (var key in cache.Keys)
                        {
                            var keyStr = key.ToString();
                            if (keyStr.Contains("Destroyed"))
                            {
                                orphans.Add(new KeyValuePair<string, string>("ScoreCache", keyStr));
                            }
                        }
                    }
                }
            }
            catch { }

            try
            {
                var trackerField = typeof(DroppedItemTracker)
                    .GetField("droppedItems", BindingFlags.NonPublic | BindingFlags.Static);
                if (trackerField != null)
                {
                    var tracker = trackerField.GetValue(null) as System.Collections.IDictionary;
                    if (tracker != null)
                    {
                        foreach (Thing thing in tracker.Keys)
                        {
                            if (thing.Destroyed)
                            {
                                orphans.Add(new KeyValuePair<string, string>("DroppedTracker", thing.ToString()));
                            }
                        }
                    }
                }
            }
            catch { }

            return orphans;
        }

        private Dictionary<int, int> GetAutoEquipTrackerData()
        {
            var data = new Dictionary<int, int>();
            try
            {
                var jobIdsField = typeof(AutoEquipState)
                    .GetField("autoEquipJobIds", BindingFlags.NonPublic | BindingFlags.Static);
                if (jobIdsField != null)
                {
                    var jobIds = jobIdsField.GetValue(null) as System.Collections.ICollection;
                    if (jobIds != null)
                    {
                        data[jobIds.Count] = jobIds.Count;
                    }
                }
            }
            catch { }
            return data;
        }

        private Dictionary<Thing, int> GetDroppedItemTrackerData()
        {
            var data = new Dictionary<Thing, int>();
            try
            {
                var trackerField = typeof(DroppedItemTracker)
                    .GetField("droppedItems", BindingFlags.NonPublic | BindingFlags.Static);
                if (trackerField != null)
                {
                    var tracker = trackerField.GetValue(null) as Dictionary<Thing, int>;
                    if (tracker != null)
                    {
                        foreach (var kvp in tracker)
                        {
                            data[kvp.Key] = kvp.Value;
                        }
                    }
                }
            }
            catch { }
            return data;
        }

        public void Cleanup()
        {
            TestHelpers.CleanupPawns(testPawns);
            TestHelpers.CleanupWeapons(testWeapons);

            AutoArm.Helpers.Cleanup.PerformFullCleanup();
        }
    }

    /// <summary>
    /// CRITICAL: Test performance under heavy load
    /// </summary>
    public class HeavyLoadPerformanceTest : ITestScenario
    {
        public string Name => "Heavy Load Performance";
        private List<Pawn> testPawns = new List<Pawn>();
        private List<ThingWithComps> testWeapons = new List<ThingWithComps>();
        private Map testMap;

        public void Setup(Map map)
        {
            if (map == null) return;
            testMap = map;

            for (int i = 0; i < 50; i++)
            {
                var pawn = TestHelpers.CreateTestPawn(map, new TestHelpers.TestPawnConfig
                {
                    Name = $"LoadPawn{i}",
                    SpawnPosition = map.Center + new IntVec3((i % 15) * 3, 0, (i / 15) * 3)
                });

                if (pawn != null)
                    testPawns.Add(pawn);
            }

            for (int i = 0; i < 100; i++)
            {
                var pos = map.Center + new IntVec3((i % 20) * 2 - 20, 0, (i / 20) * 2 - 10);
                var weaponDef = i % 3 == 0 ? AutoArmDefOf.Gun_AssaultRifle :
                               i % 3 == 1 ? AutoArmDefOf.Gun_Autopistol :
                               AutoArmDefOf.MeleeWeapon_Knife;

                var weapon = TestHelpers.CreateWeapon(map, weaponDef, pos);
                if (weapon != null)
                {
                    testWeapons.Add(weapon);
                    WeaponCacheManager.AddWeaponToCache(weapon);
                }
            }
        }

        public TestResult Run()
        {
            var result = new TestResult { Success = true };
            var jobGiver = new JobGiver_PickUpBetterWeapon();

            var startTime = DateTime.Now;
            int jobsCreated = 0;

            foreach (var pawn in testPawns)
            {
                var job = jobGiver.TestTryGiveJob(pawn);
                if (job != null)
                    jobsCreated++;
            }

            var evalTime = (DateTime.Now - startTime).TotalMilliseconds;
            result.Data["EvaluationTimeMs"] = evalTime;
            result.Data["JobsCreated"] = jobsCreated;
            result.Data["AvgTimePerPawn"] = evalTime / testPawns.Count;

            if (evalTime > 1000)
            {
                result.Success = false;
                result.Data["ERROR1"] = "Performance too slow!";
            }

            startTime = DateTime.Now;
            var nearbyWeapons = WeaponCacheManager.GetAllWeapons(testMap);
            var cacheTime = (DateTime.Now - startTime).TotalMilliseconds;

            result.Data["CacheQueryTimeMs"] = cacheTime;
            result.Data["WeaponsInRange"] = nearbyWeapons.Count();

            if (cacheTime > 50)
            {
                result.Success = false;
                result.Data["ERROR2"] = "Cache query too slow!";
            }

            var scoringStart = DateTime.Now;
            int scoresCalculated = 0;

            foreach (var pawn in testPawns.Take(10))
            {
                foreach (var weapon in testWeapons.Take(10))
                {
                    WeaponCacheManager.GetCachedScore(pawn, weapon);
                    scoresCalculated++;
                }
            }

            var scoringTime = (DateTime.Now - scoringStart).TotalMilliseconds;
            result.Data["ScoringTimeMs"] = scoringTime;
            result.Data["ScoresCalculated"] = scoresCalculated;
            result.Data["AvgScoreTimeMs"] = scoringTime / scoresCalculated;

            if (SimpleSidearmsCompat.IsLoaded)
            {
                var ssStart = DateTime.Now;
                int ssJobsCreated = 0;

                foreach (var pawn in testPawns.Take(20))
                {
                    var weapon = testWeapons.FirstOrDefault();
                    if (weapon != null)
                    {
                        var job = SimpleSidearmsCompat.TryGetWeaponJob(pawn, weapon);
                        if (job != null)
                            ssJobsCreated++;
                    }
                }

                var ssTime = (DateTime.Now - ssStart).TotalMilliseconds;
                result.Data["SimpleSidearmsTimeMs"] = ssTime;
                result.Data["SimpleSidearmsJobs"] = ssJobsCreated;
            }

            long memBefore = GC.GetTotalMemory(false);

            for (int i = 0; i < 5; i++)
            {
                foreach (var pawn in testPawns)
                {
                    jobGiver.TestTryGiveJob(pawn);
                }
            }

            long memAfter = GC.GetTotalMemory(false);
            long memIncrease = memAfter - memBefore;
            result.Data["MemoryIncreaseKB"] = memIncrease / 1024;

            if (memIncrease > 10 * 1024 * 1024)
            {
                result.Data["Warning"] = "High memory usage detected";
            }

            return result;
        }

        public void Cleanup()
        {
            TestHelpers.CleanupPawns(testPawns);
            TestHelpers.CleanupWeapons(testWeapons);

            GC.Collect();
        }
    }

    /// <summary>
    /// CRITICAL: Test forbidden weapon handling
    /// </summary>
    public class ForbiddenWeaponHandlingTest : ITestScenario
    {
        public string Name => "Forbidden Weapon Handling";
        private Pawn testPawn;
        private Pawn otherPawn;
        private ThingWithComps forbiddenWeapon;
        private ThingWithComps allowedWeapon;
        private ThingWithComps claimedWeapon;

        public void Setup(Map map)
        {
            if (map == null) return;

            testPawn = TestHelpers.CreateTestPawn(map);
            if (testPawn != null)
            {
                testPawn.equipment?.DestroyAllEquipment();

                forbiddenWeapon = TestHelpers.CreateWeapon(map, AutoArmDefOf.Gun_AssaultRifle,
                    testPawn.Position + new IntVec3(2, 0, 0), QualityCategory.Legendary);
                if (forbiddenWeapon != null)
                {
                    forbiddenWeapon.SetForbidden(true);
                    WeaponCacheManager.AddWeaponToCache(forbiddenWeapon);
                }

                allowedWeapon = TestHelpers.CreateWeapon(map, AutoArmDefOf.Gun_Autopistol,
                    testPawn.Position + new IntVec3(-2, 0, 0), QualityCategory.Good);
                if (allowedWeapon != null)
                {
                    allowedWeapon.SetForbidden(false);
                    WeaponCacheManager.AddWeaponToCache(allowedWeapon);
                }

                claimedWeapon = TestHelpers.CreateWeapon(map, AutoArmDefOf.Gun_ChainShotgun,
                    testPawn.Position + new IntVec3(0, 0, 2), QualityCategory.Masterwork);
                if (claimedWeapon != null)
                {
                    WeaponCacheManager.AddWeaponToCache(claimedWeapon);

                    otherPawn = TestHelpers.CreateTestPawn(map, new TestHelpers.TestPawnConfig
                    {
                        Name = "ClaimerPawn",
                        SpawnPosition = testPawn.Position + new IntVec3(5, 0, 0)
                    });

                    if (otherPawn != null)
                    {
                        var job = JobMaker.MakeJob(JobDefOf.Equip, claimedWeapon);
                        otherPawn.Reserve(claimedWeapon, job);
                    }
                }
            }
        }

        public TestResult Run()
        {
            if (testPawn == null)
                return TestResult.Failure("Test setup failed");

            var result = new TestResult { Success = true };
            var jobGiver = new JobGiver_PickUpBetterWeapon();

            if (testPawn.jobs?.curJob?.def == JobDefOf.Equip)
            {
                var targetWeapon = testPawn.jobs.curJob.targetA.Thing;
                if (targetWeapon == forbiddenWeapon)
                {
                    result.Success = false;
                    result.Data["CRITICAL_ERROR"] = "Unarmed pawn already equipping forbidden weapon!";
                }
                else if (targetWeapon == allowedWeapon)
                {
                    result.Data["CorrectlyChoseAllowed"] = true;
                }
                else if (targetWeapon == claimedWeapon)
                {
                    result.Data["AttemptedClaimed"] = true;
                }
            }
            else
            {
                var job1 = jobGiver.TestTryGiveJob(testPawn);

                if (job1 != null && job1.targetA.Thing == forbiddenWeapon)
                {
                    result.Success = false;
                    result.Data["CRITICAL_ERROR"] = "Unarmed pawn trying to pick up forbidden weapon!";
                }
                else if (job1 != null && job1.targetA.Thing == allowedWeapon)
                {
                    result.Data["CorrectlyChoseAllowed"] = true;
                }
                else if (job1 == null)
                {
                    var nearbyWeapons = WeaponCacheManager.GetAllWeapons(testPawn.Map);
                    result.Data["NearbyWeapons"] = nearbyWeapons.Count();

                    foreach (var weapon in nearbyWeapons)
                    {
                        // Use production validation
                        bool canUse = jobGiver.ShouldConsiderWeapon(testPawn, weapon, testPawn.equipment?.Primary);
                        result.Data[$"{weapon.Label}_CanUse"] = canUse;
                    }
                }
            }

            if (claimedWeapon != null && !claimedWeapon.Destroyed)
            {
                bool canReserve = testPawn.CanReserve(claimedWeapon);
                result.Data["CanReserveClaimedWeapon"] = canReserve;

                if (canReserve)
                {
                    result.Success = false;
                    result.Data["ERROR2"] = "Can reserve already claimed weapon!";
                }
            }

            if (forbiddenWeapon != null)
            {
                forbiddenWeapon.SetForbidden(false);

                var job2 = jobGiver.TestTryGiveJob(testPawn);
                if (job2 != null && job2.targetA.Thing == forbiddenWeapon)
                {
                    result.Data["PicksUpUnforbiddenWeapon"] = true;
                }
            }

            if (testPawn.outfits?.CurrentApparelPolicy != null && forbiddenWeapon != null)
            {
                testPawn.outfits.CurrentApparelPolicy.filter.SetAllow(forbiddenWeapon.def, false);

                WeaponCacheManager.OnOutfitFilterChanged(testPawn.outfits.CurrentApparelPolicy);
                WeaponCacheManager.ForceRebuildAllOutfitCaches(testPawn.Map);

                var job3 = jobGiver.TestTryGiveJob(testPawn);
                if (job3 != null && job3.targetA.Thing == forbiddenWeapon)
                {
                    result.Success = false;
                    result.Data["ERROR3"] = "Picks up weapon not allowed by outfit!";
                }
                else
                {
                    result.Data["RespectsOutfitFilter"] = true;
                }
            }

            return result;
        }

        public void Cleanup()
        {
            if (claimedWeapon != null && otherPawn != null && otherPawn.Map != null)
            {
                otherPawn.Map.reservationManager?.ReleaseAllClaimedBy(otherPawn);
            }

            TestHelpers.SafeDestroyPawn(testPawn);
            TestHelpers.SafeDestroyPawn(otherPawn);
            TestHelpers.SafeDestroyWeapon(forbiddenWeapon);
            TestHelpers.SafeDestroyWeapon(allowedWeapon);
            TestHelpers.SafeDestroyWeapon(claimedWeapon);
        }
    }
}
