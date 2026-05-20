
using AutoArm.Definitions;
using RimWorld;
using System;
using Verse;

namespace AutoArm.Caching
{
    internal static class EquipEligibility
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
                => PawnId == other.PawnId && ThingId == other.ThingId && Flags == other.Flags;

            public override bool Equals(object obj) => obj is Key k && Equals(k);

            public override int GetHashCode()
            {
                unchecked { return ((PawnId * 397) ^ ThingId) * 31 ^ Flags; }
            }
        }

        private struct Entry
        {
            public bool Can;
            public string Reason;
        }

        private const int MaxEntries = 512;

        private static readonly TickExpiringLruCache<Key, Entry> cache =
            new TickExpiringLruCache<Key, Entry>(MaxEntries, Constants.StandardCacheDuration, MaxEntries);

        public static void Clear() => cache.Clear();

        public static void InvalidateWeapon(int weaponId)
            => cache.RemoveWhere((key, _) => key.ThingId == weaponId);

        public static void InvalidatePawn(int pawnId)
            => cache.RemoveWhere((key, _) => key.PawnId == pawnId);

        public static bool CanEquip(Pawn pawn, ThingWithComps weapon, out string cantReason, bool checkBonded = true)
        {
            cantReason = null;
            if (pawn == null || weapon == null)
                return false;

            if (!ModsConfig.RoyaltyActive && !ModsConfig.IdeologyActive)
            {
                if (CompBiocodable.IsBiocoded(weapon) && !CompBiocodable.IsBiocodedFor(weapon, pawn))
                {
                    cantReason = "biocoded to another pawn";
                    return false;
                }
                return true;
            }

            var key = new Key(pawn.thingIDNumber, weapon.thingIDNumber, checkBonded);
            int now = Find.TickManager.TicksGame;

            if (cache.TryGet(key, now, out var entry))
            {
                PerfMetrics.ReportEligibilityCacheHit();
                cantReason = entry.Can ? null : entry.Reason;
                return entry.Can;
            }

            PerfMetrics.ReportEligibilityCacheMiss();

            bool can = EquipmentUtility.CanEquip(weapon, pawn, out string reason, checkBonded);
            entry = new Entry { Can = can, Reason = can ? null : reason };
            cache.Set(key, entry, now);

            cantReason = entry.Can ? null : entry.Reason;
            return entry.Can;
        }
    }
}
