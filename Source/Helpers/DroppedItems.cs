
using AutoArm.Definitions;
using System.Collections.Generic;
using Verse;

namespace AutoArm.Helpers
{
    internal static class DroppedItems
    {
        private static readonly Dictionary<Thing, int> droppedItems = new Dictionary<Thing, int>();
        private static readonly HashSet<Thing> upgrades = new HashSet<Thing>();

        private static readonly Dictionary<Pawn, ThingWithComps> lastDropped = new Dictionary<Pawn, ThingWithComps>();
        private static readonly Dictionary<Pawn, int> lastDroppedExpiry = new Dictionary<Pawn, int>();
        private static readonly Dictionary<Thing, Pawn> itemToPawnLookup = new Dictionary<Thing, Pawn>();

        private const int PawnDropCooldownTicks = 2500;

        private static readonly Dictionary<int, Thing> idToThingLookup = new Dictionary<int, Thing>();


        public const int DefaultIgnoreTicks = Constants.DefaultDropIgnoreTicks;
        public const int LongCooldownTicks = Constants.LongDropCooldownTicks;

        public static void MarkAsDropped(Thing item, int ignoreTicks = DefaultIgnoreTicks, Pawn pawn = null)
        {
            if (item == null)
                return;

            int currentTick = Find.TickManager.TicksGame;
            int expireTick = currentTick + ignoreTicks;
            int itemId = item.thingIDNumber;

            if (droppedItems.ContainsKey(item))
            {
                // Cancel old schedule
                TickScheduler.Cancel(TickScheduler.EventType.DroppedItemExpiry, itemId);
            }

            droppedItems[item] = expireTick;
            idToThingLookup[itemId] = item;
            TickScheduler.Schedule(expireTick, TickScheduler.EventType.DroppedItemExpiry, itemId);

            if (pawn != null && item is ThingWithComps weapon && weapon.def.IsWeapon)
            {
                lastDropped[pawn] = weapon;
                lastDroppedExpiry[pawn] = currentTick + PawnDropCooldownTicks;
                itemToPawnLookup[weapon] = pawn;
            }
        }

        public static bool IsDropped(Thing item)
        {
            if (item == null)
                return false;

            int currentTick = Find.TickManager.TicksGame;
            return droppedItems.TryGetValue(item, out int expireTick) &&
                   currentTick < expireTick;
        }

        public static void OnItemExpiredEvent(int itemId)
        {
            if (!idToThingLookup.TryGetValue(itemId, out var item))
                return;

            droppedItems.Remove(item);
            idToThingLookup.Remove(itemId);

            if (item is ThingWithComps weapon && itemToPawnLookup.TryGetValue(weapon, out var pawn))
            {
                lastDropped.Remove(pawn);
                itemToPawnLookup.Remove(weapon);
            }
        }

        public static int CleanupOldEntries()
        {
            if (droppedItems.Count == 0 && lastDropped.Count == 0 && upgrades.Count == 0)
                return 0;

            int removed = 0;
            int now = Find.TickManager.TicksGame;

            var staleItems = ListPool<Thing>.Get();
            try
            {
                foreach (var kvp in droppedItems)
                {
                    if (kvp.Key?.Destroyed != false || now >= kvp.Value)
                    {
                        staleItems.Add(kvp.Key);
                    }
                }

                foreach (var item in staleItems)
                {
                    droppedItems.Remove(item);
                    int itemId = item?.thingIDNumber ?? 0;
                    if (itemId != 0)
                    {
                        idToThingLookup.Remove(itemId);
                        TickScheduler.Cancel(TickScheduler.EventType.DroppedItemExpiry, itemId);
                    }
                    removed++;
                }
            }
            finally
            {
                ListPool<Thing>.Return(staleItems);
            }

            var expiredPawns = ListPool<Pawn>.Get();
            try
            {
                foreach (var kvp in lastDropped)
                {
                    if (kvp.Value?.Destroyed != false || kvp.Key?.Dead != false || kvp.Key?.Destroyed != false)
                    {
                        expiredPawns.Add(kvp.Key);
                    }
                    else if (!droppedItems.ContainsKey(kvp.Value))
                    {
                        expiredPawns.Add(kvp.Key);
                    }
                }
                foreach (var pawn in expiredPawns)
                {
                    if (lastDropped.TryGetValue(pawn, out var weapon))
                    {
                        itemToPawnLookup.Remove(weapon);
                    }
                    lastDropped.Remove(pawn);
                    lastDroppedExpiry.Remove(pawn);
                }
            }
            finally
            {
                ListPool<Pawn>.Return(expiredPawns);
            }

            upgrades.RemoveWhere(weapon => weapon?.Destroyed != false);

            return removed;
        }

        public static void ClearAll()
        {
            droppedItems.Clear();
            idToThingLookup.Clear();
            upgrades.Clear();
            lastDropped.Clear();
            lastDroppedExpiry.Clear();
            itemToPawnLookup.Clear();
            // TickScheduler clears events
        }

        public static void Reset()
        {
            ClearAll();
            AutoArmLogger.Debug(() => "DroppedItems reset");
        }

        public static ThingWithComps GetLastDropped(Pawn pawn)
        {
            if (pawn == null || !lastDropped.TryGetValue(pawn, out var weapon))
                return null;

            int currentTick = Find.TickManager.TicksGame;
            if (weapon?.Destroyed != false ||
                (lastDroppedExpiry.TryGetValue(pawn, out int expiry) && currentTick >= expiry))
            {
                lastDropped.Remove(pawn);
                lastDroppedExpiry.Remove(pawn);
                itemToPawnLookup.Remove(weapon);
                return null;
            }

            return weapon;
        }

        public static bool IsPendingSameTypeUpgrade(Thing weapon)
        {
            return weapon != null && upgrades.Contains(weapon);
        }

        public static void ClearPendingUpgrade(Thing weapon)
        {
            if (weapon != null)
            {
                upgrades.Remove(weapon);
            }
        }

        public static void ClearAllPendingUpgrades()
        {
            upgrades.Clear();
        }

        public static void RemoveWeapon(Thing weapon)
        {
            if (weapon == null) return;

            if (droppedItems.Remove(weapon))
            {
                int weaponId = weapon.thingIDNumber;
                idToThingLookup.Remove(weaponId);
                TickScheduler.Cancel(TickScheduler.EventType.DroppedItemExpiry, weaponId);
            }

            itemToPawnLookup.Remove(weapon);
            upgrades.Remove(weapon);
        }

        public static void RemovePawn(Pawn pawn)
        {
            if (pawn == null) return;

            if (lastDropped.TryGetValue(pawn, out var weapon))
            {
                itemToPawnLookup.Remove(weapon);
                lastDropped.Remove(pawn);
                lastDroppedExpiry.Remove(pawn);
            }
        }
    }
}
