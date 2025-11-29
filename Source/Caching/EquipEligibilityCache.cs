
using AutoArm.Definitions;
using AutoArm.Helpers;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace AutoArm.Caching
{
    /// <summary>
    /// Cached equip
    /// TTL-based cache
    /// </summary>
    public static class EquipEligibilityCache
    {
        private struct Key : IEquatable<Key>
        {
            public readonly int PawnId;
            public readonly int ThingId;
            public readonly int Flags;

            public Key(int pawnId, int thingId, bool checkBonded)
            {
                PawnId = pawnId;
                ThingId = thingId;
                Flags = checkBonded ? 1 : 0;
            }

            public bool Equals(Key other)
            {
                return PawnId == other.PawnId && ThingId == other.ThingId && Flags == other.Flags;
            }

            public override bool Equals(object obj)
            {
                return obj is Key k && Equals(k);
            }

            public override int GetHashCode()
            {
                unchecked { return ((PawnId * 397) ^ ThingId) * 31 ^ Flags; }
            }
        }

        private struct Entry
        {
            public bool Can;
            public string Reason;
            public int LastTick;
            public int LastAccess;
        }

        private static readonly Dictionary<Key, Entry> cache = new Dictionary<Key, Entry>(512);
        private static int accessCounter = 0;

        private const int TicksTTL = Constants.StandardCacheDuration;

        private const int MaxEntries = 512;

        public static void Clear()
        {
            cache.Clear();
            accessCounter = 0;
        }

        // Cache DLC status at startup - avoid repeated property lookups
        private static bool? _royaltyActive;
        private static bool? _ideologyActive;

        private static bool RoyaltyActive => _royaltyActive ?? (_royaltyActive = ModsConfig.RoyaltyActive).Value;
        private static bool IdeologyActive => _ideologyActive ?? (_ideologyActive = ModsConfig.IdeologyActive).Value;

        /// <summary>
        /// Cached equip check
        /// Boolean reason
        /// </summary>
        public static bool CanEquip(Pawn pawn, ThingWithComps weapon, out string cantReason, bool checkBonded = true)
        {
            cantReason = null;
            if (pawn == null || weapon == null)
                return false;

            // Fast path: No DLCs = no restrictions from EquipmentUtility.CanEquip
            // Biocode/bladelink = Royalty, Role restrictions = Ideology
            // We already check biocode separately in ShouldConsiderWeapon
            if (!RoyaltyActive && !IdeologyActive)
            {
                AutoArmPerfOverlayWindow.ReportEligibilityCacheHit();
                return true;
            }

            var key = new Key(pawn.thingIDNumber, weapon.thingIDNumber, checkBonded);
            int now = Find.TickManager.TicksGame;

            if (cache.TryGetValue(key, out var entry))
            {
                if (now - entry.LastTick <= TicksTTL)
                {
                    entry.LastAccess = ++accessCounter;
                    cache[key] = entry;
                    cantReason = entry.Can ? null : entry.Reason;
                    AutoArmPerfOverlayWindow.ReportEligibilityCacheHit();
                    return entry.Can;
                }
            }

            AutoArmPerfOverlayWindow.ReportEligibilityCacheMiss();

            string reason;
            bool can = EquipmentUtility.CanEquip(weapon, pawn, out reason, checkBonded);

            entry.Can = can;
            entry.Reason = can ? null : reason;
            entry.LastTick = now;
            entry.LastAccess = ++accessCounter;
            cache[key] = entry;

            if (cache.Count > MaxEntries)
            {
                var allEntries = ListPool<KeyValuePair<Key, Entry>>.Get(cache.Count);
                foreach (var kvp in cache)
                {
                    allEntries.Add(kvp);
                }

                allEntries.SortBy(kvp => kvp.Value.LastAccess);

                var oldest = ListPool<Key>.Get();
                int entriesToRemove = MaxEntries / 4;
                for (int i = 0; i < Math.Min(entriesToRemove, allEntries.Count); i++)
                {
                    oldest.Add(allEntries[i].Key);
                }

                ListPool<KeyValuePair<Key, Entry>>.Return(allEntries);

                foreach (var removeKey in oldest)
                {
                    cache.Remove(removeKey);
                }

                ListPool<Key>.Return(oldest);
            }

            cantReason = entry.Can ? null : entry.Reason;
            return entry.Can;
        }
    }
}
