
using AutoArm.Definitions;
using AutoArm.Logging;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace AutoArm.Helpers
{
    public static class DroppedItemTracker
    {
        private static Dictionary<Thing, int> droppedItems = new Dictionary<Thing, int>();
        private static HashSet<Thing> upgrades = new HashSet<Thing>();

        private static Dictionary<Pawn, ThingWithComps> lastDropped = new Dictionary<Pawn, ThingWithComps>();
        private static Dictionary<Thing, Pawn> itemToPawnLookup = new Dictionary<Thing, Pawn>();

        private static Dictionary<int, Thing> idToThingLookup = new Dictionary<int, Thing>();


        public const int DefaultIgnoreTicks = Constants.DefaultDropIgnoreTicks;
        public const int LongCooldownTicks = Constants.LongDropCooldownTicks;

        public static Dictionary<Thing, int> GetAllDroppedItems()
        {
            return new Dictionary<Thing, int>(droppedItems);
        }

        public static void MarkAsDropped(Thing item, int ignoreTicks = DefaultIgnoreTicks, Pawn pawn = null)
        {
            if (item == null)
                return;

            int currentTick = Find.TickManager.TicksGame;
            int expireTick = currentTick + ignoreTicks;
            int itemId = item.thingIDNumber;

            if (droppedItems.ContainsKey(item))
            {
                // Cancel old schedule before rescheduling
                TickScheduler.Cancel(TickScheduler.EventType.DroppedItemExpiry, itemId);
            }

            droppedItems[item] = expireTick;
            idToThingLookup[itemId] = item;
            TickScheduler.Schedule(expireTick, TickScheduler.EventType.DroppedItemExpiry, itemId);

            if (pawn != null && item is ThingWithComps weapon && weapon.def.IsWeapon)
            {
                lastDropped[pawn] = weapon;
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

        // Legacy stub
        public static void ProcessExpiredItems(int tick) { }

        public static void ClearDroppedStatus(Thing item)
        {
            if (item != null)
            {
                droppedItems.Remove(item);
                int itemId = item.thingIDNumber;
                idToThingLookup.Remove(itemId);
                TickScheduler.Cancel(TickScheduler.EventType.DroppedItemExpiry, itemId);
            }
        }

        public static int CleanupOldEntries()
        {
            if (droppedItems.Count == 0 && lastDropped.Count == 0 && upgrades.Count == 0)
                return 0;

            int removed = 0;

            var destroyedItems = ListPool<Thing>.Get();
            foreach (var kvp in droppedItems)
            {
                if (kvp.Key?.Destroyed != false)
                {
                    destroyedItems.Add(kvp.Key);
                }
            }

            foreach (var item in destroyedItems)
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
            ListPool<Thing>.Return(destroyedItems);

            var expiredPawns = ListPool<Pawn>.Get();
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
            }
            ListPool<Pawn>.Return(expiredPawns);

            upgrades.RemoveWhere(weapon => weapon?.Destroyed != false);

            return removed;
        }

        public static void ClearAll()
        {
            droppedItems.Clear();
            idToThingLookup.Clear();
            upgrades.Clear();
            lastDropped.Clear();
            itemToPawnLookup.Clear();
            // TickScheduler.Reset() handles clearing all scheduled events
        }

        /// <summary>
        /// EVENT-BASED: Reset all state (for map changes, game reset)
        /// </summary>
        public static void Reset()
        {
            ClearAll();
            AutoArmLogger.Debug(() => "DroppedItemTracker reset");
        }

        /// <summary>
        /// Rebuild expiry schedule from existing items (for load/initialization)
        /// </summary>
        public static void RebuildFromExistingItems()
        {
            idToThingLookup.Clear();
            itemToPawnLookup.Clear();

            int scheduledCount = 0;
            foreach (var kvp in droppedItems)
            {
                var item = kvp.Key;
                int expireTick = kvp.Value;

                if (item?.Destroyed == false)
                {
                    int itemId = item.thingIDNumber;
                    idToThingLookup[itemId] = item;
                    TickScheduler.Schedule(expireTick, TickScheduler.EventType.DroppedItemExpiry, itemId);
                    scheduledCount++;
                }
            }

            foreach (var kvp in lastDropped)
            {
                if (kvp.Value != null)
                {
                    itemToPawnLookup[kvp.Value] = kvp.Key;
                }
            }

            AutoArmLogger.Debug(() => $"DroppedItemTracker rebuilt: {droppedItems.Count} tracked items, " +
                              $"{scheduledCount} expiry events scheduled");
        }

        /// <summary>
        /// Tracked count
        /// </summary>
        public static int TrackedItemCount => droppedItems.Count;

        /// <summary>
        /// Dropped weapons
        /// </summary>
        public static IEnumerable<ThingWithComps> GetRecentlyDroppedWeapons()
        {
            CleanupOldEntries();
            return droppedItems.Keys
                .OfType<ThingWithComps>()
                .Where(t => t != null && !t.Destroyed && t.def.IsWeapon);
        }

        /// <summary>
        /// NEW: Get the last weapon dropped by a specific pawn (O(1) lookup)
        /// Null if expired
        /// </summary>
        public static ThingWithComps GetLastDropped(Pawn pawn)
        {
            if (pawn == null || !lastDropped.TryGetValue(pawn, out var weapon))
                return null;

            if (weapon?.Destroyed != false || !droppedItems.ContainsKey(weapon))
            {
                lastDropped.Remove(pawn);
                itemToPawnLookup.Remove(weapon);
                return null;
            }

            return weapon;
        }

        /// <summary>
        /// Mark a weapon as pending drop for same-type upgrade
        /// This prevents SimpleSidearms from saving it to inventory
        /// </summary>
        public static void MarkPendingSameTypeUpgrade(Thing weapon)
        {
            if (weapon != null)
            {
                upgrades.Add(weapon);
                AutoArmLogger.Debug(() => $"Marked {weapon.Label} as pending same-type upgrade drop");
            }
        }

        /// <summary>
        /// Upgrade drop
        /// </summary>
        public static bool IsPendingSameTypeUpgrade(Thing weapon)
        {
            return weapon != null && upgrades.Contains(weapon);
        }

        /// <summary>
        /// Clear pending upgrade status for a weapon
        /// </summary>
        public static void ClearPendingUpgrade(Thing weapon)
        {
            if (weapon != null)
            {
                upgrades.Remove(weapon);
            }
        }

        /// <summary>
        /// Clear upgrades
        /// </summary>
        public static void ClearAllPendingUpgrades()
        {
            upgrades.Clear();
        }

        /// <summary>
        /// Primary upgrade drop
        /// </summary>
        public static bool WasDroppedFromPrimaryUpgrade(Thing weapon)
        {
            if (weapon == null || !droppedItems.TryGetValue(weapon, out int expireTick))
                return false;

            int remainingTicks = expireTick - Find.TickManager.TicksGame;
            return remainingTicks > LongCooldownTicks;
        }

        /// <summary>
        /// Mark a weapon as pending drop (will be dropped soon)
        /// This prevents other systems from trying to pick it up during the drop/equip sequence
        /// </summary>
        public static void MarkPendingDrop(Thing weapon)
        {
            if (weapon != null)
            {
                MarkAsDropped(weapon, LongCooldownTicks);
            }
        }

        /// <summary>
        /// Event-driven cleanup when weapon destroyed
        /// </summary>
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

        /// <summary>
        /// Event-driven cleanup when pawn dies/destroyed
        /// </summary>
        public static void RemovePawn(Pawn pawn)
        {
            if (pawn == null) return;

            if (lastDropped.TryGetValue(pawn, out var weapon))
            {
                itemToPawnLookup.Remove(weapon);
                lastDropped.Remove(pawn);
            }
        }
    }
}
