// AutoArm RimWorld 1.5+ mod - automatic weapon management
// This file: Tracks weapons that fail to equip due to mod restrictions
// Prevents repeated equip attempts on incompatible weapons
// Uses: Time-based blacklisting with automatic expiry
// Note: Body-size restricted weapons can be retried if pawn equips power armor

using AutoArm.Definitions;
using AutoArm.Logging;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace AutoArm.Weapons
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
            if (AutoArmMod.settings?.debugLogging == true)
            {
                AutoArmLogger.LogPawn(pawn, $"Blacklisted {weaponDef.label} - {reason ?? "mod restriction"}");
            }
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
                if (blacklistedWeapons[pawn].Remove(weaponDef))
                {
                    // Log removal of important blacklist entries
                    if (AutoArmMod.settings?.debugLogging == true)
                    {
                        AutoArmLogger.Debug($"[{pawn.Name?.ToStringShort ?? "Unknown"}] Removed {weaponDef.label} from blacklist");
                    }
                }

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

            // Log when clearing blacklist (important state change)
            if (blacklistedWeapons.ContainsKey(pawn) && blacklistedWeapons[pawn].Any())
            {
                int count = blacklistedWeapons[pawn].Count;
                AutoArmLogger.Debug($"[{pawn.Name?.ToStringShort ?? "Unknown"}] Cleared weapon blacklist ({count} weapons)");
            }

            blacklistedWeapons.Remove(pawn);
            blacklistTimestamps.Remove(pawn);
        }

        /// <summary>
        /// Clean up old blacklist entries and dead pawns
        /// </summary>
        public static void CleanupOldEntries()
        {
            int currentTick = Find.TickManager.TicksGame;
            int deadPawnCount = 0;
            int expiredCount = 0;

            // Clean up dead pawns
            var deadPawns = blacklistedWeapons.Keys.Where(p => p.Destroyed || p.Dead).ToList();
            foreach (var pawn in deadPawns)
            {
                blacklistedWeapons.Remove(pawn);
                blacklistTimestamps.Remove(pawn);
                deadPawnCount++;
            }

            // Clean up expired blacklist entries
            foreach (var pawn in blacklistTimestamps.Keys.ToList())
            {
                if (!blacklistTimestamps.ContainsKey(pawn))
                    continue;

                var expiredWeapons = blacklistTimestamps[pawn]
                    .Where(kvp => currentTick - kvp.Value > Constants.WeaponBlacklistDuration)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var weaponDef in expiredWeapons)
                {
                    RemoveFromBlacklist(weaponDef, pawn);
                    expiredCount++;
                }

                // Remove pawn if no blacklisted weapons remain
                if (!blacklistedWeapons[pawn].Any())
                {
                    blacklistedWeapons.Remove(pawn);
                    blacklistTimestamps.Remove(pawn);
                }
            }

            // Log cleanup summary if anything was cleaned
            if ((deadPawnCount > 0 || expiredCount > 0) && AutoArmMod.settings?.debugLogging == true)
            {
                AutoArmLogger.Debug($"Blacklist cleanup: {deadPawnCount} dead pawns, {expiredCount} expired weapons");
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

        // ========== Test-specific method overloads ==========

        /// <summary>
        /// Check if a weapon is blacklisted (overload for tests)
        /// </summary>
        public static bool IsBlacklisted(Thing weapon, Pawn pawn)
        {
            return weapon != null && IsBlacklisted(weapon.def, pawn);
        }

        /// <summary>
        /// Add a weapon to the blacklist (overload for tests)
        /// </summary>
        public static void AddToBlacklist(Thing weapon, Pawn pawn, string reason = null)
        {
            if (weapon != null)
                AddToBlacklist(weapon.def, pawn, reason);
        }

        /// <summary>
        /// Remove a weapon from the blacklist (overload for tests)
        /// </summary>
        public static void RemoveFromBlacklist(Thing weapon, Pawn pawn)
        {
            if (weapon != null)
                RemoveFromBlacklist(weapon.def, pawn);
        }

        /// <summary>
        /// Clear all blacklists (test method)
        /// </summary>
        public static void ClearAll()
        {
            blacklistedWeapons.Clear();
            blacklistTimestamps.Clear();
        }
    }
}