
using AutoArm.Caching;
using AutoArm.Compatibility;
using AutoArm.Definitions;
using AutoArm.Jobs;
using System;
using System.Collections.Generic;
using Verse;

namespace AutoArm.Helpers
{
    internal static class Cleanup
    {
        private const int MaxPawnRecords = Constants.MaxPawnRecords;
        private const int MaxJobRecords = Constants.MaxJobRecords;

        private static bool autoCleanupDisabled = false;

        private static int currentCleanupIndex = 0;
        private static CleanupStats accumulatedStats = new CleanupStats();
        private const int TOTAL_CLEANUP_OPERATIONS = 16;
        private const int OPERATIONS_PER_BATCH = 1;

        // Warmup grace period
        private static int _warmupCompletedTick = -1;
        private const int WARMUP_GRACE_PERIOD = 3000; // ~50 seconds

        public static void OnWarmupCompleted()
        {
            _warmupCompletedTick = Find.TickManager?.TicksGame ?? 0;
        }

        public static void PerformStaggeredCleanup()
        {
            if (autoCleanupDisabled) return;

            // Skip during warmup
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
                    AutoArmLogger.WarnCleanup(e, $"Staggered cleanup operation {currentCleanupIndex}");
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
                    AutoArm.Jobs.AutoEquipState.Cleanup(force: Testing.TestRunner.IsRunningTests);
                    break;
                case 2:
                    ForcedWeaponState.Cleanup();
                    break;
                case 3:
                    if (SimpleSidearmsCompat.IsLoaded)
                    {
                        SimpleSidearmsCompat.CleanupCaches(force: Testing.TestRunner.IsRunningTests);
                    }
                    break;
                case 4:
                    accumulatedStats.DroppedItems += DroppedItems.CleanupOldEntries();
                    break;
                case 5:
                    DroppedItems.ClearAllPendingUpgrades();
                    break;
                case 6:
                    if (!Testing.TestRunner.IsRunningTests)
                    {
                        WeaponCache.CleanupDestroyedMaps();
                    }
                    break;
                case 7:
                    PawnValidation.CleanupDeadPawns(force: Testing.TestRunner.IsRunningTests);
                    break;
                case 8:
                    accumulatedStats.WeaponScores = WeaponCache.CleanupScoreCache(forceDeadPawnCleanup: Testing.TestRunner.IsRunningTests);
                    break;
                case 9:
                    Scoring.CleanupSkillCache();
                    break;
                case 10:
                    AutoArm.Thing_LabelPatches.CleanupLabelCache();
                    break;
                case 11:
                    accumulatedStats.CacheEntries = AutoArm.UI.StatusOverviewDataGatherer.CleanupTopWeaponsCache();
                    break;
                case 12:
                    ForcedWeaponLabelHelper.CleanupDeadPawnCaches();
                    break;
                case 13:
                    Blacklist.CleanupOldEntries();
                    break;
                case 14:
                    JobGiver_PickUpBetterWeapon.CleanupMessageCache();
                    break;
                case 15:
                    JobGiver_PickUpBetterWeapon.CleanupCaches(force: Testing.TestRunner.IsRunningTests);
                    break;
            }
        }

        public static void PerformFullCleanup()
        {
            try
            {
                var savedStats = accumulatedStats;
                accumulatedStats = new CleanupStats();

                for (int i = 0; i < TOTAL_CLEANUP_OPERATIONS; i++)
                {
                    try { ExecuteCleanupOperation(i); }
                    catch (Exception e) { AutoArmLogger.WarnCleanup(e, $"Full cleanup operation {i}"); }
                }

                if (AutoArmMod.settings?.debugLogging == true || accumulatedStats.IsUnusual())
                {
                    accumulatedStats.LogSummary();
                }

                accumulatedStats = savedStats;
            }
            catch (Exception e)
            {
                AutoArmLogger.WarnCleanup(e, "PerformFullCleanup");
            }
        }

        public static void OnWeaponRemoved(Thing weapon)
        {
            if (weapon == null) return;
            if (!weapon.def.IsWeapon) return;

            try { DroppedItems.RemoveWeapon(weapon); }
            catch (Exception e) { AutoArmLogger.ErrorPatch(e, "OnWeaponRemoved.DroppedItems"); }

            if (weapon is ThingWithComps twc)
            {
                try { ForcedWeaponState.RemoveWeapon(twc); }
                catch (Exception e) { AutoArmLogger.ErrorPatch(e, "OnWeaponRemoved.ForcedWeaponState"); }
            }
        }

        public static bool ShouldRunCleanup()
        {
            if (autoCleanupDisabled)
                return false;

            return Find.TickManager.TicksGame % 600 == 0;
        }

        public static void DisableAutoCleanup()
        {
            autoCleanupDisabled = true;
            AutoArmLogger.Debug(() => "[TEST] Automatic cleanup disabled for testing");
        }

        public static void EnableAutoCleanup()
        {
            autoCleanupDisabled = false;
            AutoArmLogger.Debug(() => "[TEST] Automatic cleanup re-enabled");
        }

        public static void ClearAllCaches()
        {
            WeaponCache.ClearAllCaches();

            PawnValidation.ClearCache();

            AutoArm.UI.StatusOverviewDataGatherer.ClearTopWeaponsCache();

            if (Find.Maps != null)
            {
                foreach (var map in Find.Maps)
                {
                    WeaponCache.MarkCacheAsChanged(map);
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
                    AutoArmLogger.WarnFileOnly(message + " (unusual amount)");
                }
                else
                {
                    AutoArmLogger.Debug(() => message);
                }
            }
        }
    }
}
