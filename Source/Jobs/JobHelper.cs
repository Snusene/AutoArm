// AutoArm RimWorld 1.5+ mod - automatic weapon management
// This file: Job creation and interruption logic
// Handles: Safe job interruption decisions, critical job detection
// Uses: Job categories and work priorities to avoid disrupting important tasks
// Critical: Prevents mod from interrupting emergency/critical jobs

using AutoArm.Logging;
using RimWorld;
using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace AutoArm.Jobs
{
    /// <summary>
    /// Centralized job handling (fixes #17, #27)
    /// </summary>
    public static class JobHelper
    {
        // Job categories for interruption logic - initialized once
        private static readonly HashSet<JobDef> AlwaysCriticalJobs = InitializeCriticalJobs();
        
        private static HashSet<JobDef> InitializeCriticalJobs()
        {
            return new HashSet<JobDef>
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
        }

        // Cached EquipSecondary job def
        private static readonly JobDef equipSecondaryJobDef = SimpleSidearmsCompat.IsLoaded() ? 
            DefDatabase<JobDef>.GetNamedSilentFail("EquipSecondary") : null;

        // String prefix sets for performance
        private static readonly string[] CriticalJobPatterns = new string[]
        {
            "Ritual", "Surgery", "Operate", "Prisoner", "Mental", "PrepareCaravan"
        };

        /// <summary>
        /// Create an equip job (fixes #27)
        /// </summary>
        public static Job CreateEquipJob(ThingWithComps weapon, bool isSidearm = false)
        {
            if (weapon == null)
            {
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.Debug("CreateEquipJob called with null weapon");
                }
                return null;
            }

            if (isSidearm && equipSecondaryJobDef != null)
            {
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.Debug($"Creating EquipSecondary job for {weapon.Label} (sidearm)");
                }
                return JobMaker.MakeJob(equipSecondaryJobDef, weapon);
            }

            // Default to vanilla equip job
            if (AutoArmMod.settings?.debugLogging == true)
            {
                AutoArmLogger.Debug($"Creating vanilla Equip job for {weapon.Label}{(isSidearm ? " (marked as sidearm)" : "")}");
            }
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

            // Check job name patterns - optimized
            var defName = jobDef.defName;
            for (int i = 0; i < CriticalJobPatterns.Length; i++)
            {
                if (defName.IndexOf(CriticalJobPatterns[i], StringComparison.Ordinal) >= 0)
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

            // Check common low priority jobs - use switch for better performance
            if (IsLowPriorityJobDef(jobDef))
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
        
        private static bool IsLowPriorityJobDef(JobDef jobDef)
        {
            return jobDef == JobDefOf.Wait || 
                   jobDef == JobDefOf.Wait_Wander ||
                   jobDef == JobDefOf.GotoWander || 
                   jobDef == JobDefOf.Goto ||
                   jobDef == JobDefOf.LayDownResting || 
                   jobDef == JobDefOf.Clean ||
                   jobDef == JobDefOf.ClearSnow || 
                   jobDef == JobDefOf.RemoveFloor;
        }
    }
}