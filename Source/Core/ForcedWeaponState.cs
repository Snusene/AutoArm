
using AutoArm.Definitions;
using RimWorld;
using System.Collections.Generic;
using Verse;

namespace AutoArm
{
    internal static class ForcedWeaponState
    {
        private sealed class DroppedForcedWeapon
        {
            public Pawn Pawn;
            public ThingWithComps Weapon;
            public int DroppedTick;
            public int FirstObservedTick;
        }

        private static readonly List<DroppedForcedWeapon> droppedWeapons = new List<DroppedForcedWeapon>(32);
        private static readonly Dictionary<int, DroppedForcedWeapon> droppedWeaponsLookup = new Dictionary<int, DroppedForcedWeapon>(32);

        private const int BaseGracePeriodTicks = Constants.DefaultDropIgnoreTicks;
        private const int GraceCheckIntervalTicks = 60;
        private const int CleanupTimeoutTicks = 1200;
        private const int HardTimeoutTicks = 1200;

        public static void MarkForcedWeaponDropped(Pawn pawn, ThingWithComps weapon)
        {
            if (pawn == null || weapon == null)
                return;

            int weaponId = weapon.thingIDNumber;
            if (droppedWeaponsLookup.TryGetValue(weaponId, out var existing))
            {
                droppedWeapons.Remove(existing);
                droppedWeaponsLookup.Remove(weaponId);
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
            droppedWeaponsLookup[weaponId] = entry;

            int graceCheckTick = now + BaseGracePeriodTicks;
            TickScheduler.Schedule(graceCheckTick, TickScheduler.EventType.ForcedWeaponGraceCheck, weaponId);
        }

        public static void WeaponPickedUp(ThingWithComps weapon)
        {
            if (weapon == null)
                return;

            int weaponId = weapon.thingIDNumber;
            if (droppedWeaponsLookup.TryGetValue(weaponId, out var entry))
            {
                droppedWeapons.Remove(entry);
                droppedWeaponsLookup.Remove(weaponId);
                TickScheduler.Cancel(TickScheduler.EventType.ForcedWeaponGraceCheck, weaponId);
            }
        }

        public static void OnGraceCheckEvent(int weaponId)
        {
            if (!droppedWeaponsLookup.TryGetValue(weaponId, out var entry))
                return;

            var weapon = entry.Weapon;
            if (weapon == null || weapon.Destroyed)
            {
                CleanupWeaponEntry(weaponId);
                return;
            }

            var pawn = entry.Pawn;
            int currentTick = Find.TickManager.TicksGame;

            if (pawn == null || pawn.Destroyed || pawn.Dead)
            {
                CleanupWeaponEntry(weaponId);
                return;
            }

            if (IsWeaponReEquipped(pawn, weapon))
            {
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.Debug(() =>
                        $"[{pawn.Name?.ToStringShort ?? pawn.LabelShort}] Maintained forced status for {weapon.Label} - re-equipped within grace period");
                }
                CleanupWeaponEntry(weaponId);
                return;
            }

            if (ShouldExtendGrace(currentTick, entry))
            {
                // Reschedule
                int nextCheckTick = currentTick + GraceCheckIntervalTicks;
                TickScheduler.Schedule(nextCheckTick, TickScheduler.EventType.ForcedWeaponGraceCheck, weaponId);
                return;
            }

            // Grace period expired
            droppedWeapons.Remove(entry);
            droppedWeaponsLookup.Remove(weaponId);

            if (ForcedWeapons.IsForcedPrimary(pawn, weapon))
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

        private static void CleanupWeaponEntry(int weaponId)
        {
            if (droppedWeaponsLookup.TryGetValue(weaponId, out var entry))
            {
                droppedWeapons.Remove(entry);
                droppedWeaponsLookup.Remove(weaponId);
            }
        }

        public static void Clear()
        {
            droppedWeapons.Clear();
            droppedWeaponsLookup.Clear();
            // TickScheduler clears events
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
                    droppedWeaponsLookup.Remove(weaponId);
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

            // Don't extend grace
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



        public static void Reset()
        {
            Clear();
            AutoArmLogger.Debug(() => "ForcedWeaponState reset");
        }

        public static void RemoveWeapon(ThingWithComps weapon)
        {
            if (weapon == null) return;

            int weaponId = weapon.thingIDNumber;
            if (droppedWeaponsLookup.TryGetValue(weaponId, out var entry))
            {
                droppedWeapons.Remove(entry);
                droppedWeaponsLookup.Remove(weaponId);
                TickScheduler.Cancel(TickScheduler.EventType.ForcedWeaponGraceCheck, weaponId);
            }
        }

    }
}
