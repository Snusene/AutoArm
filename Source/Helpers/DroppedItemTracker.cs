// AutoArm RimWorld 1.5+ mod - automatic weapon management
// This file: Tracks recently dropped weapons to prevent re-pickup loops
// Prevents pawns from immediately picking up weapons they just dropped
// Uses: Outfit changes, weapon upgrades, SimpleSidearms integration
// Critical: Prevents infinite drop/pickup loops that break pawn behavior

using AutoArm.Definitions;
using AutoArm.Logging;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace AutoArm.Helpers
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
        
        public const int DefaultIgnoreTicks = Constants.DefaultDropIgnoreTicks;
        public const int LongCooldownTicks = Constants.LongDropCooldownTicks;
        
        private static int lastCleanupTick = 0;
        private const int CleanupInterval = 600; // Cleanup every 10 seconds

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
                AutoArmLogger.Debug($"Marked {item.Label} as recently dropped - will ignore for {ignoreTicks} ticks");
            }
        }

        /// <summary>
        /// Check if an item was recently dropped
        /// </summary>
        public static bool IsRecentlyDropped(Thing item)
        {
            if (item == null)
                return false;

            // Periodic cleanup
            int currentTick = Find.TickManager.TicksGame;
            if (currentTick - lastCleanupTick > CleanupInterval)
            {
                CleanupOldEntries();
                lastCleanupTick = currentTick;
            }

            return recentlyDroppedItems.TryGetValue(item, out int expireTick) && 
                   currentTick < expireTick;
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
        public static int CleanupOldEntries()
        {
            int currentTick = Find.TickManager.TicksGame;
            int removed = 0;

            // Clean expired or destroyed items
            var expiredItems = new List<Thing>();
            foreach (var kvp in recentlyDroppedItems)
            {
                if (currentTick >= kvp.Value || kvp.Key?.Destroyed != false)
                {
                    expiredItems.Add(kvp.Key);
                }
            }
            
            foreach (var item in expiredItems)
            {
                recentlyDroppedItems.Remove(item);
                removed++;
            }

            // Clean destroyed pending upgrades
            pendingUpgradeDrops.RemoveWhere(weapon => weapon?.Destroyed != false);

            // Clean dead/destroyed pawns
            pawnsWithSimpleSidearmsSwapInProgress.RemoveWhere(p => p?.Destroyed != false || p.Dead);

            return removed;
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
        /// Get recently dropped weapons (for SimpleSidearms upgrade detection)
        /// </summary>
        public static IEnumerable<ThingWithComps> GetRecentlyDroppedWeapons()
        {
            CleanupOldEntries();
            return recentlyDroppedItems.Keys
                .OfType<ThingWithComps>()
                .Where(t => t != null && !t.Destroyed && t.def.IsWeapon);
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
                    AutoArmLogger.Debug($"Marked {weapon.Label} as pending same-type upgrade drop");
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
        /// </summary>
        public static bool WasDroppedFromPrimaryUpgrade(Thing weapon)
        {
            if (weapon == null || !recentlyDroppedItems.TryGetValue(weapon, out int expireTick))
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
                // Mark it as dropped with a longer cooldown to ensure it's ignored during the job sequence
                MarkAsDropped(weapon, LongCooldownTicks);
                // Debug log already handled by MarkAsDropped
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
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.Debug($"SimpleSidearms swap in progress for {pawn.LabelShort}");
                }
            }
        }

        /// <summary>
        /// Clear SimpleSidearms swap in progress flag for a pawn
        /// </summary>
        public static void ClearSimpleSidearmsSwapInProgress(Pawn pawn)
        {
            if (pawn != null)
            {
                if (pawnsWithSimpleSidearmsSwapInProgress.Remove(pawn) && AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.Debug($"SimpleSidearms swap completed for {pawn.LabelShort}");
                }
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