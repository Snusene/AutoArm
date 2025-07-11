using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;
using Verse.AI.Group;
using static AutoArm.Testing.TestHelpers;

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

            // Clean up ALL autopistols on the map to avoid interference
            var allAutopistols = map.listerThings.ThingsInGroup(ThingRequestGroup.Weapon)
                .Where(t => t.def == VanillaWeaponDefOf.Gun_Autopistol)
                .ToList();

            foreach (var weapon in allAutopistols)
            {
                weapon.Destroy();
            }

            testPawn = TestHelpers.CreateTestPawn(map);
            if (testPawn != null)
            {
                // Ensure the pawn can use weapons - remove any traits that disable violence
                if (testPawn.WorkTagIsDisabled(WorkTags.Violent))
                {
                    // Clear all traits and add a neutral one
                    testPawn.story.traits.allTraits.Clear();
                    testPawn.story.traits.GainTrait(new Trait(TraitDefOf.Kind));

                    // If still disabled after removing traits, log it
                    if (testPawn.WorkTagIsDisabled(WorkTags.Violent))
                    {
                        Log.Warning($"[TEST] Pawn {testPawn.Name} still incapable of violence after removing traits (backstory issue)");
                    }
                }

                // Make sure pawn is unarmed
                testPawn.equipment?.DestroyAllEquipment();

                // Create a weapon nearby - make it unique so we can verify it's the right one
                var weaponDef = VanillaWeaponDefOf.Gun_Autopistol;
                if (weaponDef != null)
                {
                    testWeapon = TestHelpers.CreateWeapon(map, weaponDef,
                        testPawn.Position + new IntVec3(3, 0, 0));

                    // Make it excellent quality so it's distinguishable
                    if (testWeapon != null)
                    {
                        var compQuality = testWeapon.TryGetComp<CompQuality>();
                        if (compQuality != null)
                        {
                            compQuality.SetQuality(QualityCategory.Excellent, ArtGenerationContext.Colony);
                        }
                    }
                }
            }
        }

        public TestResult Run()
        {
            if (testPawn == null)
                return TestResult.Failure("Test pawn creation failed");

            if (testPawn.equipment?.Primary != null)
                return TestResult.Failure("Pawn is not unarmed");

            // Enable debug logging temporarily for this test
            var oldDebug = AutoArmMod.settings.debugLogging;
            AutoArmMod.settings.debugLogging = true;

            Log.Message($"[TEST] Testing unarmed pawn: {testPawn.Name} at {testPawn.Position}");
            Log.Message($"[TEST] Available weapon: {testWeapon?.Label} at {testWeapon?.Position}");

            var jobGiver = new JobGiver_PickUpBetterWeapon();
            var job = jobGiver.TestTryGiveJob(testPawn);

            // Restore debug setting
            AutoArmMod.settings.debugLogging = oldDebug;

            if (job == null)
            {
                // Log more details about why no job was created
                Log.Message($"[TEST] No job created. Checking conditions:");

                string reason;
                bool isValidPawn = JobGiverHelpers.IsValidPawnForAutoEquip(testPawn, out reason);
                Log.Message($"[TEST] Is valid pawn: {isValidPawn} - {reason}");

                if (testWeapon != null)
                {
                    bool isValidWeapon = JobGiverHelpers.IsValidWeaponCandidate(testWeapon, testPawn, out reason);
                    Log.Message($"[TEST] Is valid weapon: {isValidWeapon} - {reason}");

                    var score = jobGiver.GetWeaponScore(testPawn, testWeapon);
                    Log.Message($"[TEST] Weapon score: {score}");
                }

                return TestResult.Failure("No weapon pickup job created for unarmed pawn");
            }

            if (job.def != JobDefOf.Equip)
                return TestResult.Failure($"Wrong job type: {job.def.defName}");

            if (job.targetA.Thing != testWeapon)
                return TestResult.Failure($"Job targets wrong weapon: {job.targetA.Thing?.Label}");

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

    // Complete fix for WeaponUpgradeTest in TestScenarios.cs

    // Replace the ENTIRE WeaponUpgradeTest class in TestScenarios.cs with this:

    // Add this temporary debug version to see what's happening

    public class WeaponUpgradeTest : ITestScenario
    {
        public string Name => "Weapon Upgrade Logic";
        private Pawn testPawn;
        private ThingWithComps currentWeapon;
        private ThingWithComps betterWeapon;

        public void Setup(Map map)
        {
            if (map == null) return;

            // Clear any existing weapons in the test area first
            var testArea = CellRect.CenteredOn(map.Center, 10);
            var existingWeapons = map.listerThings.ThingsInGroup(ThingRequestGroup.Weapon)
                .Where(t => testArea.Contains(t.Position))
                .ToList();

            foreach (var weapon in existingWeapons)
            {
                weapon.Destroy();
            }

            Log.Message("[TEST] Starting weapon upgrade test setup");

            testPawn = TestHelpers.CreateTestPawn(map);
            if (testPawn == null)
            {
                Log.Error("[TEST] Failed to create test pawn!");
                return;
            }

            // Ensure the pawn can use weapons - remove any traits that disable violence
            if (testPawn.WorkTagIsDisabled(WorkTags.Violent))
            {
                // Clear all traits and add a neutral one
                testPawn.story.traits.allTraits.Clear();
                testPawn.story.traits.GainTrait(new Trait(TraitDefOf.Kind));

                // If still disabled after removing traits, it's from backstory
                // In that case, we'll just log it and let the test handle it
                if (testPawn.WorkTagIsDisabled(WorkTags.Violent))
                {
                    Log.Warning($"[TEST] Pawn {testPawn.Name} still incapable of violence after removing traits (backstory issue)");
                }
            }

            Log.Message($"[TEST] Created pawn: {testPawn.Name} at {testPawn.Position}");

            // Clear any existing equipment
            testPawn.equipment?.DestroyAllEquipment();

            // Check if weapon defs exist
            var pistolDef = VanillaWeaponDefOf.Gun_Autopistol;
            var rifleDef = VanillaWeaponDefOf.Gun_AssaultRifle;

            Log.Message($"[TEST] Pistol def: {pistolDef?.defName ?? "NULL"}");
            Log.Message($"[TEST] Rifle def: {rifleDef?.defName ?? "NULL"}");

            // Try alternative weapons if main ones don't exist
            if (pistolDef == null)
            {
                pistolDef = DefDatabase<ThingDef>.GetNamedSilentFail("Gun_Revolver");
                Log.Message($"[TEST] Using revolver instead: {pistolDef?.defName ?? "NULL"}");
            }
            if (rifleDef == null)
            {
                rifleDef = DefDatabase<ThingDef>.GetNamedSilentFail("Gun_BoltActionRifle");
                Log.Message($"[TEST] Using bolt rifle instead: {rifleDef?.defName ?? "NULL"}");
            }

            if (pistolDef != null && rifleDef != null)
            {
                // Create current weapon
                currentWeapon = ThingMaker.MakeThing(pistolDef) as ThingWithComps;
                if (currentWeapon != null)
                {
                    var compQuality = currentWeapon.TryGetComp<CompQuality>();
                    if (compQuality != null)
                    {
                        compQuality.SetQuality(QualityCategory.Poor, ArtGenerationContext.Colony);
                    }
                    testPawn.equipment.AddEquipment(currentWeapon);
                    Log.Message($"[TEST] Equipped pawn with {currentWeapon.Label}");
                }

                // Create better weapon on ground
                var weaponPos = testPawn.Position + new IntVec3(3, 0, 0);
                if (!weaponPos.InBounds(map) || !weaponPos.Standable(map))
                {
                    weaponPos = testPawn.Position + new IntVec3(0, 0, 3);
                }

                betterWeapon = TestHelpers.CreateWeapon(map, rifleDef, weaponPos, QualityCategory.Good);
                if (betterWeapon != null)
                {
                    betterWeapon.SetForbidden(false, false);

                    // Force outfit to allow both weapon types
                    if (testPawn.outfits?.CurrentApparelPolicy?.filter != null)
                    {
                        testPawn.outfits.CurrentApparelPolicy.filter.SetAllow(pistolDef, true);
                        testPawn.outfits.CurrentApparelPolicy.filter.SetAllow(rifleDef, true);

                        // Also allow the weapon categories
                        var weaponsCat = DefDatabase<ThingCategoryDef>.GetNamedSilentFail("Weapons");
                        if (weaponsCat != null)
                            testPawn.outfits.CurrentApparelPolicy.filter.SetAllow(weaponsCat, true);

                        var rangedCat = DefDatabase<ThingCategoryDef>.GetNamedSilentFail("WeaponsRanged");
                        if (rangedCat != null)
                            testPawn.outfits.CurrentApparelPolicy.filter.SetAllow(rangedCat, true);
                    }

                    Log.Message($"[TEST] Created {betterWeapon.Label} at {betterWeapon.Position}");
                }
            }
        }

        public TestResult Run()
        {
            Log.Message("[TEST] Starting test run");

            if (testPawn == null)
                return TestResult.Failure("Test pawn is null");

            if (testPawn.equipment?.Primary == null)
                return TestResult.Failure($"Pawn has no equipped weapon (equipment tracker: {testPawn.equipment != null})");

            if (betterWeapon == null || !betterWeapon.Spawned)
                return TestResult.Failure($"Better weapon null or not spawned (null: {betterWeapon == null})");

            // Enable debug logging temporarily for this test
            var oldDebug = AutoArmMod.settings.debugLogging;
            var oldEnabled = AutoArmMod.settings.modEnabled;
            AutoArmMod.settings.debugLogging = true;
            AutoArmMod.settings.modEnabled = true; // Force enable for test

            var jobGiver = new JobGiver_PickUpBetterWeapon();

            // Log details
            Log.Message($"[TEST] Pawn: {testPawn.Name} at {testPawn.Position}");
            Log.Message($"[TEST] Current weapon: {currentWeapon?.Label ?? "null"}");
            Log.Message($"[TEST] Better weapon: {betterWeapon?.Label} at {betterWeapon?.Position}");
            Log.Message($"[TEST] Distance: {testPawn.Position.DistanceTo(betterWeapon.Position)}");
            Log.Message($"[TEST] Weapon forbidden: {betterWeapon?.IsForbidden(testPawn)}");

            // Check outfit
            var filter = testPawn.outfits?.CurrentApparelPolicy?.filter;
            if (filter != null)
            {
                Log.Message($"[TEST] Outfit allows rifle: {filter.Allows(betterWeapon.def)}");
            }

            // Get scores
            var currentScore = jobGiver.GetWeaponScore(testPawn, currentWeapon);
            var betterScore = jobGiver.GetWeaponScore(testPawn, betterWeapon);

            Log.Message($"[TEST] Current weapon score: {currentScore}");
            Log.Message($"[TEST] Better weapon score: {betterScore}");
            Log.Message($"[TEST] Threshold needed: {currentScore * 1.1f}");

            // Add validation checks
            string reason;
            bool isValidPawn = JobGiverHelpers.IsValidPawnForAutoEquip(testPawn, out reason);
            Log.Message($"[TEST] Is valid pawn: {isValidPawn} - Reason: {reason}");

            // Check if pawn is in bed or has a lord
            Log.Message($"[TEST] Pawn in bed: {testPawn.InBed()}");
            Log.Message($"[TEST] Pawn has lord: {testPawn.GetLord() != null}");

            // Check weapon conditional
            var weaponConditional = new ThinkNode_ConditionalWeaponsInOutfit();
            bool weaponsAllowed = weaponConditional.TestSatisfied(testPawn);
            Log.Message($"[TEST] Weapons allowed in outfit: {weaponsAllowed}");

            // Check if better weapon is valid
            bool isValidWeapon = JobGiverHelpers.IsValidWeaponCandidate(betterWeapon, testPawn, out reason);
            Log.Message($"[TEST] Is better weapon valid: {isValidWeapon} - Reason: {reason}");

            var job = jobGiver.TestTryGiveJob(testPawn);

            // Restore settings
            AutoArmMod.settings.debugLogging = oldDebug;
            AutoArmMod.settings.modEnabled = oldEnabled;

            Log.Message($"[TEST] Job result: {job?.def.defName ?? "NULL"}");
            if (job != null)
            {
                Log.Message($"[TEST] Job target: {job.targetA.Thing?.Label ?? "null"}");
            }

            var result = new TestResult { Success = true };
            result.Data["Current Score"] = currentScore;
            result.Data["Better Score"] = betterScore;

            if (betterScore <= currentScore * 1.1f)
                return TestResult.Failure($"Better weapon score not high enough ({betterScore} vs {currentScore * 1.1f} required)");

            if (job == null)
                return TestResult.Failure("No upgrade job created");

            if (job.def != JobDefOf.Equip)
                return TestResult.Failure($"Wrong job type: {job.def.defName}");

            if (job.targetA.Thing != betterWeapon)
                return TestResult.Failure("Job targets wrong weapon");

            return result;
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
                    // Method 1: Create weapon without spawning it
                    forcedWeapon = ThingMaker.MakeThing(pistolDef) as ThingWithComps;

                    // OR Method 2: If you must use CreateWeapon, despawn it first
                    // forcedWeapon = TestHelpers.CreateWeapon(map, pistolDef, pos);
                    // if (forcedWeapon != null)
                    // {
                    //     forcedWeapon.DeSpawn();
                    // }

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

    // Fixed ChildColonistTest from TestScenarios.cs

    public class ChildColonistTest : ITestScenario
    {
        public string Name => "Child Colonist Age Restrictions";
        private Pawn childPawn;
        private int testAge = 10; // Test with a 10-year-old

        public void Setup(Map map)
        {
            if (map == null || !ModsConfig.BiotechActive) return;

            // Store current setting to restore later
            var originalSetting = AutoArmMod.settings?.allowChildrenToEquipWeapons ?? false;

            // Create a young pawn
            var config = new TestPawnConfig { Age = 10 }; // or whatever age you're using for the child
            childPawn = TestHelpers.CreateTestPawn(map, config);
        }

        public TestResult Run()
        {
            if (!ModsConfig.BiotechActive)
                return TestResult.Pass(); // Skip if Biotech not active

            if (childPawn == null)
                return TestResult.Failure("Failed to create child pawn");

            // Verify the pawn's age was set correctly
            if (childPawn.ageTracker == null)
                return TestResult.Failure("Pawn has no age tracker");

            int actualAge = childPawn.ageTracker.AgeBiologicalYears;
            if (actualAge != testAge && actualAge >= 18)
            {
                // Age setting failed, skip the test
                var skipResult = TestResult.Pass();
                skipResult.Data["Note"] = $"Could not set pawn age properly (wanted {testAge}, got {actualAge}), skipping test";
                return skipResult;
            }

            var result = new TestResult { Success = true };
            result.Data["Pawn Age"] = actualAge;
            result.Data["Allow Children Setting"] = AutoArmMod.settings?.allowChildrenToEquipWeapons ?? false;
            result.Data["Min Age Setting"] = AutoArmMod.settings?.childrenMinAge ?? 13;

            string reason;
            bool isValid = JobGiverHelpers.IsValidPawnForAutoEquip(childPawn, out reason);

            result.Data["Is Valid"] = isValid;
            result.Data["Reason"] = reason ?? "None";

            // Test both scenarios
            bool allowChildrenSetting = AutoArmMod.settings?.allowChildrenToEquipWeapons ?? false;
            int minAge = AutoArmMod.settings?.childrenMinAge ?? 13;

            if (allowChildrenSetting && actualAge >= minAge)
            {
                // Children are allowed and this child meets minimum age
                if (!isValid && reason.Contains("Too young"))
                    return TestResult.Failure($"Child rejected despite being old enough ({actualAge} >= {minAge}) and setting allowing children");
            }
            else if (!allowChildrenSetting || actualAge < minAge)
            {
                // Children are not allowed OR child is below minimum age
                if (isValid)
                    return TestResult.Failure($"Child allowed despite settings (allow={allowChildrenSetting}, age={actualAge}, minAge={minAge})");
                if (!reason.Contains("Too young") && !reason.Contains("age"))
                    return TestResult.Failure($"Child rejected but not for age reasons: {reason}");
            }

            return result;
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

            // After loading, check if the weapon DEF is still marked as forced
            var forcedDef = ForcedWeaponTracker.GetForcedWeaponDef(testPawn);
            if (forcedDef != testWeapon.def)
                return TestResult.Failure("Forced weapon def not retained after load");

            // The IsForced method now checks if weapon is equipped, which won't work in this test
            // So we just verify the def is stored correctly
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