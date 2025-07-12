using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace AutoArm
{
    // Centralized cleanup manager to prevent memory leaks
    [StaticConstructorOnStartup]
    public static class MemoryCleanupManager
    {
        private const int CleanupInterval = 2500; // Ticks between cleanups
        private const int MaxPawnRecords = 100; // Maximum pawns to track in any dictionary
        private const int MaxJobRecords = 50; // Maximum jobs to track

        static MemoryCleanupManager()
        {
            try
            {
                // Register for game tick to perform periodic cleanup
                HarmonyLib.Harmony harmony = new HarmonyLib.Harmony("AutoArm.MemoryCleanup");
                harmony.Patch(
                    typeof(Game).GetMethod("UpdatePlay"),
                    postfix: new HarmonyLib.HarmonyMethod(typeof(MemoryCleanupManager).GetMethod(nameof(GameUpdatePlay_Postfix)))
                );
            }
            catch (Exception e)
            {
                Log.Error($"[AutoArm] Failed to patch Game.UpdatePlay for memory cleanup: {e.Message}");
            }
        }

        public static void GameUpdatePlay_Postfix()
        {
            if (Find.TickManager.TicksGame % CleanupInterval == 0)
            {
                PerformCleanup();
            }
        }

        public static void PerformCleanup()
        {
            int totalCleaned = 0;

            try
            {
                // Clean up UnifiedTickRarePatch dictionaries
                totalCleaned += CleanupPawnDictionary(UnifiedTickRarePatch_CleanupHelper.GetLastInterruptionTick());
                totalCleaned += CleanupPawnDictionary(UnifiedTickRarePatch_CleanupHelper.GetLastSidearmCheckTick());
                totalCleaned += CleanupPawnDictionary(UnifiedTickRarePatch_CleanupHelper.GetLastWeaponCheckTick());
                totalCleaned += CleanupPawnDictionary(UnifiedTickRarePatch_CleanupHelper.GetLastWeaponSearchTick());
                totalCleaned += CleanupPawnJobDictionary(UnifiedTickRarePatch_CleanupHelper.GetCachedWeaponJobs());
                totalCleaned += CleanupPawnHashSet(UnifiedTickRarePatch_CleanupHelper.GetRecentlyUnarmedPawns());
            }
            catch (Exception e)
            {
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    Log.Warning($"[AutoArm] Error cleaning UnifiedTickRarePatch: {e.Message}");
                }
            }

            try
            {
                // Clean up SimpleSidearmsCompat dictionaries
                totalCleaned += CleanupPawnDictionary(SimpleSidearmsCompat_CleanupHelper.GetLastSidearmPickupTick());
                totalCleaned += CleanupPawnCategoryDictionary(SimpleSidearmsCompat_CleanupHelper.GetRecentSidearmPickups());
            }
            catch (Exception e)
            {
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    Log.Warning($"[AutoArm] Error cleaning SimpleSidearmsCompat: {e.Message}");
                }
            }

            // Clean up ForcedWeaponTracker
            totalCleaned += CleanupForcedWeapons();

            // Clean up AutoEquipTracker
            totalCleaned += CleanupAutoEquipTracker();

            // Clean up WeaponDecisionLog
            totalCleaned += CleanupWeaponDecisionLog();

            // Clean up JobGiver_PickUpBetterWeapon static fields
            totalCleaned += CleanupJobGiverStatics();

            // Clean up weapon caches
            ImprovedWeaponCacheManager.CleanupDestroyedMaps();

            if (AutoArmMod.settings?.debugLogging == true && totalCleaned > 0)
            {
                Log.Message($"[AutoArm] Memory cleanup: removed {totalCleaned} stale entries");
            }
        }

        private static int CleanupPawnDictionary<T>(Dictionary<Pawn, T> dict)
        {
            if (dict == null) return 0;

            int removed = 0;
            var toRemove = dict.Keys
                .Where(p => p == null || p.Destroyed || p.Dead || !p.Spawned)
                .ToList();

            foreach (var pawn in toRemove)
            {
                dict.Remove(pawn);
                removed++;
            }

            // Also enforce size limit
            if (dict.Count > MaxPawnRecords)
            {
                var oldestPawns = dict.Keys
                    .OrderBy(p => p.thingIDNumber)
                    .Take(dict.Count - MaxPawnRecords)
                    .ToList();

                foreach (var pawn in oldestPawns)
                {
                    dict.Remove(pawn);
                    removed++;
                }
            }

            return removed;
        }

        private static int CleanupPawnJobDictionary(Dictionary<Pawn, Job> dict)
        {
            if (dict == null) return 0;

            int removed = 0;
            var toRemove = new List<Pawn>();

            foreach (var kvp in dict)
            {
                if (kvp.Key == null || kvp.Key.Destroyed || kvp.Key.Dead || !kvp.Key.Spawned ||
                    kvp.Value == null || kvp.Value.targetA.Thing?.Destroyed == true)
                {
                    toRemove.Add(kvp.Key);
                }
            }

            foreach (var pawn in toRemove)
            {
                dict.Remove(pawn);
                removed++;
            }

            return removed;
        }

        private static int CleanupPawnHashSet(HashSet<Pawn> set)
        {
            if (set == null) return 0;

            var toRemove = set.Where(p => p == null || p.Destroyed || p.Dead || !p.Spawned).ToList();

            foreach (var pawn in toRemove)
            {
                set.Remove(pawn);
            }

            // Limit size
            if (set.Count > MaxPawnRecords)
            {
                set.Clear();
                return MaxPawnRecords;
            }

            return toRemove.Count;
        }

        private static int CleanupPawnCategoryDictionary(Dictionary<Pawn, Dictionary<string, int>> dict)
        {
            if (dict == null) return 0;

            int removed = 0;
            var toRemove = dict.Keys
                .Where(p => p == null || p.Destroyed || p.Dead || !p.Spawned)
                .ToList();

            foreach (var pawn in toRemove)
            {
                dict.Remove(pawn);
                removed++;
            }

            // Also clean up old entries within each pawn's dictionary
            foreach (var kvp in dict)
            {
                if (kvp.Value != null)
                {
                    var oldEntries = kvp.Value
                        .Where(e => Find.TickManager.TicksGame - e.Value > 10000)
                        .Select(e => e.Key)
                        .ToList();

                    foreach (var key in oldEntries)
                    {
                        kvp.Value.Remove(key);
                    }
                }
            }

            return removed;
        }

        private static int CleanupForcedWeapons()
        {
            return ForcedWeaponTracker_CleanupHelper.PerformCleanup();
        }

        private static int CleanupAutoEquipTracker()
        {
            AutoEquipTracker.CleanupOldJobs();
            return 0; // AutoEquipTracker handles its own counting
        }

        private static int CleanupWeaponDecisionLog()
        {
            WeaponDecisionLog.Cleanup();
            return 0; // WeaponDecisionLog handles its own counting
        }

        private static int CleanupJobGiverStatics()
        {
            JobGiver_PickUpBetterWeapon.CleanupCaches();
            return 0;
        }
    }

    // Helper classes to access private static fields for cleanup
    internal static class UnifiedTickRarePatch_CleanupHelper
    {
        public static Dictionary<Pawn, int> GetLastInterruptionTick()
        {
            try
            {
                var field = typeof(Pawn_TickRare_Unified_Patch).GetField("lastInterruptionTick",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                return field?.GetValue(null) as Dictionary<Pawn, int>;
            }
            catch (Exception e)
            {
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    Log.Warning($"[AutoArm] Failed to get lastInterruptionTick: {e.Message}");
                }
                return null;
            }
        }

        public static Dictionary<Pawn, int> GetLastSidearmCheckTick()
        {
            try
            {
                var field = typeof(Pawn_TickRare_Unified_Patch).GetField("lastSidearmCheckTick",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                return field?.GetValue(null) as Dictionary<Pawn, int>;
            }
            catch (Exception e)
            {
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    Log.Warning($"[AutoArm] Failed to get lastSidearmCheckTick: {e.Message}");
                }
                return null;
            }
        }

        public static Dictionary<Pawn, int> GetLastWeaponCheckTick()
        {
            try
            {
                var field = typeof(Pawn_TickRare_Unified_Patch).GetField("lastWeaponCheckTick",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                return field?.GetValue(null) as Dictionary<Pawn, int>;
            }
            catch (Exception e)
            {
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    Log.Warning($"[AutoArm] Failed to get lastWeaponCheckTick: {e.Message}");
                }
                return null;
            }
        }

        public static Dictionary<Pawn, int> GetLastWeaponSearchTick()
        {
            try
            {
                var field = typeof(Pawn_TickRare_Unified_Patch).GetField("lastWeaponSearchTick",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                return field?.GetValue(null) as Dictionary<Pawn, int>;
            }
            catch (Exception e)
            {
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    Log.Warning($"[AutoArm] Failed to get lastWeaponSearchTick: {e.Message}");
                }
                return null;
            }
        }

        public static Dictionary<Pawn, Job> GetCachedWeaponJobs()
        {
            try
            {
                var field = typeof(Pawn_TickRare_Unified_Patch).GetField("cachedWeaponJobs",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                return field?.GetValue(null) as Dictionary<Pawn, Job>;
            }
            catch (Exception e)
            {
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    Log.Warning($"[AutoArm] Failed to get cachedWeaponJobs: {e.Message}");
                }
                return null;
            }
        }

        public static HashSet<Pawn> GetRecentlyUnarmedPawns()
        {
            try
            {
                var field = typeof(Pawn_TickRare_Unified_Patch).GetField("recentlyUnarmedPawns",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                return field?.GetValue(null) as HashSet<Pawn>;
            }
            catch (Exception e)
            {
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    Log.Warning($"[AutoArm] Failed to get recentlyUnarmedPawns: {e.Message}");
                }
                return null;
            }
        }
    }

    internal static class SimpleSidearmsCompat_CleanupHelper
    {
        public static Dictionary<Pawn, int> GetLastSidearmPickupTick()
        {
            try
            {
                var field = typeof(SimpleSidearmsCompat).GetField("lastSidearmPickupTick",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                return field?.GetValue(null) as Dictionary<Pawn, int>;
            }
            catch (Exception e)
            {
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    Log.Warning($"[AutoArm] Failed to get lastSidearmPickupTick: {e.Message}");
                }
                return null;
            }
        }

        public static Dictionary<Pawn, Dictionary<string, int>> GetRecentSidearmPickups()
        {
            try
            {
                var field = typeof(SimpleSidearmsCompat).GetField("recentSidearmPickups",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                return field?.GetValue(null) as Dictionary<Pawn, Dictionary<string, int>>;
            }
            catch (Exception e)
            {
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    Log.Warning($"[AutoArm] Failed to get recentSidearmPickups: {e.Message}");
                }
                return null;
            }
        }
    }

    internal static class ForcedWeaponTracker_CleanupHelper
    {
        public static int PerformCleanup()
        {
            // ForcedWeaponTracker already has a Cleanup method
            ForcedWeaponTracker.Cleanup();
            return 0;
        }
    }
}