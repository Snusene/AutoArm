using AutoArm.Definitions;
using AutoArm.Helpers;
using AutoArm.Jobs;
using AutoArm.Testing.Helpers;
using RimWorld;
using Verse;

namespace AutoArm.Testing.Scenarios
{
    internal sealed class ForcedIncineratorTest : ITestScenario
    {
        public string Name => "Forced Incinerator not swapped";

        private Pawn testPawn;
        private ThingWithComps incinerator;
        private ThingWithComps betterWeapon;
        private bool incineratorAvailable;

        public void Setup(Map map)
        {
            if (map == null) return;

            var incineratorDef = DefDatabase<ThingDef>.GetNamedSilentFail("Gun_Incinerator");
            if (incineratorDef == null)
                return;

            incineratorAvailable = true;

            testPawn = TestHelpers.CreateTestPawn(map);
            if (testPawn == null) return;

            testPawn.equipment?.DestroyAllEquipment();

            var incPos = TestPositions.GetNearbyPosition(testPawn.Position, 1.5f, 3f, map);
            incinerator = TestHelpers.CreateWeapon(map, incineratorDef, incPos, QualityCategory.Normal);

            var riflePos = TestPositions.GetNearbyPosition(testPawn.Position, 1.5f, 3f, map);
            betterWeapon = TestHelpers.CreateWeapon(map, AutoArmDefOf.Gun_AssaultRifle, riflePos, QualityCategory.Excellent);

            if (incinerator != null && betterWeapon != null)
            {
                incinerator.DeSpawn();
                testPawn.equipment.AddEquipment(incinerator);
                ForcedWeapons.SetForced(testPawn, incinerator);
            }
        }

        public TestResult Run()
        {
            if (!incineratorAvailable)
                return TestResult.Skip("Gun_Incinerator def not present (Anomaly DLC required)");

            if (testPawn == null || incinerator == null || betterWeapon == null)
                return TestResult.Failure("Test setup failed");

            var result = new TestResult { Success = true };

            float incineratorScore = Scoring.GetWeaponPropertyScore(testPawn, incinerator);
            float rifleScore = Scoring.GetWeaponPropertyScore(testPawn, betterWeapon);
            result.Data["IncineratorBaseScore"] = incineratorScore;
            result.Data["RifleBaseScore"] = rifleScore;

            bool isForced = ForcedWeapons.IsForced(testPawn, incinerator);
            result.Data["IncineratorIsForced"] = isForced;

            if (!isForced)
            {
                result.Success = false;
                result.FailureReason = "Incinerator not marked as forced";
                return result;
            }

            var jobGiver = new JobGiver_PickUpBetterWeapon();
            var job = jobGiver.TestTryGiveJob(testPawn);

            bool noSwap = (job == null);
            result.Data["NoSwapJobCreated"] = noSwap;

            if (!noSwap)
            {
                result.Success = false;
                result.FailureReason = $"Forced Incinerator (score {incineratorScore:F1}) was swapped for {job.targetA.Thing?.Label ?? "another weapon"} (score {rifleScore:F1})";
            }

            return result;
        }

        public void Cleanup()
        {
            if (testPawn != null && incinerator != null)
                ForcedWeapons.ClearForced(testPawn);

            if (testPawn != null && !testPawn.Destroyed)
            {
                testPawn.equipment?.DestroyAllEquipment();
                TestHelpers.SafeDestroyPawn(testPawn);
            }

            if (incinerator != null && !incinerator.Destroyed && incinerator.Spawned)
                incinerator.Destroy();

            if (betterWeapon != null && !betterWeapon.Destroyed && betterWeapon.Spawned)
                betterWeapon.Destroy();
        }
    }
}
