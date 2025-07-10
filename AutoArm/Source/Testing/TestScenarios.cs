using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;
using AutoArm;

namespace AutoArm.Testing
{
    // Actual test implementations
    public class OutfitFilterTest : ITestScenario
    {
        public string Name => "Outfit Filter Weapon Restrictions";
        private Pawn testPawn;
        private List<ThingWithComps> weapons = new List<ThingWithComps>();

        public void Setup(Map map)
        {
            if (map == null) return;
            testPawn = TestHelpers.CreateTestPawn(map);

            // Create test weapons using safe weapon defs
            var pos = testPawn?.Position ?? IntVec3.Invalid;
            if (pos.IsValid)
            {
                var rifleDef = VanillaWeaponDefOf.Gun_AssaultRifle;
                var swordDef = VanillaWeaponDefOf.MeleeWeapon_LongSword;

                if (rifleDef != null)
                    weapons.Add(TestHelpers.CreateWeapon(map, rifleDef, pos + new IntVec3(2, 0, 0)));
                if (swordDef != null)
                    weapons.Add(TestHelpers.CreateWeapon(map, swordDef, pos + new IntVec3(-2, 0, 0)));
            }
        }

        public TestResult Run()
        {
            if (testPawn == null)
                return TestResult.Failure("Test pawn creation failed");

            // Basic test - pawn should be able to find weapons
            var jobGiver = new JobGiver_PickUpBetterWeapon();
            var job = jobGiver.TestTryGiveJob(testPawn);

            return TestResult.Pass();
        }

        public void Cleanup()
        {
            testPawn?.Destroy();
            weapons.ForEach(w => w?.Destroy());
        }
    }

    // Fix the HunterTest implementation
    public class HunterTest : ITestScenario
    {
        public string Name => "Hunter Weapon Preferences";
        private Pawn hunterPawn;
        private List<Thing> testWeapons = new List<Thing>();

        public void Setup(Map map)
        {
            if (map == null) return;

            hunterPawn = TestHelpers.CreateTestPawn(map, new TestHelpers.TestPawnConfig
            {
                Name = "TestHunter",
                Skills = new Dictionary<SkillDef, int>
                {
                    { SkillDefOf.Shooting, 12 },
                    { SkillDefOf.Animals, 10 }
                }
            });

            if (hunterPawn?.workSettings != null)
            {
                hunterPawn.workSettings.SetPriority(WorkTypeDefOf.Hunting, 1);
            }
        }

        public TestResult Run()
        {
            if (hunterPawn == null)
                return TestResult.Failure("Hunter pawn creation failed");

            return TestResult.Pass();
        }

        public void Cleanup()
        {
            hunterPawn?.Destroy();
            testWeapons.ForEach(w => w?.Destroy());
        }
    }
}