using System.Collections.Generic;
using System.Linq;
using Verse;

namespace AutoArm
{
    /// <summary>
    /// Centralized dropped item tracking (fixes #18)
    /// Tracks recently dropped items to prevent immediate re-pickup
    /// </summary>
    public static class DroppedItemTracker
    {
        private static Dictionary<Thing, int> recentlyDroppedItems = new Dictionary<Thing, int>();
        private static HashSet<Thing> pendingUpgradeDrops = new HashSet<Thing>();
        private const int DefaultIgnoreTicks = 300; // 5 seconds

        /// <summary>
        /// Mark an item as recently dropped
        /// </summary>
        public static void MarkAsDropped(Thing item, int ignoreTicks = DefaultIgnoreTicks)
        {
            if (item == null)
                return;

            recentlyDroppedItems[item] = Find.TickManager.TicksGame + ignoreTicks;

            if (AutoArmMod.settings?.debugLogging == true)
            {
                AutoArmDebug.Log($"Marked {item.Label} as recently dropped - will ignore for {ignoreTicks} ticks");
            }
        }

        /// <summary>
        /// Check if an item was recently dropped
        /// </summary>
        public static bool IsRecentlyDropped(Thing item)
        {
            if (item == null)
                return false;

            CleanupOldEntries();

            if (recentlyDroppedItems.TryGetValue(item, out int expireTick))
            {
                return Find.TickManager.TicksGame < expireTick;
            }

            return false;
        }

        /// <summary>
        /// Clear dropped status for an item
        /// </summary>
        public static void ClearDroppedStatus(Thing item)
        {
            if (item != null)
            {
                recentlyDroppedItems.Remove(item);
            }
        }

        /// <summary>
        /// Clean up old entries
        /// </summary>
        public static void CleanupOldEntries()
        {
            int currentTick = Find.TickManager.TicksGame;
            
            var toRemove = recentlyDroppedItems
                .Where(kvp => currentTick >= kvp.Value || kvp.Key.Destroyed)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var item in toRemove)
            {
                recentlyDroppedItems.Remove(item);
            }
            
            // Also clean up destroyed pending upgrades
            var destroyedUpgrades = pendingUpgradeDrops
                .Where(weapon => weapon.Destroyed)
                .ToList();
                
            foreach (var weapon in destroyedUpgrades)
            {
                pendingUpgradeDrops.Remove(weapon);
            }
        }

        /// <summary>
        /// Clear all tracking
        /// </summary>
        public static void ClearAll()
        {
            recentlyDroppedItems.Clear();
            pendingUpgradeDrops.Clear();
        }

        /// <summary>
        /// Get count of tracked items (for debugging)
        /// </summary>
        public static int TrackedItemCount => recentlyDroppedItems.Count;

        /// <summary>
        /// Check if an item was recently dropped (alias for IsRecentlyDropped)
        /// </summary>
        public static bool WasRecentlyDropped(Thing item)
        {
            return IsRecentlyDropped(item);
        }

        /// <summary>
        /// Mark a weapon as pending drop for same-type upgrade
        /// This prevents SimpleSidearms from saving it to inventory
        /// </summary>
        public static void MarkPendingSameTypeUpgrade(Thing weapon)
        {
            if (weapon != null)
            {
                pendingUpgradeDrops.Add(weapon);
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmDebug.Log($"Marked {weapon.Label} as pending same-type upgrade drop");
                }
            }
        }

        /// <summary>
        /// Check if a weapon is marked for same-type upgrade drop
        /// </summary>
        public static bool IsPendingSameTypeUpgrade(Thing weapon)
        {
            return weapon != null && pendingUpgradeDrops.Contains(weapon);
        }

        /// <summary>
        /// Clear pending upgrade status for a weapon
        /// </summary>
        public static void ClearPendingUpgrade(Thing weapon)
        {
            if (weapon != null)
            {
                pendingUpgradeDrops.Remove(weapon);
            }
        }

        /// <summary>
        /// Clear all pending upgrade tracking
        /// </summary>
        public static void ClearAllPendingUpgrades()
        {
            pendingUpgradeDrops.Clear();
        }
    }
}
