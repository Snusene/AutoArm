using AutoArm.Jobs;
using Verse;
using Verse.AI;
using System.Reflection;

namespace AutoArm.Testing.Helpers
{
    /// <summary>
    /// Test helper to bypass timing restrictions in JobGiver
    /// </summary>
    public static class JobGiverTestHelper
    {
        private static readonly FieldInfo lastProcessTickField;
        private static readonly FieldInfo processedThisTickField;
        
        static JobGiverTestHelper()
        {
            var jobGiverType = typeof(JobGiver_PickUpBetterWeapon);
            lastProcessTickField = jobGiverType.GetField("lastProcessTick", 
                BindingFlags.NonPublic | BindingFlags.Static);
            processedThisTickField = jobGiverType.GetField("processedThisTick", 
                BindingFlags.NonPublic | BindingFlags.Static);
        }
        
        /// <summary>
        /// Force the JobGiver to think it's on a different tick for each test call
        /// </summary>
        public static void ForceNewTickForTest()
        {
            if (lastProcessTickField != null)
            {
                // Set to a very old tick so the current tick is always "new"
                lastProcessTickField.SetValue(null, -999999);
            }
            
            if (processedThisTickField != null)
            {
                var hashSet = processedThisTickField.GetValue(null) as System.Collections.Generic.HashSet<Pawn>;
                hashSet?.Clear();
            }
        }
        
        /// <summary>
        /// Ensure a pawn will pass hash interval checks
        /// </summary>
        public static void EnsurePawnPassesHashInterval(Pawn pawn)
        {
            if (pawn == null || Find.TickManager == null) return;
            
            // The hash interval check uses pawn.IsHashIntervalTick(interval)
            // We need to make sure the current tick matches the pawn's hash interval
            // The formula is: (thingIDNumber + Find.TickManager.TicksGame) % interval == 0
            
            // For emergency checks (unarmed pawns), the interval is usually small (60-120)
            // We'll advance ticks until we hit one that works
            int maxTries = 200;
            int emergencyInterval = 60; // Typical emergency check interval
            
            for (int i = 0; i < maxTries; i++)
            {
                if (pawn.IsHashIntervalTick(emergencyInterval))
                {
                    break; // Found a good tick
                }
                Find.TickManager.DoSingleTick();
            }
        }
    }
}
