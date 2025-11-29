
using AutoArm.Definitions;
using AutoArm.Helpers;
using AutoArm.Logging;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace AutoArm.Weapons
{
    /// <summary>
    /// Prevent weapon re-equip loops - uses TickScheduler
    /// </summary>
    public static class WeaponBlacklist
    {
        private static Dictionary<Pawn, HashSet<ThingDef>> blacklist = new Dictionary<Pawn, HashSet<ThingDef>>();

        private static Dictionary<Pawn, Dictionary<ThingDef, int>> timestamps = new Dictionary<Pawn, Dictionary<ThingDef, int>>();

        private static Dictionary<(Pawn, string), List<string>> pendingBlacklistLogs = new Dictionary<(Pawn, string), List<string>>();

        // Lookups for TickScheduler event handling (pawnId -> Pawn, defHash -> ThingDef)
        private static Dictionary<int, Pawn> idToPawnLookup = new Dictionary<int, Pawn>();
        private static Dictionary<int, ThingDef> hashToDefLookup = new Dictionary<int, ThingDef>();

        /// <summary>
        /// All blacklists
        /// </summary>
        public static Dictionary<Pawn, HashSet<ThingDef>> GetAllBlacklists()
        {
            var result = new Dictionary<Pawn, HashSet<ThingDef>>();
            foreach (var kvp in blacklist)
            {
                result[kvp.Key] = new HashSet<ThingDef>(kvp.Value);
            }
            return result;
        }

        public static bool IsBlacklisted(ThingDef weaponDef, Pawn pawn)
        {
            if (weaponDef == null || pawn == null)
                return false;

            if (!blacklist.ContainsKey(pawn))
                return false;

            return blacklist[pawn].Contains(weaponDef);
        }

        public static void AddToBlacklist(ThingDef weaponDef, Pawn pawn, string reason = null)
        {
            if (weaponDef == null || pawn == null)
                return;

            if (!blacklist.ContainsKey(pawn))
            {
                blacklist[pawn] = new HashSet<ThingDef>();
                timestamps[pawn] = new Dictionary<ThingDef, int>();
            }

            int currentTick = Find.TickManager.TicksGame;
            int expireTick = currentTick + Constants.WeaponBlacklistDuration;
            int pawnId = pawn.thingIDNumber;
            int defHash = weaponDef.shortHash;

            if (blacklist[pawn].Contains(weaponDef))
            {
                // Cancel old schedule before rescheduling
                TickScheduler.Cancel(TickScheduler.EventType.BlacklistExpiry, pawnId, defHash);
            }

            blacklist[pawn].Add(weaponDef);
            timestamps[pawn][weaponDef] = currentTick;

            // Track lookups for event handling
            idToPawnLookup[pawnId] = pawn;
            hashToDefLookup[defHash] = weaponDef;

            TickScheduler.Schedule(expireTick, TickScheduler.EventType.BlacklistExpiry, pawnId, defHash);

            if (AutoArmMod.settings?.debugLogging == true)
            {
                string reasonText = string.IsNullOrEmpty(reason) ? "Unknown" : reason;
                var key = (pawn, reasonText);

                if (!pendingBlacklistLogs.ContainsKey(key))
                {
                    pendingBlacklistLogs[key] = new List<string>();
                }

                pendingBlacklistLogs[key].Add(weaponDef.label);
            }
        }

        /// <summary>
        /// Flush pending blacklist log messages (consolidates multiple weapons with same reason)
        /// Output batched logs
        /// </summary>
        public static void FlushPendingLogs()
        {
            if (pendingBlacklistLogs.Count == 0)
                return;

            pendingBlacklistLogs.Clear();
        }

        /// <summary>
        /// Remove a weapon def from the blacklist for a pawn
        /// </summary>
        public static void RemoveFromBlacklist(ThingDef weaponDef, Pawn pawn)
        {
            if (weaponDef == null || pawn == null)
                return;

            if (blacklist.ContainsKey(pawn))
            {
                if (blacklist[pawn].Remove(weaponDef))
                {
                    TickScheduler.Cancel(TickScheduler.EventType.BlacklistExpiry, pawn.thingIDNumber, weaponDef.shortHash);
                }

                if (timestamps.ContainsKey(pawn))
                {
                    timestamps[pawn].Remove(weaponDef);
                }
            }
        }

        /// <summary>
        /// Clear pawn blacklist
        /// </summary>
        public static void ClearBlacklist(Pawn pawn)
        {
            if (pawn == null)
                return;

            if (blacklist.ContainsKey(pawn) && blacklist[pawn].Any())
            {
                int count = blacklist[pawn].Count;
                AutoArmLogger.Debug(() => $"[{pawn.Name?.ToStringShort ?? "Unknown"}] Cleared weapon blacklist ({count} weapons)");
            }

            blacklist.Remove(pawn);
            timestamps.Remove(pawn);
        }

        /// <summary>
        /// Event-driven removal when pawn dies/destroyed
        /// </summary>
        public static void RemovePawn(Pawn pawn)
        {
            if (pawn == null) return;

            int pawnId = pawn.thingIDNumber;
            if (blacklist.TryGetValue(pawn, out var weapons))
            {
                foreach (var weaponDef in weapons)
                    TickScheduler.Cancel(TickScheduler.EventType.BlacklistExpiry, pawnId, weaponDef.shortHash);
            }

            blacklist.Remove(pawn);
            timestamps.Remove(pawn);
            idToPawnLookup.Remove(pawnId);

            // Clean pendingBlacklistLogs entries for this pawn
            var keysToRemove = ListPool<(Pawn, string)>.Get();
            foreach (var key in pendingBlacklistLogs.Keys)
            {
                if (key.Item1 == pawn)
                    keysToRemove.Add(key);
            }
            foreach (var key in keysToRemove)
                pendingBlacklistLogs.Remove(key);
            ListPool<(Pawn, string)>.Return(keysToRemove);
        }

        /// <summary>
        /// Event handler called by TickScheduler when blacklist entry expires
        /// </summary>
        public static void OnBlacklistExpiredEvent(int pawnId, int defHash)
        {
            if (!idToPawnLookup.TryGetValue(pawnId, out var pawn))
                return;
            if (!hashToDefLookup.TryGetValue(defHash, out var weaponDef))
                return;

            if (blacklist.TryGetValue(pawn, out var weaponSet))
            {
                weaponSet.Remove(weaponDef);

                if (weaponSet.Count == 0)
                {
                    blacklist.Remove(pawn);
                    idToPawnLookup.Remove(pawnId);
                }
            }

            if (timestamps.TryGetValue(pawn, out var ts))
            {
                ts.Remove(weaponDef);

                if (ts.Count == 0)
                {
                    timestamps.Remove(pawn);
                }
            }
        }

        /// <summary>
        /// Legacy method - kept for backward compatibility
        /// Now handled by TickScheduler.ProcessTick() -> OnBlacklistExpiredEvent
        /// </summary>
        public static void ProcessExpiredBlacklists(int tick)
        {
            // Now handled by TickScheduler
        }

        /// <summary>
        /// Cleanup dead pawns (expiry handled by TickScheduler)
        /// </summary>
        public static void CleanupOldEntries()
        {
            if (blacklist.Count == 0)
                return;

            int deadPawnCount = 0;

            var deadPawns = ListPool<Pawn>.Get();
            foreach (var pawn in blacklist.Keys)
            {
                if (pawn.Destroyed || pawn.Dead)
                    deadPawns.Add(pawn);
            }

            foreach (var pawn in deadPawns)
            {
                int pawnId = pawn.thingIDNumber;
                if (blacklist.TryGetValue(pawn, out var weapons))
                {
                    foreach (var weaponDef in weapons)
                    {
                        TickScheduler.Cancel(TickScheduler.EventType.BlacklistExpiry, pawnId, weaponDef.shortHash);
                    }
                }

                blacklist.Remove(pawn);
                timestamps.Remove(pawn);
                idToPawnLookup.Remove(pawnId);
                deadPawnCount++;
            }
            ListPool<Pawn>.Return(deadPawns);

            if (deadPawnCount > 0 && AutoArmMod.settings?.debugLogging == true)
            {
                AutoArmLogger.Debug(() => $"Blacklist cleanup: {deadPawnCount} dead pawns");
            }
        }

        /// <summary>
        /// Blacklist debug
        /// </summary>
        public static string GetDebugInfo()
        {
            var info = new System.Text.StringBuilder();
            info.AppendLine("Weapon Blacklist Status:");

            foreach (var kvp in blacklist)
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


        /// <summary>
        /// Blacklisted
        /// </summary>
        public static bool IsBlacklisted(Thing weapon, Pawn pawn)
        {
            return weapon != null && IsBlacklisted(weapon.def, pawn);
        }

        /// <summary>
        /// Blacklist weapon
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
        /// Clear blacklists
        /// </summary>
        public static void ClearAll()
        {
            blacklist.Clear();
            timestamps.Clear();
            idToPawnLookup.Clear();
            hashToDefLookup.Clear();
            pendingBlacklistLogs.Clear();
            // TickScheduler.Reset() handles clearing all scheduled events
        }

        /// <summary>
        /// EVENT-BASED: Reset all state (for map changes, game reset)
        /// </summary>
        public static void Reset()
        {
            ClearAll();
            AutoArmLogger.Debug(() => "WeaponBlacklist reset");
        }

        /// <summary>
        /// Rebuild expiry schedule from existing blacklists (for load/initialization)
        /// </summary>
        public static void RebuildFromExistingBlacklists()
        {
            idToPawnLookup.Clear();
            hashToDefLookup.Clear();

            int scheduledCount = 0;
            foreach (var pawnKvp in timestamps)
            {
                var pawn = pawnKvp.Key;
                var pawnTimestamps = pawnKvp.Value;

                if (pawn?.Destroyed != false || pawn.Dead)
                    continue;

                int pawnId = pawn.thingIDNumber;
                idToPawnLookup[pawnId] = pawn;

                foreach (var weaponKvp in pawnTimestamps)
                {
                    var weaponDef = weaponKvp.Key;
                    int blacklistTick = weaponKvp.Value;
                    int expireTick = blacklistTick + Constants.WeaponBlacklistDuration;

                    int currentTick = Find.TickManager.TicksGame;
                    if (expireTick > currentTick)
                    {
                        int defHash = weaponDef.shortHash;
                        hashToDefLookup[defHash] = weaponDef;
                        TickScheduler.Schedule(expireTick, TickScheduler.EventType.BlacklistExpiry, pawnId, defHash);
                        scheduledCount++;
                    }
                }
            }

            AutoArmLogger.Debug(() => $"WeaponBlacklist rebuilt: {blacklist.Count} pawns tracked, " +
                              $"{scheduledCount} expiry events scheduled");
        }
    }
}
