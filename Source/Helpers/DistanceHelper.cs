using System.Collections.Generic;
using Verse;

namespace AutoArm
{
    /// <summary>
    /// Centralized distance calculations (fixes #9)
    /// Standardizes distance calculations throughout the mod
    /// </summary>
    public static class DistanceHelper
    {
        /// <summary>
        /// Get squared distance for performance (use for comparisons)
        /// </summary>
        public static float GetSquaredDistance(IntVec3 a, IntVec3 b)
        {
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
            return GetSquaredDistance(a, b) <= maxRange * maxRange;
        }

        /// <summary>
        /// Check if within range (squared for performance)
        /// </summary>
        public static bool IsWithinRange(Thing a, Thing b, float maxRange)
        {
            if (a == null || b == null)
                return false;
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