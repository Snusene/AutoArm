using System;
using System.Diagnostics;
using System.Linq;
using Verse;

namespace AutoArm.Testing
{
    public static class PerformanceTestRunner
    {
        public static void RunPerformanceTest()
        {
            var map = Find.CurrentMap;
            if (map == null)
            {
                Log.Error("[AutoArm] No map to run performance test on");
                return;
            }

            Log.Message("[AutoArm] Starting performance test...");

            // Test 1: Cache rebuild performance
            TestCacheRebuild(map);

            // Test 2: Weapon search performance
            TestWeaponSearch(map);

            // Test 3: Score calculation performance
            TestScoreCalculation(map);

            // Test 4: Memory usage
            TestMemoryUsage();

            Log.Message("[AutoArm] Performance test complete!");
        }

        private static void TestCacheRebuild(Map map)
        {
            Log.Message("[AutoArm] Testing cache rebuild performance...");

            // Clear all caches
            ImprovedWeaponCacheManager.InvalidateCache(map);
            WeaponScoringHelper.ClearWeaponScoreCache();

            var sw = Stopwatch.StartNew();

            // Force rebuild by querying weapons
            var weapons = ImprovedWeaponCacheManager.GetWeaponsNear(map, map.Center, 100f);

            sw.Stop();

            Log.Message($"[AutoArm] Cache rebuild took {sw.ElapsedMilliseconds}ms for {weapons.Count} weapons");
        }

        private static void TestWeaponSearch(Map map)
        {
            Log.Message("[AutoArm] Testing weapon search performance...");

            var colonists = map.mapPawns.FreeColonists;
            if (colonists.Count == 0)
            {
                Log.Warning("[AutoArm] No colonists to test with");
                return;
            }

            var sw = Stopwatch.StartNew();
            int totalSearches = 0;

            foreach (var pawn in colonists)
            {
                var weapons = ImprovedWeaponCacheManager.GetWeaponsNear(map, pawn.Position, 50f);
                totalSearches++;
            }

            sw.Stop();

            float avgTime = (float)sw.ElapsedMilliseconds / totalSearches;
            Log.Message($"[AutoArm] Average weapon search time: {avgTime:F2}ms ({totalSearches} searches)");
        }

        private static void TestScoreCalculation(Map map)
        {
            Log.Message("[AutoArm] Testing weapon score calculation...");

            var colonist = map.mapPawns.FreeColonists.FirstOrFallback();
            if (colonist == null)
            {
                Log.Warning("[AutoArm] No colonist to test with");
                return;
            }

            var weapons = map.listerThings.ThingsInGroup(ThingRequestGroup.Weapon)
                .OfType<ThingWithComps>()
                .Take(20)
                .ToList();

            if (weapons.Count == 0)
            {
                Log.Warning("[AutoArm] No weapons to test with");
                return;
            }

            // First pass - populate cache
            var sw = Stopwatch.StartNew();
            foreach (var weapon in weapons)
            {
                WeaponScoreCache.GetCachedScore(colonist, weapon);
            }
            sw.Stop();

            float firstPassTime = (float)sw.ElapsedMilliseconds / weapons.Count;

            // Second pass - should be cached
            sw.Restart();
            foreach (var weapon in weapons)
            {
                WeaponScoreCache.GetCachedScore(colonist, weapon);
            }
            sw.Stop();

            float secondPassTime = (float)sw.ElapsedMilliseconds / weapons.Count;

            Log.Message($"[AutoArm] Score calculation - First pass: {firstPassTime:F2}ms/weapon, Cached: {secondPassTime:F2}ms/weapon");
            Log.Message($"[AutoArm] Cache speedup: {(firstPassTime / secondPassTime):F1}x faster");
            Log.Message($"[AutoArm] Note: Using optimized scoring with ~66% fewer calculations per weapon");
        }

        private static void TestMemoryUsage()
        {
            Log.Message("[AutoArm] Testing memory usage...");

            long beforeGC = GC.GetTotalMemory(false);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            long afterGC = GC.GetTotalMemory(true);

            long memoryFreed = beforeGC - afterGC;

            Log.Message($"[AutoArm] Memory freed by GC: {memoryFreed / 1024:N0} KB");
            Log.Message($"[AutoArm] Current memory usage: {afterGC / 1024:N0} KB");
        }
    }
}