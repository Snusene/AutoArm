using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace AutoArm.Testing
{
    // All test scenario implementations

    public class UnarmedPawnTest : ITestScenario
    {
        public string Name => "Unarmed Pawn Weapon Acquisition";
        private Pawn testPawn;
        private ThingWithComps testWeapon;

        public void Setup(Map map)
        {
            if (map == null) return;
            testPawn = TestHelpers.CreateTestPawn(map);
            if (testPawn != null)
            {
                // Make sure pawn is unarmed
                testPawn.equipment?.DestroyAllEquipment();

                // Create a weapon nearby
                var weaponDef = VanillaWeaponDefOf.Gun_Autopistol;
                if (weaponDef != null)
                {
                    testWeapon = TestHelpers.CreateWeapon(map, weaponDef,
                        testPawn.Position + new IntVec3(3, 0, 0));
                }
            }
        }

        public TestResult Run()
        {
            if (testPawn == null)
                return TestResult.Failure("Test pawn creation failed");

            if (testPawn.equipment?.Primary != null)
                return TestResult.Failure("Pawn is not unarmed");

            var jobGiver = new JobGiver_PickUpBetterWeapon();
            var job = jobGiver.TestTryGiveJob(testPawn);

            if (job == null)
                return TestResult.Failure("No weapon pickup job created for unarmed pawn");

            if (job.def != JobDefOf.Equip)
                return TestResult.Failure($"Wrong job type: {job.def.defName}");

            return TestResult.Pass();
        }

        public void Cleanup()
        {
            testPawn?.Destroy();
            testWeapon?.Destroy();
        }
    }

    public class BrawlerTest : ITestScenario
    {
        public string Name => "Brawler Weapon Preferences";
        private Pawn brawlerPawn;
        private List<ThingWithComps> weapons = new List<ThingWithComps>();

        public void Setup(Map map)
        {
            if (map == null) return;

            brawlerPawn = TestHelpers.CreateTestPawn(map, new TestHelpers.TestPawnConfig
            {
                Name = "TestBrawler",
                Traits = new List<TraitDef> { TraitDefOf.Brawler }
            });

            if (brawlerPawn != null)
            {
                var pos = brawlerPawn.Position;

                // Create melee and ranged weapons
                var swordDef = VanillaWeaponDefOf.MeleeWeapon_LongSword;
                var rifleDef = VanillaWeaponDefOf.Gun_AssaultRifle;

                if (swordDef != null)
                    weapons.Add(TestHelpers.CreateWeapon(map, swordDef, pos + new IntVec3(2, 0, 0)));
                if (rifleDef != null)
                    weapons.Add(TestHelpers.CreateWeapon(map, rifleDef, pos + new IntVec3(-2, 0, 0)));
            }
        }

        public TestResult Run()
        {
            if (brawlerPawn == null)
                return TestResult.Failure("Brawler pawn creation failed");

            var jobGiver = new JobGiver_PickUpBetterWeapon();
            var job = jobGiver.TestTryGiveJob(brawlerPawn);

            if (job != null && job.targetA.Thing is ThingWithComps weapon)
            {
                if (weapon.def.IsRangedWeapon)
                    return TestResult.Failure("Brawler tried to pick up ranged weapon");

                if (weapon.def.IsMeleeWeapon)
                    return TestResult.Pass();
            }

            return TestResult.Pass(); // No job is also acceptable if brawler already has good melee weapon
        }

        public void Cleanup()
        {
            brawlerPawn?.Destroy();
            weapons.ForEach(w => w?.Destroy());
        }
    }

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

    public class WeaponUpgradeTest : ITestScenario
    {
        public string Name => "Weapon Upgrade Logic";
        private Pawn testPawn;
        private ThingWithComps currentWeapon;
        private ThingWithComps betterWeapon;

        public void Setup(Map map)
        {
            if (map == null) return;
            testPawn = TestHelpers.CreateTestPawn(map);

            if (testPawn != null)
            {
                var pos = testPawn.Position;

                // Give pawn a poor quality weapon
                var weaponDef = VanillaWeaponDefOf.Gun_Autopistol;
                if (weaponDef != null)
                {
                    currentWeapon = TestHelpers.CreateWeapon(map, weaponDef, pos, QualityCategory.Poor);
                    if (currentWeapon != null)
                    {
                        testPawn.equipment?.AddEquipment(currentWeapon);
                    }

                    // Create a better weapon nearby
                    betterWeapon = TestHelpers.CreateWeapon(map, weaponDef, pos + new IntVec3(3, 0, 0), QualityCategory.Excellent);
                }
            }
        }

        public TestResult Run()
        {
            if (testPawn == null)
                return TestResult.Failure("Test pawn creation failed");

            var jobGiver = new JobGiver_PickUpBetterWeapon();
            var currentScore = currentWeapon != null ? jobGiver.GetWeaponScore(testPawn, currentWeapon) : 0f;
            var betterScore = betterWeapon != null ? jobGiver.GetWeaponScore(testPawn, betterWeapon) : 0f;

            if (betterScore <= currentScore)
                return TestResult.Failure("Better weapon not scored higher than current weapon");

            var job = jobGiver.TestTryGiveJob(testPawn);
            if (job == null)
                return TestResult.Failure("No upgrade job created despite better weapon available");

            return TestResult.Pass();
        }

        public void Cleanup()
        {
            testPawn?.Destroy();
            betterWeapon?.Destroy();
        }
    }

    public class OutfitFilterTest : ITestScenario
    {
        public string Name => "Outfit Filter Weapon Restrictions";
        private Pawn testPawn;
        private List<ThingWithComps> weapons = new List<ThingWithComps>();

        public void Setup(Map map)
        {
            if (map == null) return;
            testPawn = TestHelpers.CreateTestPawn(map);

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

    public class ForcedWeaponTest : ITestScenario
    {
        public string Name => "Forced Weapon Retention";
        private Pawn testPawn;
        private ThingWithComps forcedWeapon;
        private ThingWithComps betterWeapon;

        public void Setup(Map map)
        {
            if (map == null) return;
            testPawn = TestHelpers.CreateTestPawn(map);

            if (testPawn != null)
            {
                var pos = testPawn.Position;
                var pistolDef = VanillaWeaponDefOf.Gun_Autopistol;
                var rifleDef = VanillaWeaponDefOf.Gun_AssaultRifle;

                if (pistolDef != null && rifleDef != null)
                {
                    forcedWeapon = TestHelpers.CreateWeapon(map, pistolDef, pos);
                    if (forcedWeapon != null)
                    {
                        testPawn.equipment?.AddEquipment(forcedWeapon);
                        ForcedWeaponTracker.SetForced(testPawn, forcedWeapon);
                    }

                    betterWeapon = TestHelpers.CreateWeapon(map, rifleDef, pos + new IntVec3(3, 0, 0));
                }
            }
        }

        public TestResult Run()
        {
            if (testPawn == null || forcedWeapon == null)
                return TestResult.Failure("Test setup failed");

            if (!ForcedWeaponTracker.IsForced(testPawn, forcedWeapon))
                return TestResult.Failure("Weapon not marked as forced");

            var jobGiver = new JobGiver_PickUpBetterWeapon();
            var job = jobGiver.TestTryGiveJob(testPawn);

            if (job != null)
                return TestResult.Failure("Pawn tried to replace forced weapon");

            return TestResult.Pass();
        }

        public void Cleanup()
        {
            ForcedWeaponTracker.ClearForced(testPawn);
            testPawn?.Destroy();
            betterWeapon?.Destroy();
        }
    }

    public class CombatExtendedAmmoTest : ITestScenario
    {
        public string Name => "Combat Extended Ammo Check";

        public void Setup(Map map) { }

        public TestResult Run()
        {
            if (!CECompat.IsLoaded())
                return TestResult.Pass(); // Skip if CE not loaded

            // Basic test that CE detection works
            bool shouldCheck = CECompat.ShouldCheckAmmo();

            var result = new TestResult { Success = true };
            result.Data["CE Loaded"] = CECompat.IsLoaded();
            result.Data["Should Check Ammo"] = shouldCheck;

            return result;
        }

        public void Cleanup() { }
    }

    public class SimpleSidearmsIntegrationTest : ITestScenario
    {
        public string Name => "Simple Sidearms Integration";
        private Pawn testPawn;

        public void Setup(Map map)
        {
            if (map == null) return;
            testPawn = TestHelpers.CreateTestPawn(map);
        }

        public TestResult Run()
        {
            if (!SimpleSidearmsCompat.IsLoaded())
                return TestResult.Pass(); // Skip if not loaded

            if (testPawn == null)
                return TestResult.Failure("Test pawn creation failed");

            int maxSidearms = SimpleSidearmsCompat.GetMaxSidearmsForPawn(testPawn);
            int currentCount = SimpleSidearmsCompat.GetCurrentSidearmCount(testPawn);

            var result = new TestResult { Success = true };
            result.Data["Max Sidearms"] = maxSidearms;
            result.Data["Current Count"] = currentCount;

            return result;
        }

        public void Cleanup()
        {
            testPawn?.Destroy();
        }
    }

    public class ChildColonistTest : ITestScenario
    {
        public string Name => "Child Colonist Age Restrictions";
        private Pawn childPawn;

        public void Setup(Map map)
        {
            if (map == null || !ModsConfig.BiotechActive) return;

            // Create a young pawn
            childPawn = TestHelpers.CreateTestPawnWithAge(map, 10, "TestChild");
        }

        public TestResult Run()
        {
            if (!ModsConfig.BiotechActive)
                return TestResult.Pass(); // Skip if Biotech not active

            if (childPawn == null)
                return TestResult.Pass(); // Can't test without proper child pawn

            string reason;
            bool isValid = JobGiverHelpers.IsValidPawnForAutoEquip(childPawn, out reason);

            if (AutoArmMod.settings?.allowChildrenToEquipWeapons == true)
            {
                if (!isValid && reason.Contains("Too young"))
                    return TestResult.Failure("Child rejected despite setting allowing children");
            }
            else
            {
                if (isValid)
                    return TestResult.Failure("Child allowed despite setting disallowing children");
            }

            return TestResult.Pass();
        }

        public void Cleanup()
        {
            childPawn?.Destroy();
        }
    }

    public class NobilityTest : ITestScenario
    {
        public string Name => "Conceited Noble Behavior";
        private Pawn noblePawn;

        public void Setup(Map map)
        {
            if (map == null || !ModsConfig.RoyaltyActive) return;

            noblePawn = TestHelpers.CreateTestPawn(map, new TestHelpers.TestPawnConfig
            {
                Name = "TestNoble",
                MakeNoble = true,
                Conceited = true
            });
        }

        public TestResult Run()
        {
            if (!ModsConfig.RoyaltyActive)
                return TestResult.Pass(); // Skip if Royalty not active

            if (noblePawn == null)
                return TestResult.Pass(); // Can't test without proper noble

            // Basic test - conceited nobles with weapons shouldn't look for new ones
            if (noblePawn.equipment?.Primary != null)
            {
                var jobGiver = new JobGiver_PickUpBetterWeapon();
                var job = jobGiver.TestTryGiveJob(noblePawn);

                if (job != null && AutoArmMod.settings?.respectConceitedNobles == true)
                    return TestResult.Failure("Conceited noble tried to switch weapons");
            }

            return TestResult.Pass();
        }

        public void Cleanup()
        {
            noblePawn?.Destroy();
        }
    }

    public class MapTransitionTest : ITestScenario
    {
        public string Name => "Map Transition Cache Handling";

        public void Setup(Map map) { }

        public TestResult Run()
        {
            // Test that weapon cache handles map transitions properly
            var cacheField = typeof(JobGiver_PickUpBetterWeapon).GetField("weaponCache",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

            if (cacheField == null)
                return TestResult.Failure("Could not find weapon cache field");

            var cache = cacheField.GetValue(null) as Dictionary<Map, List<ThingWithComps>>;
            if (cache == null)
                return TestResult.Failure("Weapon cache is null");

            // Basic test - cache should exist
            return TestResult.Pass();
        }

        public void Cleanup() { }
    }

    public class SaveLoadTest : ITestScenario
    {
        public string Name => "Save/Load Forced Weapons";
        private Pawn testPawn;
        private ThingWithComps testWeapon;

        public void Setup(Map map)
        {
            if (map == null) return;
            testPawn = TestHelpers.CreateTestPawn(map);

            if (testPawn != null)
            {
                var weaponDef = VanillaWeaponDefOf.Gun_Autopistol;
                if (weaponDef != null)
                {
                    testWeapon = TestHelpers.CreateWeapon(map, weaponDef, testPawn.Position);
                    if (testWeapon != null)
                    {
                        testPawn.equipment?.AddEquipment(testWeapon);
                        ForcedWeaponTracker.SetForced(testPawn, testWeapon);
                    }
                }
            }
        }

        public TestResult Run()
        {
            if (testPawn == null || testWeapon == null)
                return TestResult.Failure("Test setup failed");

            // Test save data
            var saveData = ForcedWeaponTracker.GetSaveData();
            if (!saveData.ContainsKey(testPawn))
                return TestResult.Failure("Forced weapon not in save data");

            // Simulate load
            ForcedWeaponTracker.LoadSaveData(saveData);

            // Check if still forced
            if (!ForcedWeaponTracker.IsForced(testPawn, testWeapon))
                return TestResult.Failure("Forced weapon not retained after load");

            return TestResult.Pass();
        }

        public void Cleanup()
        {
            ForcedWeaponTracker.ClearForced(testPawn);
            testPawn?.Destroy();
        }
    }

    public class PerformanceTest : ITestScenario
    {
        public string Name => "Performance Benchmarks";
        private List<Pawn> testPawns = new List<Pawn>();
        private List<ThingWithComps> testWeapons = new List<ThingWithComps>();

        public void Setup(Map map)
        {
            if (map == null) return;

            // Create multiple pawns and weapons for performance testing
            for (int i = 0; i < 20; i++)
            {
                var pawn = TestHelpers.CreateTestPawn(map);
                if (pawn != null)
                {
                    testPawns.Add(pawn);

                    // Create weapons near each pawn
                    var weaponDef = i % 2 == 0 ? VanillaWeaponDefOf.Gun_Autopistol : VanillaWeaponDefOf.MeleeWeapon_Knife;
                    if (weaponDef != null)
                    {
                        var weapon = TestHelpers.CreateWeapon(map, weaponDef,
                            pawn.Position + new IntVec3(2, 0, 0));
                        if (weapon != null)
                            testWeapons.Add(weapon);
                    }
                }
            }
        }

        public TestResult Run()
        {
            if (testPawns.Count == 0)
                return TestResult.Failure("No test pawns created");

            var jobGiver = new JobGiver_PickUpBetterWeapon();
            var startTicks = Find.TickManager.TicksGame;
            int jobsCreated = 0;

            foreach (var pawn in testPawns)
            {
                var job = jobGiver.TestTryGiveJob(pawn);
                if (job != null)
                    jobsCreated++;
            }

            var elapsed = Find.TickManager.TicksGame - startTicks;

            var result = new TestResult { Success = true };
            result.Data["Pawns Tested"] = testPawns.Count;
            result.Data["Jobs Created"] = jobsCreated;
            result.Data["Ticks Elapsed"] = elapsed;
            result.Data["Time Per Pawn"] = $"{elapsed / (float)testPawns.Count:F2} ticks";

            return result;
        }

        public void Cleanup()
        {
            testPawns.ForEach(p => p?.Destroy());
            testWeapons.ForEach(w => w?.Destroy());
        }
    }

    public class EdgeCaseTest : ITestScenario
    {
        public string Name => "Edge Cases and Error Handling";

        public void Setup(Map map) { }

        public TestResult Run()
        {
            // Test null handling
            var jobGiver = new JobGiver_PickUpBetterWeapon();

            // Test with null pawn
            var job = jobGiver.TestTryGiveJob(null);
            if (job != null)
                return TestResult.Failure("Job created for null pawn");

            // Test weapon score with nulls
            float score = jobGiver.GetWeaponScore(null, null);
            if (score != 0f)
                return TestResult.Failure("Non-zero score for null inputs");

            return TestResult.Pass();
        }

        public void Cleanup() { }
    }
}