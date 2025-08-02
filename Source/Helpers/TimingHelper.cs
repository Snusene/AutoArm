// AutoArm RimWorld 1.5+ mod - automatic weapon management
// This file: Centralized cooldown and timing management system
// Prevents spam and controls action frequency across all mod systems
// Uses: Unified dictionary tracking with configurable durations
// Critical: Prevents performance issues and log spam in large colonies

using System.Collections.Generic;
using System.Linq;
using Verse;

namespace AutoArm
{
    /// <summary>
    /// Centralized timing and cooldown management (fixes #8, #13, #19, #20, #21)
    /// </summary>
    public static class TimingHelper
    {
        public enum CooldownType
        {
            WeaponSearch,
            SidearmSearch,
            Interruption,
            SidearmCheck,
            InBed,
            IncapableOfViolence,
            ForcedWeaponLog,
            DroppedWeapon,
            UpgradeCheck,
            PrimarySidearmCache,
            FailedUpgradeSearch,  // Used for both weapon upgrades and sidearm searches (30s)
            OnlySidearmsLog,      // Used to prevent spam when pawn has only sidearms
            UnarmedPrimaryLog,    // Used to prevent spam when unarmed pawn picks up primary
            ReplacePrimaryLog     // Used to prevent spam when replacing primary weapon
        }

        // Unified cooldown tracking (fixes #13)
        private static readonly Dictionary<CooldownType, Dictionary<object, int>> cooldowns = new Dictionary<CooldownType, Dictionary<object, int>>();

        // Cooldown durations
        private static readonly Dictionary<CooldownType, int> cooldownDurations = new Dictionary<CooldownType, int>
        {
            { CooldownType.WeaponSearch, 300 },
            { CooldownType.SidearmSearch, 300 },
            { CooldownType.Interruption, 300 },
            { CooldownType.SidearmCheck, 300 },
            { CooldownType.InBed, 2500 },
            { CooldownType.IncapableOfViolence, int.MaxValue / 2 }, // Effectively never log again after first time
            { CooldownType.ForcedWeaponLog, 10000 },
            { CooldownType.DroppedWeapon, 300 },
            { CooldownType.UpgradeCheck, 2500 },
            { CooldownType.PrimarySidearmCache, 120 },
            { CooldownType.FailedUpgradeSearch, 1800 },  // 30 seconds for non-emergency searches
            { CooldownType.OnlySidearmsLog, 3000 },      // 5 minutes to prevent spam
            { CooldownType.UnarmedPrimaryLog, 1800 },    // 30 seconds for unarmed pickup messages
            { CooldownType.ReplacePrimaryLog, 1800 }     // 30 seconds for primary replacement messages
        };

        static TimingHelper()
        {
            foreach (var type in System.Enum.GetValues(typeof(CooldownType)).Cast<CooldownType>())
            {
                cooldowns[type] = new Dictionary<object, int>();
            }
        }

        /// <summary>
        /// Check if an action is on cooldown
        /// </summary>
        public static bool IsOnCooldown(object key, CooldownType type)
        {
            if (!cooldowns.ContainsKey(type) || key == null)
                return false;

            var dict = cooldowns[type];
            if (dict.TryGetValue(key, out int lastTick))
            {
                int currentTick = Find.TickManager.TicksGame;
                int cooldownDuration = cooldownDurations.ContainsKey(type) ? cooldownDurations[type] : 300;
                return currentTick - lastTick < cooldownDuration;
            }

            return false;
        }

        /// <summary>
        /// Set cooldown for an action
        /// </summary>
        public static void SetCooldown(object key, CooldownType type)
        {
            if (!cooldowns.ContainsKey(type) || key == null)
                return;

            cooldowns[type][key] = Find.TickManager.TicksGame;
        }

        /// <summary>
        /// Get remaining cooldown ticks
        /// </summary>
        public static int GetRemainingCooldown(object key, CooldownType type)
        {
            if (!cooldowns.ContainsKey(type) || key == null)
                return 0;

            var dict = cooldowns[type];
            if (dict.TryGetValue(key, out int lastTick))
            {
                int currentTick = Find.TickManager.TicksGame;
                int cooldownDuration = cooldownDurations.ContainsKey(type) ? cooldownDurations[type] : 300;
                int remaining = cooldownDuration - (currentTick - lastTick);
                return remaining > 0 ? remaining : 0;
            }

            return 0;
        }

        /// <summary>
        /// Clear cooldown for a specific key
        /// </summary>
        public static void ClearCooldown(object key, CooldownType type)
        {
            if (!cooldowns.ContainsKey(type) || key == null)
                return;

            cooldowns[type].Remove(key);
        }

        /// <summary>
        /// Log with cooldown to prevent spam (fixes #20)
        /// </summary>
        public static void LogWithCooldown(Pawn pawn, string message, CooldownType cooldownType)
        {
            if (pawn == null)
                return;

            if (!IsOnCooldown(pawn, cooldownType))
            {
                AutoArmLogger.LogPawn(pawn, message);
                SetCooldown(pawn, cooldownType);
            }
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

        /// <summary>
        /// Clear all cooldowns for all pawns and objects
        /// </summary>
        public static void ClearAllCooldowns()
        {
            foreach (var cooldownType in cooldowns.Keys)
            {
                cooldowns[cooldownType].Clear();
            }
            
            AutoArmLogger.Log("Cleared all cooldowns");
        }

        /// <summary>
        /// Clean up old cooldown entries (part of fixes #4, #11)
        /// </summary>
        public static void CleanupOldCooldowns()
        {
            int currentTick = Find.TickManager.TicksGame;

            foreach (var cooldownType in cooldowns.Keys.ToList())
            {
                var dict = cooldowns[cooldownType];
                var maxAge = (cooldownDurations.ContainsKey(cooldownType) ? cooldownDurations[cooldownType] : 300) * 5; // Keep for 5x the cooldown duration

                // Remove entries for destroyed pawns
                var toRemove = new List<object>();

                foreach (var kvp in dict)
                {
                    if (kvp.Key is Pawn pawn && (pawn.Destroyed || pawn.Dead || !pawn.Spawned))
                    {
                        toRemove.Add(kvp.Key);
                    }
                    else if (kvp.Key is Thing thing && (thing.Destroyed || !thing.Spawned))
                    {
                        toRemove.Add(kvp.Key);
                    }
                    else if (currentTick - kvp.Value > maxAge)
                    {
                        toRemove.Add(kvp.Key);
                    }
                }

                foreach (var key in toRemove)
                {
                    dict.Remove(key);
                }
            }
        }

        /// <summary>
        /// Get a composite key for complex cooldown tracking
        /// </summary>
        public static string GetCompositeKey(params object[] parts)
        {
            return string.Join("_", parts.Select(p => p?.ToString() ?? "null"));
        }

        /// <summary>
        /// Check and set cooldown in one operation
        /// </summary>
        public static bool TrySetCooldown(object key, CooldownType type)
        {
            if (IsOnCooldown(key, type))
                return false;

            SetCooldown(key, type);
            return true;
        }
    }
}