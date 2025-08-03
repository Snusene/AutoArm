// AutoArm RimWorld 1.5+ mod - automatic weapon management
// This file: Memory management and cleanup coordination
// Prevents memory leaks in long-running games

using System;
using Verse;
using AutoArm.Helpers;
using AutoArm.Logging;

namespace AutoArm.Caching
{
    /// <summary>
    /// Simple memory cleanup manager that uses the consolidated CleanupHelper
    /// </summary>
    [StaticConstructorOnStartup]
    public static class MemoryCleanupManager
    {
        private static HarmonyLib.Harmony harmony;
        private static int lastCleanupTick = 0;
        
        static MemoryCleanupManager()
        {
            try
            {
                var methodToPatch = typeof(Game).GetMethod("UpdatePlay");
                if (methodToPatch == null)
                {
                    Log.Error("[AutoArm] Could not find Game.UpdatePlay method for memory cleanup patch");
                    return;
                }
                
                harmony = new HarmonyLib.Harmony("AutoArm.MemoryCleanup.GameUpdatePlay");
                harmony.Patch(
                    methodToPatch,
                    postfix: new HarmonyLib.HarmonyMethod(typeof(MemoryCleanupManager).GetMethod(nameof(GameUpdatePlay_Postfix)))
                );
                
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.Debug("MemoryCleanupManager patched Game.UpdatePlay successfully");
                }
            }
            catch (Exception e)
            {
                AutoArmLogger.Error("Failed to patch Game.UpdatePlay for memory cleanup", e);
            }
        }

        public static void GameUpdatePlay_Postfix()
        {
            try
            {
                // Use consolidated cleanup helper (fixes #4, #11, #28)
                if (CleanupHelper.ShouldRunCleanup())
                {
                    int currentTick = Find.TickManager?.TicksGame ?? 0;
                    
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    
                    // Get memory before cleanup in dev mode
                    long memoryBefore = 0;
                    if (Prefs.DevMode)
                    {
                        GC.Collect(0, GCCollectionMode.Optimized);
                        memoryBefore = GC.GetTotalMemory(false);
                    }
                    
                    CleanupHelper.PerformFullCleanup();
                    
                    sw.Stop();
                    
                    // Log cleanup metrics
                    if (AutoArmMod.settings?.debugLogging == true || (Prefs.DevMode && sw.ElapsedMilliseconds > 50))
                    {
                        if (Prefs.DevMode)
                        {
                            GC.Collect(0, GCCollectionMode.Optimized);
                            long memoryAfter = GC.GetTotalMemory(false);
                            long memoryFreed = memoryBefore - memoryAfter;
                            
                            AutoArmLogger.Debug($"Memory cleanup completed in {sw.ElapsedMilliseconds}ms, freed ~{memoryFreed / 1024}KB");
                        }
                        else
                        {
                            AutoArmLogger.Debug($"Memory cleanup completed in {sw.ElapsedMilliseconds}ms");
                        }
                        
                        if (sw.ElapsedMilliseconds > 100)
                        {
                            AutoArmLogger.Warn($"Memory cleanup took {sw.ElapsedMilliseconds}ms - performance impact detected");
                        }
                    }
                    
                    lastCleanupTick = currentTick;
                }
            }
            catch (Exception e)
            {
                // Don't spam errors every frame if cleanup fails
                if (Find.TickManager?.TicksGame % 10000 == 0)
                {
                    AutoArmLogger.Error("Memory cleanup failed", e);
                }
            }
        }
    }
}