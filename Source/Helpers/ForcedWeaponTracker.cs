// AutoArm RimWorld 1.5+ mod - automatic weapon management
// This file: Tracks forced weapons that have been dropped with a timer
// Purpose: Allows SimpleSidearms and other mods to swap weapons without losing forced status
// Timer: 60 ticks (1 second) grace period before clearing forced status

using AutoArm;
using AutoArm.Logging;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace AutoArm.Helpers
{
    /// <summary>
    /// Tracks forced weapons that have been dropped to provide a grace period
    /// before clearing their forced status. This handles SimpleSidearms swaps
    /// and other mod interactions without complex detection logic.
    /// </summary>
    public static class ForcedWeaponTracker
    {
        private class DroppedForcedWeapon
        {
            public Pawn Pawn { get; set; }
            public ThingWithComps Weapon { get; set; }
            public int DroppedTick { get; set; }
        }

        // Track dropped forced weapons with their drop time
        private static List<DroppedForcedWeapon> droppedWeapons = new List<DroppedForcedWeapon>();

        // Grace period in ticks (60 ticks = 1 second)
        private const int GracePeriodTicks = 60;

        /// <summary>
        /// Mark a forced weapon as dropped and start the timer
        /// </summary>
        public static void MarkForcedWeaponDropped(Pawn pawn, ThingWithComps weapon)
        {
            if (pawn == null || weapon == null)
                return;

            // Remove any existing entry for this weapon
            droppedWeapons.RemoveAll(d => d.Weapon == weapon);

            // Add new tracking entry
            droppedWeapons.Add(new DroppedForcedWeapon
            {
                Pawn = pawn,
                Weapon = weapon,
                DroppedTick = Find.TickManager.TicksGame
            });
        }

        /// <summary>
        /// Check if a weapon was picked back up and remove from tracking
        /// </summary>
        public static void WeaponPickedUp(ThingWithComps weapon)
        {
            if (weapon == null)
                return;

            droppedWeapons.RemoveAll(d => d.Weapon == weapon);
        }

        /// <summary>
        /// Process all tracked weapons and clear forced status after grace period
        /// Called from a game component or harmony patch
        /// </summary>
        public static void ProcessDroppedWeapons()
        {
            if (droppedWeapons.Count == 0)
                return;

            int currentTick = Find.TickManager.TicksGame;
            var weaponsToProcess = droppedWeapons.Where(d =>
                currentTick - d.DroppedTick >= GracePeriodTicks).ToList();

            foreach (var dropped in weaponsToProcess)
            {
                // Check if weapon still exists and isn't destroyed
                if (dropped.Weapon?.Destroyed == false && dropped.Pawn != null)
                {
                    // Check if pawn re-equipped this weapon (primary or inventory)
                    bool wasReEquipped = false;

                    // Check primary slot
                    if (dropped.Pawn.equipment?.Primary == dropped.Weapon)
                    {
                        wasReEquipped = true;
                    }
                    // Check inventory
                    else if (dropped.Pawn.inventory?.innerContainer?.Contains(dropped.Weapon) == true)
                    {
                        wasReEquipped = true;
                    }

                    if (!wasReEquipped)
                    {
                        // Grace period expired and weapon wasn't re-equipped
                        // When SimpleSidearms is loaded, we can't clear its forced status
                        // (that's managed through SS's UI), but we should clear AutoArm's tracking
                        
                        if (!SimpleSidearmsCompat.IsLoaded())
                        {
                            // Only clear forced status when SimpleSidearms is NOT loaded
                            // When SS is loaded, it manages its own forced status
                            if (ForcedWeaponHelper.GetForcedPrimary(dropped.Pawn) == dropped.Weapon)
                            {
                                ForcedWeaponHelper.ClearForcedPrimary(dropped.Pawn);

                                if (AutoArmMod.settings?.debugLogging == true)
                                {
                                    AutoArmLogger.Debug($"[{dropped.Pawn.LabelShort}] Cleared forced status for {dropped.Weapon.Label} - not re-equipped within grace period");
                                }
                            }

                            // Remove from forced weapon list
                            ForcedWeaponHelper.RemoveForcedWeapon(dropped.Pawn, dropped.Weapon);
                        }
                        else if (AutoArmMod.settings?.debugLogging == true)
                        {
                            AutoArmLogger.Debug($"[{dropped.Pawn.LabelShort}] {dropped.Weapon.Label} dropped but forced status managed by SimpleSidearms");
                        }
                    }
                    else if (AutoArmMod.settings?.debugLogging == true)
                    {
                        AutoArmLogger.Debug($"[{dropped.Pawn.LabelShort}] Maintained forced status for {dropped.Weapon.Label} - re-equipped within grace period");
                    }
                }

                // Remove from tracking
                droppedWeapons.Remove(dropped);
            }

            // Clean up null references
            droppedWeapons.RemoveAll(d => d.Weapon?.Destroyed == true || d.Pawn?.Destroyed == true);
        }

        /// <summary>
        /// Clear all tracking (used when game ends or resets)
        /// </summary>
        public static void Clear()
        {
            droppedWeapons.Clear();
        }

        /// <summary>
        /// Clean up old entries and destroyed references
        /// </summary>
        public static int Cleanup()
        {
            if (droppedWeapons.Count == 0)
                return 0;

            int removed = 0;
            int currentTick = Find.TickManager.TicksGame;

            // Remove entries that are:
            // 1. Very old (more than 10 seconds)
            // 2. Have destroyed weapons or pawns
            // 3. Have null references
            var toRemove = droppedWeapons.Where(d =>
                d == null ||
                d.Weapon?.Destroyed == true ||
                d.Pawn?.Destroyed == true ||
                d.Weapon == null ||
                d.Pawn == null ||
                currentTick - d.DroppedTick > 600 // 10 seconds max
            ).ToList();

            foreach (var entry in toRemove)
            {
                droppedWeapons.Remove(entry);
                removed++;
            }

            return removed;
        }
    }
}