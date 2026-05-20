
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

        private static int markedCount = 0;

        private static int lastSummaryTick = -1;
        private const int SummaryWindowTicks = 300;

        public static void MarkAsAutoEquip(Job job, Pawn pawn)
        {
            if (job == null || pawn == null)
                return;

            autoEquipJobIds[job.loadID] = Find.TickManager?.TicksGame ?? 0;

            if (AutoArmMod.settings?.debugLogging == true)
            {
                int now = Find.TickManager?.TicksGame ?? 0;
                markedCount++;

                if (lastSummaryTick < 0)
                {
                    lastSummaryTick = now;
                }
                else if (now - lastSummaryTick >= SummaryWindowTicks)
                {
                    AutoArmLogger.Debug(() => $"Auto-equip jobs created: {markedCount} in last 5s");
                    markedCount = 0;
                    lastSummaryTick = now;
                }
            }
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
            markedCount = 0;
            lastSummaryTick = -1;
        }

        public static void Cleanup()
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
                        AutoArmLogger.Debug(() => $"Expired {expired.Count} stale auto-equip job tags");
                }
                finally
                {
                    ListPool<int>.Return(expired);
                }
            }

            if (previousWeaponLabels.Count == 0 && weaponsToForce.Count == 0)
                return;

            var liveIds = new HashSet<int>();
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
            if (Find.WorldPawns != null)
            {
                foreach (var p in Find.WorldPawns.AllPawnsAlive)
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

        public static void RemovePawn(Pawn pawn)
        {
            if (pawn == null) return;

            previousWeaponLabels.Remove(pawn.thingIDNumber);
            weaponsToForce.Remove(pawn.thingIDNumber);
        }
    }
}
