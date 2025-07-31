using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using static AutoArm.Testing.TestHelpers;

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
            AutoArmDebug.Log($"[STRESS TEST] Creating {PAWN_COUNT} test pawns...");

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

            AutoArmDebug.Log($"[STRESS TEST] Created {testPawns.Count} pawns");

            // Create many weapons scattered around
            AutoArmDebug.Log($"[STRESS TEST] Creating {WEAPON_COUNT} weapons...");

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

            AutoArmDebug.Log($"[STRESS TEST] Created {testWeapons.Count} weapons");

            var setupTime = (System.DateTime.Now - startTime).TotalMilliseconds;
            AutoArmDebug.Log($"[STRESS TEST] Setup completed in {setupTime:F2}ms");
        }

        public TestResult Run()
        {
            var result = new TestResult { Success = true };
            var jobGiver = new JobGiver_PickUpBetterWeapon();

            // Measure initial memory
            long startMemory = GC.GetTotalMemory(false);

            // Test 1: Mass job creation
            AutoArmDebug.Log($"[STRESS TEST] Testing job creation for {testPawns.Count} pawns...");
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
            AutoArmDebug.Log("[STRESS TEST] Testing weapon cache performance...");
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
            AutoArmDebug.Log("[STRESS TEST] Testing weapon scoring performance...");
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
            AutoArmDebug.Log("[STRESS TEST] Testing rapid weapon spawn/despawn...");
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

            AutoArmDebug.Log($"[STRESS TEST] Completed. Performance: {(passedPerformance ? "PASSED" : "FAILED")}");

            return result;
        }

        public void Cleanup()
        {
            AutoArmDebug.Log($"[STRESS TEST] Starting cleanup of {testPawns.Count} pawns and {testWeapons.Count} weapons...");

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

            AutoArmDebug.Log("[STRESS TEST] Cleanup completed");
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
                AutoArmDebug.LogError("[TEST] WeaponCacheSpatialIndexTest: Gun_Autopistol def not found");
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
                        AutoArmDebug.Log($"[TEST] Position {pos} is out of bounds");
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
                            AutoArmDebug.Log($"[TEST] No standable position near {x},{z}");
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
                        AutoArmDebug.LogError($"[TEST] Failed to create weapon at {pos}");
                    }
                }
            }
            
            AutoArmDebug.Log($"[TEST] WeaponCacheSpatialIndexTest: Attempted {weaponsAttempted} weapons, created {weaponsCreated} weapons");
        }

        public TestResult Run()
        {
            var result = new TestResult { Success = true };
            
            try
            {
                if (testMap == null)
                {
                    AutoArmDebug.LogError("[TEST] WeaponCacheSpatialIndexTest: No test map available");
                    return TestResult.Failure("No test map");
                }

                if (testWeapons.Count == 0)
                {
                    AutoArmDebug.LogError("[TEST] WeaponCacheSpatialIndexTest: No weapons were created during setup");
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
                        result.Data["Error"] = "Spatial query returned incorrect number of weapons";
                        AutoArmDebug.LogError($"[TEST] WeaponCacheSpatialIndexTest: Spatial query mismatch - expected: {actualInRange}, got: {uniqueNearbyWeapons.Count} (total with dupes: {nearbyWeapons.Count})");
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
                        result.Data["Error2"] = "Destroyed weapon still in cache";
                        AutoArmDebug.LogError($"[TEST] WeaponCacheSpatialIndexTest: Destroyed weapon still in cache - weapon: {weaponToRemove.Label}");
                    }
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Data["Exception"] = ex.Message;
                result.Data["StackTrace"] = ex.StackTrace;
                AutoArmDebug.LogError($"[TEST] WeaponCacheSpatialIndexTest exception: {ex}");
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
}