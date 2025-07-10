using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace AutoArm
{
    public static class JobGiverHelpers
    {
        // Move these HashSets from UnifiedTickRarePatch
        private static readonly HashSet<JobDef> AlwaysCriticalJobs = new HashSet<JobDef>
        {
            // Medical/Emergency
            JobDefOf.TendPatient,
            JobDefOf.Rescue,
            JobDefOf.ExtinguishSelf,
            JobDefOf.BeatFire,
            JobDefOf.GotoSafeTemperature,
            
            // Combat
            JobDefOf.AttackMelee,
            JobDefOf.AttackStatic,
            JobDefOf.Hunt,
            JobDefOf.ManTurret,
            JobDefOf.Wait_Combat,
            JobDefOf.FleeAndCower,
            JobDefOf.Reload,
            
            // Critical personal needs
            JobDefOf.Vomit,
            JobDefOf.LayDown,
            JobDefOf.Lovin,
            JobDefOf.Ingest,
            
            // Transportation
            JobDefOf.EnterTransporter,
            JobDefOf.EnterCryptosleepCasket,
            
            // Prison/Security
            JobDefOf.Arrest,
            JobDefOf.Capture,
            JobDefOf.EscortPrisonerToBed,
            JobDefOf.TakeWoundedPrisonerToBed,
            JobDefOf.ReleasePrisoner,
            JobDefOf.Kidnap,
            JobDefOf.CarryDownedPawnToExit,
            JobDefOf.CarryToCryptosleepCasket,
            
            // Trading
            JobDefOf.TradeWithPawn,
            
            // Communications
            JobDefOf.UseCommsConsole,
            
            // Equipment handling
            JobDefOf.DropEquipment
        };

        private static readonly HashSet<JobDef> ConditionalCriticalJobs = new HashSet<JobDef>
        {
            JobDefOf.Sow,
            JobDefOf.Harvest,
            JobDefOf.HaulToCell,
            JobDefOf.HaulToContainer,
            JobDefOf.DoBill,
            JobDefOf.FinishFrame,
            JobDefOf.SmoothFloor,
            JobDefOf.Mine,
            JobDefOf.Refuel,
            JobDefOf.Research
        };

        public static bool IsCriticalJob(Pawn pawn, bool hasNoSidearms = false)
        {
            if (pawn.CurJob == null)
                return false;

            var job = pawn.CurJob.def;

            // Always check player-forced
            if (pawn.CurJob.playerForced)
                return true;

            // Check always-critical jobs
            if (AlwaysCriticalJobs.Contains(job))
                return true;

            // Check conditional jobs (only critical if pawn has sidearms)
            if (!hasNoSidearms && ConditionalCriticalJobs.Contains(job))
                return true;

            // Check string-based patterns for DLC/modded content
            var defName = job.defName;
            if (defName.Contains("Ritual") ||
                defName.Contains("Surgery") ||
                defName.Contains("Operate") ||
                defName.Contains("Prisoner") ||
                defName.Contains("Mental") ||
                defName.Contains("PrepareCaravan"))
                return true;

            return false;
        }

        // Move AlwaysSafeToInterrupt here too
        private static readonly HashSet<JobDef> AlwaysSafeToInterrupt = new HashSet<JobDef>
        {
            JobDefOf.Wait,
            JobDefOf.Wait_Wander,
            JobDefOf.GotoWander
        };

        public static bool IsSafeToInterrupt(JobDef jobDef)
        {
            if (AlwaysSafeToInterrupt.Contains(jobDef))
                return true;

            var defName = jobDef.defName;
            return defName.StartsWith("Joy") ||
                   defName.Contains("Social");
        }
    }
}