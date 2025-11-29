
using AutoArm.Caching;
using AutoArm.Compatibility;
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
    /// Cleanup coordination
    /// </summary>
    public static class Cleanup
    {
        private const int MaxPawnRecords = Constants.MaxPawnRecords;
        private const int MaxJobRecords = Constants.MaxJobRecords;

        private static bool autoCleanupDisabled = false;

        private static int currentCleanupIndex = 0;
        private static CleanupStats accumulatedStats = new CleanupStats();
        private const int TOTAL_CLEANUP_OPERATIONS = 17;
        private const int OPERATIONS_PER_BATCH = 2;

        // Duplicate cleanup guard - prevents double cleanup when Kill + Destroy both fire
        private static int _lastCleanedPawnId = -1;
        private static int _lastCleanupTick = -1;

        // Warmup grace period - skip cleanup until caches could actually have stale entries
        private static int _warmupCompletedTick = -1;
        private const int WARMUP_GRACE_PERIOD = 3000; // ~50 seconds

        /// <summary>
        /// Called after WarmupCaches completes to start grace period
        /// </summary>
        public static void OnWarmupCompleted()
        {
            _warmupCompletedTick = Find.TickManager?.TicksGame ?? 0;
        }

        /// <summary>
        /// Perform staggered cleanup - spreads operations across multiple ticks
        /// Runs 2 operations per call, cycling through all 19 operations over 10 seconds
        /// </summary>
        public static void PerformStaggeredCleanup()
        {
            if (autoCleanupDisabled) return;

            // Skip cleanup during warmup grace period - nothing can be stale yet
            int currentTick = Find.TickManager?.TicksGame ?? 0;
            if (_warmupCompletedTick > 0 && currentTick - _warmupCompletedTick < WARMUP_GRACE_PERIOD)
                return;

            for (int i = 0; i < OPERATIONS_PER_BATCH; i++)
            {
                if (currentCleanupIndex >= TOTAL_CLEANUP_OPERATIONS)
                {
                    if (AutoArmMod.settings?.debugLogging == true || accumulatedStats.IsUnusual())
                    {
                        accumulatedStats.LogSummary();
                    }

                    accumulatedStats = new CleanupStats();
                    currentCleanupIndex = 0;
                }

                try
                {
                    ExecuteCleanupOperation(currentCleanupIndex);
                }
                catch (Exception e)
                {
                    AutoArmLogger.ErrorCleanup(e, $"Staggered cleanup operation {currentCleanupIndex}");
                }

                currentCleanupIndex++;
            }
        }


        private static void ExecuteCleanupOperation(int index)
        {
            switch (index)
            {
                case 0:
                    accumulatedStats.ForcedWeapons = ForcedWeapons.Cleanup();
                    break;
                case 1:
                    AutoArm.Jobs.AutoEquipState.Cleanup();
                    break;
                case 2:
                    ForcedWeaponState.Cleanup();
                    break;
                case 3:
                    if (SimpleSidearmsCompat.IsLoaded)
                    {
                        SimpleSidearmsCompat.CleanupCaches();
                    }
                    break;
                case 4:
                    accumulatedStats.DroppedItems += DroppedItemTracker.CleanupOldEntries();
                    break;
                case 5:
                    DroppedItemTracker.ClearAllPendingUpgrades();
                    break;
                case 6:
                    if (!Testing.TestRunner.IsRunningTests)
                    {
                        WeaponCacheManager.CleanupDestroyedMaps();
                    }
                    break;
                case 7:
                    PawnValidationCache.CleanupDeadPawns();
                    break;
                case 8:
                    accumulatedStats.WeaponScores = WeaponCacheManager.CleanupScoreCache();
                    break;
                case 9:
                    WeaponScoringHelper.CleanupSkillCache();
                    break;
                case 10:
                    AutoArm.Thing_LabelPatches.CleanupLabelCache();
                    break;
                case 11:
                    accumulatedStats.CacheEntries = GenericCache.CleanupExpired();
                    break;
                case 12:
                    ForcedWeaponLabelHelper.CleanupDeadPawnCaches();
                    break;
                case 13:
                    WeaponBlacklist.CleanupOldEntries();
                    break;
                case 14:
                    JobGiver_PickUpBetterWeapon.CleanupMessageCache();
                    break;
                case 15:
                    JobGiver_PickUpBetterWeapon.CleanupCaches();
                    break;
                case 16:
                    ThinkNode_ConditionalWeaponStatus.CleanupDeadPawns();
                    break;
            }
        }

        public static void PerformFullCleanup()
        {
            try
            {
                var cleanupStats = new CleanupStats();


                cleanupStats.ForcedWeapons = ForcedWeapons.Cleanup();

                AutoArm.Jobs.AutoEquipState.Cleanup();

                ForcedWeaponState.Cleanup();

                cleanupStats.DroppedItems = DroppedItemTracker.CleanupOldEntries();
                DroppedItemTracker.ClearAllPendingUpgrades();

                if (!Testing.TestRunner.IsRunningTests)
                {
                    WeaponCacheManager.CleanupDestroyedMaps();
                }

                cleanupStats.WeaponScores = WeaponCacheManager.CleanupScoreCache(forceDeadPawnCleanup: Testing.TestRunner.IsRunningTests);

                WeaponScoringHelper.CleanupSkillCache();

                AutoArm.Thing_LabelPatches.CleanupLabelCache();

                ForcedWeaponLabelHelper.CleanupDeadPawnCaches();

                WeaponBlacklist.CleanupOldEntries();

                JobGiver_PickUpBetterWeapon.CleanupMessageCache();

                JobGiver_PickUpBetterWeapon.CleanupCaches();


                ThinkNode_ConditionalWeaponStatus.CleanupDeadPawns();

                if (SimpleSidearmsCompat.IsLoaded)
                {
                    SimpleSidearmsCompat.CleanupCaches();
                }

                PawnValidationCache.CleanupDeadPawns();

                cleanupStats.CacheEntries = GenericCache.CleanupExpired();

                if (AutoArmMod.settings?.debugLogging == true || cleanupStats.IsUnusual())
                {
                    cleanupStats.LogSummary();
                }
            }
            catch (Exception e)
            {
                AutoArmLogger.ErrorCleanup(e, "PerformFullCleanup");
            }
        }

        /// <summary>
        /// Generic cleanup for pawn dictionaries
        /// </summary>
        public static int CleanupPawnDictionary<T>(Dictionary<Pawn, T> dict, int maxRecords = MaxPawnRecords)
        {
            if (dict == null || dict.Count == 0) return 0;

            int removed = 0;
            var toRemove = ListPool<Pawn>.Get();

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

            if (dict.Count > maxRecords)
            {
                int toRemoveCount = dict.Count - maxRecords;
                var trimList = ListPool<Pawn>.Get();
                int count = 0;
                foreach (var pawn in dict.Keys)
                {
                    if (count++ >= toRemoveCount) break;
                    trimList.Add(pawn);
                }
                foreach (var pawn in trimList)
                {
                    dict.Remove(pawn);
                    removed++;
                }
                ListPool<Pawn>.Return(trimList);
            }

            ListPool<Pawn>.Return(toRemove);

            return removed;
        }

        /// <summary>
        /// Thing cleanup
        /// </summary>
        public static int CleanupThingDictionary<T>(Dictionary<Thing, T> dict, Predicate<T> shouldRemove = null)
        {
            if (dict == null) return 0;

            int removed = 0;
            var toRemove = ListPool<Thing>.Get();

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

            int result = removed;
            ListPool<Thing>.Return(toRemove);
            return result;
        }

        /// <summary>
        /// HashSet cleanup
        /// </summary>
        public static int CleanupPawnHashSet(HashSet<Pawn> set, int maxRecords = MaxPawnRecords)
        {
            if (set == null) return 0;

            var toRemove = ListPool<Pawn>.Get();
            foreach (var pawn in set)
            {
                if (IsPawnInvalid(pawn))
                    toRemove.Add(pawn);
            }

            foreach (var pawn in toRemove)
            {
                set.Remove(pawn);
            }

            if (set.Count > maxRecords)
            {
                set.Clear();
                int count = maxRecords;
                ListPool<Pawn>.Return(toRemove);
                return count;
            }

            int removedCount = toRemove.Count;
            ListPool<Pawn>.Return(toRemove);
            return removedCount;
        }

        public static bool IsPawnInvalid(Pawn pawn)
        {
            return pawn == null || pawn.Destroyed || pawn.Dead || !pawn.Spawned;
        }

        /// <summary>
        /// Event-driven cleanup when pawn dies/destroyed.
        /// Called from Pawn.Destroy/Kill patches.
        /// </summary>
        public static void OnPawnRemoved(Pawn pawn)
        {
            if (pawn == null) return;
            if (pawn.RaceProps?.ToolUser != true) return;

            // Duplicate cleanup guard - Kill + Destroy can both fire for same pawn
            int pawnId = pawn.thingIDNumber;
            int currentTick = Find.TickManager.TicksGame;
            if (_lastCleanedPawnId == pawnId && _lastCleanupTick == currentTick)
                return;
            _lastCleanedPawnId = pawnId;
            _lastCleanupTick = currentTick;

            try { ForcedWeapons.RemovePawn(pawn); }
            catch (Exception e) { AutoArmLogger.ErrorPatch(e, "OnPawnRemoved.ForcedWeapons"); }

            try { WeaponBlacklist.RemovePawn(pawn); }
            catch (Exception e) { AutoArmLogger.ErrorPatch(e, "OnPawnRemoved.WeaponBlacklist"); }

            try { PawnValidationCache.RemovePawn(pawn); }
            catch (Exception e) { AutoArmLogger.ErrorPatch(e, "OnPawnRemoved.PawnValidationCache"); }

            try { WeaponCacheManager.RemovePawnFromScoreCache(pawnId); }
            catch (Exception e) { AutoArmLogger.ErrorPatch(e, "OnPawnRemoved.WeaponCacheManager"); }

            try { ForcedWeaponLabelHelper.RemovePawn(pawn); }
            catch (Exception e) { AutoArmLogger.ErrorPatch(e, "OnPawnRemoved.ForcedWeaponLabelHelper"); }

            try { DroppedItemTracker.RemovePawn(pawn); }
            catch (Exception e) { AutoArmLogger.ErrorPatch(e, "OnPawnRemoved.DroppedItemTracker"); }

            try { AutoArm.Jobs.AutoEquipState.RemovePawn(pawn); }
            catch (Exception e) { AutoArmLogger.ErrorPatch(e, "OnPawnRemoved.AutoEquipState"); }

            if (SimpleSidearmsCompat.IsLoaded)
            {
                try { SimpleSidearmsCompat.RemovePawn(pawn); }
                catch (Exception e) { AutoArmLogger.ErrorPatch(e, "OnPawnRemoved.SimpleSidearmsCompat"); }
            }
        }

        /// <summary>
        /// Event-driven cleanup when weapon destroyed/despawned.
        /// Called from Thing.Destroy/DeSpawn patches.
        /// </summary>
        public static void OnWeaponRemoved(Thing weapon)
        {
            if (weapon == null) return;
            if (!weapon.def.IsWeapon) return;

            try { DroppedItemTracker.RemoveWeapon(weapon); }
            catch (Exception e) { AutoArmLogger.ErrorPatch(e, "OnWeaponRemoved.DroppedItemTracker"); }

            if (weapon is ThingWithComps twc)
            {
                try { ForcedWeaponState.RemoveWeapon(twc); }
                catch (Exception e) { AutoArmLogger.ErrorPatch(e, "OnWeaponRemoved.ForcedWeaponState"); }
            }
        }

        public static bool IsThingInvalid(Thing thing)
        {
            return thing == null || thing.Destroyed || !thing.Spawned;
        }

        /// <summary>
        /// Cleanup by age
        /// </summary>
        public static int CleanupByAge<TKey>(Dictionary<TKey, int> tickDict, int maxAgeTicks)
        {
            if (tickDict == null) return 0;

            int currentTick = Find.TickManager.TicksGame;
            var toRemove = ListPool<TKey>.Get();
            foreach (var kvp in tickDict)
            {
                if (currentTick - kvp.Value > maxAgeTicks)
                    toRemove.Add(kvp.Key);
            }

            foreach (var key in toRemove)
            {
                tickDict.Remove(key);
            }

            int count = toRemove.Count;
            ListPool<TKey>.Return(toRemove);
            return count;
        }

        /// <summary>
        /// Cleanup timing
        /// </summary>
        public static bool ShouldRunCleanup()
        {
            if (autoCleanupDisabled)
                return false;

            return Find.TickManager.TicksGame % 600 == 0;
        }

        /// <summary>
        /// Disable automatic cleanup (for testing)
        /// </summary>
        public static void DisableAutoCleanup()
        {
            autoCleanupDisabled = true;
            AutoArmLogger.Debug(() => "[TEST] Automatic cleanup disabled for testing");
        }

        /// <summary>
        /// Re-enable automatic cleanup (after testing)
        /// </summary>
        public static void EnableAutoCleanup()
        {
            autoCleanupDisabled = false;
            AutoArmLogger.Debug(() => "[TEST] Automatic cleanup re-enabled");
        }

        /// <summary>
        /// Clear caches
        /// </summary>
        public static void ClearAllCaches()
        {
            WeaponCacheManager.ClearAllCaches();

            PawnValidationCache.ClearCache();

            GenericCache.ClearAll();

            if (Find.Maps != null)
            {
                foreach (var map in Find.Maps)
                {
                    WeaponCacheManager.MarkCacheAsChanged(map);
                }
            }

            AutoArmLogger.Debug(() => "Cleared all caches");
        }


        private class CleanupStats
        {
            public int ForcedWeapons { get; set; }
            public int DroppedItems { get; set; }
            public int WeaponScores { get; set; }
            public int CacheEntries { get; set; }

            public int Total => ForcedWeapons + DroppedItems + WeaponScores + CacheEntries;

            public bool IsUnusual()
            {
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
                    AutoArmLogger.Debug(() => message);
                }
            }
        }
    }
}
