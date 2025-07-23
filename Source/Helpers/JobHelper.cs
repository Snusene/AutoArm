using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace AutoArm
{
    /// <summary>
    /// Centralized job handling (fixes #17, #27)
    /// </summary>
    public static class JobHelper
    {
        // Job categories for interruption logic
        private static readonly HashSet<JobDef> AlwaysCriticalJobs = new HashSet<JobDef>
        {
            JobDefOf.TendPatient, JobDefOf.Rescue, JobDefOf.ExtinguishSelf,
            JobDefOf.BeatFire, JobDefOf.GotoSafeTemperature,
            JobDefOf.AttackMelee, JobDefOf.AttackStatic, JobDefOf.Hunt,
            JobDefOf.ManTurret, JobDefOf.Wait_Combat, JobDefOf.FleeAndCower, JobDefOf.Reload,
            JobDefOf.Vomit, JobDefOf.LayDown, JobDefOf.Lovin, JobDefOf.Ingest,
            JobDefOf.EnterTransporter, JobDefOf.EnterCryptosleepCasket,
            JobDefOf.Arrest, JobDefOf.Capture, JobDefOf.EscortPrisonerToBed,
            JobDefOf.TakeWoundedPrisonerToBed, JobDefOf.ReleasePrisoner,
            JobDefOf.Kidnap, JobDefOf.CarryDownedPawnToExit, JobDefOf.CarryToCryptosleepCasket,
            JobDefOf.TradeWithPawn, JobDefOf.UseCommsConsole, JobDefOf.DropEquipment
        };

        private static readonly HashSet<JobDef> AlwaysSafeToInterrupt = new HashSet<JobDef>
        {
            JobDefOf.Wait, JobDefOf.Wait_Wander, JobDefOf.GotoWander,
            JobDefOf.Goto, JobDefOf.LayDownResting, JobDefOf.Clean,
            JobDefOf.ClearSnow, JobDefOf.RemoveFloor
        };

        private static readonly HashSet<JobDef> InterruptibleForMajorUpgrade = new HashSet<JobDef>
        {
            JobDefOf.Sow, JobDefOf.Harvest, JobDefOf.CutPlant, JobDefOf.HarvestDesignated,
            JobDefOf.PlantSeed, JobDefOf.FinishFrame, JobDefOf.PlaceNoCostFrame,
            JobDefOf.BuildRoof, JobDefOf.RemoveRoof, JobDefOf.SmoothFloor,
            JobDefOf.SmoothWall, JobDefOf.Deconstruct, JobDefOf.Uninstall,
            JobDefOf.Repair, JobDefOf.FixBrokenDownBuilding, JobDefOf.HaulToCell,
            JobDefOf.HaulToContainer, JobDefOf.UnloadInventory, JobDefOf.UnloadYourInventory,
            JobDefOf.Tame, JobDefOf.Train, JobDefOf.Shear, JobDefOf.Milk, JobDefOf.Slaughter,
            JobDefOf.DoBill, JobDefOf.Mine, JobDefOf.OperateScanner, JobDefOf.OperateDeepDrill,
            JobDefOf.Research, JobDefOf.RearmTurret, JobDefOf.Refuel,
            JobDefOf.FillFermentingBarrel, JobDefOf.TakeBeerOutOfFermentingBarrel,
            JobDefOf.DeliverFood, JobDefOf.Open, JobDefOf.Flick
        };

        private static JobDef equipSecondaryJobDef;

        /// <summary>
        /// Create an equip job (fixes #27)
        /// </summary>
        public static Job CreateEquipJob(ThingWithComps weapon, bool isSidearm = false)
        {
            if (weapon == null)
                return null;

            if (isSidearm && SimpleSidearmsCompat.IsLoaded())
            {
                // Lazy load the secondary job def
                if (equipSecondaryJobDef == null)
                {
                    equipSecondaryJobDef = DefDatabase<JobDef>.GetNamedSilentFail("EquipSecondary");
                }

                if (equipSecondaryJobDef != null)
                {
                    return JobMaker.MakeJob(equipSecondaryJobDef, weapon);
                }
            }

            // Default to vanilla equip job
            var job = JobMaker.MakeJob(JobDefOf.Equip, weapon);
            job.count = 1;
            return job;
        }

        /// <summary>
        /// Check if a job is critical and shouldn't be interrupted (fixes #17)
        /// </summary>
        public static bool IsCriticalJob(Pawn pawn, Job job = null)
        {
            if (job == null)
                job = pawn?.CurJob;

            if (job == null)
                return false;

            // Player forced jobs are always critical
            if (job.playerForced)
                return true;

            var jobDef = job.def;

            // Check always critical jobs
            if (AlwaysCriticalJobs.Contains(jobDef))
                return true;

            // Check job name patterns
            var defName = jobDef.defName;
            if (defName.Contains("Ritual") ||
                defName.Contains("Surgery") ||
                defName.Contains("Operate") ||
                defName.Contains("Prisoner") ||
                defName.Contains("Mental") ||
                defName.Contains("PrepareCaravan"))
                return true;

            return false;
        }

        /// <summary>
        /// Check if a job can be safely interrupted
        /// </summary>
        public static bool IsSafeToInterrupt(Job job, float upgradePercentage = 0f)
        {
            if (job == null)
                return true;

            var jobDef = job.def;

            // Always safe jobs
            if (AlwaysSafeToInterrupt.Contains(jobDef))
                return true;

            // Check if it's interruptible for major upgrade
            if (upgradePercentage >= 1.15f && InterruptibleForMajorUpgrade.Contains(jobDef))
                return true;

            // Check job name patterns
            var defName = jobDef.defName;
            return defName.StartsWith("Joy") ||
                   defName.StartsWith("Play") ||
                   defName.Contains("Social") ||
                   defName.Contains("Relax") ||
                   defName.Contains("Clean") ||
                   defName.Contains("Haul") && !defName.Contains("Urgent") && !defName.Contains("Critical") ||
                   defName.Contains("Wander") ||
                   defName.Contains("Wait") ||
                   defName.Contains("IdleJob") ||
                   defName.Contains("Skygaze") ||
                   defName.Contains("Meditate") ||
                   defName.Contains("ViewArt") ||
                   defName.Contains("VisitGrave") ||
                   defName.Contains("BuildSnowman") ||
                   defName.Contains("CloudWatch") ||
                   defName.Contains("StandAndBeSociallyActive");
        }

        /// <summary>
        /// Check if current work is low priority
        /// </summary>
        public static bool IsLowPriorityWork(Pawn pawn)
        {
            if (pawn?.CurJob == null)
                return true;

            var job = pawn.CurJob;
            var jobDef = job.def;

            if (AlwaysSafeToInterrupt.Contains(jobDef))
                return true;

            // Check work priority
            if (pawn.workSettings != null && job.workGiverDef?.workType != null)
            {
                var workType = job.workGiverDef.workType;
                int priority = pawn.workSettings.GetPriority(workType);

                if (priority >= 4)
                    return true;

                // Check specific work types
                if (workType == WorkTypeDefOf.Cleaning ||
                    workType == WorkTypeDefOf.Hauling ||
                    workType == WorkTypeDefOf.PlantCutting ||
                    workType == WorkTypeDefOf.Research)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Check if pawn should interrupt current job for weapon pickup
        /// </summary>
        public static bool ShouldInterruptForWeapon(Pawn pawn, ThingWithComps newWeapon, float currentScore = 0f, float newScore = 0f)
        {
            if (pawn?.CurJob == null)
                return true;

            // Never interrupt critical jobs
            if (IsCriticalJob(pawn))
                return false;

            bool hasWeapon = pawn.equipment?.Primary != null;

            // Always interrupt for unarmed pawns unless doing critical work
            if (!hasWeapon)
            {
                AutoArmDebug.LogPawn(pawn, $"UNARMED - will interrupt {pawn.CurJob.def.defName} to get weapon");
                return true;
            }

            // Check if job is safe to interrupt
            if (IsSafeToInterrupt(pawn.CurJob))
            {
                AutoArmDebug.LogPawn(pawn, $"Interrupting safe job {pawn.CurJob.def.defName} for weapon upgrade");
                return true;
            }

            // Check upgrade percentage for non-safe jobs
            if (currentScore > 0 && newScore > 0)
            {
                float upgradePercentage = newScore / currentScore;
                if (upgradePercentage >= 1.10f)
                {
                    AutoArmDebug.LogPawn(pawn, $"{(upgradePercentage - 1f) * 100f:F0}% upgrade available - interrupting {pawn.CurJob.def.defName}");
                    return true;
                }
            }

            // Check if it's low priority work
            if (IsLowPriorityWork(pawn))
            {
                AutoArmDebug.LogPawn(pawn, $"Interrupting low priority work {pawn.CurJob.def.defName} for weapon upgrade");
                return true;
            }

            return false;
        }

        /// <summary>
        /// Try to start a job with appropriate interruption
        /// </summary>
        public static bool TryStartJob(Pawn pawn, Job job, bool forceInterrupt = false)
        {
            if (pawn?.jobs == null || job == null)
                return false;

            if (forceInterrupt || pawn.CurJob == null)
            {
                pawn.jobs.StartJob(job, JobCondition.InterruptForced);
                return true;
            }
            else
            {
                return pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
            }
        }
    }
}
