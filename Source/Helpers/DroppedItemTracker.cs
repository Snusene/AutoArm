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
        private static HashSet<Pawn> pawnsWithSimpleSidearmsSwapInProgress = new HashSet<Pawn>();
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

            // Clean up dead/destroyed pawns from swap tracking
            var deadPawns = pawnsWithSimpleSidearmsSwapInProgress
                .Where(p => p.Destroyed || p.Dead)
                .ToList();

            foreach (var pawn in deadPawns)
            {
                pawnsWithSimpleSidearmsSwapInProgress.Remove(pawn);
            }
        }

        /// <summary>
        /// Clear all tracking
        /// </summary>
        public static void ClearAll()
        {
            recentlyDroppedItems.Clear();
            pendingUpgradeDrops.Clear();
            pawnsWithSimpleSidearmsSwapInProgress.Clear();
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

        /// <summary>
        /// Check if a weapon was dropped as part of a primary weapon upgrade
        /// These should have a longer cooldown before being picked up as sidearms
        /// </summary>
        public static bool WasDroppedFromPrimaryUpgrade(Thing weapon)
        {
            // If it's marked as recently dropped with the longer cooldown (1200 ticks),
            // it was likely a primary weapon upgrade
            if (weapon != null && recentlyDroppedItems.TryGetValue(weapon, out int expireTick))
            {
                int remainingTicks = expireTick - Find.TickManager.TicksGame;
                // If it has more than 600 ticks remaining, it was probably dropped with the longer cooldown
                return remainingTicks > 600;
            }
            return false;
        }

        /// <summary>
        /// Mark a weapon as pending drop (will be dropped soon)
        /// This prevents other systems from trying to pick it up during the drop/equip sequence
        /// </summary>
        public static void MarkPendingDrop(Thing weapon)
        {
            if (weapon != null)
            {
                // Mark it as dropped with a longer cooldown to ensure it's ignored during the job sequence
                MarkAsDropped(weapon, 600); // 10 seconds

                if (AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmDebug.Log($"Marked {weapon.Label} as pending drop for weapon replacement");
                }
            }
        }

        /// <summary>
        /// Mark that a SimpleSidearms swap is in progress for a pawn
        /// </summary>
        public static void MarkSimpleSidearmsSwapInProgress(Pawn pawn)
        {
            if (pawn != null)
            {
                pawnsWithSimpleSidearmsSwapInProgress.Add(pawn);
            }
        }

        /// <summary>
        /// Clear SimpleSidearms swap in progress flag for a pawn
        /// </summary>
        public static void ClearSimpleSidearmsSwapInProgress(Pawn pawn)
        {
            if (pawn != null)
            {
                pawnsWithSimpleSidearmsSwapInProgress.Remove(pawn);
            }
        }

        /// <summary>
        /// Check if a pawn has a SimpleSidearms swap in progress
        /// </summary>
        public static bool IsSimpleSidearmsSwapInProgress(Pawn pawn)
        {
            return pawn != null && pawnsWithSimpleSidearmsSwapInProgress.Contains(pawn);
        }
    }
}