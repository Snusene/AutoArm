// AutoArm RimWorld 1.5+ mod - automatic weapon management
// This file: Test runner utility fixes
// Ensures proper test isolation and cleanup

using System;
using System.Linq;
using Verse;

namespace AutoArm.Testing
{
    /// <summary>
    /// Fixes for test runner to ensure proper test isolation
    /// </summary>
    public static class TestRunnerFix
    {
        /// <summary>
        /// Clear all cooldowns for a specific pawn - useful for testing
        /// </summary>
        public static void ClearAllCooldownsForPawn(Pawn pawn)
        {
            if (pawn == null) return;

            // Clear all cooldown types
            foreach (TimingHelper.CooldownType cooldownType in Enum.GetValues(typeof(TimingHelper.CooldownType)))
            {
                TimingHelper.ClearCooldown(pawn, cooldownType);
            }

            AutoArmLogger.Log($"[TEST] Cleared all cooldowns for {pawn.Name}");
        }

        /// <summary>
        /// Clear all global cooldowns - useful between test runs
        /// </summary>
        public static void ClearAllGlobalCooldowns()
        {
            // Clear all cooldowns by cleaning up
            TimingHelper.CleanupOldCooldowns();
            AutoArmLogger.Log("[TEST] Cleared all global cooldowns");
        }

        /// <summary>
        /// Reset all caches and tracking systems
        /// </summary>
        public static void ResetAllSystems()
        {
            // Use TestModEnabler to ensure mod is enabled
            TestModEnabler.EnsureModEnabled();
            
            AutoArmLogger.Log($"[TEST] Mod state after reset: modEnabled={AutoArmMod.settings?.modEnabled}");
            AutoArmLogger.Log($"[TEST] Settings instance hash: {AutoArmMod.settings?.GetHashCode() ?? -1}");
            
            // Clear weapon caches
            ClearAllWeaponCaches();
            WeaponScoreCache.ClearAllCaches();

            // Clear tracking systems
            DroppedItemTracker.ClearAll();
            ClearAllForcedWeapons();
            ClearAllAutoEquipTracking();
            ClearAllWeaponBlacklists();
            
            // Clear settings cache to ensure fresh state
            SettingsCacheHelper.ClearAllCaches();

            // Clear timing systems
            ClearAllGlobalCooldowns();

            // Clear validation caches
            CleanupHelper.PerformFullCleanup();

            AutoArmLogger.Log("[TEST] Reset all AutoArm systems");
        }

        /// <summary>
        /// Ensure a pawn is ready for weapon testing
        /// </summary>
        public static void PreparePawnForTest(Pawn pawn)
        {
            if (pawn == null) return;

            // Clear all cooldowns for this pawn
            ClearAllCooldownsForPawn(pawn);

            // Clear any forced weapon status
            ForcedWeaponHelper.ClearForced(pawn);

            // Clear from blacklists
            WeaponBlacklist.ClearBlacklist(pawn);

            // Clear auto-equip tracking
            // AutoEquipTracker.ClearPawnTracking(pawn); // Method might not exist

            // Stop any current jobs that might interfere
            pawn.jobs?.StopAll();

            AutoArmLogger.Log($"[TEST] Prepared {pawn.Name} for weapon testing");
        }

        /// <summary>
        /// Clear all forced weapons for all pawns
        /// </summary>
        private static void ClearAllForcedWeapons()
        {
            // Get all pawns that might have forced weapons
            var allPawns = Find.Maps?.SelectMany(m => m.mapPawns?.AllPawns ?? Enumerable.Empty<Pawn>())
                         ?? Enumerable.Empty<Pawn>();

            foreach (var pawn in allPawns)
            {
                ForcedWeaponHelper.ClearForced(pawn);
            }

            // Also run cleanup to remove any orphaned entries
            ForcedWeaponHelper.Cleanup();
        }

        /// <summary>
        /// Clear all weapon blacklists
        /// </summary>
        private static void ClearAllWeaponBlacklists()
        {
            // Get all pawns that might have blacklists
            var allPawns = Find.Maps?.SelectMany(m => m.mapPawns?.AllPawns ?? Enumerable.Empty<Pawn>())
                         ?? Enumerable.Empty<Pawn>();

            foreach (var pawn in allPawns)
            {
                WeaponBlacklist.ClearBlacklist(pawn);
            }

            // Also run cleanup
            WeaponBlacklist.CleanupOldEntries();
        }

        /// <summary>
        /// Clear all auto-equip tracking (if available)
        /// </summary>
        private static void ClearAllAutoEquipTracking()
        {
            // AutoEquipTracker might not have a ClearAll method
            // so we'll use reflection to find and call appropriate methods
            var autoEquipType = typeof(AutoEquipTracker);

            // Try to find a ClearAll or similar method
            var clearMethod = autoEquipType.GetMethod("ClearAll",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

            if (clearMethod != null)
            {
                clearMethod.Invoke(null, null);
            }
            else
            {
                // If no ClearAll method, try to clear individual tracking
                var clearTrackingMethod = autoEquipType.GetMethod("ClearTracking",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

                if (clearTrackingMethod != null)
                {
                    clearTrackingMethod.Invoke(null, null);
                }
            }
        }

        /// <summary>
        /// Clear all weapon caches for all maps
        /// </summary>
        private static void ClearAllWeaponCaches()
        {
            // Clear caches for all maps
            if (Find.Maps != null)
            {
                foreach (var map in Find.Maps)
                {
                    ImprovedWeaponCacheManager.InvalidateCache(map);
                }
            }

            // Also cleanup destroyed maps
            ImprovedWeaponCacheManager.CleanupDestroyedMaps();
        }
    }
}