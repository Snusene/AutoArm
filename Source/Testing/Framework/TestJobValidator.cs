using AutoArm.Jobs;
using AutoArm.Logging;
using RimWorld;
using System;
using Verse;
using Verse.AI;

namespace AutoArm.Testing.Framework
{
    /// <summary>
    /// Validates that jobs can actually be executed in test scenarios
    /// Ensures proper reservations and job setup
    /// </summary>
    public static class TestJobValidator
    {
        /// <summary>
        /// Validate and potentially fix a job before it's given to a pawn
        /// </summary>
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

            if (job.def == JobDefOf.Equip || job.def.defName == "EquipSecondary")
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

        /// <summary>
        /// Try to give a job to a pawn with proper validation for tests
        /// </summary>
        public static bool TryGiveJob(Pawn pawn, Job job, bool forceImmediate = false)
        {
            if (pawn == null || job == null) return false;

            try
            {
                if (!ValidateJob(job, pawn, out string failReason))
                {
                    AutoArmLogger.Debug(() => $"Job validation failed for {pawn.Name}: {failReason}");
                    return false;
                }

                if (forceImmediate && pawn.jobs?.curJob != null)
                {
                    pawn.jobs.EndCurrentJob(JobCondition.InterruptForced, false);
                }

                AutoEquipState.MarkAsAutoEquip(job, pawn);

                if (forceImmediate)
                {
                    pawn.jobs?.StartJob(job, JobCondition.InterruptForced);
                }
                else
                {
                    pawn.jobs?.TryTakeOrderedJob(job);
                }

                return true;
            }
            catch (Exception e)
            {
                AutoArmLogger.Debug(() => $"Failed to give job to {pawn.Name}: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// Wait for a job to complete (for testing)
        /// </summary>
        public static bool WaitForJobCompletion(Pawn pawn, int maxTicks = 300)
        {
            if (pawn?.jobs?.curJob == null) return false;

            var startJob = pawn.jobs.curJob;

            for (int i = 0; i < maxTicks; i++)
            {
                if (pawn.jobs.curJob != startJob)
                {
                    return true;
                }

                try
                {
                    pawn.jobs.JobTrackerTick();
                }
                catch
                {
                    return false;
                }
            }

            return false;
        }

        /// <summary>
        /// Create a test-safe equip job
        /// </summary>
        public static Job CreateTestEquipJob(Pawn pawn, ThingWithComps weapon)
        {
            if (pawn == null || weapon == null) return null;

            if (weapon.Map?.reservationManager != null)
            {
                weapon.Map.reservationManager.ReleaseAllForTarget(weapon);
            }

            var job = global::AutoArm.Jobs.Jobs.CreateEquipJob(weapon, false, pawn);

            if (job != null)
            {
                job.count = 1;
                job.checkOverrideOnExpire = false;
                job.expiryInterval = -1;
            }

            return job;
        }
    }
}
