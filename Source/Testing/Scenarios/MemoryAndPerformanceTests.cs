using AutoArm.Caching;
using AutoArm.Definitions;
using AutoArm.Helpers;
using AutoArm.Jobs;
using AutoArm.Logging;
using AutoArm.Testing.Helpers;
using AutoArm.Weapons;
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

            // Create many pawns and weapons to stress memory
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
                    
                    // Create weapon for each pawn
                    var weapon = TestHelpers.CreateWeapon(map, VanillaWeaponDefOf.Gun_Autopistol,
                        pawn.Position + new IntVec3(1, 0, 0));
                    if (weapon != null)
                    {
                        testWeapons.Add(weapon);
                        ImprovedWeaponCacheManager.AddWeaponToCache(weapon);
                    }
                }
            }
        }

        public TestResult Run()
        {
            var result = new TestResult { Success = true };

            // Test 1: Check initial cache sizes
            int initialCacheSize = GetWeaponCacheSize();
            int initialScoreCacheSize = GetScoreCacheSize();
            result.Data["InitialWeaponCache"] = initialCacheSize;
            result.Data["InitialScoreCache"] = initialScoreCacheSize;

            // Test 2: Generate lots of scores to populate cache
            foreach (var pawn in testPawns)
            {
                foreach (var weapon in testWeapons.Take(5))
                {
                    WeaponScoreCache.GetCachedScore(pawn, weapon);
                }
            }

            int afterScoringCache = GetScoreCacheSize();
            result.Data["ScoreCacheAfterScoring"] = afterScoringCache;

            // Test 3: Destroy half the pawns
            int pawnsToDestroy = testPawns.Count / 2;
            for (int i = 0; i < pawnsToDestroy; i++)
            {
                var pawn = testPawns[i];
                pawn.Destroy();
            }

            // Test 4: Trigger cleanup
            CleanupHelper.PerformFullCleanup();

            int afterCleanupScoreCache = GetScoreCacheSize();
            result.Data["ScoreCacheAfterCleanup"] = afterCleanupScoreCache;

            if (afterCleanupScoreCache >= afterScoringCache)
            {
                result.Success = false;
                result.Data["ERROR1"] = "Score cache not cleaned after pawn destruction!";
            }

            // Test 5: Destroy weapons and check cache
            foreach (var weapon in testWeapons.Take(10))
            {
                weapon.Destroy();
            }

            ImprovedWeaponCacheManager.InvalidateCache(testMap);
            int afterWeaponDestruction = GetWeaponCacheSize();
            result.Data["WeaponCacheAfterDestruction"] = afterWeaponDestruction;

            // Test 6: Check for orphaned references
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

            // Test 7: Test AutoEquipTracker cleanup
            var tracker = GetAutoEquipTrackerData();
            result.Data["AutoEquipTrackerEntries"] = tracker.Count;

            // Test 8: Test DroppedItemTracker cleanup
            var droppedItems = GetDroppedItemTrackerData();
            result.Data["DroppedItemTrackerEntries"] = droppedItems.Count;

            // Test 9: Force garbage collection and re-check
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
                var cacheField = typeof(ImprovedWeaponCacheManager)
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
                var cacheField = typeof(WeaponScoreCache)
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

            // Check weapon score cache for destroyed pawns
            try
            {
                var cacheField = typeof(WeaponScoreCache)
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

            // Check dropped item tracker
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
                var jobsField = typeof(AutoEquipTracker)
                    .GetField("autoEquipJobs", BindingFlags.NonPublic | BindingFlags.Static);
                if (jobsField != null)
                {
                    var jobs = jobsField.GetValue(null) as System.Collections.IDictionary;
                    if (jobs != null)
                    {
                        data[jobs.Count] = jobs.Count;
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
            foreach (var pawn in testPawns)
            {
                if (pawn != null && !pawn.Destroyed)
                    pawn.Destroy();
            }
            foreach (var weapon in testWeapons)
            {
                if (weapon != null && !weapon.Destroyed)
                    weapon.Destroy();
            }
            testPawns.Clear();
            testWeapons.Clear();
            
            // Force cleanup
            CleanupHelper.PerformFullCleanup();
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

            // Create MANY pawns (simulate large colony)
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

            // Create MANY weapons (simulate weapon-rich map)
            for (int i = 0; i < 100; i++)
            {
                var pos = map.Center + new IntVec3((i % 20) * 2 - 20, 0, (i / 20) * 2 - 10);
                var weaponDef = i % 3 == 0 ? VanillaWeaponDefOf.Gun_AssaultRifle :
                               i % 3 == 1 ? VanillaWeaponDefOf.Gun_Autopistol :
                               VanillaWeaponDefOf.MeleeWeapon_Knife;
                               
                var weapon = TestHelpers.CreateWeapon(map, weaponDef, pos);
                if (weapon != null)
                {
                    testWeapons.Add(weapon);
                    ImprovedWeaponCacheManager.AddWeaponToCache(weapon);
                }
            }
        }

        public TestResult Run()
        {
            var result = new TestResult { Success = true };
            var jobGiver = new JobGiver_PickUpBetterWeapon();

            // Test 1: Measure time for all pawns to evaluate
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
            
            if (evalTime > 1000) // More than 1 second for 50 pawns
            {
                result.Success = false;
                result.Data["ERROR1"] = "Performance too slow!";
            }

            // Test 2: Cache efficiency
            startTime = DateTime.Now;
            var nearbyWeapons = ImprovedWeaponCacheManager.GetWeaponsNear(testMap, testMap.Center, 60f);
            var cacheTime = (DateTime.Now - startTime).TotalMilliseconds;
            
            result.Data["CacheQueryTimeMs"] = cacheTime;
            result.Data["WeaponsInRange"] = nearbyWeapons.Count;
            
            if (cacheTime > 50) // Cache query should be very fast
            {
                result.Success = false;
                result.Data["ERROR2"] = "Cache query too slow!";
            }

            // Test 3: Score caching efficiency
            var scoringStart = DateTime.Now;
            int scoresCalculated = 0;
            
            foreach (var pawn in testPawns.Take(10))
            {
                foreach (var weapon in testWeapons.Take(10))
                {
                    WeaponScoreCache.GetCachedScore(pawn, weapon);
                    scoresCalculated++;
                }
            }
            
            var scoringTime = (DateTime.Now - scoringStart).TotalMilliseconds;
            result.Data["ScoringTimeMs"] = scoringTime;
            result.Data["ScoresCalculated"] = scoresCalculated;
            result.Data["AvgScoreTimeMs"] = scoringTime / scoresCalculated;

            // Test 4: SimpleSidearms integration performance
            if (SimpleSidearmsCompat.IsLoaded())
            {
                var ssStart = DateTime.Now;
                int ssJobsCreated = 0;
                
                foreach (var pawn in testPawns.Take(20))
                {
                    var job = SimpleSidearmsCompat.FindBestSidearmJob(pawn,
                        (p, w) => WeaponScoringHelper.GetTotalScore(p, w), 60);
                    if (job != null)
                        ssJobsCreated++;
                }
                
                var ssTime = (DateTime.Now - ssStart).TotalMilliseconds;
                result.Data["SimpleSidearmsTimeMs"] = ssTime;
                result.Data["SimpleSidearmsJobs"] = ssJobsCreated;
            }

            // Test 5: Memory pressure
            long memBefore = GC.GetTotalMemory(false);
            
            // Run evaluation multiple times
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
            
            if (memIncrease > 10 * 1024 * 1024) // More than 10MB increase
            {
                result.Data["Warning"] = "High memory usage detected";
            }

            return result;
        }

        public void Cleanup()
        {
            foreach (var pawn in testPawns)
            {
                pawn?.Destroy();
            }
            foreach (var weapon in testWeapons)
            {
                weapon?.Destroy();
            }
            testPawns.Clear();
            testWeapons.Clear();
            
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

                // Create forbidden weapon
                forbiddenWeapon = TestHelpers.CreateWeapon(map, VanillaWeaponDefOf.Gun_AssaultRifle,
                    testPawn.Position + new IntVec3(2, 0, 0), QualityCategory.Legendary);
                if (forbiddenWeapon != null)
                {
                    forbiddenWeapon.SetForbidden(true);
                    ImprovedWeaponCacheManager.AddWeaponToCache(forbiddenWeapon);
                }

                // Create allowed weapon
                allowedWeapon = TestHelpers.CreateWeapon(map, VanillaWeaponDefOf.Gun_Autopistol,
                    testPawn.Position + new IntVec3(-2, 0, 0), QualityCategory.Good);
                if (allowedWeapon != null)
                {
                    allowedWeapon.SetForbidden(false);
                    ImprovedWeaponCacheManager.AddWeaponToCache(allowedWeapon);
                }

                // Create claimed weapon (reserved by another pawn)
                claimedWeapon = TestHelpers.CreateWeapon(map, VanillaWeaponDefOf.Gun_ChainShotgun,
                    testPawn.Position + new IntVec3(0, 0, 2), QualityCategory.Masterwork);
                if (claimedWeapon != null)
                {
                    ImprovedWeaponCacheManager.AddWeaponToCache(claimedWeapon);
                    
                    // Create another pawn to claim it
                    var otherPawn = TestHelpers.CreateTestPawn(map, new TestHelpers.TestPawnConfig
                    {
                        Name = "ClaimerPawn",
                        SpawnPosition = testPawn.Position + new IntVec3(5, 0, 0)
                    });
                    
                    if (otherPawn != null)
                    {
                        otherPawn.Reserve(claimedWeapon, null);
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

            // Test 1: Unarmed pawn should not pick forbidden weapon
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
                // Log why no job was created
                var nearbyWeapons = ImprovedWeaponCacheManager.GetWeaponsNear(testPawn.Map, testPawn.Position, 60f);
                result.Data["NearbyWeapons"] = nearbyWeapons.Count;
                
                foreach (var weapon in nearbyWeapons)
                {
                    string reason;
                    bool canUse = ValidationHelper.CanPawnUseWeapon(testPawn, weapon, out reason);
                    result.Data[$"{weapon.Label}_CanUse"] = canUse;
                    if (!canUse)
                        result.Data[$"{weapon.Label}_Reason"] = reason;
                }
            }

            // Test 2: Check claimed weapon handling
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

            // Test 3: Unforbid weapon and check again
            if (forbiddenWeapon != null)
            {
                forbiddenWeapon.SetForbidden(false);
                
                var job2 = jobGiver.TestTryGiveJob(testPawn);
                if (job2 != null && job2.targetA.Thing == forbiddenWeapon)
                {
                    result.Data["PicksUpUnforbiddenWeapon"] = true;
                }
            }

            // Test 4: Check outfit filter interaction with forbidden
            if (testPawn.outfits?.CurrentApparelPolicy != null && forbiddenWeapon != null)
            {
                // Remove from outfit filter
                testPawn.outfits.CurrentApparelPolicy.filter.SetAllow(forbiddenWeapon.def, false);
                
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
            testPawn?.Destroy();
            forbiddenWeapon?.Destroy();
            allowedWeapon?.Destroy();
            claimedWeapon?.Destroy();
        }
    }
}
