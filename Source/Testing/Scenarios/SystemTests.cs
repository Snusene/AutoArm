using AutoArm.Caching;
using AutoArm.Definitions;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace AutoArm.Testing.Scenarios
{
    internal sealed class WeaponDestructionSafetyTest : ITestScenario
    {
        public string Name => "Weapon destruction safety";
        private List<ThingWithComps> testWeapons = new List<ThingWithComps>();
        private Pawn testPawn;

        public void Setup(Map map)
        {
            if (map == null) return;

            testPawn = TestHelpers.CreateTestPawn(map);

            for (int i = 0; i < 5; i++)
            {
                var weapon = TestHelpers.CreateWeapon(map, AutoArmDefOf.Gun_Autopistol,
                    map.Center + new IntVec3(i * 2, 0, 0));
                if (weapon != null)
                {
                    testWeapons.Add(weapon);
                }
            }
        }

        public TestResult Run()
        {
            var result = new TestResult { Success = true };

            if (testWeapons.Count > 0)
            {
                var weapon = testWeapons[0];

                TestHelpers.SafeDestroyWeapon(weapon);

                if (!weapon.Destroyed)
                {
                    result.Success = false;
                    AutoArmLogger.Warn("[TEST] WeaponDestructionSafetyTest: Weapon not marked as destroyed after Destroy()");
                }

                try
                {
                    TestHelpers.SafeDestroyWeapon(weapon);
                }
                catch (Exception e)
                {
                    result.Success = false;
                    AutoArmLogger.Warn($"[TEST] WeaponDestructionSafetyTest: Exception on double destroy - {e.Message}");
                }
            }

            if (testWeapons.Count > 1)
            {
                var weapon = testWeapons[1];
                var map = weapon.Map;

                WeaponCache.AddWeaponToCache(weapon);

                TestHelpers.SafeDestroyWeapon(weapon);

                var cachedWeapons = WeaponCache.GetAllWeapons(map);
                if (cachedWeapons.Contains(weapon))
                {
                    result.Success = false;
                    AutoArmLogger.Warn("[TEST] WeaponDestructionSafetyTest: Destroyed weapon still in cache");
                }
            }

            if (testPawn != null && testWeapons.Count > 2)
            {
                var weapon = testWeapons[2];
                weapon.DeSpawn();
                testPawn.equipment.AddEquipment(weapon);

                TestHelpers.SafeDestroyPawn(testPawn);

                if (!weapon.Destroyed)
                {
                    result.Success = false;
                    AutoArmLogger.Warn("[TEST] WeaponDestructionSafetyTest: Equipped weapon not destroyed with pawn");
                }
            }

            return result;
        }

        public void Cleanup()
        {
            TestHelpers.SafeDestroyPawn(testPawn);
            TestHelpers.CleanupWeapons(testWeapons);
        }
    }

}
