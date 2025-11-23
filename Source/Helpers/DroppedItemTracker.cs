
using AutoArm.Definitions;
using AutoArm.Logging;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace AutoArm.Helpers
{
    /// <summary>
    /// Prevent weapon re-pickup loops
    /// </summary>
    public static class DroppedItemTracker
    {
        private static Dictionary<Thing, int> droppedItems = new Dictionary<Thing, int>();
        private static HashSet<Thing> upgrades = new HashSet<Thing>();

        private static Dictionary<Pawn, ThingWithComps> lastDropped = new Dictionary<Pawn, ThingWithComps>();

        private static Dictionary<int, List<Thing>> itemExpirySchedule = new Dictionary<int, List<Thing>>();


        public const int DefaultIgnoreTicks = Constants.DefaultDropIgnoreTicks;
        public const int LongCooldownTicks = Constants.LongDropCooldownTicks;

        public static Dictionary<Thing, int> GetAllDroppedItems()
        {
            return new Dictionary<Thing, int>(droppedItems);
        }

        /// <summary>
        /// Mark an item as recently dropped
        /// </summary>
        public static void MarkAsDropped(Thing item, int ignoreTicks = DefaultIgnoreTicks, Pawn pawn = null)
        {
            if (item == null)
                return;

            int currentTick = Find.TickManager.TicksGame;
            int expireTick = currentTick + ignoreTicks;

            if (droppedItems.TryGetValue(item, out int oldExpireTick))
            {
                RemoveFromSchedule(item, oldExpireTick);
            }

            droppedItems[item] = expireTick;

            if (!itemExpirySchedule.TryGetValue(expireTick, out var list))
            {
                list = new List<Thing>();
                itemExpirySchedule[expireTick] = list;
            }
            list.Add(item);

            if (pawn != null && item is ThingWithComps weapon && weapon.def.IsWeapon)
            {
                lastDropped[pawn] = weapon;
            }
        }

        /// <summary>
        /// Recently dropped
        /// HOT PATH: Pure O(1) query - no side effects, cleanup handled by GameComponent
        /// </summary>
        public static bool IsDropped(Thing item)
        {
            if (item == null)
                return false;

            int currentTick = Find.TickManager.TicksGame;
            return droppedItems.TryGetValue(item, out int expireTick) &&
                   currentTick < expireTick;
        }

        /// <summary>
        /// EVENT-BASED: Process items expiring at this tick
        /// Tick update
        /// </summary>
        public static void ProcessExpiredItems(int tick)
        {
            if (itemExpirySchedule.TryGetValue(tick, out var expiredItems))
            {
                foreach (var item in expiredItems)
                {
                    droppedItems.Remove(item);

                    if (item is ThingWithComps weapon)
                    {
                        Pawn pawnToRemove = null;
                        foreach (var kvp in lastDropped)
                        {
                            if (kvp.Value == weapon)
                            {
                                pawnToRemove = kvp.Key;
                                break;
                            }
                        }
                        if (pawnToRemove != null)
                        {
                            lastDropped.Remove(pawnToRemove);
                        }
                    }
                }

                itemExpirySchedule.Remove(tick);

                if (AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.Debug(() =>
                        $"[DroppedItemEvent] {expiredItems.Count} items expired from tracking");
                }
            }
        }

        /// <summary>
        /// Clear dropped status for an item
        /// </summary>
        public static void ClearDroppedStatus(Thing item)
        {
            if (item != null)
            {
                droppedItems.Remove(item);
                RemoveFromSchedule(item);
            }
        }

        /// <summary>
        /// Cleanup old entries
        /// EVENT-BASED: Expiry handled by ProcessExpiredItems, this only cleans destroyed items
        /// </summary>
        public static int CleanupOldEntries()
        {
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
                RemoveFromSchedule(item);
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
                lastDropped.Remove(pawn);
            }
            ListPool<Pawn>.Return(expiredPawns);

            upgrades.RemoveWhere(weapon => weapon?.Destroyed != false);


            return removed;
        }


        private static void RemoveFromSchedule(Thing item, int? knownExpireTick = null)
        {
            if (knownExpireTick.HasValue)
            {
                if (itemExpirySchedule.TryGetValue(knownExpireTick.Value, out var list))
                {
                    list.Remove(item);
                    if (list.Count == 0)
                    {
                        itemExpirySchedule.Remove(knownExpireTick.Value);
                    }
                }
            }
            else
            {
                int keyToRemove = -1;
                foreach (var kvp in itemExpirySchedule)
                {
                    if (kvp.Value.Remove(item))
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
                    itemExpirySchedule.Remove(keyToRemove);
                }
            }
        }

        public static void ClearAll()
        {
            droppedItems.Clear();
            itemExpirySchedule.Clear();
            upgrades.Clear();
            lastDropped.Clear();
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
        /// EVENT-BASED: Rebuild expiry schedule from existing items (for load/initialization)
        /// Rebuild on load from saved data
        /// </summary>
        public static void RebuildFromExistingItems()
        {
            itemExpirySchedule.Clear();

            foreach (var kvp in droppedItems)
            {
                var item = kvp.Key;
                int expireTick = kvp.Value;

                if (item?.Destroyed == false)
                {
                    if (!itemExpirySchedule.TryGetValue(expireTick, out var list))
                    {
                        list = new List<Thing>();
                        itemExpirySchedule[expireTick] = list;
                    }
                    list.Add(item);
                }
            }

            AutoArmLogger.Debug(() => $"DroppedItemTracker rebuilt: {droppedItems.Count} tracked items, " +
                              $"{itemExpirySchedule.Count} expiry ticks scheduled");
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
    }
}
