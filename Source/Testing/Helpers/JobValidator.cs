using RimWorld;
using Verse;
using Verse.AI;

namespace AutoArm.Testing.Helpers
{
    internal static class JobValidator
    {
        public static bool ValidateJob(Job job, Pawn pawn, out string failReason)
        {
            failReason = "";

            if (job == null)
            {
                failReason = "Job is null";
                return false;
            }

            if (pawn == null)
            {
                failReason = "Pawn is null";
                return false;
            }

            if (job.def == JobDefOf.Equip || job.def == AutoArm.Definitions.AutoArmDefOf.EquipSecondary)
            {
                var weapon = job.targetA.Thing as ThingWithComps;
                if (weapon == null)
                {
                    failReason = "Weapon target is null";
                    return false;
                }

                if (weapon.Destroyed)
                {
                    failReason = "Weapon is destroyed";
                    return false;
                }

                if (weapon.Map != pawn.Map)
                {
                    failReason = "Weapon is on different map";
                    return false;
                }

                if (!pawn.CanReserve(weapon))
                {
                    if (weapon.Map?.reservationManager != null)
                    {
                        weapon.Map.reservationManager.ReleaseAllForTarget(weapon);
                    }

                    if (!pawn.CanReserve(weapon))
                    {
                        failReason = "Cannot reserve weapon (already reserved)";
                        return false;
                    }
                }

                if (!pawn.CanReach(weapon, PathEndMode.Touch, Danger.Deadly))
                {
                    failReason = "Cannot reach weapon";
                    return false;
                }
            }

            return true;
        }
    }
}
