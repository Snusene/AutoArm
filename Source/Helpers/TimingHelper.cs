// AutoArm RimWorld 1.5+ mod - automatic weapon management
// This file: Simplified timing helper with no cooldowns
// Removed: Failed search cooldown - keeping it simple

using AutoArm.Definitions;
using AutoArm.Logging;
using Verse;

namespace AutoArm.Helpers
{
    /// <summary>
    /// Simplified timing helper - no cooldowns
    /// </summary>
    public static class TimingHelper
    {
        /// <summary>
        /// Clean up method kept for compatibility
        /// </summary>
        public static void CleanupOldCooldowns()
        {
            // No cooldowns to clean up
        }

        /// <summary>
        /// Clear all cooldowns - kept for compatibility
        /// </summary>
        public static void ClearAllCooldowns()
        {
            // No cooldowns to clear
        }

        /// <summary>
        /// Check if enough time has passed for an interval check
        /// </summary>
        public static bool ShouldCheckInterval(Pawn pawn, int baseInterval, int variance, string intervalKey)
        {
            if (pawn == null)
                return false;

            // Use pawn's thingIDNumber for consistent variance
            int actualInterval = baseInterval + (pawn.thingIDNumber % variance);
            return pawn.IsHashIntervalTick(actualInterval);
        }
    }
}