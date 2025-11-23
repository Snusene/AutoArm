
using AutoArm.Definitions;
using AutoArm.Helpers;
using AutoArm.Logging;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace AutoArm.Weapons
{
    /// <summary>
    /// Prevent weapon re-equip loops
    /// </summary>
    public static class WeaponBlacklist
    {
        private static Dictionary<Pawn, HashSet<ThingDef>> blacklist = new Dictionary<Pawn, HashSet<ThingDef>>();

        private static Dictionary<Pawn, Dictionary<ThingDef, int>> timestamps = new Dictionary<Pawn, Dictionary<ThingDef, int>>();

        private static Dictionary<int, List<(Pawn, ThingDef)>> expirySchedule = new Dictionary<int, List<(Pawn, ThingDef)>>();

        private static Dictionary<(Pawn, string), List<string>> pendingBlacklistLogs = new Dictionary<(Pawn, string), List<string>>();

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

            if (blacklist[pawn].Contains(weaponDef) && timestamps[pawn].TryGetValue(weaponDef, out int oldExpireTick))
            {
                RemoveFromSchedule(pawn, weaponDef, oldExpireTick);
            }

            blacklist[pawn].Add(weaponDef);
            timestamps[pawn][weaponDef] = currentTick;

            if (!expirySchedule.TryGetValue(expireTick, out var list))
            {
                list = new List<(Pawn, ThingDef)>();
                expirySchedule[expireTick] = list;
            }
            list.Add((pawn, weaponDef));

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

            foreach (var kvp in pendingBlacklistLogs)
            {
                var (pawn, reason) = kvp.Key;
                var weapons = kvp.Value;

                if (weapons.Count == 0)
                    continue;

                string weaponList = string.Join(", ", weapons);

                AutoArmLogger.Debug(() => $"[{pawn.LabelShort}] Blacklisted {weaponList} - Reason: {reason}");
            }

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
                    RemoveFromSchedule(pawn, weaponDef);
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
        /// EVENT-BASED: Process blacklist entries expiring at this tick
        /// Tick update
        /// </summary>
        public static void ProcessExpiredBlacklists(int tick)
        {
            if (expirySchedule.TryGetValue(tick, out var expiredEntries))
            {
                foreach (var (pawn, weaponDef) in expiredEntries)
                {
                    if (pawn == null || weaponDef == null)
                        continue;

                    if (blacklist.TryGetValue(pawn, out var weaponSet))
                    {
                        weaponSet.Remove(weaponDef);

                        if (weaponSet.Count == 0)
                        {
                            blacklist.Remove(pawn);
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

                expirySchedule.Remove(tick);

                if (AutoArmMod.settings?.debugLogging == true && expiredEntries.Count > 0)
                {
                    AutoArmLogger.Debug(() =>
                        $"[BlacklistEvent] {expiredEntries.Count} blacklist entries expired");
                }
            }
        }

        /// <summary>
        /// Cleanup dead pawns
        /// EVENT-BASED: Expiry handled by ProcessExpiredBlacklists, this only cleans dead pawns
        /// </summary>
        public static void CleanupOldEntries()
        {
            int deadPawnCount = 0;

            var deadPawns = ListPool<Pawn>.Get();
            foreach (var pawn in blacklist.Keys)
            {
                if (pawn.Destroyed || pawn.Dead)
                    deadPawns.Add(pawn);
            }

            foreach (var pawn in deadPawns)
            {
                if (blacklist.TryGetValue(pawn, out var weapons))
                {
                    foreach (var weaponDef in weapons)
                    {
                        RemoveFromSchedule(pawn, weaponDef);
                    }
                }

                blacklist.Remove(pawn);
                timestamps.Remove(pawn);
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
            expirySchedule.Clear();
            pendingBlacklistLogs.Clear();
        }


        private static void RemoveFromSchedule(Pawn pawn, ThingDef weaponDef, int? knownExpireTick = null)
        {
            if (knownExpireTick.HasValue)
            {
                if (expirySchedule.TryGetValue(knownExpireTick.Value, out var list))
                {
                    list.Remove((pawn, weaponDef));
                    if (list.Count == 0)
                    {
                        expirySchedule.Remove(knownExpireTick.Value);
                    }
                }
            }
            else
            {
                int keyToRemove = -1;
                foreach (var kvp in expirySchedule)
                {
                    if (kvp.Value.Remove((pawn, weaponDef)))
                    {
                        if (kvp.Value.Count == 0)
                        {
                            keyToRemove = kvp.Key;
                        }
                        break;
                    }
                }
                if (keyToRemove != -1)
                {
                    expirySchedule.Remove(keyToRemove);
                }
            }
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
        /// EVENT-BASED: Rebuild expiry schedule from existing blacklists (for load/initialization)
        /// Rebuild on load from saved timestamp data
        /// </summary>
        public static void RebuildFromExistingBlacklists()
        {
            expirySchedule.Clear();

            foreach (var pawnKvp in timestamps)
            {
                var pawn = pawnKvp.Key;
                var timestamps = pawnKvp.Value;

                if (pawn?.Destroyed != false || pawn.Dead)
                    continue;

                foreach (var weaponKvp in timestamps)
                {
                    var weaponDef = weaponKvp.Key;
                    int blacklistTick = weaponKvp.Value;
                    int expireTick = blacklistTick + Constants.WeaponBlacklistDuration;

                    int currentTick = Find.TickManager.TicksGame;
                    if (expireTick > currentTick)
                    {
                        if (!expirySchedule.TryGetValue(expireTick, out var list))
                        {
                            list = new List<(Pawn, ThingDef)>();
                            expirySchedule[expireTick] = list;
                        }
                        list.Add((pawn, weaponDef));
                    }
                }
            }

            AutoArmLogger.Debug(() => $"WeaponBlacklist rebuilt: {blacklist.Count} pawns tracked, " +
                              $"{expirySchedule.Count} expiry ticks scheduled");
        }
    }
}
