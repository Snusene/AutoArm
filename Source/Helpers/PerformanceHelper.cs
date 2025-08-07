// AutoArm RimWorld 1.5+ mod - automatic weapon management
// This file: Performance monitoring and optimization helpers
// Handles: Performance tracking, throttling, and optimization

using AutoArm.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Verse;

namespace AutoArm.Helpers
{
    /// <summary>
    /// Simple performance monitoring for AutoArm
    /// </summary>
    public static class PerformanceHelper
    {
        private static Dictionary<string, long> operationTimes = new Dictionary<string, long>();
        private static Dictionary<string, int> operationCounts = new Dictionary<string, int>();
        private static int lastReportTick = 0;

        /// <summary>
        /// Track the time taken by an operation
        /// </summary>
        public static void TrackOperation(string operationName, long milliseconds)
        {
            if (!operationTimes.ContainsKey(operationName))
            {
                operationTimes[operationName] = 0;
                operationCounts[operationName] = 0;
            }

            operationTimes[operationName] += milliseconds;
            operationCounts[operationName]++;

            // Warn if operation took too long
            if (milliseconds > 10)
            {
                AutoArmLogger.Warn($"Performance: {operationName} took {milliseconds}ms");
            }
        }

        /// <summary>
        /// Simple timing scope for measuring operations
        /// </summary>
        public class TimingScope : IDisposable
        {
            private readonly string operationName;
            private readonly Stopwatch stopwatch;

            public TimingScope(string operationName)
            {
                this.operationName = operationName;
                this.stopwatch = Stopwatch.StartNew();
            }

            public void Dispose()
            {
                stopwatch.Stop();
                TrackOperation(operationName, stopwatch.ElapsedMilliseconds);
            }
        }

        /// <summary>
        /// Start timing an operation
        /// </summary>
        public static IDisposable Time(string operationName)
        {
            return new TimingScope(operationName);
        }

        /// <summary>
        /// Generate and log performance report
        /// </summary>
        public static void GenerateReport()
        {
            if (operationTimes.Count == 0) return;

            int ticksSinceLastReport = Find.TickManager.TicksGame - lastReportTick;
            if (ticksSinceLastReport < 6000) return; // Only report every 100 seconds

            AutoArmLogger.Log("=== AutoArm Performance Report ===");
            AutoArmLogger.Log($"Time period: {ticksSinceLastReport / 60f:F1} seconds");

            foreach (var kvp in operationTimes)
            {
                string operation = kvp.Key;
                long totalTime = kvp.Value;
                int count = operationCounts[operation];
                double avgTime = count > 0 ? (double)totalTime / count : 0;

                AutoArmLogger.Log($"{operation}: {count} calls, {totalTime}ms total, {avgTime:F1}ms avg");
            }

            // Reset counters
            operationTimes.Clear();
            operationCounts.Clear();
            lastReportTick = Find.TickManager.TicksGame;
        }
    }
}