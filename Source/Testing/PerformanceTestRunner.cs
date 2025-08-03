// AutoArm RimWorld 1.5+ mod - automatic weapon management
// This file: Performance testing framework
// Comprehensive performance tests for optimization and scalability

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using RimWorld;
using Verse;
using Verse.AI;
using AutoArm.Jobs;
using AutoArm.Caching;
using AutoArm.Helpers;
using AutoArm.Weapons;
using AutoArm.Logging;
using AutoArm.Definitions;
namespace AutoArm.Testing
{
    public static class PerformanceTestRunner
    {
        // Performance tracking
        private static readonly Dictionary<string, List<long>> performanceMetrics = new Dictionary<string, List<long>>();
        private static readonly Dictionary<string, long> memorySnapshots = new Dictionary<string, long>();
        private static readonly Stopwatch globalTimer = new Stopwatch();
        
        // Test configuration
        private static readonly int[] colonySizes = { 5, 10, 20, 35, 50, 100 };
        private static readonly int[] weaponCounts = { 50, 100, 200, 500, 1000 };
        private static readonly float[] searchRadii = { 10f, 25f, 50f, 100f, 200f };
        
        public static void RunPerformanceTest()
        {
            var map = Find.CurrentMap;
            if (map == null)
            {
                AutoArmLogger.Error("No map to run performance test on");
                return;
            }

            AutoArmLogger.Debug("========== PERFORMANCE TEST STARTING ==========");
            AutoArmLogger.Debug($"Map size: {map.Size.x}x{map.Size.z} ({map.Area:N0} cells)");
            AutoArmLogger.Debug($"Current colonists: {map.mapPawns.FreeColonistsCount}");
            AutoArmLogger.Debug($"Current weapons on map: {map.listerThings.ThingsInGroup(ThingRequestGroup.Weapon).Count()}");
            
            globalTimer.Restart();
            performanceMetrics.Clear();
            memorySnapshots.Clear();

            // Take initial memory snapshot
            TakeMemorySnapshot("Initial");

            // Core performance tests
            TestCacheSystemPerformance(map);
            TestWeaponSearchPerformance(map);
            TestScoreCalculationPerformance(map);
            TestJobCreationOverhead(map);
            TestThinkTreePerformance(map);
            
            // Scalability tests
            TestScalabilityWithColonySize(map);
            TestScalabilityWithWeaponCount(map);
            
            // Stress tests
            TestWorstCaseScenarios(map);
            TestCacheThrashing(map);
            TestMemoryPressure(map);
            
            // Integration tests
            TestModCompatibilityOverhead(map);
            TestRealWorldScenarios(map);
            
            // Generate comprehensive report
            GeneratePerformanceReport();
            
            globalTimer.Stop();
            AutoArmLogger.Debug($"========== PERFORMANCE TEST COMPLETE ({globalTimer.ElapsedMilliseconds:N0}ms total) ==========");
        }

        private static void TestCacheSystemPerformance(Map map)
        {
            AutoArmLogger.Debug("=== Testing Cache System Performance ===");
            
            // Test 1: Cache rebuild performance
            var sw = new Stopwatch();
            
            // Clear all caches
            ImprovedWeaponCacheManager.InvalidateCache(map);
            WeaponScoreCache.ClearAllCaches();
            CleanupHelper.ClearAllCaches();
            
            sw.Restart();
            var weapons = ImprovedWeaponCacheManager.GetWeaponsNear(map, map.Center, 100f);
            sw.Stop();
            RecordMetric("Cache_InitialBuild", sw.ElapsedMilliseconds);
            AutoArmLogger.Debug($"Initial cache build: {sw.ElapsedMilliseconds}ms for {weapons.Count} weapons");
            
            // Test 2: Cache hit performance
            var hitTests = 1000;
            sw.Restart();
            for (int i = 0; i < hitTests; i++)
            {
                var pos = new IntVec3(
                    Rand.Range(0, map.Size.x),
                    0,
                    Rand.Range(0, map.Size.z)
                );
                ImprovedWeaponCacheManager.GetWeaponsNear(map, pos, 30f);
            }
            sw.Stop();
            RecordMetric("Cache_HitRate", sw.ElapsedMilliseconds);
            AutoArmLogger.Debug($"Cache hit test: {sw.ElapsedMilliseconds}ms for {hitTests} queries ({sw.ElapsedMilliseconds / (float)hitTests:F3}ms avg)");
            
            // Test 3: Cache invalidation performance
            sw.Restart();
            for (int i = 0; i < 10; i++)
            {
                ImprovedWeaponCacheManager.InvalidateCache(map);
                ImprovedWeaponCacheManager.GetWeaponsNear(map, map.Center, 50f);
            }
            sw.Stop();
            RecordMetric("Cache_InvalidationCycle", sw.ElapsedMilliseconds);
            AutoArmLogger.Debug($"Cache invalidation cycle: {sw.ElapsedMilliseconds / 10f:F2}ms average");
            
            // Test 4: Measure cache memory overhead
            TakeMemorySnapshot("AfterCacheBuild");
            var cacheMemory = GetMemoryDelta("Initial", "AfterCacheBuild");
            AutoArmLogger.Debug($"Cache memory overhead: {cacheMemory / 1024:N0} KB");
        }

        private static void TestWeaponSearchPerformance(Map map)
        {
            AutoArmLogger.Debug("=== Testing Weapon Search Performance ===");
            
            var colonists = map.mapPawns.FreeColonists.ToList();
            if (colonists.Count == 0)
            {
                AutoArmLogger.Warn("No colonists for weapon search test");
                return;
            }
            
            var sw = new Stopwatch();
            
            // Test different search radii
            foreach (var radius in searchRadii)
            {
                sw.Restart();
                int totalWeaponsFound = 0;
                
                foreach (var pawn in colonists)
                {
                    var weapons = ImprovedWeaponCacheManager.GetWeaponsNear(map, pawn.Position, radius);
                    totalWeaponsFound += weapons.Count;
                }
                
                sw.Stop();
                RecordMetric($"Search_Radius_{radius}", sw.ElapsedMilliseconds);
                
                float avgTime = sw.ElapsedMilliseconds / (float)colonists.Count;
                AutoArmLogger.Debug($"Search radius {radius}: {avgTime:F2}ms avg, {totalWeaponsFound} total weapons found");
            }
            
            // Test progressive search performance
            sw.Restart();
            var progressiveWeapons = new List<ThingWithComps>();
            var testPawn = colonists.First();
            
            // Simulate the actual progressive search pattern used by AutoArm
            // Progressive search radii (from JobGiver_PickUpBetterWeapon)
            float[] progressiveRadii = { 10f, 20f, 30f, 40f, 50f, 60f };
            foreach (var radius in progressiveRadii)
            {
                var weaponsAtRadius = ImprovedWeaponCacheManager.GetWeaponsNear(map, testPawn.Position, radius);
                progressiveWeapons.AddRange(weaponsAtRadius.Except(progressiveWeapons));
                
                if (progressiveWeapons.Count >= 10) // Early exit simulation
                    break;
            }
            
            sw.Stop();
            RecordMetric("Search_Progressive", sw.ElapsedMilliseconds);
            AutoArmLogger.Debug($"Progressive search: {sw.ElapsedMilliseconds}ms, found {progressiveWeapons.Count} weapons");
        }

        private static void TestScoreCalculationPerformance(Map map)
        {
            AutoArmLogger.Debug("=== Testing Score Calculation Performance ===");
            
            var colonists = map.mapPawns.FreeColonists.ToList();
            var weapons = map.listerThings.ThingsInGroup(ThingRequestGroup.Weapon)
                .OfType<ThingWithComps>()
                .Take(50)
                .ToList();
                
            if (colonists.Count == 0 || weapons.Count == 0)
            {
                AutoArmLogger.Warn("Insufficient pawns/weapons for score test");
                return;
            }
            
            var sw = new Stopwatch();
            
            // Test 1: Cold cache performance
            WeaponScoreCache.ClearAllCaches();
            sw.Restart();
            
            foreach (var pawn in colonists.Take(5))
            {
                foreach (var weapon in weapons)
                {
                    WeaponScoreCache.GetCachedScore(pawn, weapon);
                }
            }
            
            sw.Stop();
            var coldCacheTime = sw.ElapsedMilliseconds;
            var totalCalculations = colonists.Take(5).Count() * weapons.Count;
            RecordMetric("Score_ColdCache", coldCacheTime);
            AutoArmLogger.Debug($"Cold cache scoring: {coldCacheTime}ms for {totalCalculations} calculations ({coldCacheTime / (float)totalCalculations:F3}ms each)");
            
            // Test 2: Warm cache performance
            sw.Restart();
            
            foreach (var pawn in colonists.Take(5))
            {
                foreach (var weapon in weapons)
                {
                    WeaponScoreCache.GetCachedScore(pawn, weapon);
                }
            }
            
            sw.Stop();
            var warmCacheTime = sw.ElapsedMilliseconds;
            RecordMetric("Score_WarmCache", warmCacheTime);
            AutoArmLogger.Debug($"Warm cache scoring: {warmCacheTime}ms for {totalCalculations} calculations ({warmCacheTime / (float)totalCalculations:F3}ms each)");
            AutoArmLogger.Debug($"Cache speedup: {coldCacheTime / (float)Math.Max(1, warmCacheTime):F1}x faster");
            
            // Test 3: Score calculation breakdown
            if (colonists.Any() && weapons.Any())
            {
                var testPawn = colonists.First();
                var testWeapon = weapons.First();
                
                // Get base weapon score (simplified)
                var baseWeaponScore = WeaponScoreCache.GetBaseWeaponScore(testWeapon);
                float baseScore = baseWeaponScore != null ? 
                    baseWeaponScore.WeaponPropertyScore + baseWeaponScore.ModScore : 0f;
                
                // For timing, measure the actual scoring method
                sw.Restart();
                WeaponScoringHelper.GetWeaponPropertyScore(testPawn, testWeapon);
                sw.Stop();
                var baseTime = sw.ElapsedTicks;
                
                sw.Restart();
                var skillScore = WeaponScoringHelper.GetSkillScore(testPawn, testWeapon);
                sw.Stop();
                var skillTime = sw.ElapsedTicks;
                
                // Situational scoring is part of weapon property score
                var situationalTime = 0;
                
                var totalTime = baseTime + skillTime + situationalTime;
                AutoArmLogger.Debug($"Score breakdown - Base: {baseTime * 100f / totalTime:F1}%, Skill: {skillTime * 100f / totalTime:F1}%, Situational: {situationalTime * 100f / totalTime:F1}%");
            }
        }

        private static void TestJobCreationOverhead(Map map)
        {
            AutoArmLogger.Debug("=== Testing Job Creation Overhead ===");
            
            var colonists = map.mapPawns.FreeColonists.ToList();
            if (colonists.Count == 0)
            {
                AutoArmLogger.Warn("No colonists for job creation test");
                return;
            }
            
            var sw = new Stopwatch();
            var jobGiver = new JobGiver_PickUpBetterWeapon();
            int jobsCreated = 0;
            
            // Clear all cooldowns to ensure we can create jobs
            TimingHelper.ClearAllCooldowns();
            
            sw.Restart();
            
            foreach (var pawn in colonists)
            {
                // Ensure pawn is in valid state
                if (pawn.Drafted)
                    pawn.drafter.Drafted = false;
                    
                var job = jobGiver.TestTryGiveJob(pawn);
                if (job != null)
                    jobsCreated++;
            }
            
            sw.Stop();
            RecordMetric("Job_CreationTime", sw.ElapsedMilliseconds);
            
            float avgTime = sw.ElapsedMilliseconds / (float)colonists.Count;
            AutoArmLogger.Debug($"Job creation: {sw.ElapsedMilliseconds}ms total, {avgTime:F2}ms per pawn, {jobsCreated} jobs created");
            
            // Test job validation overhead
            if (jobsCreated > 0)
            {
                var testPawn = colonists.First();
                var weapons = ImprovedWeaponCacheManager.GetWeaponsNear(map, testPawn.Position, 50f).Take(20).ToList();
                
                sw.Restart();
                
                foreach (var weapon in weapons)
                {
                    string reason;
                    ValidationHelper.IsValidWeapon(weapon, testPawn, out reason);
                }
                
                sw.Stop();
                RecordMetric("Job_ValidationTime", sw.ElapsedMilliseconds);
                
                if (weapons.Count > 0)
                {
                    AutoArmLogger.Debug($"Weapon validation: {sw.ElapsedMilliseconds / (float)weapons.Count:F3}ms per weapon");
                }
            }
        }

        private static void TestThinkTreePerformance(Map map)
        {
            AutoArmLogger.Debug("=== Testing Think Tree Performance ===");
            
            // Check if think tree injection is active by looking for emergency job giver in think tree
            bool fallbackMode = false;
            var colonistThinkTree = DefDatabase<ThinkTreeDef>.GetNamed("Humanlike", false);
            if (colonistThinkTree?.thinkRoot != null)
            {
                // If we can't find the emergency job giver in the think tree, we're in fallback mode
                fallbackMode = !ContainsJobGiver(colonistThinkTree.thinkRoot, typeof(JobGiver_PickUpBetterWeapon_Emergency));
            }
            AutoArmLogger.Debug($"Think tree mode: {(fallbackMode ? "FALLBACK (TickRare)" : "INJECTED")}");
            
            if (!fallbackMode)
            {
                // Test think tree traversal performance
                var colonists = map.mapPawns.FreeColonists.Take(10).ToList();
                if (colonists.Count == 0)
                    return;
                    
                var sw = new Stopwatch();
                var thinkNode = new ThinkNode_ConditionalUnarmed();
                var jobGiver = new JobGiver_PickUpBetterWeapon_Emergency();
                
                sw.Restart();
                
                foreach (var pawn in colonists)
                {
                    // Simulate think tree condition checks
                    var conditionMet = false;
                    try
                    {
                        var satisfiedMethod = typeof(ThinkNode_Conditional).GetMethod("Satisfied",
                            BindingFlags.NonPublic | BindingFlags.Instance);
                        if (satisfiedMethod != null)
                        {
                            conditionMet = (bool)satisfiedMethod.Invoke(thinkNode, new object[] { pawn });
                        }
                    }
                    catch { }
                    
                    if (conditionMet)
                    {
                        jobGiver.TestTryGiveJob(pawn);
                    }
                }
                
                sw.Stop();
                RecordMetric("ThinkTree_Traversal", sw.ElapsedMilliseconds);
                AutoArmLogger.Debug($"Think tree traversal: {sw.ElapsedMilliseconds / (float)colonists.Count:F2}ms per pawn");
            }
            else
            {
                // Test TickRare performance impact
                var tickRarePatches = map.mapPawns.FreeColonists.Count;
                var estimatedOverhead = tickRarePatches * 0.05f; // Estimated 0.05ms per pawn
                AutoArmLogger.Debug($"Fallback mode overhead: ~{estimatedOverhead:F2}ms per tick (estimated)");
                RecordMetric("ThinkTree_FallbackOverhead", (long)estimatedOverhead);
            }
        }

        private static void TestScalabilityWithColonySize(Map map)
        {
            AutoArmLogger.Debug("=== Testing Scalability with Colony Size ===");
            
            var sw = new Stopwatch();
            var jobGiver = new JobGiver_PickUpBetterWeapon();
            
            // Create temporary test pawns
            var originalColonists = map.mapPawns.FreeColonists.ToList();
            var testPawns = new List<Pawn>();
            
            try
            {
                foreach (var targetSize in colonySizes)
                {
                    // Adjust colony size
                    while (testPawns.Count < targetSize)
                    {
                        var config = new TestHelpers.TestPawnConfig
                        {
                            Name = $"PerfTestPawn_{testPawns.Count}"
                        };
                        var pawn = TestHelpers.CreateTestPawn(map, config);
                        if (pawn != null)
                            testPawns.Add(pawn);
                        else
                            break;
                    }
                    
                    if (testPawns.Count == 0)
                        continue;
                    
                    // Clear caches and cooldowns
                    ImprovedWeaponCacheManager.InvalidateCache(map);
                    TimingHelper.ClearAllCooldowns();
                    
                    // Test performance at this colony size
                    sw.Restart();
                    int jobsCreated = 0;
                    
                    foreach (var pawn in testPawns.Take(targetSize))
                    {
                        var job = jobGiver.TestTryGiveJob(pawn);
                        if (job != null)
                            jobsCreated++;
                    }
                    
                    sw.Stop();
                    RecordMetric($"Scale_Colony_{targetSize}", sw.ElapsedMilliseconds);
                    
                    float avgTime = sw.ElapsedMilliseconds / (float)Math.Min(targetSize, testPawns.Count);
                    AutoArmLogger.Debug($"Colony size {targetSize}: {sw.ElapsedMilliseconds}ms total, {avgTime:F2}ms per pawn, {jobsCreated} jobs");
                    
                    // Check if performance mode activated
                    bool perfMode = targetSize >= (AutoArmMod.settings?.performanceModeColonySize ?? 35);
                    if (perfMode)
                    {
                        AutoArmLogger.Debug($"  - Performance mode: ACTIVE (reduced check frequency)");
                    }
                }
            }
            finally
            {
                // Cleanup test pawns
                foreach (var pawn in testPawns)
                {
                    if (pawn != null && !pawn.Destroyed)
                        pawn.Destroy();
                }
            }
        }

        private static void TestScalabilityWithWeaponCount(Map map)
        {
            AutoArmLogger.Debug("=== Testing Scalability with Weapon Count ===");
            
            var colonist = map.mapPawns.FreeColonists.FirstOrDefault();
            if (colonist == null)
            {
                AutoArmLogger.Warn("No colonist for weapon count test");
                return;
            }
            
            var sw = new Stopwatch();
            var testWeapons = new List<Thing>();
            
            try
            {
                var weaponDef = VanillaWeaponDefOf.Gun_Autopistol;
                var basePos = colonist.Position;
                
                foreach (var targetCount in weaponCounts)
                {
                    // Create weapons up to target count
                    while (testWeapons.Count < targetCount)
                    {
                        var offset = new IntVec3(
                            Rand.Range(-50, 50),
                            0,
                            Rand.Range(-50, 50)
                        );
                        var pos = basePos + offset;
                        
                        if (pos.InBounds(map) && pos.Standable(map))
                        {
                            var weapon = GenSpawn.Spawn(weaponDef, pos, map) as ThingWithComps;
                            if (weapon != null)
                            {
                                weapon.SetForbidden(false);
                                testWeapons.Add(weapon);
                            }
                        }
                    }
                    
                    // Force cache rebuild
                    ImprovedWeaponCacheManager.InvalidateCache(map);
                    
                    // Test search performance
                    sw.Restart();
                    var foundWeapons = ImprovedWeaponCacheManager.GetWeaponsNear(map, colonist.Position, 100f);
                    sw.Stop();
                    
                    RecordMetric($"Scale_Weapons_{targetCount}", sw.ElapsedMilliseconds);
                    AutoArmLogger.Debug($"{targetCount} weapons: {sw.ElapsedMilliseconds}ms search time, {foundWeapons.Count} found");
                    
                    // Test scoring performance with many weapons
                    sw.Restart();
                    int scored = 0;
                    
                    foreach (var weapon in foundWeapons.Take(50))
                    {
                        WeaponScoreCache.GetCachedScore(colonist, weapon);
                        scored++;
                    }
                    
                    sw.Stop();
                    
                    if (scored > 0)
                    {
                        AutoArmLogger.Debug($"  - Scoring {scored} weapons: {sw.ElapsedMilliseconds / (float)scored:F3}ms each");
                    }
                }
            }
            finally
            {
                // Cleanup test weapons
                foreach (var weapon in testWeapons)
                {
                    if (weapon != null && !weapon.Destroyed)
                        weapon.Destroy();
                }
            }
        }

        private static void TestWorstCaseScenarios(Map map)
        {
            AutoArmLogger.Debug("=== Testing Worst Case Scenarios ===");
            
            var sw = new Stopwatch();
            
            // Scenario 1: All colonists unarmed, many weapons available
            AutoArmLogger.Debug("Scenario 1: Mass re-arming after raid");
            
            var colonists = map.mapPawns.FreeColonists.Take(10).ToList();
            var originalWeapons = new Dictionary<Pawn, ThingWithComps>();
            
            // Disarm all colonists
            foreach (var pawn in colonists)
            {
                if (pawn.equipment?.Primary != null)
                {
                    originalWeapons[pawn] = pawn.equipment.Primary;
                    pawn.equipment.TryDropEquipment(pawn.equipment.Primary, out _, pawn.Position);
                }
            }
            
            var jobGiver = new JobGiver_PickUpBetterWeapon_Emergency();
            sw.Restart();
            
            foreach (var pawn in colonists)
            {
                jobGiver.TestTryGiveJob(pawn);
            }
            
            sw.Stop();
            RecordMetric("WorstCase_MassRearm", sw.ElapsedMilliseconds);
            AutoArmLogger.Debug($"  - Mass re-arming: {sw.ElapsedMilliseconds}ms for {colonists.Count} pawns");
            
            // Scenario 2: Heavy mod load simulation
            AutoArmLogger.Debug("Scenario 2: Heavy mod compatibility overhead");
            
            sw.Restart();
            
            foreach (var pawn in colonists.Take(5))
            {
                var weapons = ImprovedWeaponCacheManager.GetWeaponsNear(map, pawn.Position, 50f);
                
                foreach (var weapon in weapons.Take(10))
                {
                    // Simulate mod compatibility checks
                    SimpleSidearmsCompat.IsLoaded();
                    CECompat.IsLoaded();
                    
                    if (SimpleSidearmsCompat.IsLoaded())
                    {
                        string reason;
                        SimpleSidearmsCompat.CanPickupSidearmInstance(weapon, pawn, out reason);
                    }
                    
                    if (CECompat.IsLoaded())
                    {
                        CECompat.ShouldSkipWeaponForCE(weapon, pawn);
                    }
                }
            }
            
            sw.Stop();
            RecordMetric("WorstCase_ModCompat", sw.ElapsedMilliseconds);
            AutoArmLogger.Debug($"  - Mod compatibility overhead: {sw.ElapsedMilliseconds}ms");
            
            // Restore original weapons
            foreach (var kvp in originalWeapons)
            {
                if (kvp.Key.equipment != null && kvp.Value != null && !kvp.Value.Destroyed)
                {
                    kvp.Key.equipment.AddEquipment(kvp.Value);
                }
            }
        }

        private static void TestCacheThrashing(Map map)
        {
            AutoArmLogger.Debug("=== Testing Cache Thrashing ===");
            
            var sw = new Stopwatch();
            
            // Simulate rapid weapon spawning/despawning
            var testWeapons = new List<Thing>();
            var weaponDef = VanillaWeaponDefOf.Gun_Autopistol;
            
            sw.Restart();
            
            for (int cycle = 0; cycle < 10; cycle++)
            {
                // Spawn weapons
                for (int i = 0; i < 20; i++)
                {
                    var pos = new IntVec3(
                        Rand.Range(10, map.Size.x - 10),
                        0,
                        Rand.Range(10, map.Size.z - 10)
                    );
                    
                    if (pos.Standable(map))
                    {
                        var weapon = GenSpawn.Spawn(weaponDef, pos, map);
                        testWeapons.Add(weapon);
                        ImprovedWeaponCacheManager.AddWeaponToCache(weapon as ThingWithComps);
                    }
                }
                
                // Query cache
                ImprovedWeaponCacheManager.GetWeaponsNear(map, map.Center, 100f);
                
                // Despawn half
                foreach (var weapon in testWeapons.Take(testWeapons.Count / 2).ToList())
                {
                    if (!weapon.Destroyed)
                    {
                        ImprovedWeaponCacheManager.RemoveWeaponFromCache(weapon as ThingWithComps);
                        weapon.Destroy();
                        testWeapons.Remove(weapon);
                    }
                }
            }
            
            sw.Stop();
            RecordMetric("Cache_Thrashing", sw.ElapsedMilliseconds);
            AutoArmLogger.Debug($"Cache thrashing test: {sw.ElapsedMilliseconds}ms for 10 cycles");
            
            // Cleanup
            foreach (var weapon in testWeapons)
            {
                if (!weapon.Destroyed)
                    weapon.Destroy();
            }
        }

        private static void TestMemoryPressure(Map map)
        {
            AutoArmLogger.Debug("=== Testing Memory Pressure ===");
            
            TakeMemorySnapshot("BeforeMemoryTest");
            
            // Create many score cache entries
            var colonists = map.mapPawns.FreeColonists.ToList();
            var weapons = map.listerThings.ThingsInGroup(ThingRequestGroup.Weapon)
                .OfType<ThingWithComps>()
                .ToList();
                
            if (colonists.Count > 0 && weapons.Count > 0)
            {
                // Fill up the score cache
                foreach (var pawn in colonists)
                {
                    foreach (var weapon in weapons.Take(100))
                    {
                        WeaponScoreCache.GetCachedScore(pawn, weapon);
                    }
                }
                
                TakeMemorySnapshot("AfterCacheFill");
                
                // Test cache cleanup
                CleanupHelper.PerformFullCleanup();
                
                TakeMemorySnapshot("AfterCleanup");
                
                var memoryGrowth = GetMemoryDelta("BeforeMemoryTest", "AfterCacheFill");
                var memoryRecovered = GetMemoryDelta("AfterCacheFill", "AfterCleanup");
                
                AutoArmLogger.Debug($"Memory growth: {memoryGrowth / 1024:N0} KB");
                AutoArmLogger.Debug($"Memory recovered by cleanup: {memoryRecovered / 1024:N0} KB");
                
                // Calculate cache efficiency
                var entriesCreated = colonists.Count * Math.Min(100, weapons.Count);
                if (entriesCreated > 0)
                {
                    var bytesPerEntry = memoryGrowth / (float)entriesCreated;
                    AutoArmLogger.Debug($"Average memory per cache entry: {bytesPerEntry:F1} bytes");
                }
            }
        }

        private static void TestModCompatibilityOverhead(Map map)
        {
            AutoArmLogger.Debug("=== Testing Mod Compatibility Overhead ===");
            
            var sw = new Stopwatch();
            var iterations = 1000;
            
            // Test SimpleSidearms overhead
            if (SimpleSidearmsCompat.IsLoaded())
            {
                var pawn = map.mapPawns.FreeColonists.FirstOrDefault();
                var weapon = map.listerThings.ThingsInGroup(ThingRequestGroup.Weapon)
                    .OfType<ThingWithComps>()
                    .FirstOrDefault();
                    
                if (pawn != null && weapon != null)
                {
                    sw.Restart();
                    
                    for (int i = 0; i < iterations; i++)
                    {
                        string reason;
                        SimpleSidearmsCompat.CanPickupSidearmInstance(weapon, pawn, out reason);
                    }
                    
                    sw.Stop();
                    RecordMetric("ModCompat_SimpleSidearms", sw.ElapsedMilliseconds);
                    AutoArmLogger.Debug($"SimpleSidearms validation: {sw.ElapsedMilliseconds / (float)iterations:F3}ms per check");
                }
            }
            
            // Test Combat Extended overhead
            if (CECompat.IsLoaded())
            {
                var pawn = map.mapPawns.FreeColonists.FirstOrDefault();
                var weapon = map.listerThings.ThingsInGroup(ThingRequestGroup.Weapon)
                    .OfType<ThingWithComps>()
                    .Where(w => w.def.IsRangedWeapon)
                    .FirstOrDefault();
                    
                if (pawn != null && weapon != null)
                {
                    sw.Restart();
                    
                    for (int i = 0; i < iterations; i++)
                    {
                        CECompat.ShouldSkipWeaponForCE(weapon, pawn);
                    }
                    
                    sw.Stop();
                    RecordMetric("ModCompat_CombatExtended", sw.ElapsedMilliseconds);
                    AutoArmLogger.Debug($"Combat Extended ammo check: {sw.ElapsedMilliseconds / (float)iterations:F3}ms per check");
                }
            }
            
            // Test reflection cache performance
            sw.Restart();
            
            for (int i = 0; i < iterations; i++)
            {
                ReflectionHelper.GetCachedMethod(typeof(Pawn), "IsColonist", Type.EmptyTypes);
            }
            
            sw.Stop();
            RecordMetric("Reflection_Cache", sw.ElapsedMilliseconds);
            AutoArmLogger.Debug($"Reflection cache: {sw.ElapsedMilliseconds / (float)iterations:F3}ms per lookup");
        }

        private static void TestRealWorldScenarios(Map map)
        {
            AutoArmLogger.Debug("=== Testing Real World Scenarios ===");
            
            var sw = new Stopwatch();
            
            // Scenario 1: Typical gameplay tick
            AutoArmLogger.Debug("Simulating typical gameplay performance...");
            
            var colonists = map.mapPawns.FreeColonists.ToList();
            int totalChecks = 0;
            int jobsCreated = 0;
            
            sw.Restart();
            
            // Simulate 10 game seconds (600 ticks)
            for (int tick = 0; tick < 600; tick++)
            {
                // Check colonists based on their cooldowns
                foreach (var pawn in colonists)
                {
                    if (!TimingHelper.IsOnCooldown(pawn, TimingHelper.CooldownType.WeaponSearch))
                    {
                        totalChecks++;
                        
                        // Simulate the check
                        var weapons = ImprovedWeaponCacheManager.GetWeaponsNear(map, pawn.Position, 25f);
                        if (weapons.Count > 0)
                        {
                            var currentScore = pawn.equipment?.Primary != null ? 
                                WeaponScoreCache.GetCachedScore(pawn, pawn.equipment.Primary) : 0f;
                                
                            foreach (var weapon in weapons.Take(5))
                            {
                                var score = WeaponScoreCache.GetCachedScore(pawn, weapon);
                                if (score > currentScore * 1.15f)
                                {
                                    jobsCreated++;
                                    break;
                                }
                            }
                        }
                        
                        // Set cooldown
                        TimingHelper.SetCooldown(pawn, TimingHelper.CooldownType.WeaponSearch);
                    }
                }
                
                // Simulate tick cleanup every 2 seconds
                if (tick % 120 == 0)
                {
                    TimingHelper.CleanupOldCooldowns();
                }
            }
            
            sw.Stop();
            
            var msPerTick = sw.ElapsedMilliseconds / 600f;
            var checksPerSecond = totalChecks / 10f;
            
            AutoArmLogger.Debug($"10-second simulation: {sw.ElapsedMilliseconds}ms total");
            AutoArmLogger.Debug($"  - Average per tick: {msPerTick:F3}ms");
            AutoArmLogger.Debug($"  - Checks per second: {checksPerSecond:F1}");
            AutoArmLogger.Debug($"  - Jobs created: {jobsCreated}");
            AutoArmLogger.Debug($"  - TPS impact: ~{msPerTick * 0.6f:F1}% (at 60 TPS)");
            
            RecordMetric("RealWorld_TickImpact", (long)(msPerTick * 1000)); // Store in microseconds
        }

        private static void GeneratePerformanceReport()
        {
            AutoArmLogger.Debug("\n========== PERFORMANCE REPORT ==========");
            
            // Calculate aggregates
            var categories = new Dictionary<string, List<KeyValuePair<string, long>>>();
            
            foreach (var metric in performanceMetrics)
            {
                var category = metric.Key.Split('_')[0];
                if (!categories.ContainsKey(category))
                    categories[category] = new List<KeyValuePair<string, long>>();
                    
                var avg = metric.Value.Count > 0 ? (long)metric.Value.Average() : 0;
                categories[category].Add(new KeyValuePair<string, long>(metric.Key, avg));
            }
            
            // Generate summary by category
            foreach (var category in categories.OrderBy(c => c.Key))
            {
                AutoArmLogger.Debug($"\n{category.Key} Performance:");
                
                foreach (var metric in category.Value.OrderBy(m => m.Key))
                {
                    AutoArmLogger.Debug($"  {metric.Key}: {metric.Value}ms");
                }
            }
            
            // Performance recommendations
            AutoArmLogger.Debug("\nPerformance Recommendations:");
            
            // Check cache performance
            if (performanceMetrics.ContainsKey("Cache_HitRate"))
            {
                var hitRate = performanceMetrics["Cache_HitRate"].Average();
                if (hitRate > 10)
                {
                    AutoArmLogger.Warn(" - Cache hit rate is slow. Consider increasing cache size.");
                }
            }
            
            // Check scaling
            if (performanceMetrics.ContainsKey("Scale_Colony_50") && performanceMetrics.ContainsKey("Scale_Colony_10"))
            {
                var small = performanceMetrics["Scale_Colony_10"].Average();
                var large = performanceMetrics["Scale_Colony_50"].Average();
                var scaling = large / small;
                
                if (scaling > 5)
                {
                    AutoArmLogger.Warn($" - Poor scaling detected ({scaling:F1}x slower at 50 colonists)");
                    AutoArmLogger.Debug("   Consider enabling performance mode at lower colony sizes");
                }
                else
                {
                    AutoArmLogger.Debug($" - Good scaling: {scaling:F1}x slower at 5x colony size");
                }
            }
            
            // Memory usage
            var totalMemory = memorySnapshots.Values.Max() - memorySnapshots.Values.Min();
            AutoArmLogger.Debug($"\nTotal memory usage: {totalMemory / 1024:N0} KB");
            
            if (totalMemory > 10 * 1024 * 1024) // 10MB
            {
                AutoArmLogger.Warn(" - High memory usage detected. Consider more aggressive cleanup.");
            }
            
            // Export detailed metrics
            if (AutoArmMod.settings?.debugLogging ?? false)
            {
                var report = new StringBuilder();
                report.AppendLine("AutoArm Performance Metrics Export");
                report.AppendLine($"Date: {DateTime.Now}");
                report.AppendLine($"Map: {Find.CurrentMap?.uniqueID}");
                report.AppendLine();
                
                foreach (var metric in performanceMetrics.OrderBy(m => m.Key))
                {
                    var values = metric.Value;
                    if (values.Count > 0)
                    {
                        report.AppendLine($"{metric.Key}:");
                        report.AppendLine($"  Average: {values.Average():F2}ms");
                        report.AppendLine($"  Min: {values.Min()}ms");
                        report.AppendLine($"  Max: {values.Max()}ms");
                        report.AppendLine($"  Samples: {values.Count}");
                    }
                }
                
                // Could write to file if needed
                AutoArmLogger.Debug($"Detailed performance metrics:\n{report}");
            }
        }

        private static void RecordMetric(string name, long value)
        {
            if (!performanceMetrics.ContainsKey(name))
                performanceMetrics[name] = new List<long>();
                
            performanceMetrics[name].Add(value);
        }

        private static void TakeMemorySnapshot(string name)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            
            memorySnapshots[name] = GC.GetTotalMemory(true);
        }

        private static long GetMemoryDelta(string from, string to)
        {
            if (!memorySnapshots.ContainsKey(from) || !memorySnapshots.ContainsKey(to))
                return 0;
                
            return memorySnapshots[to] - memorySnapshots[from];
        }

        // Helper method to check if a think node contains a specific job giver type
        private static bool ContainsJobGiver(ThinkNode node, Type jobGiverType)
        {
            if (node == null) return false;
            if (node.GetType() == jobGiverType) return true;
            
            var subNodes = node.subNodes;
            if (subNodes != null)
            {
                foreach (var subNode in subNodes)
                {
                    if (ContainsJobGiver(subNode, jobGiverType))
                        return true;
                }
            }
            return false;
        }
    }
}
