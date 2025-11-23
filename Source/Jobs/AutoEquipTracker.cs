
using AutoArm.Helpers;
using AutoArm.Logging;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace AutoArm.Jobs
{
    /// <summary>
    /// Track AutoArm jobs
    /// Distinguish forced/auto
    /// </summary>
    public static class AutoEquipState
    {
        private static HashSet<int> autoEquipJobIds = new HashSet<int>();

        private static Dictionary<Pawn, string> previousWeaponLabels = new Dictionary<Pawn, string>();

        private static Dictionary<Pawn, ThingWithComps> weaponsToForce = new Dictionary<Pawn, ThingWithComps>();

        private static Dictionary<Pawn, ThingWithComps> weaponsCannotMoveToInventory = new Dictionary<Pawn, ThingWithComps>();

        private static int markedCount = 0;

        private static int lastSummaryTick = -1;
        private const int SummaryWindowTicks = 300;

        /// <summary>
        /// Mark a job as created by AutoArm
        /// </summary>
        public static void MarkAsAutoEquip(Job job, Pawn pawn)
        {
            if (job == null || pawn == null)
                return;

            autoEquipJobIds.Add(job.loadID);

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

            return autoEquipJobIds.Contains(job.loadID);
        }

        /// <summary>
        /// Clear tracking for a completed job
        /// </summary>
        public static void Clear(Job job)
        {
            if (job == null)
                return;

            autoEquipJobIds.Remove(job.loadID);
        }

        /// <summary>
        /// Set previous label
        /// Stores the display label (e.g., "Eternal Howl") to preserve custom weapon names
        /// </summary>
        public static void SetPreviousWeapon(Pawn pawn, string weaponLabel)
        {
            if (pawn == null)
            {
                return;
            }

            if (!string.IsNullOrEmpty(weaponLabel))
            {
                previousWeaponLabels[pawn] = weaponLabel;
            }
            else
            {
                previousWeaponLabels.Remove(pawn);
            }
        }

        /// <summary>
        /// Previous label
        /// Display label
        /// </summary>
        public static string GetPreviousWeapon(Pawn pawn)
        {
            if (pawn == null)
                return null;

            previousWeaponLabels.TryGetValue(pawn, out string label);
            return label;
        }

        /// <summary>
        /// Clear previous weapon tracking
        /// </summary>
        public static void ClearPreviousWeapon(Pawn pawn)
        {
            if (pawn == null)
                return;

            previousWeaponLabels.Remove(pawn);
        }

        /// <summary>
        /// Mark a weapon to be forced when equipped (for forced weapon upgrades)
        /// </summary>
        public static void SetWeaponToForce(Pawn pawn, ThingWithComps weapon)
        {
            if (pawn == null || weapon == null)
                return;

            weaponsToForce[pawn] = weapon;
        }

        /// <summary>
        /// Force when equipped
        /// </summary>
        public static bool ShouldForceWeapon(Pawn pawn, ThingWithComps weapon)
        {
            if (pawn == null || weapon == null)
                return false;

            return weaponsToForce.TryGetValue(pawn, out var weaponToForce) && weaponToForce == weapon;
        }

        /// <summary>
        /// Clear weapon to force tracking
        /// </summary>
        public static void ClearWeaponToForce(Pawn pawn)
        {
            if (pawn == null)
                return;

            weaponsToForce.Remove(pawn);
        }

        /// <summary>
        /// Mark a weapon that can't be moved to inventory (for SimpleSidearms limit handling)
        /// </summary>
        public static void SetWeaponCannotMoveToInventory(Pawn pawn, ThingWithComps weapon)
        {
            if (pawn == null || weapon == null)
                return;

            weaponsCannotMoveToInventory[pawn] = weapon;
        }

        /// <summary>
        /// Unmovable weapon
        /// </summary>
        public static ThingWithComps GetWeaponCannotMoveToInventory(Pawn pawn)
        {
            if (pawn == null)
                return null;

            weaponsCannotMoveToInventory.TryGetValue(pawn, out var weapon);
            return weapon;
        }

        /// <summary>
        /// Clear weapon that can't be moved to inventory tracking
        /// </summary>
        public static void ClearWeaponCannotMoveToInventory(Pawn pawn)
        {
            if (pawn == null)
                return;

            weaponsCannotMoveToInventory.Remove(pawn);
        }

        /// <summary>
        /// Cleanup jobs/pawns
        /// </summary>
        public static void Cleanup()
        {
            if (autoEquipJobIds.Count > 100)
            {
                autoEquipJobIds.Clear();
                AutoArmLogger.Debug(() => "Cleared auto-equip job tracking (exceeded 100 entries)");
            }

            var deadPawns = ListPool<Pawn>.Get();
            foreach (var pawn in previousWeaponLabels.Keys)
            {
                if (pawn.Dead || pawn.Destroyed)
                    deadPawns.Add(pawn);
            }
            foreach (var pawn in deadPawns)
            {
                previousWeaponLabels.Remove(pawn);
            }
            ListPool<Pawn>.Return(deadPawns);

            var deadPawnsForForce = ListPool<Pawn>.Get();
            foreach (var pawn in weaponsToForce.Keys)
            {
                if (pawn.Dead || pawn.Destroyed)
                    deadPawnsForForce.Add(pawn);
            }
            foreach (var pawn in deadPawnsForForce)
            {
                weaponsToForce.Remove(pawn);
            }
            ListPool<Pawn>.Return(deadPawnsForForce);

            var deadPawnsForInventory = ListPool<Pawn>.Get();
            foreach (var pawn in weaponsCannotMoveToInventory.Keys)
            {
                if (pawn.Dead || pawn.Destroyed)
                    deadPawnsForInventory.Add(pawn);
            }
            foreach (var pawn in deadPawnsForInventory)
            {
                weaponsCannotMoveToInventory.Remove(pawn);
            }
            ListPool<Pawn>.Return(deadPawnsForInventory);
        }
    }
}
