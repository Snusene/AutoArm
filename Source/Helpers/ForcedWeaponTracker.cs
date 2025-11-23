
using AutoArm.Definitions;
using AutoArm.Logging;
using RimWorld;
using System.Collections.Generic;
using Verse;

namespace AutoArm.Helpers
{
    /// <summary>
    /// Track dropped weapons
    /// SimpleSidearms compat
    /// Mod interactions
    /// </summary>
    public static class ForcedWeaponState
    {
        private sealed class DroppedForcedWeapon
        {
            public Pawn Pawn;
            public ThingWithComps Weapon;
            public int DroppedTick;
            public int FirstObservedTick;
        }

        private static readonly List<DroppedForcedWeapon> droppedWeapons = new List<DroppedForcedWeapon>(32);
        private static readonly Dictionary<ThingWithComps, DroppedForcedWeapon> droppedWeaponsLookup = new Dictionary<ThingWithComps, DroppedForcedWeapon>(32);
        private static readonly List<DroppedForcedWeapon> processBuffer = new List<DroppedForcedWeapon>(16);

        private static readonly Dictionary<int, List<ThingWithComps>> graceCheckSchedule = new Dictionary<int, List<ThingWithComps>>();

        private const int BaseGracePeriodTicks = 300;
        private const int GraceCheckIntervalTicks = 60;
        private const int CleanupTimeoutTicks = 1200;
        private const int HardTimeoutTicks = 1200;

        public static void MarkForcedWeaponDropped(Pawn pawn, ThingWithComps weapon)
        {
            if (pawn == null || weapon == null)
                return;

            if (droppedWeaponsLookup.TryGetValue(weapon, out var existing))
            {
                droppedWeapons.Remove(existing);
                droppedWeaponsLookup.Remove(weapon);
                RemoveFromGraceSchedule(weapon);
            }

            int now = Find.TickManager.TicksGame;
            var entry = new DroppedForcedWeapon
            {
                Pawn = pawn,
                Weapon = weapon,
                DroppedTick = now,
                FirstObservedTick = now
            };

            droppedWeapons.Add(entry);
            droppedWeaponsLookup[weapon] = entry;

            int graceCheckTick = now + BaseGracePeriodTicks;
            if (!graceCheckSchedule.TryGetValue(graceCheckTick, out var list))
            {
                list = new List<ThingWithComps>();
                graceCheckSchedule[graceCheckTick] = list;
            }
            list.Add(weapon);
        }

        public static void WeaponPickedUp(ThingWithComps weapon)
        {
            if (weapon == null)
                return;

            if (droppedWeaponsLookup.TryGetValue(weapon, out var entry))
            {
                droppedWeapons.Remove(entry);
                droppedWeaponsLookup.Remove(weapon);
                RemoveFromGraceSchedule(weapon);
            }
        }

        /// <summary>
        /// EVENT-BASED: Process grace period checks for weapons scheduled at this tick
        /// Tick update
        /// Replaces per-tick iteration with scheduled checks only when needed
        /// </summary>
        public static void ProcessGraceChecks(int tick)
        {
            if (!graceCheckSchedule.TryGetValue(tick, out var weaponsToCheck))
                return;

            processBuffer.Clear();

            foreach (var weapon in weaponsToCheck)
            {
                if (weapon == null || weapon.Destroyed)
                    continue;

                if (!droppedWeaponsLookup.TryGetValue(weapon, out var entry))
                    continue;

                var pawn = entry.Pawn;

                if (pawn == null || pawn.Destroyed || pawn.Dead)
                {
                    processBuffer.Add(entry);
                    continue;
                }

                if (IsWeaponReEquipped(pawn, weapon))
                {
                    if (AutoArmMod.settings?.debugLogging == true)
                    {
                        AutoArmLogger.Debug(() =>
                            $"[{pawn.Name?.ToStringShort ?? pawn.LabelShort}] Maintained forced status for {weapon.Label} - re-equipped within grace period");
                    }
                    processBuffer.Add(entry);
                    continue;
                }

                if (ShouldExtendGrace(tick, entry))
                {
                    int nextCheckTick = tick + GraceCheckIntervalTicks;
                    if (!graceCheckSchedule.TryGetValue(nextCheckTick, out var list))
                    {
                        list = new List<ThingWithComps>();
                        graceCheckSchedule[nextCheckTick] = list;
                    }
                    list.Add(weapon);
                    continue;
                }

                droppedWeapons.Remove(entry);
                droppedWeaponsLookup.Remove(weapon);

                if (ForcedWeapons.ForcedPrimary(pawn) == weapon)
                {
                    ForcedWeapons.ClearForcedPrimary(pawn);

                    if (AutoArmMod.settings?.debugLogging == true)
                    {
                        AutoArmLogger.Debug(() =>
                            $"[{pawn.Name?.ToStringShort ?? pawn.LabelShort}] Cleared forced status for {weapon.Label} - not re-equipped within grace period");
                    }
                }

                ForcedWeapons.RemoveForcedWeapon(pawn, weapon);
            }

            foreach (var entry in processBuffer)
            {
                droppedWeapons.Remove(entry);
                if (entry.Weapon != null) droppedWeaponsLookup.Remove(entry.Weapon);
            }
            processBuffer.Clear();

            graceCheckSchedule.Remove(tick);

            if (AutoArmMod.settings?.debugLogging == true && weaponsToCheck.Count > 0)
            {
                AutoArmLogger.Debug(() =>
                    $"[ForcedWeaponEvent] Processed {weaponsToCheck.Count} grace checks at tick {tick}");
            }
        }

        /// <summary>
        /// LEGACY: Kept for compatibility, but now does minimal work
        /// Most processing moved to event-based ProcessGraceChecks
        /// </summary>
        public static void ProcessDroppedWeapons()
        {
        }

        public static void Clear()
        {
            droppedWeapons.Clear();
            droppedWeaponsLookup.Clear();
            processBuffer.Clear();
            graceCheckSchedule.Clear();
        }

        public static int Cleanup()
        {
            if (droppedWeapons.Count == 0)
                return 0;

            int removed = 0;
            int currentTick = Find.TickManager.TicksGame;

            for (int i = droppedWeapons.Count - 1; i >= 0; i--)
            {
                var entry = droppedWeapons[i];
                bool shouldRemove = entry == null || entry.Weapon == null || entry.Weapon.Destroyed ||
                    entry.Pawn == null || entry.Pawn.Destroyed || entry.Pawn.Dead ||
                    currentTick - (entry != null ? entry.FirstObservedTick : currentTick) > CleanupTimeoutTicks;

                if (!shouldRemove)
                    continue;

                var pawn = entry != null ? entry.Pawn : null;
                var weapon = entry != null ? entry.Weapon : null;
                if (pawn != null && weapon != null)
                {
                    bool wasForced = ForcedWeapons.IsForced(pawn, weapon);
                    ForcedWeapons.RemoveForcedWeapon(pawn, weapon);

                    if (wasForced && AutoArmMod.settings?.debugLogging == true)
                    {
                        AutoArmLogger.Debug(() => $"[{pawn.Name?.ToStringShort ?? pawn.LabelShort}] Cleared forced status for {weapon.Label} during tracker cleanup");
                    }
                }

                droppedWeapons.RemoveAt(i);
                if (weapon != null)
                {
                    droppedWeaponsLookup.Remove(weapon);
                    RemoveFromGraceSchedule(weapon);
                }
                removed++;
            }

            return removed;
        }

        internal static bool IsTrackingWeapon(Pawn pawn, ThingDef weaponDef)
        {
            if (pawn == null || weaponDef == null)
                return false;

            for (int i = 0; i < droppedWeapons.Count; i++)
            {
                var entry = droppedWeapons[i];
                if (entry != null && entry.Pawn == pawn && entry.Weapon != null && entry.Weapon.def == weaponDef)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsWeaponReEquipped(Pawn pawn, ThingWithComps weapon)
        {
            if (pawn.equipment?.Primary == weapon)
                return true;

            var inventory = pawn.inventory?.innerContainer;
            if (inventory != null)
            {
                for (int i = 0; i < inventory.Count; i++)
                {
                    if (inventory[i] == weapon)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool ShouldExtendGrace(int currentTick, DroppedForcedWeapon entry)
        {
            if (entry == null)
                return false;

            var pawn = entry.Pawn;
            var weapon = entry.Weapon;

            if (pawn == null || weapon == null)
                return false;

            if (!pawn.Spawned || pawn.Destroyed || pawn.Dead)
                return false;

            var job = pawn.CurJob;
            if (job != null && job.targetA.Thing == weapon && IsEquipJob(job.def))
            {
                return true;
            }

            var map = weapon.Map ?? pawn.Map;
            if (map != null)
            {
                var reservationManager = map.reservationManager;
                if (reservationManager != null)
                {
                    if (reservationManager.IsReservedByAnyoneOf(weapon, pawn.Faction))
                    {
                        var reservations = reservationManager.ReservationsReadOnly;
                        if (reservations != null)
                        {
                            for (int i = 0; i < reservations.Count; i++)
                            {
                                var reservation = reservations[i];
                                if (reservation.Target.Thing == weapon && reservation.Claimant == pawn)
                                {
                                    return true;
                                }
                            }
                        }
                    }
                }
            }

            if (currentTick - entry.FirstObservedTick >= HardTimeoutTicks)
                return false;

            return false;
        }

        private static bool IsEquipJob(JobDef jobDef)
        {
            if (jobDef == null)
                return false;

            if (jobDef == JobDefOf.Equip)
                return true;

            if (jobDef == AutoArmDefOf.AutoArmSwapPrimary ||
                jobDef == AutoArmDefOf.AutoArmSwapSidearm)
                return true;

            return jobDef == AutoArmDefOf.EquipSecondary ||
                   jobDef == AutoArmDefOf.ReequipSecondary ||
                   jobDef == AutoArmDefOf.ReequipSecondaryCombat;
        }


        private static void RemoveFromGraceSchedule(ThingWithComps weapon)
        {
            if (weapon == null)
                return;

            int keyToRemove = -1;
            foreach (var kvp in graceCheckSchedule)
            {
                if (kvp.Value.Remove(weapon))
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
                graceCheckSchedule.Remove(keyToRemove);
            }
        }

        /// <summary>
        /// EVENT-BASED: Reset all state (for map changes, game reset)
        /// </summary>
        public static void Reset()
        {
            Clear();
            AutoArmLogger.Debug(() => "ForcedWeaponState reset");
        }

        /// <summary>
        /// EVENT-BASED: Rebuild grace check schedule from existing dropped weapons
        /// Rebuild on load from saved data
        /// </summary>
        public static void RebuildFromExistingDrops()
        {
            graceCheckSchedule.Clear();

            int currentTick = Find.TickManager.TicksGame;

            foreach (var entry in droppedWeapons)
            {
                if (entry == null || entry.Weapon == null || entry.Weapon.Destroyed)
                    continue;

                if (entry.Pawn?.Destroyed != false || entry.Pawn.Dead)
                    continue;

                int ticksSinceDropped = currentTick - entry.DroppedTick;

                if (ticksSinceDropped < BaseGracePeriodTicks)
                {
                    int graceCheckTick = entry.DroppedTick + BaseGracePeriodTicks;
                    if (!graceCheckSchedule.TryGetValue(graceCheckTick, out var list))
                    {
                        list = new List<ThingWithComps>();
                        graceCheckSchedule[graceCheckTick] = list;
                    }
                    list.Add(entry.Weapon);
                }
                else if (currentTick - entry.FirstObservedTick < HardTimeoutTicks)
                {
                    int nextCheckTick = currentTick + GraceCheckIntervalTicks;
                    if (!graceCheckSchedule.TryGetValue(nextCheckTick, out var list))
                    {
                        list = new List<ThingWithComps>();
                        graceCheckSchedule[nextCheckTick] = list;
                    }
                    list.Add(entry.Weapon);
                }
            }

            AutoArmLogger.Debug(() => $"ForcedWeaponState rebuilt: {droppedWeapons.Count} tracked weapons, " +
                              $"{graceCheckSchedule.Count} grace check ticks scheduled");
        }
    }
}
