using AutoArm.Caching;
using AutoArm.Definitions;
using AutoArm.Helpers;
using AutoArm.Jobs;
using AutoArm.Testing.Helpers;
using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
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

            var watcher = testPawn.Map?.dangerWatcher;
            var ratingField = AccessTools.Field(typeof(DangerWatcher), "dangerRatingInt");
            var tickField = AccessTools.Field(typeof(DangerWatcher), "lastUpdateTick");
            if (watcher == null || ratingField == null || tickField == null)
                return TestResult.Failure("DangerWatcher fields not found");

            var savedRating = ratingField.GetValue(watcher);
            int savedTick = (int)tickField.GetValue(watcher);
            int futureTick = Find.TickManager.TicksGame + 10000;

            try
            {
                ratingField.SetValue(watcher, StoryDanger.High);
                tickField.SetValue(watcher, futureTick);

                AutoArmMod.settings.disableDuringRaids = false;
                if (RaidChecker.ShouldSkipDuringRaid(testPawn.Map))
                    return TestResult.Failure("Raid pause active when disableDuringRaids=false");

                AutoArmMod.settings.disableDuringRaids = true;
                if (!RaidChecker.ShouldSkipDuringRaid(testPawn.Map))
                    return TestResult.Failure("Raid pause inactive on a high danger map");

                float priorityDuringRaid = new WeaponStatusEvaluator().EvaluatePriority(testPawn);
                if (priorityDuringRaid > 0f)
                    return TestResult.Failure($"EvaluatePriority returned {priorityDuringRaid} during raid, expected 0");

                ratingField.SetValue(watcher, StoryDanger.None);
                if (RaidChecker.ShouldSkipDuringRaid(testPawn.Map))
                    return TestResult.Failure("Raid pause active on a calm map");

                float priorityCalm = new WeaponStatusEvaluator().EvaluatePriority(testPawn);
                if (priorityCalm <= 0f)
                    return TestResult.Failure($"EvaluatePriority returned {priorityCalm} when no raid, expected > 0");

                return TestResult.Pass();
            }
            finally
            {
                ratingField.SetValue(watcher, savedRating);
                tickField.SetValue(watcher, savedTick);
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
