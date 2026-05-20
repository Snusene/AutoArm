using AutoArm.Caching;
using AutoArm.Definitions;
using AutoArm.Helpers;
using AutoArm.Jobs;
using AutoArm.Testing.Helpers;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;
using Verse.AI;

namespace AutoArm.Testing.Scenarios
{


    internal sealed class RaidDetectionTest : ITestScenario
    {
        public string Name => "Raid detection";
        private Pawn testPawn;
        private bool originalRaidSetting;

        public void Setup(Map map)
        {
            if (map == null) return;
            originalRaidSetting = AutoArmMod.settings?.disableDuringRaids ?? false;
            testPawn = TestHelpers.CreateTestPawn(map);
            testPawn?.equipment?.DestroyAllEquipment();
        }

        public TestResult Run()
        {
            if (testPawn == null)
                return TestResult.Failure("Test setup failed");

            var raidCheckerType = typeof(ModInit).Assembly.GetType("AutoArm.RaidChecker");
            if (raidCheckerType == null)
                return TestResult.Failure("RaidChecker type not found");

            var activeField = raidCheckerType.GetField("isLargeRaidActive", BindingFlags.NonPublic | BindingFlags.Static);
            var tickField = raidCheckerType.GetField("lastCheckTick", BindingFlags.NonPublic | BindingFlags.Static);
            if (activeField == null || tickField == null)
                return TestResult.Failure("RaidChecker fields not found");

            bool savedActive = (bool)activeField.GetValue(null);
            int savedTick = (int)tickField.GetValue(null);
            int futureTick = (Find.TickManager?.TicksGame ?? 0) + 10000;

            try
            {
                AutoArmMod.settings.disableDuringRaids = false;
                activeField.SetValue(null, true);
                tickField.SetValue(null, futureTick);
                if (ModInit.IsLargeRaidActive)
                    return TestResult.Failure("IsLargeRaidActive true when disableDuringRaids=false");

                AutoArmMod.settings.disableDuringRaids = true;
                activeField.SetValue(null, true);
                tickField.SetValue(null, futureTick);
                if (!ModInit.IsLargeRaidActive)
                    return TestResult.Failure("IsLargeRaidActive false when forced true");

                float priorityDuringRaid = new WeaponStatusEvaluator().EvaluatePriority(testPawn);
                if (priorityDuringRaid > 0f)
                    return TestResult.Failure($"EvaluatePriority returned {priorityDuringRaid} during raid, expected 0");

                activeField.SetValue(null, false);
                tickField.SetValue(null, futureTick);
                if (ModInit.IsLargeRaidActive)
                    return TestResult.Failure("IsLargeRaidActive true when forced false");

                float priorityCalm = new WeaponStatusEvaluator().EvaluatePriority(testPawn);
                if (priorityCalm <= 0f)
                    return TestResult.Failure($"EvaluatePriority returned {priorityCalm} when no raid, expected > 0");

                return TestResult.Pass();
            }
            finally
            {
                activeField.SetValue(null, savedActive);
                tickField.SetValue(null, savedTick);
            }
        }

        public void Cleanup()
        {
            if (AutoArmMod.settings != null)
                AutoArmMod.settings.disableDuringRaids = originalRaidSetting;
            TestHelpers.SafeDestroyPawn(testPawn);
        }
    }

    internal sealed class CaravanTest : ITestScenario
    {
        public string Name => "Caravan formation blocks";

        public void Setup(Map map) { }

        public TestResult Run()
        {
            var caravanLordName = typeof(RimWorld.LordJob_FormAndSendCaravan).Name;

            var restrictedField = typeof(AutoArm.Caching.PawnValidation).GetField(
                "restrictedLordJobTypes",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

            if (restrictedField == null)
                return TestResult.Failure("PawnValidation.restrictedLordJobTypes field not found");

            var set = restrictedField.GetValue(null) as System.Collections.Generic.HashSet<string>;
            if (set == null)
                return TestResult.Failure("restrictedLordJobTypes was not a HashSet<string>");

            if (!set.Contains(caravanLordName))
                return TestResult.Failure($"{caravanLordName} missing from restrictedLordJobTypes");

            return TestResult.Pass();
        }

        public void Cleanup() { }
    }

}
