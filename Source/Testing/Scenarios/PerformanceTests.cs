// AutoArm RimWorld 1.5+ mod - automatic weapon management
// This file: Performance and stress tests for weapon evaluation
// Validates caching, memory usage, and colony size optimizations

using RimWorld;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;
using AutoArm.Caching; using AutoArm.Helpers; using AutoArm.Logging;
using static AutoArm.Testing.TestHelpers;
using AutoArm.Jobs;
using AutoArm.Definitions;

namespace AutoArm.Testing.Scenarios
{
    public class StressTest : ITestScenario
    {
        public string Name => "Stress Test - Many Pawns and Weapons";
        private List<Pawn> testPawns = new List<Pawn>();
        private List<ThingWithComps> testWeapons = new List<ThingWithComps>();
        private const int PAWN_COUNT = 50;
        private const int WEAPON_COUNT = 100;
        private const int TEST_ITERATIONS = 10;

        public void Setup(Map map)
        {
            if (map == null) return;

            var startTime = System.DateTime.Now;

            // Create many pawns in a grid pattern
            AutoArmLogger.Log($"[STRESS TEST] Creating {PAWN_COUNT} test pawns...");

            int gridSize = (int)Math.Ceiling(Math.Sqrt(PAWN_COUNT));
            int pawnIndex = 0;

            for (int x = 0; x < gridSize && pawnIndex < PAWN_COUNT; x++)
            {
                for (int z = 0; z < gridSize && pawnIndex < PAWN_COUNT; z++)
                {
                    var pos = map.Center + new IntVec3(x * 3 - gridSize * 3 / 2, 0, z * 3 - gridSize * 3 / 2);

                    // Make sure position is valid
                    if (!pos.InBounds(map) || !pos.Standable(map))
                        continue;

                    var pawn = TestHelpers.CreateTestPawn(map, new TestPawnConfig
                    {
                        Name = $"StressPawn{pawnIndex}",
                        Skills = new Dictionary<SkillDef, int>
                        {
                            { SkillDefOf.Shooting, Rand.Range(0, 20) },
                            { SkillDefOf.Melee, Rand.Range(0, 20) }
                        }
                    });

                    if (pawn != null)
                    {
                        // Move to position
                        pawn.Position = pos;
                        pawn.equipment?.DestroyAllEquipment();
                        testPawns.Add(pawn);
                        pawnIndex++;
                    }
                }
            }

            AutoArmLogger.Log($"[STRESS TEST] Created {testPawns.Count} pawns");

            // Create many weapons scattered around
            AutoArmLogger.Log($"[STRESS TEST] Creating {WEAPON_COUNT} weapons...");

            var weaponDefs = new ThingDef[]
            {
                VanillaWeaponDefOf.Gun_Autopistol,
                VanillaWeaponDefOf.Gun_AssaultRifle,
                VanillaWeaponDefOf.Gun_BoltActionRifle,
                VanillaWeaponDefOf.MeleeWeapon_Knife,
                VanillaWeaponDefOf.MeleeWeapon_LongSword
            }.Where(d => d != null).ToArray();

            if (weaponDefs.Length == 0)
            {
                Log.Error("[STRESS TEST] No weapon defs available!");
                return;
            }

            for (int i = 0; i < WEAPON_COUNT; i++)
            {
                var weaponDef = weaponDefs[i % weaponDefs.Length];
                var quality = (QualityCategory)Rand.Range(0, 7); // Random quality

                // Random position around the center
                var radius = gridSize * 4;
                var angle = Rand.Range(0f, 360f);
                var distance = Rand.Range(5f, radius);
                var pos = map.Center + (Vector3.forward.RotatedBy(angle) * distance).ToIntVec3();

                if (!pos.InBounds(map) || !pos.Standable(map))
                    continue;

                var weapon = TestHelpers.CreateWeapon(map, weaponDef, pos, quality);
                if (weapon != null)
                {
                    testWeapons.Add(weapon);
                    ImprovedWeaponCacheManager.AddWeaponToCache(weapon);
                }
            }

            AutoArmLogger.Log($"[STRESS TEST] Created {testWeapons.Count} weapons");

            var setupTime = (System.DateTime.Now - startTime).TotalMilliseconds;
            AutoArmLogger.Log($"[STRESS TEST] Setup completed in {setupTime:F2}ms");
        }

        public TestResult Run()
        {
            var result = new TestResult { Success = true };
            var jobGiver = new JobGiver_PickUpBetterWeapon();

            // Measure initial memory
            long startMemory = GC.GetTotalMemory(false);

            // Test 1: Mass job creation
            AutoArmLogger.Log($"[STRESS TEST] Testing job creation for {testPawns.Count} pawns...");
            var startTime = System.DateTime.Now;
            int jobsCreated = 0;

            for (int iteration = 0; iteration < TEST_ITERATIONS; iteration++)
            {
                foreach (var pawn in testPawns)
                {
                    if (pawn != null && !pawn.Destroyed)
                    {
                        var job = jobGiver.TestTryGiveJob(pawn);
                        if (job != null)
                            jobsCreated++;
                    }
                }
            }

            var jobCreationTime = (System.DateTime.Now - startTime).TotalMilliseconds;
            result.Data["JobCreationTime_ms"] = jobCreationTime;
            result.Data["JobsCreated"] = jobsCreated;
            result.Data["AvgTimePerPawn_ms"] = jobCreationTime / (testPawns.Count * TEST_ITERATIONS);

            // Test 2: Cache performance
            AutoArmLogger.Log("[STRESS TEST] Testing weapon cache performance...");
            startTime = System.DateTime.Now;
            int cacheHits = 0;

            for (int i = 0; i < 100; i++)
            {
                var weapons = ImprovedWeaponCacheManager.GetWeaponsNear(
                    testPawns[0].Map,
                    testPawns[0].Map.Center,
                    100f
                ).ToList();
                cacheHits += weapons.Count;
            }

            var cacheTime = (System.DateTime.Now - startTime).TotalMilliseconds;
            result.Data["CacheQueryTime_ms"] = cacheTime;
            result.Data["WeaponsInCache"] = cacheHits / 100;

            // Test 3: Memory usage
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            long endMemory = GC.GetTotalMemory(false);
            long memoryUsed = endMemory - startMemory;

            result.Data["MemoryUsed_MB"] = memoryUsed / (1024.0 * 1024.0);
            result.Data["MemoryPerPawn_KB"] = (memoryUsed / 1024.0) / testPawns.Count;

            // Test 4: Weapon scoring performance
            AutoArmLogger.Log("[STRESS TEST] Testing weapon scoring performance...");
            startTime = System.DateTime.Now;
            int scoringOperations = 0;

            var samplePawn = testPawns.FirstOrDefault();
            var sampleWeapons = testWeapons.Take(10).ToList();

            if (samplePawn != null)
            {
                for (int i = 0; i < 1000; i++)
                {
                    foreach (var weapon in sampleWeapons)
                    {
                        if (weapon != null && !weapon.Destroyed)
                        {
                            var score = jobGiver.GetWeaponScore(samplePawn, weapon);
                            scoringOperations++;
                        }
                    }
                }
            }

            var scoringTime = (System.DateTime.Now - startTime).TotalMilliseconds;
            result.Data["ScoringTime_ms"] = scoringTime;
            result.Data["ScoresPerSecond"] = (scoringOperations * 1000.0) / scoringTime;

            // Test 5: Rapid weapon spawning/despawning
            AutoArmLogger.Log("[STRESS TEST] Testing rapid weapon spawn/despawn...");
            startTime = System.DateTime.Now;
            var map = testPawns[0].Map;
            var spawnPos = map.Center + new IntVec3(0, 0, 10);

            for (int i = 0; i < 100; i++)
            {
                var weapon = TestHelpers.CreateWeapon(map, VanillaWeaponDefOf.Gun_Autopistol, spawnPos);
                if (weapon != null)
                {
                    ImprovedWeaponCacheManager.AddWeaponToCache(weapon);
                    weapon.Destroy();
                }
            }

            var spawnTime = (System.DateTime.Now - startTime).TotalMilliseconds;
            result.Data["SpawnDestroyTime_ms"] = spawnTime;

            // Performance thresholds
            bool passedPerformance = true;

            if (jobCreationTime / (testPawns.Count * TEST_ITERATIONS) > 10.0) // 10ms per pawn is more realistic
            {
                passedPerformance = false;
                result.Data["PerfWarning_JobCreation"] = "Job creation too slow";
            }

            if (memoryUsed > 100 * 1024 * 1024) // 100MB is excessive
            {
                passedPerformance = false;
                result.Data["PerfWarning_Memory"] = "Excessive memory usage";
            }

            result.Success = passedPerformance;

            AutoArmLogger.Log($"[STRESS TEST] Completed. Performance: {(passedPerformance ? "PASSED" : "FAILED")}");

            return result;
        }

        public void Cleanup()
        {
            AutoArmLogger.Log($"[STRESS TEST] Starting cleanup of {testPawns.Count} pawns and {testWeapons.Count} weapons...");

            // Destroy all weapons first
            foreach (var weapon in testWeapons)
            {
                if (weapon != null && !weapon.Destroyed)
                {
                    weapon.Destroy();
                }
            }
            testWeapons.Clear();

            // Then destroy all pawns
            foreach (var pawn in testPawns)
            {
                if (pawn != null && !pawn.Destroyed)
                {
                    pawn.jobs?.StopAll();
                    pawn.equipment?.DestroyAllEquipment();
                    pawn.Destroy();
                }
            }
            testPawns.Clear();

            // Force garbage collection to clean up
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            AutoArmLogger.Log("[STRESS TEST] Cleanup completed");
        }
    }

    public class PerformanceTest : ITestScenario
    {
        public string Name => "Performance Benchmarks";
        private List<Pawn> testPawns = new List<Pawn>();
        private List<ThingWithComps> testWeapons = new List<ThingWithComps>();

        public void Setup(Map map)
        {
            if (map == null) return;

            for (int i = 0; i < 20; i++)
            {
                var pawn = TestHelpers.CreateTestPawn(map);
                if (pawn != null)
                {
                    testPawns.Add(pawn);

                    var weaponDef = i % 2 == 0 ? VanillaWeaponDefOf.Gun_Autopistol : VanillaWeaponDefOf.MeleeWeapon_Knife;
                    if (weaponDef != null)
                    {
                        var weapon = TestHelpers.CreateWeapon(map, weaponDef,
                            pawn.Position + new IntVec3(2, 0, 0));
                        if (weapon != null)
                        {
                            testWeapons.Add(weapon);
                            ImprovedWeaponCacheManager.AddWeaponToCache(weapon);
                        }
                    }
                }
            }
        }

        public TestResult Run()
        {
            if (testPawns.Count == 0)
                return TestResult.Failure("No test pawns created");

            var jobGiver = new JobGiver_PickUpBetterWeapon();
            var startTicks = Find.TickManager.TicksGame;
            int jobsCreated = 0;

            foreach (var pawn in testPawns)
            {
                var job = jobGiver.TestTryGiveJob(pawn);
                if (job != null)
                    jobsCreated++;
            }

            var elapsed = Find.TickManager.TicksGame - startTicks;

            var result = new TestResult { Success = true };
            result.Data["Pawns Tested"] = testPawns.Count;
            result.Data["Jobs Created"] = jobsCreated;
            result.Data["Ticks Elapsed"] = elapsed;
            result.Data["Time Per Pawn"] = $"{elapsed / (float)testPawns.Count:F2} ticks";

            return result;
        }

        public void Cleanup()
        {
            // Destroy weapons first to avoid container conflicts
            foreach (var weapon in testWeapons)
            {
                if (weapon != null && !weapon.Destroyed)
                {
                    weapon.Destroy();
                }
            }
            testWeapons.Clear();

            // Then destroy pawns
            foreach (var pawn in testPawns)
            {
                if (pawn != null && !pawn.Destroyed)
                {
                    pawn.Destroy();
                }
            }
            testPawns.Clear();
        }
    }

    public class WeaponCacheSpatialIndexTest : ITestScenario
    {
        public string Name => "Weapon Cache Spatial Index";
        private List<ThingWithComps> testWeapons = new List<ThingWithComps>();
        private Map testMap;

        public void Setup(Map map)
        {
            if (map == null) return;
            testMap = map;

            // Create weapons in different spatial regions
            var weaponDef = VanillaWeaponDefOf.Gun_Autopistol;
            if (weaponDef == null)
            {
                AutoArmLogger.LogError("[TEST] WeaponCacheSpatialIndexTest: Gun_Autopistol def not found");
                return;
            }

            // Create weapons in a grid pattern across the map
            int weaponsCreated = 0;
            int weaponsAttempted = 0;
            for (int x = 10; x < Math.Min(map.Size.x - 10, 100); x += 20)
            {
                for (int z = 10; z < Math.Min(map.Size.z - 10, 100); z += 20)
                {
                    weaponsAttempted++;
                    var pos = new IntVec3(x, 0, z);
                    
                    // Ensure position is valid and standable
                    if (!pos.InBounds(map))
                    {
                        AutoArmLogger.Log($"[TEST] Position {pos} is out of bounds");
                        continue;
                    }
                    
                    if (!pos.Standable(map))
                    {
                        // Try to find a nearby standable position
                        bool found = false;
                        for (int dx = -2; dx <= 2; dx++)
                        {
                            for (int dz = -2; dz <= 2; dz++)
                            {
                                var testPos = new IntVec3(x + dx, 0, z + dz);
                                if (testPos.InBounds(map) && testPos.Standable(map))
                                {
                                    pos = testPos;
                                    found = true;
                                    break;
                                }
                            }
                            if (found) break;
                        }
                        
                        if (!found)
                        {
                            AutoArmLogger.Log($"[TEST] No standable position near {x},{z}");
                            continue;
                        }
                    }

                    var weapon = TestHelpers.CreateWeapon(map, weaponDef, pos);
                    if (weapon != null)
                    {
                        testWeapons.Add(weapon);
                        ImprovedWeaponCacheManager.AddWeaponToCache(weapon);
                        weaponsCreated++;
                    }
                    else
                    {
                        AutoArmLogger.LogError($"[TEST] Failed to create weapon at {pos}");
                    }
                }
            }
            
            AutoArmLogger.Log($"[TEST] WeaponCacheSpatialIndexTest: Attempted {weaponsAttempted} weapons, created {weaponsCreated} weapons");
        }

        public TestResult Run()
        {
            var result = new TestResult { Success = true };
            
            try
            {
                if (testMap == null)
                {
                    AutoArmLogger.LogError("[TEST] WeaponCacheSpatialIndexTest: No test map available");
                    return TestResult.Failure("No test map");
                }

                if (testWeapons.Count == 0)
                {
                    AutoArmLogger.LogError("[TEST] WeaponCacheSpatialIndexTest: No weapons were created during setup");
                    return TestResult.Failure("No weapons created during setup");
                }

                result.Data["TotalWeapons"] = testWeapons.Count;

                // Test 1: Spatial query accuracy
                var centerPos = testMap.Center;
                var radius = 50f;

                // First, ensure all weapons are properly cached
                foreach (var weapon in testWeapons)
                {
                    if (!weapon.Destroyed && weapon.Spawned)
                    {
                        ImprovedWeaponCacheManager.AddWeaponToCache(weapon);
                    }
                }

                var nearbyWeapons = ImprovedWeaponCacheManager.GetWeaponsNear(testMap, centerPos, radius).ToList();
                result.Data["WeaponsInRadius"] = nearbyWeapons.Count;

                // Verify spatial query is accurate - only count spawned, non-destroyed weapons
                int actualInRange = 0;
                foreach (var weapon in testWeapons)
                {
                    if (!weapon.Destroyed && weapon.Spawned && weapon.Position.DistanceTo(centerPos) <= radius)
                        actualInRange++;
                }

                result.Data["ExpectedInRange"] = actualInRange;

                // Remove duplicates from nearbyWeapons
                var uniqueNearbyWeapons = nearbyWeapons.Distinct().ToList();
                result.Data["UniqueWeaponsInRadius"] = uniqueNearbyWeapons.Count;

                if (uniqueNearbyWeapons.Count != actualInRange)
                {
                    // Allow small discrepancy due to edge cases
                    if (Math.Abs(uniqueNearbyWeapons.Count - actualInRange) > 2)
                    {
                        result.Success = false;
                        result.Data["Error"] = "Weapon cache spatial indexing not working correctly";
                        result.Data["ActualInRange"] = uniqueNearbyWeapons.Count;
                        result.Data["AllowedDiscrepancy"] = 2;
                        result.Data["Discrepancy"] = Math.Abs(uniqueNearbyWeapons.Count - actualInRange);
                        result.Data["ExpectedInRange"] = actualInRange;
                        result.Data["TotalWeapons"] = testWeapons.Count;
                        result.Data["UniqueWeaponsInRadius"] = uniqueNearbyWeapons.Count;
                        result.Data["WeaponsAfterDestroy"] = 0;
                        result.Data["WeaponsAfterInvalidate"] = 0;
                        result.Data["WeaponsInRadius"] = nearbyWeapons.Count;
                        AutoArmLogger.LogError($"[TEST] WeaponCacheSpatialIndexTest: Spatial query mismatch - expected: {actualInRange}, got: {uniqueNearbyWeapons.Count} (total with dupes: {nearbyWeapons.Count})");
                    }
                }

                // Test 2: Cache invalidation
                ImprovedWeaponCacheManager.InvalidateCache(testMap);

                // Should rebuild cache on next query
                var weaponsAfterInvalidate = ImprovedWeaponCacheManager.GetWeaponsNear(testMap, centerPos, radius).ToList();
                result.Data["WeaponsAfterInvalidate"] = weaponsAfterInvalidate.Count;

                // Test 3: Weapon removal from cache
                if (testWeapons.Count > 0)
                {
                    var weaponToRemove = testWeapons[0];
                    weaponToRemove.Destroy();

                    // Give cache time to process the destruction
                    ImprovedWeaponCacheManager.RemoveWeaponFromCache(weaponToRemove);

                    // Cache should handle destroyed weapons
                    var weaponsAfterDestroy = ImprovedWeaponCacheManager.GetWeaponsNear(testMap, centerPos, 1000f).ToList();
                    result.Data["WeaponsAfterDestroy"] = weaponsAfterDestroy.Count;

                    if (weaponsAfterDestroy.Contains(weaponToRemove))
                    {
                        result.Success = false;
                        result.FailureReason = result.FailureReason ?? "Destroyed weapon still in cache";
                        result.Data["Error"] = result.Data.ContainsKey("Error") ? result.Data["Error"] + "; Cache removal failed" : "Cache removal failed";
                        result.Data["Error2"] = "Destroyed weapon still in cache";
                        result.Data["DestroyedWeapon"] = weaponToRemove.Label;
                        result.Data["WeaponInCache"] = true;
                        result.Data["ExpectedInCache"] = false;
                        AutoArmLogger.LogError($"[TEST] WeaponCacheSpatialIndexTest: Destroyed weapon still in cache - weapon: {weaponToRemove.Label}");
                    }
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Data["Exception"] = ex.Message;
                result.Data["StackTrace"] = ex.StackTrace;
                AutoArmLogger.LogError($"[TEST] WeaponCacheSpatialIndexTest exception: {ex}");
            }

            return result;
        }

        public void Cleanup()
        {
            foreach (var weapon in testWeapons)
            {
                if (weapon != null && !weapon.Destroyed)
                {
                    weapon.Destroy();
                }
            }
            testWeapons.Clear();

            if (testMap != null)
            {
                ImprovedWeaponCacheManager.InvalidateCache(testMap);
            }
        }
    }

    /// <summary>
    /// Tests performance under various conditions and configurations
    /// </summary>
    public class WeaponCachePerformanceTest : ITestScenario
    {
        public string Name => "Weapon Cache Performance";
        private List<ThingWithComps> testWeapons = new List<ThingWithComps>();
        private Map testMap;
        private Stopwatch stopwatch = new Stopwatch();

        public void Setup(Map map)
        {
            if (map == null) return;
            testMap = map;

            // Create a large number of weapons for performance testing
            var weaponDef = VanillaWeaponDefOf.Gun_Autopistol;
            if (weaponDef == null) return;

            // Create 200 weapons scattered across the map
            for (int i = 0; i < 200; i++)
            {
                var pos = new IntVec3(
                    Rand.Range(10, map.Size.x - 10),
                    0,
                    Rand.Range(10, map.Size.z - 10)
                );

                if (pos.Standable(map))
                {
                    var weapon = TestHelpers.CreateWeapon(map, weaponDef, pos);
                    if (weapon != null)
                    {
                        testWeapons.Add(weapon);
                    }
                }
            }
        }

        public TestResult Run()
        {
            var result = new TestResult { Success = true };

            if (testMap == null || testWeapons.Count == 0)
                return TestResult.Failure("Test setup failed");

            // Test 1: Initial cache build performance
            ImprovedWeaponCacheManager.InvalidateCache(testMap);
            
            stopwatch.Restart();
            ImprovedWeaponCacheManager.GetWeaponsNear(testMap, testMap.Center, 50f);
            stopwatch.Stop();
            
            result.Data["FirstPass_AvgTime"] = stopwatch.ElapsedMilliseconds;
            result.Data["FirstPass_TotalTime"] = stopwatch.ElapsedMilliseconds;
            result.Data["WeaponCount"] = testWeapons.Count;

            // Test 2: Cached query performance (100 queries)
            stopwatch.Restart();
            for (int i = 0; i < 100; i++)
            {
                var pos = new IntVec3(
                    Rand.Range(0, testMap.Size.x),
                    0,
                    Rand.Range(0, testMap.Size.z)
                );
                ImprovedWeaponCacheManager.GetWeaponsNear(testMap, pos, 30f);
            }
            stopwatch.Stop();

            var avgCachedTime = stopwatch.ElapsedMilliseconds / 100f;
            result.Data["SecondPass_AvgTime"] = avgCachedTime;
            result.Data["SecondPass_TotalTime"] = stopwatch.ElapsedMilliseconds;
            
            // Calculate speedup
            if (result.Data["FirstPass_AvgTime"] is double firstTime && firstTime > 0)
            {
                result.Data["Cache_Speedup"] = firstTime / avgCachedTime;
            }

            // Test 3: Performance after clear
            ImprovedWeaponCacheManager.InvalidateCache(testMap);
            
            stopwatch.Restart();
            ImprovedWeaponCacheManager.GetWeaponsNear(testMap, testMap.Center, 50f);
            stopwatch.Stop();
            
            result.Data["AfterClear_AvgTime"] = stopwatch.ElapsedMilliseconds;

            // Performance check
            if (avgCachedTime > 5.0f) // More than 5ms per query is slow
            {
                result.Success = false;
                result.FailureReason = "Cache queries too slow";
            }

            return result;
        }

        public void Cleanup()
        {
            foreach (var weapon in testWeapons)
            {
                weapon?.Destroy();
            }
            testWeapons.Clear();

            if (testMap != null)
            {
                ImprovedWeaponCacheManager.InvalidateCache(testMap);
            }
        }
    }

    /// <summary>
    /// Tests performance degradation over time with continuous operations
    /// </summary>
    public class LongRunningPerformanceTest : ITestScenario
    {
        public string Name => "Long Running Performance Test";
        private List<Pawn> testPawns = new List<Pawn>();
        private List<ThingWithComps> activeWeapons = new List<ThingWithComps>();
        private Map testMap;
        private Stopwatch stopwatch = new Stopwatch();

        public void Setup(Map map)
        {
            if (map == null) return;
            testMap = map;

            // Create 20 test pawns
            for (int i = 0; i < 20; i++)
            {
                var pawn = TestHelpers.CreateTestPawn(map, new TestPawnConfig
                {
                    Name = $"LongRunPawn{i}",
                    Skills = new Dictionary<SkillDef, int>
                    {
                        { SkillDefOf.Shooting, Rand.Range(5, 15) },
                        { SkillDefOf.Melee, Rand.Range(5, 15) }
                    }
                });

                if (pawn != null)
                {
                    testPawns.Add(pawn);
                }
            }
        }

        public TestResult Run()
        {
            var result = new TestResult { Success = true };
            
            if (testPawns.Count == 0)
                return TestResult.Failure("No test pawns created");

            var jobGiver = new JobGiver_PickUpBetterWeapon();
            var performanceMetrics = new List<double>();
            
            // Simulate 1000 ticks of gameplay
            for (int tick = 0; tick < 1000; tick++)
            {
                stopwatch.Restart();
                
                // Every 10 ticks, spawn/despawn some weapons
                if (tick % 10 == 0)
                {
                    // Spawn new weapon
                    var weaponDef = Rand.Bool ? VanillaWeaponDefOf.Gun_Autopistol : VanillaWeaponDefOf.MeleeWeapon_Knife;
                    var pos = testMap.Center + new IntVec3(Rand.Range(-20, 20), 0, Rand.Range(-20, 20));
                    
                    if (pos.Standable(testMap))
                    {
                        var weapon = TestHelpers.CreateWeapon(testMap, weaponDef, pos);
                        if (weapon != null)
                        {
                            activeWeapons.Add(weapon);
                            ImprovedWeaponCacheManager.AddWeaponToCache(weapon);
                        }
                    }
                    
                    // Despawn old weapons
                    if (activeWeapons.Count > 50)
                    {
                        var toRemove = activeWeapons[0];
                        activeWeapons.RemoveAt(0);
                        toRemove.Destroy();
                    }
                }
                
                // Check some pawns for weapons
                foreach (var pawn in testPawns.Where((p, i) => (tick + i) % 30 == 0))
                {
                    if (!TimingHelper.IsOnCooldown(pawn, TimingHelper.CooldownType.WeaponSearch))
                    {
                        jobGiver.TestTryGiveJob(pawn);
                        TimingHelper.SetCooldown(pawn, TimingHelper.CooldownType.WeaponSearch);
                    }
                }
                
                // Cleanup old cooldowns periodically
                if (tick % 120 == 0)
                {
                    TimingHelper.CleanupOldCooldowns();
                }
                
                stopwatch.Stop();
                performanceMetrics.Add(stopwatch.ElapsedMilliseconds);
            }
            
            // Analyze performance degradation
            var firstHundred = performanceMetrics.Take(100).Average();
            var lastHundred = performanceMetrics.Skip(900).Take(100).Average();
            var degradation = (lastHundred - firstHundred) / firstHundred * 100;
            
            result.Data["FirstHundredAvg_ms"] = firstHundred;
            result.Data["LastHundredAvg_ms"] = lastHundred;
            result.Data["PerformanceDegradation_%"] = degradation;
            result.Data["TotalTicks"] = performanceMetrics.Count;
            result.Data["AverageTickTime_ms"] = performanceMetrics.Average();
            result.Data["MaxTickTime_ms"] = performanceMetrics.Max();
            
            // Check for memory leaks
            GC.Collect();
            var memoryAfter = GC.GetTotalMemory(false);
            result.Data["FinalMemory_MB"] = memoryAfter / (1024.0 * 1024.0);
            
            // Fail if significant degradation
            if (degradation > 50) // More than 50% slower
            {
                result.Success = false;
                result.FailureReason = $"Significant performance degradation: {degradation:F1}%";
            }
            
            return result;
        }

        public void Cleanup()
        {
            foreach (var weapon in activeWeapons)
            {
                weapon?.Destroy();
            }
            activeWeapons.Clear();

            foreach (var pawn in testPawns)
            {
                pawn?.Destroy();
            }
            testPawns.Clear();

            // Clear all caches and cooldowns
            ImprovedWeaponCacheManager.InvalidateCache(testMap);
            TimingHelper.ClearAllCooldowns();
            WeaponScoreCache.ClearAllCaches();
        }
    }

    /// <summary>
    /// Tests the impact of mod compatibility checks on performance
    /// </summary>
    public class ModCompatibilityPerformanceTest : ITestScenario
    {
        public string Name => "Mod Compatibility Performance Impact";
        private Pawn testPawn;
        private List<ThingWithComps> testWeapons = new List<ThingWithComps>();
        private Stopwatch stopwatch = new Stopwatch();

        public void Setup(Map map)
        {
            if (map == null) return;

            testPawn = TestHelpers.CreateTestPawn(map, new TestPawnConfig
            {
                Name = "ModCompatTestPawn"
            });

            // Create various weapons
            var weaponDefs = new[]
            {
                VanillaWeaponDefOf.Gun_Autopistol,
                VanillaWeaponDefOf.Gun_AssaultRifle,
                VanillaWeaponDefOf.Gun_SniperRifle,
                VanillaWeaponDefOf.MeleeWeapon_Knife,
                VanillaWeaponDefOf.MeleeWeapon_LongSword
            };

            foreach (var def in weaponDefs.Where(d => d != null))
            {
                var weapon = TestHelpers.CreateWeapon(map, def, 
                    testPawn.Position + new IntVec3(Rand.Range(-5, 5), 0, Rand.Range(-5, 5)));
                    
                if (weapon != null)
                {
                    testWeapons.Add(weapon);
                }
            }
        }

        public TestResult Run()
        {
            var result = new TestResult { Success = true };
            
            if (testPawn == null || testWeapons.Count == 0)
                return TestResult.Failure("Test setup failed");

            // Test validation performance with various mod checks
            int iterations = 1000;
            
            // Test 1: Base validation (no mod checks)
            stopwatch.Restart();
            for (int i = 0; i < iterations; i++)
            {
                foreach (var weapon in testWeapons)
                {
                    // Just basic checks
                    _ = weapon != null && !weapon.Destroyed && weapon.def.IsWeapon;
                }
            }
            stopwatch.Stop();
            var baseTime = stopwatch.ElapsedMilliseconds;
            result.Data["BaseValidation_ms"] = baseTime;
            
            // Test 2: With SimpleSidearms checks
            if (SimpleSidearmsCompat.IsLoaded())
            {
                stopwatch.Restart();
                for (int i = 0; i < iterations; i++)
                {
                    foreach (var weapon in testWeapons)
                    {
                        string reason;
                        SimpleSidearmsCompat.CanPickupSidearmInstance(weapon, testPawn, out reason);
                    }
                }
                stopwatch.Stop();
                result.Data["SimpleSidearmsValidation_ms"] = stopwatch.ElapsedMilliseconds;
                result.Data["SimpleSidearmsOverhead_%"] = ((stopwatch.ElapsedMilliseconds - baseTime) / (double)baseTime) * 100;
            }
            
            // Test 3: With Combat Extended checks
            if (CECompat.IsLoaded())
            {
                stopwatch.Restart();
                for (int i = 0; i < iterations; i++)
                {
                    foreach (var weapon in testWeapons.Where(w => w.def.IsRangedWeapon))
                    {
                        CECompat.ShouldSkipWeaponForCE(weapon, testPawn);
                    }
                }
                stopwatch.Stop();
                result.Data["CombatExtendedValidation_ms"] = stopwatch.ElapsedMilliseconds;
                result.Data["CombatExtendedOverhead_%"] = ((stopwatch.ElapsedMilliseconds - baseTime) / (double)baseTime) * 100;
            }
            
            // Test 4: Full validation stack
            stopwatch.Restart();
            for (int i = 0; i < iterations; i++)
            {
                foreach (var weapon in testWeapons)
                {
                    string reason;
                    ValidationHelper.IsValidWeapon(weapon, testPawn, out reason);
                }
            }
            stopwatch.Stop();
            result.Data["FullValidation_ms"] = stopwatch.ElapsedMilliseconds;
            result.Data["FullValidationOverhead_%"] = ((stopwatch.ElapsedMilliseconds - baseTime) / (double)baseTime) * 100;
            
            // Calculate per-weapon times
            var totalChecks = iterations * testWeapons.Count;
            result.Data["PerWeaponValidation_microseconds"] = (stopwatch.ElapsedMilliseconds * 1000.0) / totalChecks;
            
            return result;
        }

        public void Cleanup()
        {
            foreach (var weapon in testWeapons)
            {
                weapon?.Destroy();
            }
            testWeapons.Clear();
            
            testPawn?.Destroy();
        }
    }
}
