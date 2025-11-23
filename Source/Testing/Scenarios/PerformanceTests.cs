using AutoArm.Caching;
using AutoArm.Definitions;
using AutoArm.Jobs;
using AutoArm.Testing.Helpers;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Verse;

namespace AutoArm.Testing.Scenarios
{
    public class StressTest : ITestScenario
    {
        public string Name => "Stress Test - Large Scale Operations";
        private List<Pawn> testPawns = new List<Pawn>();
        private List<ThingWithComps> testWeapons = new List<ThingWithComps>();
        private const int PAWN_COUNT = 50;
        private const int WEAPON_COUNT = 100;
        private const int TEST_ITERATIONS = 5;

        public void Setup(Map map)
        {
            if (map == null) return;

            var setupStopwatch = Stopwatch.StartNew();
            TestRunner.TestLog($"[STRESS TEST] Starting setup with {PAWN_COUNT} pawns and {WEAPON_COUNT} weapons");

            int gridSize = (int)Math.Ceiling(Math.Sqrt(PAWN_COUNT));
            int pawnIndex = 0;

            for (int x = 0; x < gridSize && pawnIndex < PAWN_COUNT; x++)
            {
                for (int z = 0; z < gridSize && pawnIndex < PAWN_COUNT; z++)
                {
                    var pos = map.Center + new IntVec3(x * 3 - gridSize * 3 / 2, 0, z * 3 - gridSize * 3 / 2);

                    if (!pos.InBounds(map) || !pos.Standable(map))
                        continue;

                    var config = new TestHelpers.TestPawnConfig
                    {
                        Name = $"StressPawn{pawnIndex}",
                        Skills = new Dictionary<SkillDef, int>
                        {
                            { SkillDefOf.Shooting, Rand.Range(0, 20) },
                            { SkillDefOf.Melee, Rand.Range(0, 20) }
                        },
                        SpawnPosition = pos
                    };

                    var pawn = TestHelpers.CreateTestPawn(map, config);
                    if (pawn != null)
                    {
                        pawn.equipment?.DestroyAllEquipment();
                        testPawns.Add(pawn);
                        pawnIndex++;
                    }
                }
            }

            var weaponDefs = new[]
            {
                AutoArmDefOf.Gun_Autopistol,
                AutoArmDefOf.Gun_AssaultRifle,
                AutoArmDefOf.Gun_BoltActionRifle,
                AutoArmDefOf.MeleeWeapon_Knife,
                AutoArmDefOf.MeleeWeapon_LongSword
            }.Where(d => d != null).ToArray();

            if (weaponDefs.Length > 0)
            {
                for (int i = 0; i < WEAPON_COUNT; i++)
                {
                    var weaponDef = weaponDefs[i % weaponDefs.Length];
                    var quality = (QualityCategory)Rand.Range(0, 7);

                    var radius = gridSize * 4;
                    var pos = TestPositions.GetRandomPosition(map);

                    var weapon = TestHelpers.CreateWeapon(map, weaponDef, pos, quality);
                    if (weapon != null)
                    {
                        testWeapons.Add(weapon);
                        WeaponCacheManager.AddWeaponToCache(weapon);
                    }
                }
            }

            setupStopwatch.Stop();
            TestRunner.TestLog($"[STRESS TEST] Setup completed in {setupStopwatch.ElapsedMilliseconds}ms - Created {testPawns.Count} pawns, {testWeapons.Count} weapons");
        }

        public TestResult Run()
        {
            var result = new TestResult { Success = true };
            var jobGiver = new JobGiver_PickUpBetterWeapon();

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            long startMemory = GC.GetTotalMemory(true);

            TestRunner.TestLog($"[STRESS TEST] Testing job creation for {testPawns.Count} pawns");
            var jobStopwatch = Stopwatch.StartNew();
            int jobsCreated = 0;
            var jobTimes = new List<double>();

            for (int iteration = 0; iteration < TEST_ITERATIONS; iteration++)
            {
                var iterationStopwatch = Stopwatch.StartNew();
                foreach (var pawn in testPawns)
                {
                    if (pawn != null && !pawn.Destroyed)
                    {
                        var job = jobGiver.TestTryGiveJob(pawn);
                        if (job != null)
                            jobsCreated++;
                    }
                }
                iterationStopwatch.Stop();
                jobTimes.Add(iterationStopwatch.Elapsed.TotalMilliseconds);
            }

            jobStopwatch.Stop();
            result.Data["JobCreationTime_ms"] = jobStopwatch.Elapsed.TotalMilliseconds;
            result.Data["JobsCreated"] = jobsCreated;
            result.Data["AvgTimePerIteration_ms"] = jobTimes.Average();
            result.Data["AvgTimePerPawn_ms"] = jobTimes.Average() / testPawns.Count;

            TestRunner.TestLog("[STRESS TEST] Testing weapon cache performance");
            var cacheStopwatch = Stopwatch.StartNew();
            var cacheTimes = new List<double>();

            for (int i = 0; i < 50; i++)
            {
                var queryStopwatch = Stopwatch.StartNew();
                var weapons = WeaponCacheManager.GetAllWeapons(testPawns[0].Map).ToList();
                queryStopwatch.Stop();
                cacheTimes.Add(queryStopwatch.Elapsed.TotalMilliseconds);
            }

            cacheStopwatch.Stop();
            result.Data["CacheQueryTime_ms"] = cacheStopwatch.Elapsed.TotalMilliseconds;
            result.Data["AvgCacheQuery_ms"] = cacheTimes.Average();
            result.Data["WeaponsInRadius"] = WeaponCacheManager.GetAllWeapons(testPawns[0].Map).Count();

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            long endMemory = GC.GetTotalMemory(true);
            long memoryUsed = endMemory - startMemory;

            result.Data["MemoryUsed_MB"] = memoryUsed / (1024.0 * 1024.0);
            result.Data["MemoryPerPawn_KB"] = (memoryUsed / 1024.0) / testPawns.Count;

            TestRunner.TestLog("[STRESS TEST] Testing weapon scoring performance");
            var scoringStopwatch = Stopwatch.StartNew();
            int scoringOperations = 0;

            var samplePawn = testPawns.FirstOrDefault();
            var sampleWeapons = testWeapons.Take(10).ToList();

            if (samplePawn != null)
            {
                for (int i = 0; i < 100; i++)
                {
                    foreach (var weapon in sampleWeapons)
                    {
                        if (weapon != null && !weapon.Destroyed)
                        {
                            var score = WeaponCacheManager.GetCachedScore(samplePawn, weapon);
                            scoringOperations++;
                        }
                    }
                }
            }

            scoringStopwatch.Stop();
            result.Data["ScoringTime_ms"] = scoringStopwatch.Elapsed.TotalMilliseconds;
            result.Data["ScoresPerSecond"] = (scoringOperations * 1000.0) / scoringStopwatch.Elapsed.TotalMilliseconds;

            bool passedPerformance = true;
            var warnings = new List<string>();

            double avgTimePerPawn = jobTimes.Average() / testPawns.Count;
            if (avgTimePerPawn > 20.0)
            {
                warnings.Add($"Job creation slow: {avgTimePerPawn:F2}ms per pawn");
                if (avgTimePerPawn > 50.0)
                    passedPerformance = false;
            }

            double memoryMB = memoryUsed / (1024.0 * 1024.0);
            if (memoryMB > 50.0)
            {
                warnings.Add($"High memory usage: {memoryMB:F2}MB");
                if (memoryMB > 200.0)
                    passedPerformance = false;
            }

            double avgCacheTime = cacheTimes.Average();
            if (avgCacheTime > 10.0)
            {
                warnings.Add($"Cache queries slow: {avgCacheTime:F2}ms average");
                if (avgCacheTime > 50.0)
                    passedPerformance = false;
            }

            result.Success = passedPerformance;
            if (warnings.Count > 0)
            {
                result.Data["Warnings"] = string.Join(", ", warnings);
            }

            TestRunner.TestLog($"[STRESS TEST] Completed. Performance: {(passedPerformance ? "PASSED" : "FAILED")}");
            return result;
        }

        public void Cleanup()
        {
            TestRunner.TestLog($"[STRESS TEST] Starting cleanup of {testPawns.Count} pawns and {testWeapons.Count} weapons");

            foreach (var weapon in testWeapons)
            {
                if (weapon != null && !weapon.Destroyed)
                {
                    TestHelpers.SafeDestroyWeapon(weapon);
                }
            }
            testWeapons.Clear();

            foreach (var pawn in testPawns)
            {
                if (pawn != null && !pawn.Destroyed)
                {
                    pawn.jobs?.StopAll();
                    pawn.equipment?.DestroyAllEquipment();
                    TestHelpers.SafeDestroyPawn(pawn);
                }
            }
            testPawns.Clear();

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            TestRunner.TestLog("[STRESS TEST] Cleanup completed");
        }
    }

    public class PerformanceTest : ITestScenario
    {
        public string Name => "Performance Benchmarks";
        private List<Pawn> testPawns = new List<Pawn>();
        private List<ThingWithComps> testWeapons = new List<ThingWithComps>();
        private const int BENCHMARK_PAWN_COUNT = 20;
        private const int BENCHMARK_ITERATIONS = 10;

        public void Setup(Map map)
        {
            if (map == null) return;

            for (int i = 0; i < BENCHMARK_PAWN_COUNT; i++)
            {
                var config = new TestHelpers.TestPawnConfig
                {
                    Name = $"BenchmarkPawn{i}",
                    Skills = new Dictionary<SkillDef, int>
                    {
                        { SkillDefOf.Shooting, Rand.Range(5, 15) },
                        { SkillDefOf.Melee, Rand.Range(5, 15) }
                    }
                };

                var pawn = TestHelpers.CreateTestPawn(map, config);
                if (pawn != null)
                {
                    testPawns.Add(pawn);

                    var weaponDef = i % 2 == 0 ? AutoArmDefOf.Gun_Autopistol : AutoArmDefOf.MeleeWeapon_Knife;
                    if (weaponDef != null)
                    {
                        var weapon = TestHelpers.CreateWeapon(map, weaponDef,
                            TestPositions.GetNearbyPosition(pawn.Position, 2, 4, map));
                        if (weapon != null)
                        {
                            testWeapons.Add(weapon);
                            WeaponCacheManager.AddWeaponToCache(weapon);
                        }
                    }
                }
            }
        }

        public TestResult Run()
        {
            if (testPawns.Count == 0)
                return TestResult.Failure("No test pawns created");

            var result = new TestResult { Success = true };
            var jobGiver = new JobGiver_PickUpBetterWeapon();

            var jobStopwatch = Stopwatch.StartNew();
            int jobsCreated = 0;

            for (int iteration = 0; iteration < BENCHMARK_ITERATIONS; iteration++)
            {
                foreach (var pawn in testPawns)
                {
                    var job = jobGiver.TestTryGiveJob(pawn);
                    if (job != null)
                        jobsCreated++;
                }
            }

            jobStopwatch.Stop();
            double avgJobTime = jobStopwatch.Elapsed.TotalMilliseconds / (testPawns.Count * BENCHMARK_ITERATIONS);

            result.Data["Pawns Tested"] = testPawns.Count;
            result.Data["Total Iterations"] = BENCHMARK_ITERATIONS;
            result.Data["Jobs Created"] = jobsCreated;
            result.Data["Total Time (ms)"] = jobStopwatch.Elapsed.TotalMilliseconds;
            result.Data["Avg Time Per Job (ms)"] = avgJobTime;

            WeaponCacheManager.ClearAllCaches();
            var cacheStopwatch = Stopwatch.StartNew();

            foreach (var pawn in testPawns)
            {
                foreach (var weapon in testWeapons.Take(5))
                {
                    if (weapon != null && !weapon.Destroyed)
                    {
                        WeaponCacheManager.GetCachedScore(pawn, weapon);
                    }
                }
            }

            var firstPassTime = cacheStopwatch.Elapsed.TotalMilliseconds;
            cacheStopwatch.Restart();

            foreach (var pawn in testPawns)
            {
                foreach (var weapon in testWeapons.Take(5))
                {
                    if (weapon != null && !weapon.Destroyed)
                    {
                        WeaponCacheManager.GetCachedScore(pawn, weapon);
                    }
                }
            }

            var secondPassTime = cacheStopwatch.Elapsed.TotalMilliseconds;
            cacheStopwatch.Stop();

            result.Data["Cache Miss Time (ms)"] = firstPassTime;
            result.Data["Cache Hit Time (ms)"] = secondPassTime;
            result.Data["Cache Speedup"] = $"{(firstPassTime / Math.Max(secondPassTime, 0.1)):F1}x";

            if (avgJobTime > 10.0)
            {
                result.Data["Warning"] = $"Slow job creation: {avgJobTime:F2}ms average";
            }

            return result;
        }

        public void Cleanup()
        {
            foreach (var weapon in testWeapons)
            {
                TestHelpers.SafeDestroyWeapon(weapon);
            }
            testWeapons.Clear();

            foreach (var pawn in testPawns)
            {
                if (pawn != null && !pawn.Destroyed)
                {
                    TestHelpers.SafeDestroyPawn(pawn);
                }
            }
            testPawns.Clear();
        }
    }
}
