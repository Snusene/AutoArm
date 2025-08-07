// AutoArm RimWorld 1.5+ mod - automatic weapon management
// This file: Memory management and cleanup coordination
// Prevents memory leaks in long-running games
//
// PURPOSE: Cleans up tracking dictionaries that accumulate stale entries
// This is STILL NEEDED even with real-time weapon cache because other
// systems (forced weapons, dropped items, job tracking) need cleanup

using AutoArm.Definitions;
using AutoArm.Helpers;
using AutoArm.Logging;
using System;
using System.Diagnostics;
using System.Linq;
using Verse;

namespace AutoArm.Caching
{
    /// <summary>
    /// GameComponent that handles periodic memory cleanup
    /// </summary>
    public class MemoryCleanupGameComponent : GameComponent
    {
        private int nextCleanupTick = 0;
        private int cleanupInterval = Constants.MemoryCleanupInterval;
        
        // Track cleanup performance
        private static readonly Stopwatch _sw = new Stopwatch();
        private int lastCleanupDuration = 0;
        private int consecutiveSlowCleanups = 0;

        public MemoryCleanupGameComponent(Game game) : base()
        {
        }

        public override void GameComponentTick()
        {
            // Only run cleanup at intervals
            if (Find.TickManager.TicksGame < nextCleanupTick)
                return;

            // Dynamic interval based on colony size and last cleanup duration
            UpdateCleanupInterval();
            nextCleanupTick = Find.TickManager.TicksGame + cleanupInterval;

            try
            {
                // Get memory before cleanup in dev mode
                long memoryBefore = 0;
                if (AutoArmMod.settings?.debugLogging == true && Prefs.DevMode)
                {
                    GC.Collect(0, GCCollectionMode.Optimized);
                    memoryBefore = GC.GetTotalMemory(false);
                }

                _sw.Restart();
                CleanupHelper.PerformFullCleanup();
                _sw.Stop();
                
                lastCleanupDuration = (int)_sw.ElapsedMilliseconds;

                // Track slow cleanups
                if (lastCleanupDuration > Constants.CleanupPerformanceWarningMs)
                {
                    consecutiveSlowCleanups++;
                    if (consecutiveSlowCleanups > 3)
                    {
                        // If cleanup is consistently slow, increase interval
                        cleanupInterval = Math.Min(cleanupInterval * 2, 10000); // Cap at ~167 seconds
                        if (AutoArmMod.settings?.debugLogging == true)
                        {
                            AutoArmLogger.Warn($"Cleanup taking too long ({lastCleanupDuration}ms), increasing interval to {cleanupInterval} ticks");
                        }
                        consecutiveSlowCleanups = 0;
                    }
                }
                else
                {
                    consecutiveSlowCleanups = 0;
                }

                // Log cleanup metrics in debug mode
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    if (Prefs.DevMode && memoryBefore > 0)
                    {
                        GC.Collect(0, GCCollectionMode.Optimized);
                        long memoryAfter = GC.GetTotalMemory(false);
                        long memoryFreed = Math.Max(0, memoryBefore - memoryAfter);

                        AutoArmLogger.Debug($"Memory cleanup completed in {lastCleanupDuration}ms, freed ~{memoryFreed / 1024}KB");
                    }
                    else if (lastCleanupDuration > Constants.CleanupPerformanceLogMs)
                    {
                        AutoArmLogger.Debug($"Memory cleanup completed in {lastCleanupDuration}ms");
                    }

                    if (lastCleanupDuration > Constants.CleanupPerformanceWarningMs)
                    {
                        AutoArmLogger.Warn($"Memory cleanup took {lastCleanupDuration}ms - performance impact detected");
                    }
                }
            }
            catch (Exception e)
            {
                // Don't spam errors every cleanup cycle
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.Error("Memory cleanup failed", e);
                }
                
                // Reset to default interval on error
                cleanupInterval = Constants.MemoryCleanupInterval;
            }
        }

        /// <summary>
        /// Dynamically adjust cleanup interval based on game state
        /// </summary>
        private void UpdateCleanupInterval()
        {
            // Start with base interval
            int baseInterval = Constants.MemoryCleanupInterval;
            
            // Adjust based on colony size
            int totalPawns = 0;
            foreach (Map map in Find.Maps)
            {
                if (map?.mapPawns != null)
                {
                    totalPawns += map.mapPawns.FreeColonists.Count;
                }
            }
            
            if (totalPawns > 50)
            {
                // Large colonies need more frequent cleanup
                cleanupInterval = baseInterval / 2; // ~21 seconds
            }
            else if (totalPawns < 10)
            {
                // Small colonies can clean up less frequently
                cleanupInterval = baseInterval * 2; // ~84 seconds
            }
            else
            {
                // Normal interval for medium colonies
                cleanupInterval = baseInterval;
            }
            
            // Adjust based on game speed
            int gameSpeed = (int)Find.TickManager.CurTimeSpeed;
            if (gameSpeed == 3) // Ultra speed
            {
                // Clean up more frequently at high speed
                cleanupInterval = cleanupInterval * 2 / 3;
            }
            
            // Clamp to reasonable bounds
            cleanupInterval = Math.Max(1000, Math.Min(10000, cleanupInterval));
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref nextCleanupTick, "nextCleanupTick", 0);
            Scribe_Values.Look(ref cleanupInterval, "cleanupInterval", Constants.MemoryCleanupInterval);
            Scribe_Values.Look(ref lastCleanupDuration, "lastCleanupDuration", 0);
        }

        /// <summary>
        /// Force an immediate cleanup (for debugging)
        /// </summary>
        public static void ForceCleanup()
        {
            if (!Prefs.DevMode)
                return;
                
            AutoArmLogger.Warn("Forcing immediate memory cleanup...");
            CleanupHelper.PerformFullCleanup();
            
            // Also force our weapon cache validation
            foreach (Map map in Find.Maps)
            {
                ImprovedWeaponCacheManager.LogCacheStatistics();
            }
        }
    }
}