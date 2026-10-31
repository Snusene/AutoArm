
using AutoArm.Helpers;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace AutoArm.Jobs
{
    internal static class AutoEquipState
    {
        private static Dictionary<int, int> autoEquipJobIds = new Dictionary<int, int>();
        private const int AutoEquipJobMaxAgeTicks = 10000;

        private static Dictionary<int, string> previousWeaponLabels = new Dictionary<int, string>();

        private static Dictionary<int, int> weaponsToForce = new Dictionary<int, int>();


        public static void MarkAsAutoEquip(Job job, Pawn pawn)
        {
            if (job == null || pawn == null)
                return;

            autoEquipJobIds[job.loadID] = Find.TickManager?.TicksGame ?? 0;
        }

        public static bool IsAutoEquip(Job job)
        {
            if (job == null)
                return false;

            return autoEquipJobIds.ContainsKey(job.loadID);
        }

        public static void Clear(Job job)
        {
            if (job == null)
                return;

            autoEquipJobIds.Remove(job.loadID);
        }

        public static void SetPreviousWeapon(Pawn pawn, string weaponLabel)
        {
            if (pawn == null)
            {
                return;
            }

            if (!string.IsNullOrEmpty(weaponLabel))
            {
                previousWeaponLabels[pawn.thingIDNumber] = weaponLabel;
            }
            else
            {
                previousWeaponLabels.Remove(pawn.thingIDNumber);
            }
        }

        public static string GetPreviousWeapon(Pawn pawn)
        {
            if (pawn == null)
                return null;

            previousWeaponLabels.TryGetValue(pawn.thingIDNumber, out string label);
            return label;
        }

        public static void ClearPreviousWeapon(Pawn pawn)
        {
            if (pawn == null)
                return;

            previousWeaponLabels.Remove(pawn.thingIDNumber);
        }

        public static void SetWeaponToForce(Pawn pawn, ThingWithComps weapon)
        {
            if (pawn == null || weapon == null)
                return;

            weaponsToForce[pawn.thingIDNumber] = weapon.thingIDNumber;
        }

        public static bool ShouldForceWeapon(Pawn pawn, ThingWithComps weapon)
        {
            if (pawn == null || weapon == null)
                return false;

            return weaponsToForce.TryGetValue(pawn.thingIDNumber, out var weaponId)
                && weaponId == weapon.thingIDNumber;
        }

        public static void ClearWeaponToForce(Pawn pawn)
        {
            if (pawn == null)
                return;

            weaponsToForce.Remove(pawn.thingIDNumber);
        }

        public static void Reset()
        {
            autoEquipJobIds.Clear();
            previousWeaponLabels.Clear();
            weaponsToForce.Clear();
        }

        private static int liveSweepCounter;

        public static void Cleanup(bool force = false)
        {
            if (autoEquipJobIds.Count > 0)
            {
                int now = Find.TickManager?.TicksGame ?? 0;
                var expired = ListPool<int>.Get();
                try
                {
                    foreach (var kvp in autoEquipJobIds)
                    {
                        if (now - kvp.Value > AutoEquipJobMaxAgeTicks)
                            expired.Add(kvp.Key);
                    }
                    foreach (var id in expired)
                        autoEquipJobIds.Remove(id);
                    if (expired.Count > 0)
                        AutoArmLogger.Debug(() => $"Expired {expired.Count} stale auto equip job tags");
                }
                finally
                {
                    ListPool<int>.Return(expired);
                }
            }

            if (previousWeaponLabels.Count == 0 && weaponsToForce.Count == 0)
                return;

            liveSweepCounter++;
            if (!force && !Testing.TestRunner.IsRunningTests && liveSweepCounter < 8)
                return;
            liveSweepCounter = 0;

            var liveIds = HashSetPool<int>.Get();
            try
            {
                if (Find.Maps != null)
                {
                    foreach (var map in Find.Maps)
                    {
                        if (map?.mapPawns?.AllPawns == null) continue;
                        foreach (var p in map.mapPawns.AllPawns)
                        {
                            if (p == null || p.Dead || p.Destroyed) continue;
                            liveIds.Add(p.thingIDNumber);
                        }
                    }
                }
                var worldPawns = Find.World?.worldPawns;
                if (worldPawns != null)
                {
                    foreach (var p in worldPawns.AllPawnsAlive)
                    {
                        if (p != null) liveIds.Add(p.thingIDNumber);
                    }
                }

                var toRemove = ListPool<int>.Get();
                try
                {
                    foreach (var id in previousWeaponLabels.Keys)
                    {
                        if (!liveIds.Contains(id)) toRemove.Add(id);
                    }
                    foreach (var id in toRemove) previousWeaponLabels.Remove(id);

                    toRemove.Clear();
                    foreach (var id in weaponsToForce.Keys)
                    {
                        if (!liveIds.Contains(id)) toRemove.Add(id);
                    }
                    foreach (var id in toRemove) weaponsToForce.Remove(id);
                }
                finally
                {
                    ListPool<int>.Return(toRemove);
                }
            }
            finally
            {
                HashSetPool<int>.Return(liveIds);
            }
        }
    }
}
