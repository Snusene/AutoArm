// AutoArm RimWorld 1.5+ mod - automatic weapon management
// This file: Centralized memory management and cleanup operations
// Prevents memory leaks by cleaning up dead pawns, destroyed things, old dictionaries
// Uses: All major systems register their cleanup needs here
// Critical: Runs every 2500 ticks to prevent long-game performance degradation

using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using AutoArm.Caching;
using AutoArm.Jobs;
using AutoArm.Logging;
using AutoArm.Patches;
using AutoArm.Weapons;

namespace AutoArm.Helpers
{
    /// <summary>
    /// Centralized cleanup management (fixes #4, #11, #28)
    /// </summary>
    public static class CleanupHelper
    {
        // Generic cache functionality (fixes #14)
        private static readonly object cacheLock = new object();
        private static Dictionary<string, object> genericCache = new Dictionary<string, object>();
        private static Dictionary<string, int> cacheExpiration = new Dictionary<string, int>();
        private const int DefaultCacheDuration = 600; // 10 seconds
        private const int CleanupInterval = 2500;
        private const int MaxPawnRecords = 100;
        private const int MaxJobRecords = 50;

        /// <summary>
        /// Perform all cleanup operations
        /// </summary>
        public static void PerformFullCleanup()
        {
            try
            {
                var cleanupStats = new CleanupStats();
                
                // Clean up timing data
                TimingHelper.CleanupOldCooldowns();

                // Clean up SimpleSidearms data
                if (SimpleSidearmsCompat.IsLoaded())
                {
                    SimpleSidearmsCompat.CleanupOldTrackingData();
                }

                // Clean up forced weapon tracker
                cleanupStats.ForcedWeapons = ForcedWeaponHelper.Cleanup();

                // Clean up auto-equip tracker
                AutoEquipTracker.CleanupOldJobs();

                // Clean up dropped item tracker
                cleanupStats.DroppedItems = DroppedItemTracker.CleanupOldEntries();
                DroppedItemTracker.ClearAllPendingUpgrades();

                // Clean up weapon caches
                ImprovedWeaponCacheManager.CleanupDestroyedMaps();
                cleanupStats.WeaponScores = WeaponScoreCache.CleanupCache();
                
                // Clean up our new optimized caches
                WeaponScoringHelper.ClearWeaponScoreCache();
                
                // Clean up validation helper storage type caches
                ValidationHelper.ClearStorageTypeCaches();

                // Clean up job giver data
                CleanupJobGiverData();

                // Clean up debug logging
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    WeaponDecisionLog.Cleanup();
                }

                // Clean up weapon blacklist
                WeaponBlacklist.CleanupOldEntries();
                
                // Clean up tick rare patch dictionaries (fixes memory leak)
                cleanupStats.TickRarePawns = Pawn_TickRare_Unified_Patch.CleanupDeadPawns();
                
                // Clean up think node evaluation failures
                ThinkNode_ConditionalUnarmed.CleanupEvaluationFailures();
                
                // Clean up expired generic cache entries
                cleanupStats.CacheEntries = CleanupExpiredCacheEntries();
                
                // Log summary if debug mode or if unusual amounts cleaned
                if (AutoArmMod.settings?.debugLogging == true || cleanupStats.IsUnusual())
                {
                    cleanupStats.LogSummary();
                }
            }
            catch (Exception e)
            {
                AutoArmLogger.Error("Critical error during cleanup", e);
            }
        }

        /// <summary>
        /// Generic cleanup for pawn dictionaries
        /// </summary>
        public static int CleanupPawnDictionary<T>(Dictionary<Pawn, T> dict, int maxRecords = MaxPawnRecords)
        {
            if (dict == null) return 0;

            int removed = 0;

            // Remove dead/destroyed pawns
            var toRemove = dict.Keys
                .Where(IsPawnInvalid)
                .ToList();

            foreach (var pawn in toRemove)
            {
                dict.Remove(pawn);
                removed++;
            }

            // Trim to max size if needed
            if (dict.Count > maxRecords)
            {
                var oldestPawns = dict.Keys
                    .OrderBy(p => p.thingIDNumber)
                    .Take(dict.Count - maxRecords)
                    .ToList();

                foreach (var pawn in oldestPawns)
                {
                    dict.Remove(pawn);
                    removed++;
                }
            }

            return removed;
        }

        /// <summary>
        /// Cleanup for thing dictionaries
        /// </summary>
        public static int CleanupThingDictionary<T>(Dictionary<Thing, T> dict, Predicate<T> shouldRemove = null)
        {
            if (dict == null) return 0;

            int removed = 0;
            var toRemove = new List<Thing>();

            foreach (var kvp in dict)
            {
                if (IsThingInvalid(kvp.Key) || (shouldRemove?.Invoke(kvp.Value) ?? false))
                {
                    toRemove.Add(kvp.Key);
                }
            }

            foreach (var thing in toRemove)
            {
                dict.Remove(thing);
                removed++;
            }

            return removed;
        }

        /// <summary>
        /// Cleanup for HashSets
        /// </summary>
        public static int CleanupPawnHashSet(HashSet<Pawn> set, int maxRecords = MaxPawnRecords)
        {
            if (set == null) return 0;

            var toRemove = set.Where(IsPawnInvalid).ToList();

            foreach (var pawn in toRemove)
            {
                set.Remove(pawn);
            }

            // Trim if too large
            if (set.Count > maxRecords)
            {
                set.Clear();
                return maxRecords;
            }

            return toRemove.Count;
        }

        /// <summary>
        /// Check if a pawn is invalid for cleanup
        /// </summary>
        public static bool IsPawnInvalid(Pawn pawn)
        {
            return pawn == null || pawn.Destroyed || pawn.Dead || !pawn.Spawned;
        }

        /// <summary>
        /// Check if a thing is invalid for cleanup
        /// </summary>
        public static bool IsThingInvalid(Thing thing)
        {
            return thing == null || thing.Destroyed || !thing.Spawned;
        }

        /// <summary>
        /// Cleanup old entries based on tick age
        /// </summary>
        public static int CleanupByAge<TKey>(Dictionary<TKey, int> tickDict, int maxAgeTicks)
        {
            if (tickDict == null) return 0;

            int currentTick = Find.TickManager.TicksGame;
            var toRemove = tickDict
                .Where(kvp => currentTick - kvp.Value > maxAgeTicks)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in toRemove)
            {
                tickDict.Remove(key);
            }

            return toRemove.Count;
        }

        /// <summary>
        /// Cleanup job giver specific data
        /// </summary>
        private static void CleanupJobGiverData()
        {
            // This replaces JobGiver_PickUpBetterWeapon.CleanupCaches()
            // Since we don't have that class anymore, we'll clean up what we can access

            // Clean up any static data in JobGiverHelpers
            JobGiverHelpers.CleanupLogCooldowns();
        }

        /// <summary>
        /// Schedule cleanup to run
        /// </summary>
        public static bool ShouldRunCleanup()
        {
            return Find.TickManager.TicksGame % CleanupInterval == 0;
        }

        // ========================================
        // Generic Cache Methods (from SettingsCacheHelper)
        // ========================================

        /// <summary>
        /// Get or compute a cached value
        /// </summary>
        public static T GetCached<T>(string key, Func<T> computeValue, int cacheDuration = DefaultCacheDuration)
        {
            int currentTick = Find.TickManager?.TicksGame ?? 0;

            // First check without full lock
            lock (cacheLock)
            {
                if (genericCache.TryGetValue(key, out object cachedObj) && 
                    cacheExpiration.TryGetValue(key, out int expiration) &&
                    currentTick < expiration &&
                    cachedObj is T cachedValue)
                {
                    return cachedValue;
                }
            }

            // Compute new value outside of lock to avoid blocking
            T value = computeValue();

            // Double-check and cache it
            lock (cacheLock)
            {
                // Check again in case another thread computed it while we were waiting
                if (genericCache.TryGetValue(key, out object cachedObj2) && 
                    cacheExpiration.TryGetValue(key, out int expiration2) &&
                    currentTick < expiration2 &&
                    cachedObj2 is T cachedValue2)
                {
                    return cachedValue2;
                }
                
                genericCache[key] = value;
                cacheExpiration[key] = currentTick + cacheDuration;
            }

            return value;
        }

        /// <summary>
        /// Clear a specific cached value
        /// </summary>
        public static void ClearCache(string key)
        {
            lock (cacheLock)
            {
                genericCache.Remove(key);
                cacheExpiration.Remove(key);
            }
        }

        /// <summary>
        /// Clear all cached values in the generic cache
        /// </summary>
        public static void ClearAllCaches()
        {
            lock (cacheLock)
            {
                int count = genericCache.Count;
                genericCache.Clear();
                cacheExpiration.Clear();
                
                if (count > 0 && AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.Debug($"Cleared {count} cached values");
                }
            }
        }
        
        /// <summary>
        /// Clean up expired entries in the generic cache
        /// </summary>
        private static int CleanupExpiredCacheEntries()
        {
            int currentTick = Find.TickManager?.TicksGame ?? 0;
            List<string> expiredKeys;
            
            lock (cacheLock)
            {
                expiredKeys = cacheExpiration
                    .Where(kvp => currentTick >= kvp.Value)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var key in expiredKeys)
                {
                    genericCache.Remove(key);
                    cacheExpiration.Remove(key);
                }
            }
            
            return expiredKeys.Count;
        }
        
        /// <summary>
        /// Tracks cleanup statistics
        /// </summary>
        private class CleanupStats
        {
            public int ForcedWeapons { get; set; }
            public int DroppedItems { get; set; }
            public int WeaponScores { get; set; }
            public int TickRarePawns { get; set; }
            public int CacheEntries { get; set; }
            
            public int Total => ForcedWeapons + DroppedItems + WeaponScores + TickRarePawns + CacheEntries;
            
            public bool IsUnusual()
            {
                // Warn if cleaning up too much at once (might indicate a problem)
                return Total > 100 || TickRarePawns > 50 || WeaponScores > 200;
            }
            
            public void LogSummary()
            {
                if (Total == 0) return;
                
                string message = $"Cleanup complete: {Total} items removed";
                if (ForcedWeapons > 0) message += $" | Forced weapons: {ForcedWeapons}";
                if (DroppedItems > 0) message += $" | Dropped items: {DroppedItems}";
                if (WeaponScores > 0) message += $" | Weapon scores: {WeaponScores}";
                if (TickRarePawns > 0) message += $" | Dead pawns: {TickRarePawns}";
                if (CacheEntries > 0) message += $" | Cache entries: {CacheEntries}";
                
                if (IsUnusual())
                {
                    AutoArmLogger.Warn(message + " (unusual amount)");
                }
                else
                {
                    AutoArmLogger.Debug(message);
                }
            }
        }
    }
}