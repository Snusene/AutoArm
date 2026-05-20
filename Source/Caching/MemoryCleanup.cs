
using AutoArm.Definitions;
using AutoArm.Helpers;
using System;
using System.Linq;
using Verse;

namespace AutoArm.Caching
{
    public sealed class MemoryCleanupGameComponent : GameComponent
    {
        private int cleanupInterval = Constants.MemoryCleanupInterval;

        private int lastCleanupDuration = 0;

        private int consecutiveSlowCleanups = 0;

        public MemoryCleanupGameComponent(Game game) : base()
        {
        }

        public override void GameComponentTick()
        {
            PawnValidation.FlushPendingInvalidationLogs();

            if (!Cleanup.ShouldRunCleanup())
                return;

            int currentTick = Find.TickManager.TicksGame;

            int offset = Gen.HashCombineInt(Current.Game?.World?.info?.seedString?.GetHashCode() ?? 0, 0xBEEF) & 0xFF;

            if (((currentTick + offset) % cleanupInterval) != 0)
                return;

            UpdateCleanupInterval();

            try
            {
                long memoryBefore = 0;
                if (AutoArmMod.settings?.debugLogging == true && Prefs.DevMode)
                {
                    GC.Collect(0, GCCollectionMode.Optimized);
                    memoryBefore = GC.GetTotalMemory(false);
                }

                long startTicks = DateTime.UtcNow.Ticks;
                Cleanup.PerformFullCleanup();
                long elapsedTicks = DateTime.UtcNow.Ticks - startTicks;

                lastCleanupDuration = (int)(elapsedTicks / TimeSpan.TicksPerMillisecond);

                if (lastCleanupDuration > Constants.CleanupPerformanceWarningMs)
                {
                    consecutiveSlowCleanups++;
                    if (consecutiveSlowCleanups > 3)
                    {
                        cleanupInterval = Math.Min(cleanupInterval * 2, 10000);
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

                if (AutoArmMod.settings?.debugLogging == true)
                {
                    if (Prefs.DevMode && memoryBefore > 0)
                    {
                        GC.Collect(0, GCCollectionMode.Optimized);
                        long memoryAfter = GC.GetTotalMemory(false);
                        long memoryFreed = Math.Max(0, memoryBefore - memoryAfter);

                        AutoArmLogger.Debug(() => $"Memory cleanup completed in {lastCleanupDuration}ms, freed ~{memoryFreed / 1024}KB");
                    }
                    else if (lastCleanupDuration > Constants.CleanupPerformanceLogMs)
                    {
                        AutoArmLogger.Debug(() => $"Memory cleanup completed in {lastCleanupDuration}ms");
                    }

                    if (lastCleanupDuration > Constants.CleanupPerformanceWarningMs)
                    {
                        AutoArmLogger.Warn($"Memory cleanup took {lastCleanupDuration}ms - performance impact detected");
                    }
                }
            }
            catch (Exception e)
            {
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.WarnFileOnly($"Memory cleanup failed: {e.GetType().Name}: {e.Message}");
                }

                cleanupInterval = Constants.MemoryCleanupInterval;
            }
        }


        private void UpdateCleanupInterval()
        {
            int baseInterval = Constants.MemoryCleanupInterval;

            int totalPawns = 0;
            foreach (Map map in Find.Maps)
            {
                if (map?.mapPawns != null)
                {
                    totalPawns += map.mapPawns.FreeColonistsSpawnedCount;
                }
            }

            if (totalPawns > 50)
            {
                cleanupInterval = baseInterval / 2;
            }
            else if (totalPawns < 10)
            {
                cleanupInterval = baseInterval * 2;
            }
            else
            {
                cleanupInterval = baseInterval;
            }

            int gameSpeed = (int)Find.TickManager.CurTimeSpeed;
            if (gameSpeed == 3)
            {
                cleanupInterval = cleanupInterval * 2 / 3;
            }

            cleanupInterval = Math.Max(1000, Math.Min(10000, cleanupInterval));
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref cleanupInterval, "cleanupInterval", Constants.MemoryCleanupInterval);
            Scribe_Values.Look(ref lastCleanupDuration, "lastCleanupDuration", 0);

            if (Scribe.mode == LoadSaveMode.LoadingVars || Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (cleanupInterval < 1000 || cleanupInterval > 10000)
                    cleanupInterval = Constants.MemoryCleanupInterval;
            }
        }

    }
}
