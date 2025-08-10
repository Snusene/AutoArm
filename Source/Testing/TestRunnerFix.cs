using AutoArm.Caching;
using AutoArm.Helpers;
using AutoArm.Jobs;
using AutoArm.Logging;
using AutoArm.Weapons;
using System;
using System.Linq;
using System.Reflection;
using Verse;

namespace AutoArm.Testing
{
    /// <summary>
    /// Fixes and utilities for test runner to ensure proper test isolation
    /// </summary>
    public static class TestRunnerFix
    {
        /// <summary>
        /// Clear all cooldowns for a specific pawn
        /// </summary>
        public static void ClearAllCooldownsForPawn(Pawn pawn)
        {
            if (pawn == null) return;

            try
            {
                // TimingHelper removed - it only had empty methods
                
                // Clear from blacklist
                WeaponBlacklist.ClearBlacklist(pawn);
                
                // Clear forced weapon status
                ForcedWeaponHelper.ClearForced(pawn);
                
                AutoArmLogger.Debug($"[TEST] Cleared all cooldowns and restrictions for {pawn.Name}");
            }
            catch (Exception e)
            {
                AutoArmLogger.Error($"Error clearing cooldowns for pawn {pawn.Name}", e);
            }
        }

        /// <summary>
        /// Clear all global cooldowns and caches
        /// </summary>
        public static void ClearAllGlobalCooldowns()
        {
            try
            {
                // TimingHelper removed - it only had empty methods
                WeaponScoreCache.ClearAllCaches();
                DroppedItemTracker.ClearAll();
                AutoArmLogger.Debug("[TEST] Cleared all global cooldowns and caches");
            }
            catch (Exception e)
            {
                AutoArmLogger.Error("Error clearing global cooldowns", e);
            }
        }

        /// <summary>
        /// Reset all AutoArm systems for clean test state
        /// </summary>
        public static void ResetAllSystems()
        {
            try
            {
                // Ensure mod is enabled for tests
                if (AutoArmMod.settings != null)
                {
                    AutoArmMod.settings.modEnabled = true;
                    AutoArmLogger.Debug("[TEST] Enabled AutoArm mod for test execution");
                }

                // Clear all caches
                ClearAllWeaponCaches();
                WeaponScoreCache.ClearAllCaches();

                // Clear all tracking systems
                DroppedItemTracker.ClearAll();
                ClearAllForcedWeapons();
                ClearAllWeaponBlacklists();

                // Clear timing systems
                ClearAllGlobalCooldowns();
                
                // CRITICAL: Clear JobGiver per-tick tracking
                ClearJobGiverPerTickTracking();

                // Perform full cleanup
                CleanupHelper.PerformFullCleanup();

                AutoArmLogger.Debug("[TEST] Reset all AutoArm systems");
            }
            catch (Exception e)
            {
                AutoArmLogger.Error("Error resetting AutoArm systems", e);
            }
        }

        /// <summary>
        /// Prepare a pawn for weapon testing
        /// </summary>
        public static void PreparePawnForTest(Pawn pawn)
        {
            if (pawn == null) return;

            try
            {
                // Clear all restrictions and cooldowns
                ClearAllCooldownsForPawn(pawn);
                
                // CRITICAL: Clear JobGiver per-tick tracking for this test
                ClearJobGiverPerTickTracking();

                // Stop any current jobs
                pawn.jobs?.StopAll();
                
                // jobsGivenThisTick fields no longer exist in RimWorld 1.5+
                // These fields were removed from the API

                // Ensure pawn is not drafted
                if (pawn.Drafted)
                {
                    pawn.drafter.Drafted = false;
                }

                // Ensure pawn is not downed
                if (pawn.Downed && pawn.health != null)
                {
                    // Try to remove any hediffs causing the pawn to be downed
                    // Note: capacityMods field structure changed in RimWorld 1.5+
                    var hediffsToRemove = pawn.health.hediffSet.hediffs
                        .Where(h => h.def.stages?.Any(s => s.capMods?.Any() == true) == true)
                        .ToList();
                    
                    foreach (var hediff in hediffsToRemove)
                    {
                        pawn.health.RemoveHediff(hediff);
                    }
                }
                
                // Advance tick to ensure hash interval checks pass
                if (Find.TickManager != null)
                {
                    Find.TickManager.DoSingleTick();
                }

                AutoArmLogger.Debug($"[TEST] Prepared {pawn.Name} for weapon testing");
            }
            catch (Exception e)
            {
                AutoArmLogger.Error($"Error preparing pawn {pawn.Name} for test", e);
            }
        }

        /// <summary>
        /// Clear all forced weapons for all pawns
        /// </summary>
        private static void ClearAllForcedWeapons()
        {
            try
            {
                // Get all pawns that might have forced weapons
                var allPawns = Find.Maps?.SelectMany(m => m.mapPawns?.AllPawns ?? Enumerable.Empty<Pawn>())
                             ?? Enumerable.Empty<Pawn>();

                foreach (var pawn in allPawns)
                {
                    ForcedWeaponHelper.ClearForced(pawn);
                }

                // Run cleanup to remove orphaned entries
                ForcedWeaponHelper.Cleanup();
                
                AutoArmLogger.Debug("[TEST] Cleared all forced weapons");
            }
            catch (Exception e)
            {
                AutoArmLogger.Error("Error clearing forced weapons", e);
            }
        }

        /// <summary>
        /// Clear all weapon blacklists
        /// </summary>
        private static void ClearAllWeaponBlacklists()
        {
            try
            {
                // Get all pawns that might have blacklists
                var allPawns = Find.Maps?.SelectMany(m => m.mapPawns?.AllPawns ?? Enumerable.Empty<Pawn>())
                             ?? Enumerable.Empty<Pawn>();

                foreach (var pawn in allPawns)
                {
                    WeaponBlacklist.ClearBlacklist(pawn);
                }

                // Run cleanup
                WeaponBlacklist.CleanupOldEntries();
                
                AutoArmLogger.Debug("[TEST] Cleared all weapon blacklists");
            }
            catch (Exception e)
            {
                AutoArmLogger.Error("Error clearing weapon blacklists", e);
            }
        }

        /// <summary>
        /// Clear all weapon caches for all maps
        /// </summary>
        private static void ClearAllWeaponCaches()
        {
            try
            {
                // Clear caches for all maps
                if (Find.Maps != null)
                {
                    foreach (var map in Find.Maps)
                    {
                        ImprovedWeaponCacheManager.InvalidateCache(map);
                    }
                }

                // Cleanup destroyed maps
                ImprovedWeaponCacheManager.CleanupDestroyedMaps();
                
                AutoArmLogger.Debug("[TEST] Cleared all weapon caches");
            }
            catch (Exception e)
            {
                AutoArmLogger.Error("Error clearing weapon caches", e);
            }
        }

        /// <summary>
        /// Clear JobGiver per-tick tracking to allow multiple test pawns to be processed
        /// </summary>
        public static void ClearJobGiverPerTickTracking()
        {
            try
            {
                var jobGiverType = typeof(JobGiver_PickUpBetterWeapon);
                
                // Clear lastProcessTick - set to -999999 to ensure it's different from current tick
                var lastProcessTickField = jobGiverType.GetField("lastProcessTick", 
                    BindingFlags.NonPublic | BindingFlags.Static);
                if (lastProcessTickField != null)
                {
                    lastProcessTickField.SetValue(null, -999999);
                }
                
                // Clear processedThisTick HashSet
                var processedThisTickField = jobGiverType.GetField("processedThisTick", 
                    BindingFlags.NonPublic | BindingFlags.Static);
                if (processedThisTickField != null)
                {
                    var hashSet = processedThisTickField.GetValue(null) as System.Collections.Generic.HashSet<Pawn>;
                    hashSet?.Clear();
                }
                
                // Reset unarmedProcessedThisTick
                var unarmedProcessedField = jobGiverType.GetField("unarmedProcessedThisTick", 
                    BindingFlags.NonPublic | BindingFlags.Static);
                if (unarmedProcessedField != null)
                {
                    unarmedProcessedField.SetValue(null, 0);
                }
                
                // Reset armedProcessedThisTick
                var armedProcessedField = jobGiverType.GetField("armedProcessedThisTick", 
                    BindingFlags.NonPublic | BindingFlags.Static);
                if (armedProcessedField != null)
                {
                    armedProcessedField.SetValue(null, 0);
                }
                
                // Also advance the game tick to ensure we're on a "new" tick for testing
                if (Find.TickManager != null)
                {
                    // Advance by 1 tick to ensure tick-based checks pass
                    Find.TickManager.DoSingleTick();
                }
                
                AutoArmLogger.Debug("[TEST] Cleared JobGiver per-tick tracking and advanced tick");
            }
            catch (Exception e)
            {
                AutoArmLogger.Error("Error clearing JobGiver per-tick tracking", e);
            }
        }

        /// <summary>
        /// Verify test environment is properly set up
        /// </summary>
        public static bool VerifyTestEnvironment(Map map)
        {
            if (map == null)
            {
                AutoArmLogger.Error("[TEST] No map available for testing");
                return false;
            }

            if (AutoArmMod.settings == null)
            {
                AutoArmLogger.Error("[TEST] AutoArm settings not initialized");
                return false;
            }

            if (!AutoArmMod.settings.modEnabled)
            {
                AutoArmLogger.Warn("[TEST] AutoArm mod is disabled, enabling for tests");
                AutoArmMod.settings.modEnabled = true;
            }

            return true;
        }
    }
}
