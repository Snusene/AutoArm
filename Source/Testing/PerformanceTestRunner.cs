using AutoArm.Caching;
using AutoArm.Jobs;
using AutoArm.Logging;
using AutoArm.Weapons;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Verse;

namespace AutoArm.Testing
{
    public static class PerformanceTestRunner
    {
        private static readonly Stopwatch stopwatch = new Stopwatch();

        public static Dictionary<string, object> RunPerformanceTest()
        {
            var map = Find.CurrentMap;
            if (map == null)
            {
                AutoArmLogger.Error("No map available for performance test");
                return new Dictionary<string, object> { { "Error", "No map available" } };
            }

            AutoArmLogger.Debug("=== Starting Performance Test ===");
            var results = new Dictionary<string, object>();

            try
            {
                // Test 1: Real-time cache operations (add/remove/update)
                results["CacheOperations"] = TestRealtimeCacheOperations(map);

                // Test 2: Weapon search performance
                results["WeaponSearch"] = TestWeaponSearch(map);

                // Test 3: Score calculation performance
                results["ScoreCalculation"] = TestScoreCalculation(map);

                // Test 4: Job creation performance
                results["JobCreation"] = TestJobCreation(map);

                // Test 5: Memory usage
                results["Memory"] = TestMemoryUsage();

                // Log results
                LogResults(results);
            }
            catch (Exception e)
            {
                AutoArmLogger.Error($"Performance test failed: {e.Message}", e);
                results["Error"] = e.Message;
            }

            AutoArmLogger.Debug("=== Performance Test Complete ===");
            return results;
        }

        private static Dictionary<string, object> TestRealtimeCacheOperations(Map map)
        {
            var result = new Dictionary<string, object>();
            
            try
            {
                AutoArmLogger.Debug("Testing real-time cache operations...");

                // Get some existing weapons to test with
                // Count total weapons on map for context
                var allWeapons = map.listerThings.ThingsInGroup(ThingRequestGroup.Weapon)
                    .OfType<ThingWithComps>()
                    .Where(w => WeaponValidation.IsProperWeapon(w))
                    .ToList();
                
                result["TotalWeaponsOnMap"] = allWeapons.Count;
                
                // Get a few weapons to test operations with
                var testWeapons = allWeapons.Take(10).ToList();

                if (testWeapons.Count == 0)
                {
                    result["Error"] = "No weapons available for testing";
                    return result;
                }

                var testWeapon = testWeapons.First();
                var originalPosition = testWeapon.Position;
                
                // Test 1: Remove operation
                stopwatch.Restart();
                for (int i = 0; i < 100; i++)
                {
                    ImprovedWeaponCacheManager.RemoveWeaponFromCache(testWeapon);
                }
                stopwatch.Stop();
                long removeTime = stopwatch.ElapsedTicks;
                
                // Test 2: Add operation
                stopwatch.Restart();
                for (int i = 0; i < 100; i++)
                {
                    ImprovedWeaponCacheManager.AddWeaponToCache(testWeapon);
                }
                stopwatch.Stop();
                long addTime = stopwatch.ElapsedTicks;
                
                // Test 3: Position update operation
                var newPosition = originalPosition + IntVec3.North;
                stopwatch.Restart();
                for (int i = 0; i < 100; i++)
                {
                    ImprovedWeaponCacheManager.UpdateWeaponPosition(testWeapon, originalPosition, newPosition);
                    ImprovedWeaponCacheManager.UpdateWeaponPosition(testWeapon, newPosition, originalPosition);
                }
                stopwatch.Stop();
                long updateTime = stopwatch.ElapsedTicks;
                
                // Convert ticks to microseconds for better precision
                double ticksPerMs = Stopwatch.Frequency / 1000.0;
                double ticksPerUs = Stopwatch.Frequency / 1000000.0;
                
                result["AddOperation_us"] = Math.Round(addTime / ticksPerUs / 100, 2); // Per operation
                result["RemoveOperation_us"] = Math.Round(removeTime / ticksPerUs / 100, 2);
                result["UpdateOperation_us"] = Math.Round(updateTime / ticksPerUs / 200, 2); // 200 because we do 2 updates per iteration
                result["TotalOperations"] = 400; // 100 adds + 100 removes + 200 position updates
                
                // Ensure weapon is back in cache properly
                ImprovedWeaponCacheManager.AddWeaponToCache(testWeapon);

                AutoArmLogger.Debug($"Cache operations - Add: {result["AddOperation_us"]}µs, Remove: {result["RemoveOperation_us"]}µs, Update: {result["UpdateOperation_us"]}µs");
            }
            catch (Exception e)
            {
                result["Error"] = e.Message;
                AutoArmLogger.Error($"Cache operations test failed: {e.Message}", e);
            }

            return result;
        }

        private static Dictionary<string, object> TestWeaponSearch(Map map)
        {
            var result = new Dictionary<string, object>();
            
            try
            {
                AutoArmLogger.Debug("Testing weapon search performance...");

                var colonists = map.mapPawns.FreeColonists.ToList();
                if (colonists.Count == 0)
                {
                    result["Error"] = "No colonists to test";
                    return result;
                }

                var searchTimes = new List<double>(); // Changed to double for microseconds
                const int iterations = 50; // Increased iterations for better averages
                double ticksPerUs = Stopwatch.Frequency / 1000000.0;

                foreach (var pawn in colonists.Take(5)) // Test with up to 5 pawns
                {
                    for (int i = 0; i < iterations; i++)
                    {
                        stopwatch.Restart();
                        var weapons = ImprovedWeaponCacheManager.GetWeaponsNear(map, pawn.Position, 50f).ToList();
                        stopwatch.Stop();
                        searchTimes.Add(stopwatch.ElapsedTicks / ticksPerUs); // Convert to microseconds
                    }
                }

                if (searchTimes.Count > 0)
                {
                    result["TotalSearches"] = searchTimes.Count;
                    result["Average_us"] = searchTimes.Average();
                    result["Min_us"] = searchTimes.Min();
                    result["Max_us"] = searchTimes.Max();
                    result["Median_us"] = searchTimes.OrderBy(x => x).Skip(searchTimes.Count / 2).First();

                    AutoArmLogger.Debug($"Weapon search: {searchTimes.Average():F2}µs average ({searchTimes.Count} searches)");
                }
            }
            catch (Exception e)
            {
                result["Error"] = e.Message;
                AutoArmLogger.Error($"Weapon search test failed: {e.Message}", e);
            }

            return result;
        }

        private static Dictionary<string, object> TestScoreCalculation(Map map)
        {
            var result = new Dictionary<string, object>();
            
            try
            {
                AutoArmLogger.Debug("Testing weapon score calculation...");

                var colonist = map.mapPawns.FreeColonists.FirstOrFallback();
                if (colonist == null)
                {
                    result["Error"] = "No colonist to test";
                    return result;
                }

                var weapons = map.listerThings.ThingsInGroup(ThingRequestGroup.Weapon)
                    .OfType<ThingWithComps>()
                    .Take(30) // Increased sample size
                    .ToList();

                if (weapons.Count == 0)
                {
                    result["Error"] = "No weapons to test";
                    return result;
                }

                // Clear cache first
                WeaponScoreCache.ClearAllCaches();

                double ticksPerUs = Stopwatch.Frequency / 1000000.0;
                const int iterations = 5; // Run multiple iterations for better accuracy
                var firstPassTimes = new List<double>();
                var cachedPassTimes = new List<double>();

                for (int iter = 0; iter < iterations; iter++)
                {
                    // Clear cache before each iteration
                    WeaponScoreCache.ClearAllCaches();
                    
                    // First pass - cache miss
                    stopwatch.Restart();
                    foreach (var weapon in weapons)
                    {
                        WeaponScoreCache.GetCachedScore(colonist, weapon);
                    }
                    stopwatch.Stop();
                    firstPassTimes.Add(stopwatch.ElapsedTicks / ticksPerUs);

                    // Second pass - cache hit
                    stopwatch.Restart();
                    foreach (var weapon in weapons)
                    {
                        WeaponScoreCache.GetCachedScore(colonist, weapon);
                    }
                    stopwatch.Stop();
                    cachedPassTimes.Add(stopwatch.ElapsedTicks / ticksPerUs);
                }

                double avgFirstPass = firstPassTimes.Average();
                double avgCachedPass = cachedPassTimes.Average();
                double perWeaponFirst = avgFirstPass / weapons.Count;
                double perWeaponCached = avgCachedPass / weapons.Count;

                result["WeaponCount"] = weapons.Count;
                result["FirstPass_us"] = avgFirstPass;
                result["CachedPass_us"] = avgCachedPass;
                result["PerWeaponFirst_us"] = perWeaponFirst;
                result["PerWeaponCached_us"] = perWeaponCached;
                result["CacheSpeedup"] = avgFirstPass > 0 ? avgFirstPass / Math.Max(avgCachedPass, 0.1) : 0;
                result["Iterations"] = iterations;

                AutoArmLogger.Debug($"Score calculation - First: {avgFirstPass:F2}µs, Cached: {avgCachedPass:F2}µs, " +
                          $"Speedup: {result["CacheSpeedup"]:F1}x, Per weapon: {perWeaponFirst:F2}µs -> {perWeaponCached:F2}µs");
            }
            catch (Exception e)
            {
                result["Error"] = e.Message;
                AutoArmLogger.Error($"Score calculation test failed: {e.Message}", e);
            }

            return result;
        }

        private static Dictionary<string, object> TestJobCreation(Map map)
        {
            var result = new Dictionary<string, object>();
            
            try
            {
                AutoArmLogger.Debug("Testing job creation performance...");

                var colonists = map.mapPawns.FreeColonists.ToList();
                if (colonists.Count == 0)
                {
                    result["Error"] = "No colonists to test";
                    return result;
                }

                var jobGiver = new JobGiver_PickUpBetterWeapon();
                var jobTimes = new List<double>(); // Changed to double for microseconds
                int jobsCreated = 0;
                double ticksPerUs = Stopwatch.Frequency / 1000000.0;
                const int iterations = 3; // Multiple iterations per pawn for better accuracy

                foreach (var pawn in colonists)
                {
                    double totalTimeForPawn = 0;
                    int jobsForPawn = 0;
                    
                    for (int i = 0; i < iterations; i++)
                    {
                        stopwatch.Restart();
                        var job = jobGiver.TestTryGiveJob(pawn);
                        stopwatch.Stop();
                        
                        totalTimeForPawn += stopwatch.ElapsedTicks / ticksPerUs;
                        if (job != null && i == 0) // Only count first iteration for job creation
                            jobsForPawn = 1;
                    }
                    
                    jobTimes.Add(totalTimeForPawn / iterations); // Average per pawn
                    jobsCreated += jobsForPawn;
                }

                // Calculate statistics
                double avgTime = jobTimes.Average();
                double minTime = jobTimes.Min();
                double maxTime = jobTimes.Max();
                double medianTime = jobTimes.OrderBy(x => x).Skip(jobTimes.Count / 2).First();
                double totalTime = jobTimes.Sum();

                result["PawnsTested"] = colonists.Count;
                result["JobsCreated"] = jobsCreated;
                result["Average_us"] = avgTime;
                result["Min_us"] = minTime;
                result["Max_us"] = maxTime;
                result["Median_us"] = medianTime;
                result["Total_us"] = totalTime;
                result["IterationsPerPawn"] = iterations;

                AutoArmLogger.Debug($"Job creation: {avgTime:F2}µs average (min: {minTime:F2}µs, max: {maxTime:F2}µs), " +
                          $"{jobsCreated}/{colonists.Count} jobs created");
            }
            catch (Exception e)
            {
                result["Error"] = e.Message;
                AutoArmLogger.Error($"Job creation test failed: {e.Message}", e);
            }

            return result;
        }

        private static Dictionary<string, object> TestMemoryUsage()
        {
            var result = new Dictionary<string, object>();
            
            try
            {
                AutoArmLogger.Debug("Testing memory usage...");

                long beforeGC = GC.GetTotalMemory(false);
                
                // Force garbage collection
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                
                long afterGC = GC.GetTotalMemory(true);
                long memoryFreed = beforeGC - afterGC;

                result["BeforeGC_MB"] = beforeGC / (1024.0 * 1024.0);
                result["AfterGC_MB"] = afterGC / (1024.0 * 1024.0);
                result["Freed_MB"] = memoryFreed / (1024.0 * 1024.0);

                AutoArmLogger.Debug($"Memory - Current: {afterGC / 1048576:F2}MB, " +
                          $"Freed by GC: {memoryFreed / 1048576:F2}MB");
            }
            catch (Exception e)
            {
                result["Error"] = e.Message;
                AutoArmLogger.Error($"Memory test failed: {e.Message}", e);
            }

            return result;
        }

        private static void LogResults(Dictionary<string, object> results)
        {
            AutoArmLogger.Debug("=== Performance Test Results ===");
            
            foreach (var category in results)
            {
                AutoArmLogger.Debug($"{category.Key}:");
                
                if (category.Value is Dictionary<string, object> categoryResults)
                {
                    foreach (var kvp in categoryResults)
                    {
                        string key = kvp.Key;
                        
                        // Clean up the key for display (remove _us suffix for cleaner display)
                        string displayKey = key.EndsWith("_us") ? key.Substring(0, key.Length - 3) : key;
                        
                        if (key.EndsWith("_us"))
                        {
                            // Microsecond values
                            if (kvp.Value is double d)
                                AutoArmLogger.Debug($"  {displayKey}: {d:F2}µs");
                            else
                                AutoArmLogger.Debug($"  {displayKey}: {kvp.Value}µs");
                        }
                        else if (kvp.Value is double d)
                        {
                            if (key.Contains("Speedup"))
                                AutoArmLogger.Debug($"  {displayKey}: {d:F1}x");
                            else if (key.Contains("MB"))
                                AutoArmLogger.Debug($"  {displayKey}: {d:F2}MB");
                            else
                                AutoArmLogger.Debug($"  {displayKey}: {d:F2}");
                        }
                        else
                        {
                            AutoArmLogger.Debug($"  {displayKey}: {kvp.Value}");
                        }
                    }
                }
            }
        }
    }
}
