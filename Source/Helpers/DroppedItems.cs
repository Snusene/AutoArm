
using AutoArm.Definitions;
using System.Collections.Generic;
using Verse;

namespace AutoArm.Helpers
{
    internal static class DroppedItems
    {
        private static readonly Dictionary<Thing, int> droppedItems = new Dictionary<Thing, int>();

        private static readonly Dictionary<int, Thing> idToThingLookup = new Dictionary<int, Thing>();


        public const int DefaultIgnoreTicks = Constants.DefaultDropIgnoreTicks;
        public const int LongCooldownTicks = Constants.LongDropCooldownTicks;

        public static void MarkAsDropped(Thing item, int ignoreTicks = DefaultIgnoreTicks)
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
        }

        public static int CleanupOldEntries()
        {
            if (droppedItems.Count == 0)
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

            return removed;
        }

        public static void ClearAll()
        {
            droppedItems.Clear();
            idToThingLookup.Clear();
            // TickScheduler clears events
        }

        public static void Reset()
        {
            ClearAll();
            AutoArmLogger.Debug(() => "DroppedItems reset");
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
        }
    }
}
