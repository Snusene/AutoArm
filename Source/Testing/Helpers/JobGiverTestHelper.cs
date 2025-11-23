using AutoArm.Jobs;
using System.Reflection;
using Verse;

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


            int maxTries = 200;
            int emergencyInterval = 60;

            for (int i = 0; i < maxTries; i++)
            {
                if (pawn.IsHashIntervalTick(emergencyInterval))
                {
                    break;
                }
                Find.TickManager.DoSingleTick();
            }
        }
    }
}
