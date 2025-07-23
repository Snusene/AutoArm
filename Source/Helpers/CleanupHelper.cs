using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace AutoArm
{
    /// <summary>
    /// Centralized cleanup management (fixes #4, #11, #28)
    /// </summary>
    public static class CleanupHelper
    {
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
                // Clean up timing data
                TimingHelper.CleanupOldCooldowns();
                
                // Clean up SimpleSidearms data
                if (SimpleSidearmsCompat.IsLoaded())
                {
                    SimpleSidearmsCompat.CleanupOldTrackingData();
                }
                
                // Clean up forced weapon tracker
                ForcedWeaponHelper.Cleanup();
                
                // Clean up auto-equip tracker
                AutoEquipTracker.CleanupOldJobs();
                
                // Clean up dropped item tracker
                DroppedItemTracker.ClearAllPendingUpgrades();
                
                // Clean up weapon caches
                ImprovedWeaponCacheManager.CleanupDestroyedMaps();
                WeaponScoreCache.CleanupCache();
                
                // Clean up job giver data
                CleanupJobGiverData();
                
                // Clean up debug logging
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    WeaponDecisionLog.Cleanup();
                }
            }
            catch (Exception e)
            {
                AutoArmDebug.LogError("Error in cleanup", e);
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
    }
}
