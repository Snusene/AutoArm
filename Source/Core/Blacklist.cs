
using AutoArm.Definitions;
using AutoArm.Helpers;
using System.Collections.Generic;
using Verse;

namespace AutoArm
{
    internal static class Blacklist
    {
        private static readonly Dictionary<(int pawnId, ushort defHash), int> entries =
            new Dictionary<(int, ushort), int>();

        private static readonly Dictionary<(Pawn, string), List<string>> pendingBlacklistLogs =
            new Dictionary<(Pawn, string), List<string>>();

        public static bool IsBlacklisted(ThingDef weaponDef, Pawn pawn)
        {
            if (weaponDef == null || pawn == null)
                return false;

            var key = (pawn.thingIDNumber, weaponDef.shortHash);
            if (!entries.TryGetValue(key, out int expireTick))
                return false;

            if (Find.TickManager.TicksGame >= expireTick)
            {
                entries.Remove(key);
                return false;
            }

            return true;
        }

        public static void AddToBlacklist(ThingDef weaponDef, Pawn pawn, string reason = null)
        {
            if (weaponDef == null || pawn == null)
                return;

            var key = (pawn.thingIDNumber, weaponDef.shortHash);
            entries[key] = Find.TickManager.TicksGame + Constants.WeaponBlacklistDuration;

            if (AutoArmMod.settings?.debugLogging == true)
            {
                string reasonText = string.IsNullOrEmpty(reason) ? "Unknown" : reason;
                var logKey = (pawn, reasonText);

                if (!pendingBlacklistLogs.ContainsKey(logKey))
                {
                    pendingBlacklistLogs[logKey] = new List<string>();
                }

                pendingBlacklistLogs[logKey].Add(weaponDef.label);
            }
        }

        public static void FlushPendingLogs()
        {
            if (pendingBlacklistLogs.Count == 0)
                return;

            foreach (var kvp in pendingBlacklistLogs)
            {
                var pawn = kvp.Key.Item1;
                var reason = kvp.Key.Item2;
                var labels = kvp.Value;
                if (pawn == null || labels == null || labels.Count == 0) continue;

                AutoArmLogger.Debug(() => $"[{pawn.LabelShort}] Blacklisted {labels.Count} weapons ({reason}): {string.Join(", ", labels)}");
            }

            pendingBlacklistLogs.Clear();
        }

        public static void RemoveFromBlacklist(ThingDef weaponDef, Pawn pawn)
        {
            if (weaponDef == null || pawn == null)
                return;

            entries.Remove((pawn.thingIDNumber, weaponDef.shortHash));
        }

        public static void ClearBlacklist(Pawn pawn)
        {
            if (pawn == null)
                return;

            int pawnId = pawn.thingIDNumber;
            var keysToRemove = ListPool<(int, ushort)>.Get();
            try
            {
                foreach (var key in entries.Keys)
                {
                    if (key.pawnId == pawnId)
                        keysToRemove.Add(key);
                }

                if (keysToRemove.Count > 0 && AutoArmMod.settings?.debugLogging == true)
                {
                    int count = keysToRemove.Count;
                    AutoArmLogger.Debug(() => $"[{pawn.Name?.ToStringShort ?? "Unknown"}] Cleared weapon blacklist ({count} weapons)");
                }

                foreach (var key in keysToRemove)
                    entries.Remove(key);
            }
            finally
            {
                ListPool<(int, ushort)>.Return(keysToRemove);
            }
        }

        public static void RemovePawn(Pawn pawn)
        {
            if (pawn == null) return;

            int pawnId = pawn.thingIDNumber;
            var keysToRemove = ListPool<(int, ushort)>.Get();
            try
            {
                foreach (var key in entries.Keys)
                {
                    if (key.pawnId == pawnId)
                        keysToRemove.Add(key);
                }
                foreach (var key in keysToRemove)
                    entries.Remove(key);
            }
            finally
            {
                ListPool<(int, ushort)>.Return(keysToRemove);
            }

            var logKeysToRemove = ListPool<(Pawn, string)>.Get();
            try
            {
                foreach (var key in pendingBlacklistLogs.Keys)
                {
                    if (key.Item1 == pawn)
                        logKeysToRemove.Add(key);
                }
                foreach (var key in logKeysToRemove)
                    pendingBlacklistLogs.Remove(key);
            }
            finally
            {
                ListPool<(Pawn, string)>.Return(logKeysToRemove);
            }
        }

        public static void CleanupOldEntries()
        {
            if (entries.Count == 0)
                return;

            int currentTick = Find.TickManager.TicksGame;
            var keysToRemove = ListPool<(int, ushort)>.Get();
            int removedCount;
            try
            {
                foreach (var kvp in entries)
                {
                    if (kvp.Value <= currentTick)
                        keysToRemove.Add(kvp.Key);
                }

                foreach (var key in keysToRemove)
                    entries.Remove(key);

                removedCount = keysToRemove.Count;
            }
            finally
            {
                ListPool<(int, ushort)>.Return(keysToRemove);
            }

            if (removedCount > 0 && AutoArmMod.settings?.debugLogging == true)
            {
                AutoArmLogger.Debug(() => $"Blacklist cleanup: {removedCount} expired entries removed");
            }
        }

        public static bool IsBlacklisted(Thing weapon, Pawn pawn)
        {
            return weapon != null && IsBlacklisted(weapon.def, pawn);
        }

        public static void AddToBlacklist(Thing weapon, Pawn pawn, string reason = null)
        {
            if (weapon != null)
                AddToBlacklist(weapon.def, pawn, reason);
        }

        public static void RemoveFromBlacklist(Thing weapon, Pawn pawn)
        {
            if (weapon != null)
                RemoveFromBlacklist(weapon.def, pawn);
        }

        public static void ClearAll()
        {
            entries.Clear();
            pendingBlacklistLogs.Clear();
        }

        public static void Reset()
        {
            ClearAll();
            AutoArmLogger.Debug(() => "Blacklist reset");
        }
    }
}
