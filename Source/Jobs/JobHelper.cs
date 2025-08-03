// AutoArm RimWorld 1.5+ mod - automatic weapon management
// This file: Job creation and interruption logic
// Handles: Safe job interruption decisions, critical job detection
// Uses: Job categories and work priorities to avoid disrupting important tasks
// Critical: Prevents mod from interrupting emergency/critical jobs

using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;
using AutoArm.Helpers;
using AutoArm.Logging;
using AutoArm.Jobs;

namespace AutoArm.Jobs
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
        private static bool equipSecondaryJobDefChecked = false;
        
        // String prefix sets for performance
        private static readonly HashSet<string> CriticalJobPatterns = new HashSet<string>
        {
            "Ritual", "Surgery", "Operate", "Prisoner", "Mental", "PrepareCaravan"
        };
        
        private static readonly HashSet<string> SafeJobPrefixes = new HashSet<string>
        {
            "Joy", "Play", "Social", "Relax", "Clean", "Wander", "Wait", 
            "IdleJob", "Skygaze", "Meditate", "ViewArt", "VisitGrave", 
            "BuildSnowman", "CloudWatch", "StandAndBeSociallyActive"
        };

        /// <summary>
        /// Create an equip job (fixes #27)
        /// </summary>
        public static Job CreateEquipJob(ThingWithComps weapon, bool isSidearm = false)
        {
            // Weapon null check
            
            if (weapon == null)
            {
                // Weapon is null
                return null;
            }

            if (isSidearm && SimpleSidearmsCompat.IsLoaded())
            {
                // Lazy load the secondary job def with caching
                if (!equipSecondaryJobDefChecked)
                {
                    equipSecondaryJobDef = DefDatabase<JobDef>.GetNamedSilentFail("EquipSecondary");
                    equipSecondaryJobDefChecked = true;
                    if (equipSecondaryJobDef == null)
                    {
                        // EquipSecondary job def not found
                    }
                }

                if (equipSecondaryJobDef != null)
                {
                    // Creating SimpleSidearms job
                    return JobMaker.MakeJob(equipSecondaryJobDef, weapon);
                }
            }

            // Default to vanilla equip job
            // Creating vanilla equip job
            var job = JobMaker.MakeJob(JobDefOf.Equip, weapon);
            job.count = 1;
            // Job created
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
            {
                // Always critical job
                return true;
            }

            // Check job name patterns
            var defName = jobDef.defName;
            foreach (var pattern in CriticalJobPatterns)
            {
                if (defName.Contains(pattern))
                {
                    // Critical pattern found
                    return true;
                }
            }

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

            // Check if it's interruptible for major upgrade (15% better by default)
            float majorUpgradeThreshold = 1f + (AutoArmMod.settings.weaponUpgradeThreshold - 1f) * 3f; // Triple the threshold for interrupting work
            if (upgradePercentage >= majorUpgradeThreshold && InterruptibleForMajorUpgrade.Contains(jobDef))
                return true;

            // Check job name patterns
            var defName = jobDef.defName;
            
            // Special handling for haul jobs
            if (defName.Contains("Haul"))
                return !defName.Contains("Urgent") && !defName.Contains("Critical");
            
            // Check safe job prefixes
            foreach (var prefix in SafeJobPrefixes)
            {
                if (defName.Contains(prefix))
                    return true;
            }
            
            return false;
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


    }
}