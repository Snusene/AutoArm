
using AutoArm.Definitions;
using AutoArm.Logging;
using RimWorld;
using System.Collections.Generic;
using Verse;

namespace AutoArm.Helpers
{
    /// <summary>
    /// Track dropped weapons - uses TickScheduler for grace checks
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

        // Lookup from weaponId to weapon for TickScheduler event handling
        private static readonly Dictionary<int, ThingWithComps> idToWeaponLookup = new Dictionary<int, ThingWithComps>(32);

        private const int BaseGracePeriodTicks = 300;
        private const int GraceCheckIntervalTicks = 60;
        private const int CleanupTimeoutTicks = 1200;
        private const int HardTimeoutTicks = 1200;

        public static void MarkForcedWeaponDropped(Pawn pawn, ThingWithComps weapon)
        {
            if (pawn == null || weapon == null)
                return;

            int weaponId = weapon.thingIDNumber;
            if (droppedWeaponsLookup.TryGetValue(weapon, out var existing))
            {
                droppedWeapons.Remove(existing);
                droppedWeaponsLookup.Remove(weapon);
                TickScheduler.Cancel(TickScheduler.EventType.ForcedWeaponGraceCheck, weaponId);
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
            idToWeaponLookup[weaponId] = weapon;

            int graceCheckTick = now + BaseGracePeriodTicks;
            TickScheduler.Schedule(graceCheckTick, TickScheduler.EventType.ForcedWeaponGraceCheck, weaponId);
        }

        public static void WeaponPickedUp(ThingWithComps weapon)
        {
            if (weapon == null)
                return;

            if (droppedWeaponsLookup.TryGetValue(weapon, out var entry))
            {
                droppedWeapons.Remove(entry);
                droppedWeaponsLookup.Remove(weapon);
                int weaponId = weapon.thingIDNumber;
                idToWeaponLookup.Remove(weaponId);
                TickScheduler.Cancel(TickScheduler.EventType.ForcedWeaponGraceCheck, weaponId);
            }
        }

        /// <summary>
        /// Event handler called by TickScheduler for grace check events
        /// </summary>
        public static void OnGraceCheckEvent(int weaponId)
        {
            if (!idToWeaponLookup.TryGetValue(weaponId, out var weapon))
                return;

            if (weapon == null || weapon.Destroyed)
            {
                CleanupWeaponEntry(weapon, weaponId);
                return;
            }

            if (!droppedWeaponsLookup.TryGetValue(weapon, out var entry))
                return;

            var pawn = entry.Pawn;
            int currentTick = Find.TickManager.TicksGame;

            if (pawn == null || pawn.Destroyed || pawn.Dead)
            {
                CleanupWeaponEntry(weapon, weaponId);
                return;
            }

            if (IsWeaponReEquipped(pawn, weapon))
            {
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.Debug(() =>
                        $"[{pawn.Name?.ToStringShort ?? pawn.LabelShort}] Maintained forced status for {weapon.Label} - re-equipped within grace period");
                }
                CleanupWeaponEntry(weapon, weaponId);
                return;
            }

            if (ShouldExtendGrace(currentTick, entry))
            {
                // Reschedule for another check
                int nextCheckTick = currentTick + GraceCheckIntervalTicks;
                TickScheduler.Schedule(nextCheckTick, TickScheduler.EventType.ForcedWeaponGraceCheck, weaponId);
                return;
            }

            // Grace period expired - clear forced status
            droppedWeapons.Remove(entry);
            droppedWeaponsLookup.Remove(weapon);
            idToWeaponLookup.Remove(weaponId);

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

        private static void CleanupWeaponEntry(ThingWithComps weapon, int weaponId)
        {
            if (weapon != null && droppedWeaponsLookup.TryGetValue(weapon, out var entry))
            {
                droppedWeapons.Remove(entry);
                droppedWeaponsLookup.Remove(weapon);
            }
            idToWeaponLookup.Remove(weaponId);
        }

        /// <summary>
        /// Legacy method - kept for backward compatibility
        /// Now handled by TickScheduler.ProcessTick() -> OnGraceCheckEvent
        /// </summary>
        public static void ProcessGraceChecks(int tick)
        {
            // Now handled by TickScheduler
        }

        public static void Clear()
        {
            droppedWeapons.Clear();
            droppedWeaponsLookup.Clear();
            processBuffer.Clear();
            idToWeaponLookup.Clear();
            // TickScheduler.Reset() handles clearing all scheduled events
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
                    int weaponId = weapon.thingIDNumber;
                    droppedWeaponsLookup.Remove(weapon);
                    idToWeaponLookup.Remove(weaponId);
                    TickScheduler.Cancel(TickScheduler.EventType.ForcedWeaponGraceCheck, weaponId);
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

            // Don't extend grace unless pawn is actively trying to re-equip
            // (checked above via job/reservation). Manual drops should clear quickly.
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



        /// <summary>
        /// EVENT-BASED: Reset all state (for map changes, game reset)
        /// </summary>
        public static void Reset()
        {
            Clear();
            AutoArmLogger.Debug(() => "ForcedWeaponState reset");
        }

        /// <summary>
        /// Event-driven cleanup when weapon destroyed
        /// </summary>
        public static void RemoveWeapon(ThingWithComps weapon)
        {
            if (weapon == null) return;

            if (droppedWeaponsLookup.TryGetValue(weapon, out var entry))
            {
                droppedWeapons.Remove(entry);
                droppedWeaponsLookup.Remove(weapon);
                int weaponId = weapon.thingIDNumber;
                idToWeaponLookup.Remove(weaponId);
                TickScheduler.Cancel(TickScheduler.EventType.ForcedWeaponGraceCheck, weaponId);
            }
        }

        /// <summary>
        /// Rebuild grace check schedule from existing dropped weapons (for load/initialization)
        /// </summary>
        public static void RebuildFromExistingDrops()
        {
            idToWeaponLookup.Clear();

            int currentTick = Find.TickManager.TicksGame;
            int scheduledCount = 0;

            foreach (var entry in droppedWeapons)
            {
                if (entry == null || entry.Weapon == null || entry.Weapon.Destroyed)
                    continue;

                if (entry.Pawn?.Destroyed != false || entry.Pawn.Dead)
                    continue;

                int weaponId = entry.Weapon.thingIDNumber;
                idToWeaponLookup[weaponId] = entry.Weapon;

                int ticksSinceDropped = currentTick - entry.DroppedTick;

                if (ticksSinceDropped < BaseGracePeriodTicks)
                {
                    int graceCheckTick = entry.DroppedTick + BaseGracePeriodTicks;
                    TickScheduler.Schedule(graceCheckTick, TickScheduler.EventType.ForcedWeaponGraceCheck, weaponId);
                    scheduledCount++;
                }
                else if (currentTick - entry.FirstObservedTick < HardTimeoutTicks)
                {
                    int nextCheckTick = currentTick + GraceCheckIntervalTicks;
                    TickScheduler.Schedule(nextCheckTick, TickScheduler.EventType.ForcedWeaponGraceCheck, weaponId);
                    scheduledCount++;
                }
            }

            AutoArmLogger.Debug(() => $"ForcedWeaponState rebuilt: {droppedWeapons.Count} tracked weapons, " +
                              $"{scheduledCount} grace check events scheduled");
        }
    }
}
