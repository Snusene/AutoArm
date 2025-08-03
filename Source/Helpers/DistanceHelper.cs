// AutoArm RimWorld 1.5+ mod - automatic weapon management
// This file: Optimized distance calculations for weapon searches
// Uses squared distances to avoid expensive sqrt operations in hot paths
// Critical: Performance optimization for large colonies with many weapons
// Note: Always use squared distance for comparisons, real distance only when needed

using System.Collections.Generic;
using Verse;
using AutoArm.Logging;
using System.Diagnostics;

namespace AutoArm.Helpers
{
    /// <summary>
    /// Centralized distance calculations (fixes #9)
    /// Standardizes distance calculations throughout the mod
    /// </summary>
    public static class DistanceHelper
    {
        // Diagnostic tracking (only when explicitly enabled)
        private static bool _enableDiagnostics = false;
        private static int _distanceCalculations = 0;
        private static int _rangeChecks = 0;
        private static Stopwatch _diagnosticTimer = new Stopwatch();
        private const int DiagnosticReportInterval = 10000; // Report every 10k calculations
        
        /// <summary>
        /// Enable diagnostic tracking for performance analysis
        /// </summary>
        public static void EnableDiagnostics(bool enable)
        {
            _enableDiagnostics = enable && AutoArmMod.settings?.debugLogging == true;
            if (_enableDiagnostics)
            {
                _diagnosticTimer.Restart();
                AutoArmLogger.Debug("Distance calculation diagnostics enabled");
            }
            else
            {
                ReportDiagnostics();
                _distanceCalculations = 0;
                _rangeChecks = 0;
                _diagnosticTimer.Reset();
            }
        }
        
        private static void ReportDiagnostics()
        {
            if (_distanceCalculations > 0 && _diagnosticTimer.ElapsedMilliseconds > 0)
            {
                var callsPerSecond = (_distanceCalculations * 1000.0) / _diagnosticTimer.ElapsedMilliseconds;
                AutoArmLogger.Debug($"Distance calculations: {_distanceCalculations:N0} total, {_rangeChecks:N0} range checks, {callsPerSecond:F0} calls/sec");
            }
        }
        /// <summary>
        /// Get squared distance for performance (use for comparisons)
        /// </summary>
        public static float GetSquaredDistance(IntVec3 a, IntVec3 b)
        {
            if (_enableDiagnostics)
            {
                _distanceCalculations++;
                if (_distanceCalculations % DiagnosticReportInterval == 0)
                {
                    ReportDiagnostics();
                }
            }
            return (a - b).LengthHorizontalSquared;
        }

        /// <summary>
        /// Get squared distance for performance (use for comparisons)
        /// </summary>
        public static float GetSquaredDistance(Thing a, Thing b)
        {
            if (a == null || b == null)
                return float.MaxValue;
            return GetSquaredDistance(a.Position, b.Position);
        }

        /// <summary>
        /// Get actual distance (use when you need the real value)
        /// </summary>
        public static float GetDistance(IntVec3 a, IntVec3 b)
        {
            return (a - b).LengthHorizontal;
        }

        /// <summary>
        /// Get actual distance (use when you need the real value)
        /// </summary>
        public static float GetDistance(Thing a, Thing b)
        {
            if (a == null || b == null)
                return float.MaxValue;
            return GetDistance(a.Position, b.Position);
        }

        /// <summary>
        /// Check if within range (squared for performance)
        /// </summary>
        public static bool IsWithinRange(IntVec3 a, IntVec3 b, float maxRange)
        {
            if (_enableDiagnostics)
            {
                _rangeChecks++;
            }
            return GetSquaredDistance(a, b) <= maxRange * maxRange;
        }

        /// <summary>
        /// Check if within range (squared for performance)
        /// </summary>
        public static bool IsWithinRange(Thing a, Thing b, float maxRange)
        {
            if (a == null || b == null)
            {
                if (_enableDiagnostics && AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.Warn($"IsWithinRange called with null thing: a={a?.GetType().Name ?? "null"}, b={b?.GetType().Name ?? "null"}");
                }
                return false;
            }
            return IsWithinRange(a.Position, b.Position, maxRange);
        }

        /// <summary>
        /// Sort things by distance from a position (uses squared distance for performance)
        /// </summary>
        public static void SortByDistance<T>(List<T> things, IntVec3 from) where T : Thing
        {
            things.Sort((a, b) =>
                GetSquaredDistance(a.Position, from).CompareTo(GetSquaredDistance(b.Position, from)));
        }
    }
}