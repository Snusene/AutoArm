using AutoArm.Definitions;
using AutoArm.Helpers;
using AutoArm.Jobs;
using AutoArm.Logging;
using AutoArm.Testing.Helpers;
using RimWorld;
using Verse;

namespace AutoArm.Testing.Scenarios
{
    public class ForcedWeaponPersistenceTest : ITestScenario
    {
        public string Name => "Forced Weapon Persistence";
        private Pawn testPawn;
        private ThingWithComps forcedWeapon;
        private ThingWithComps betterWeapon;

        public void Setup(Map map)
        {
            if (map == null) return;

            testPawn = TestHelpers.CreateTestPawn(map);
            if (testPawn != null)
            {
                testPawn.equipment?.DestroyAllEquipment();

                var pistolPos = TestPositions.GetNearbyPosition(testPawn.Position, 1.5f, 3f, map);
                var riflePos = TestPositions.GetNearbyPosition(testPawn.Position, 1.5f, 3f, map);

                forcedWeapon = TestHelpers.CreateWeapon(map, AutoArmDefOf.Gun_Autopistol, pistolPos, QualityCategory.Normal);
                betterWeapon = TestHelpers.CreateWeapon(map, AutoArmDefOf.Gun_AssaultRifle, riflePos, QualityCategory.Excellent);

                if (forcedWeapon != null && betterWeapon != null)
                {
                    forcedWeapon.DeSpawn();
                    testPawn.equipment.AddEquipment(forcedWeapon);
                    ForcedWeapons.SetForced(testPawn, forcedWeapon);
                }
            }
        }

        public TestResult Run()
        {
            if (testPawn == null || forcedWeapon == null || betterWeapon == null)
            {
                return TestResult.Failure("Test setup failed");
            }

            var result = new TestResult { Success = true };

            bool isForced = ForcedWeapons.IsForced(testPawn, forcedWeapon);
            result.Data["WeaponIsForced"] = isForced;

            if (!isForced)
            {
                result.Success = false;
                AutoArmLogger.Error("[TEST] ForcedWeaponPersistenceTest: Weapon not marked as forced");
                return result;
            }

            var jobGiver = new JobGiver_PickUpBetterWeapon();
            var job = jobGiver.TestTryGiveJob(testPawn);

            bool jobNotCreated = (job == null);
            result.Data["JobNotCreatedForForcedWeapon"] = jobNotCreated;

            if (!jobNotCreated)
            {
                result.Success = false;
                AutoArmLogger.Error("[TEST] ForcedWeaponPersistenceTest: Job created for forced weapon - forced status not respected");
                return result;
            }

            ForcedWeapons.ClearForced(testPawn);
            bool unforced = !ForcedWeapons.IsForced(testPawn, forcedWeapon);
            result.Data["WeaponUnforced"] = unforced;

            ForcedWeapons.SetForced(testPawn, forcedWeapon);
            bool reforced = ForcedWeapons.IsForced(testPawn, forcedWeapon);
            result.Data["WeaponReforced"] = reforced;

            if (!unforced || !reforced)
            {
                result.Success = false;
                AutoArmLogger.Error("[TEST] ForcedWeaponPersistenceTest: Force/unforce cycle failed");
                return result;
            }

            var initiallyForcedWeapons = ForcedWeapons.GetAllForcedWeapons();
            result.Data["InitialForcedCount"] = initiallyForcedWeapons.Count;

            ForcedWeapons.Cleanup();

            bool stillForcedAfterCleanup = ForcedWeapons.IsForced(testPawn, forcedWeapon);
            result.Data["StillForcedAfterCleanup"] = stillForcedAfterCleanup;

            if (!stillForcedAfterCleanup)
            {
                result.Success = false;
                AutoArmLogger.Error("[TEST] ForcedWeaponPersistenceTest: Forced weapon lost during cleanup");
            }

            return result;
        }

        public void Cleanup()
        {
            if (testPawn != null && forcedWeapon != null)
            {
                ForcedWeapons.ClearForced(testPawn);
            }

            if (testPawn != null && !testPawn.Destroyed)
            {
                testPawn.equipment?.DestroyAllEquipment();
                TestHelpers.SafeDestroyPawn(testPawn);
            }

            if (forcedWeapon != null && !forcedWeapon.Destroyed && forcedWeapon.Spawned)
            {
                forcedWeapon.Destroy();
            }

            if (betterWeapon != null && !betterWeapon.Destroyed && betterWeapon.Spawned)
            {
                betterWeapon.Destroy();
            }
        }
    }
}
