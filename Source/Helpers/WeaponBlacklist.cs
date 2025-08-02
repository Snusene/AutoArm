// AutoArm RimWorld 1.5+ mod - automatic weapon management
// This file: Tracks weapons that fail to equip due to mod restrictions
// Prevents repeated equip attempts on incompatible weapons
// Uses: Time-based blacklisting with automatic expiry
// Note: Body-size restricted weapons can be retried if pawn equips power armor

using System.Collections.Generic;
using System.Linq;
using Verse;

namespace AutoArm
{
    /// <summary>
    /// Tracks weapons that have failed to equip due to mod restrictions
    /// This prevents AutoArm from repeatedly trying to equip incompatible weapons
    /// </summary>
    public static class WeaponBlacklist
    {
        // Track blacklisted weapon defs per pawn (some restrictions are pawn-specific)
        private static Dictionary<Pawn, HashSet<ThingDef>> blacklistedWeapons = new Dictionary<Pawn, HashSet<ThingDef>>();

        // Track when weapons were blacklisted for cleanup
        private static Dictionary<Pawn, Dictionary<ThingDef, int>> blacklistTimestamps = new Dictionary<Pawn, Dictionary<ThingDef, int>>();

        // How long to keep weapons blacklisted (1 minute in-game)
        private const int BLACKLIST_DURATION = 60;

        /// <summary>
        /// Check if a weapon def is blacklisted for a pawn
        /// </summary>
        public static bool IsBlacklisted(ThingDef weaponDef, Pawn pawn)
        {
            if (weaponDef == null || pawn == null)
                return false;

            if (!blacklistedWeapons.ContainsKey(pawn))
                return false;

            return blacklistedWeapons[pawn].Contains(weaponDef);
        }

        /// <summary>
        /// Add a weapon def to the blacklist for a pawn
        /// </summary>
        public static void AddToBlacklist(ThingDef weaponDef, Pawn pawn, string reason = null)
        {
            if (weaponDef == null || pawn == null)
                return;

            // Initialize collections if needed
            if (!blacklistedWeapons.ContainsKey(pawn))
            {
                blacklistedWeapons[pawn] = new HashSet<ThingDef>();
                blacklistTimestamps[pawn] = new Dictionary<ThingDef, int>();
            }

            // Add to blacklist
            blacklistedWeapons[pawn].Add(weaponDef);
            blacklistTimestamps[pawn][weaponDef] = Find.TickManager.TicksGame;

            // Log the blacklisting
            AutoArmLogger.LogPawn(pawn, $"Blacklisted {weaponDef.label} - {reason ?? "mod restriction"}");
        }

        /// <summary>
        /// Remove a weapon def from the blacklist for a pawn
        /// </summary>
        public static void RemoveFromBlacklist(ThingDef weaponDef, Pawn pawn)
        {
            if (weaponDef == null || pawn == null)
                return;

            if (blacklistedWeapons.ContainsKey(pawn))
            {
                blacklistedWeapons[pawn].Remove(weaponDef);

                if (blacklistTimestamps.ContainsKey(pawn))
                {
                    blacklistTimestamps[pawn].Remove(weaponDef);
                }
            }
        }

        /// <summary>
        /// Clear all blacklisted weapons for a pawn
        /// </summary>
        public static void ClearBlacklist(Pawn pawn)
        {
            if (pawn == null)
                return;

            blacklistedWeapons.Remove(pawn);
            blacklistTimestamps.Remove(pawn);
        }

        /// <summary>
        /// Clean up old blacklist entries and dead pawns
        /// </summary>
        public static void CleanupOldEntries()
        {
            int currentTick = Find.TickManager.TicksGame;

            // Clean up dead pawns
            var deadPawns = blacklistedWeapons.Keys.Where(p => p.Destroyed || p.Dead).ToList();
            foreach (var pawn in deadPawns)
            {
                blacklistedWeapons.Remove(pawn);
                blacklistTimestamps.Remove(pawn);
            }

            // Clean up expired blacklist entries
            foreach (var pawn in blacklistTimestamps.Keys.ToList())
            {
                if (!blacklistTimestamps.ContainsKey(pawn))
                    continue;

                var expiredWeapons = blacklistTimestamps[pawn]
                    .Where(kvp => currentTick - kvp.Value > BLACKLIST_DURATION)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var weaponDef in expiredWeapons)
                {
                    RemoveFromBlacklist(weaponDef, pawn);
                }

                // Remove pawn if no blacklisted weapons remain
                if (!blacklistedWeapons[pawn].Any())
                {
                    blacklistedWeapons.Remove(pawn);
                    blacklistTimestamps.Remove(pawn);
                }
            }
        }

        /// <summary>
        /// Get debug info about blacklisted weapons
        /// </summary>
        public static string GetDebugInfo()
        {
            var info = new System.Text.StringBuilder();
            info.AppendLine("[AutoArm] Weapon Blacklist Status:");

            foreach (var kvp in blacklistedWeapons)
            {
                if (kvp.Value.Any())
                {
                    info.AppendLine($"  {kvp.Key.Name}:");
                    foreach (var weaponDef in kvp.Value)
                    {
                        info.AppendLine($"    - {weaponDef.label}");
                    }
                }
            }

            return info.ToString();
        }
    }
}