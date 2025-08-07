// AutoArm RimWorld 1.5+ mod - automatic weapon management
// This file: Centralized cleanup operations and utilities
// Coordinates cleanup for all subsystems and provides utility methods
// Called by MemoryCleanupManager periodically

using AutoArm.Caching;
using AutoArm.Definitions;
using AutoArm.Jobs;
using AutoArm.Logging;
using AutoArm.Weapons;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace AutoArm.Helpers
{
    /// <summary>
    /// Centralized cleanup coordination and utility methods
    /// </summary>
    public static class CleanupHelper
    {
        private const int MaxPawnRecords = Constants.MaxPawnRecords;
        private const int MaxJobRecords = Constants.MaxJobRecords;

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

                // Clean up forced weapon tracker
                cleanupStats.ForcedWeapons = ForcedWeaponHelper.Cleanup();

                // Clean up auto-equip tracker
                AutoArm.Jobs.AutoEquipTracker.Cleanup();

                // Clean up forced weapon tracker (remove stale entries)
                ForcedWeaponTracker.Cleanup();

                // Clean up dropped item tracker
                cleanupStats.DroppedItems = DroppedItemTracker.CleanupOldEntries();
                DroppedItemTracker.ClearAllPendingUpgrades();

                // Clean up weapon caches - MINIMAL since they're now self-maintaining
                // Only clean up destroyed maps as a safety net
                ImprovedWeaponCacheManager.CleanupDestroyedMaps();
                
                // Clean up weapon score cache (pawn-weapon combinations)
                cleanupStats.WeaponScores = WeaponScoreCache.CleanupCache();

                // Note: WeaponScoringHelper.ClearWeaponScoreCache() removed - too aggressive
                // The cache has its own size limits and expiration

                // Clean up validation helper storage type caches - only if they're too large
                // These have size limits now so only clear if approaching limit
                if (Prefs.DevMode || UnityEngine.Random.Range(0, 10) == 0) // 10% chance or dev mode
                {
                    ValidationHelper.ClearStorageTypeCaches();
                }

                // Clean up job giver data
                CleanupJobGiverData();

                // Clean up UI patch caches
                ForcedWeaponLabelHelper.CleanupDeadPawnCaches();

                // Clean up weapon blacklist
                WeaponBlacklist.CleanupOldEntries();

                // Removed tick rare patch cleanup - no longer needed

                // Clean up think node evaluation failures
                ThinkNode_ConditionalWeaponStatus.CleanupDeadPawns();

                // Clean up expired generic cache entries
                cleanupStats.CacheEntries = GenericCache.CleanupExpired();

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
        /// Generic cleanup for pawn dictionaries - optimized
        /// </summary>
        public static int CleanupPawnDictionary<T>(Dictionary<Pawn, T> dict, int maxRecords = MaxPawnRecords)
        {
            if (dict == null || dict.Count == 0) return 0;

            int removed = 0;
            var toRemove = new List<Pawn>();

            // Single pass to find invalid pawns
            foreach (var pawn in dict.Keys)
            {
                if (IsPawnInvalid(pawn))
                {
                    toRemove.Add(pawn);
                }
            }

            foreach (var pawn in toRemove)
            {
                dict.Remove(pawn);
                removed++;
            }

            // Trim to max size if needed
            if (dict.Count > maxRecords)
            {
                int toRemoveCount = dict.Count - maxRecords;
                // Take the first N keys (don't need to sort for cleanup)
                foreach (var pawn in dict.Keys.Take(toRemoveCount).ToList())
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
        /// Check if cleanup should run based on timing and conditions
        /// </summary>
        public static bool ShouldRunCleanup()
        {
            // Run cleanup every 10 seconds in game time
            return Find.TickManager.TicksGame % 600 == 0;
        }

        /// <summary>
        /// Clear all caches (compatibility method)
        /// </summary>
        public static void ClearAllCaches()
        {
            // Clear weapon score cache
            WeaponScoreCache.ClearCache();
            
            // Clear validation helper storage type caches
            ValidationHelper.ClearStorageTypeCaches();
            
            // Clear generic cache
            GenericCache.ClearAll();
            
            // Mark all weapon caches as changed
            if (Find.Maps != null)
            {
                foreach (var map in Find.Maps)
                {
                    ImprovedWeaponCacheManager.MarkCacheAsChanged(map);
                }
            }
            
            if (AutoArmMod.settings?.debugLogging == true)
            {
                AutoArmLogger.Debug("Cleared all caches");
            }
        }

        /// <summary>
        /// Tracks cleanup statistics
        /// </summary>
        private class CleanupStats
        {
            public int ForcedWeapons { get; set; }
            public int DroppedItems { get; set; }
            public int WeaponScores { get; set; }
            public int CacheEntries { get; set; }

            public int Total => ForcedWeapons + DroppedItems + WeaponScores + CacheEntries;

            public bool IsUnusual()
            {
                // Warn if cleaning up too much at once (might indicate a problem)
                return Total > Constants.UnusualCleanupTotal || WeaponScores > Constants.UnusualCleanupScores;
            }

            public void LogSummary()
            {
                if (Total == 0) return;

                string message = $"Cleanup complete: {Total} items removed";
                if (ForcedWeapons > 0) message += $" | Forced weapons: {ForcedWeapons}";
                if (DroppedItems > 0) message += $" | Dropped items: {DroppedItems}";
                if (WeaponScores > 0) message += $" | Weapon scores: {WeaponScores}";
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