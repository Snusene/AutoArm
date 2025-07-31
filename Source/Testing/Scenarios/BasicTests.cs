using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace AutoArm.Testing.Scenarios
{
    public class UnarmedPawnTest : ITestScenario
    {
        public string Name => "Unarmed Pawn Weapon Acquisition";
        private Pawn testPawn;
        private ThingWithComps testWeapon;

        public void Setup(Map map)
        {
            if (map == null) return;

            // Clear all systems before test
            TestRunnerFix.ResetAllSystems();
            
            // Clear any existing weapons in the test area to prevent interference
            var testArea = CellRect.CenteredOn(map.Center, 20);
            var existingWeapons = map.listerThings.ThingsInGroup(ThingRequestGroup.Weapon)
                .Where(t => testArea.Contains(t.Position) && t.Spawned)
                .ToList();
            
            foreach (var weapon in existingWeapons)
            {
                weapon.DeSpawn();
            }
            
            // Force cache rebuild
            ImprovedWeaponCacheManager.InvalidateCache(map);

            testPawn = TestHelpers.CreateTestPawn(map);
            if (testPawn != null)
            {
                // Prepare pawn for testing
                TestRunnerFix.PreparePawnForTest(testPawn);
                testPawn.equipment?.DestroyAllEquipment();

                var weaponDef = VanillaWeaponDefOf.Gun_Autopistol;
                if (weaponDef != null)
                {
                    testWeapon = TestHelpers.CreateWeapon(map, weaponDef,
                        testPawn.Position + new IntVec3(3, 0, 0));

                    if (testWeapon != null)
                    {
                        var compQuality = testWeapon.TryGetComp<CompQuality>();
                        if (compQuality != null)
                        {
                            compQuality.SetQuality(QualityCategory.Excellent, ArtGenerationContext.Colony);
                        }

                        // Add to cache to ensure it can be found
                        ImprovedWeaponCacheManager.AddWeaponToCache(testWeapon);
                    }
                }
            }
        }

        public TestResult Run()
        {
            if (testPawn == null)
            {
                AutoArmDebug.LogError("[TEST] UnarmedPawnTest: Test pawn creation failed");
                return TestResult.Failure("Test pawn creation failed");
            }

            // Ensure pawn is ready for test
            TestRunnerFix.PreparePawnForTest(testPawn);

            if (testPawn.equipment?.Primary != null)
            {
                AutoArmDebug.LogError($"[TEST] UnarmedPawnTest: Pawn is not unarmed - expected: null weapon, got: {testPawn.equipment.Primary.Label}");
                return TestResult.Failure("Pawn is not unarmed");
            }

            var jobGiver = new JobGiver_PickUpBetterWeapon();
            var job = jobGiver.TestTryGiveJob(testPawn);

            if (job == null)
            {
                string reason;
                bool isValidPawn = JobGiverHelpers.IsValidPawnForAutoEquip(testPawn, out reason);

                AutoArmDebug.LogError($"[TEST] UnarmedPawnTest: No weapon pickup job created - pawn valid: {isValidPawn}, reason: {reason}");

                if (testWeapon != null)
                {
                    bool isValidWeapon = JobGiverHelpers.IsValidWeaponCandidate(testWeapon, testPawn, out reason);
                    var score = WeaponScoreCache.GetCachedScore(testPawn, testWeapon);
                    AutoArmDebug.LogError($"[TEST] UnarmedPawnTest: Available weapon {testWeapon.Label} - valid: {isValidWeapon}, score: {score}, reason: {reason}");
                }

                return TestResult.Failure("No weapon pickup job created for unarmed pawn");
            }

            if (job.def != JobDefOf.Equip)
            {
                AutoArmDebug.LogError($"[TEST] UnarmedPawnTest: Wrong job type - expected: Equip, got: {job.def.defName}");
                return TestResult.Failure($"Wrong job type: {job.def.defName}");
            }

            if (job.targetA.Thing != testWeapon)
            {
                AutoArmDebug.LogError($"[TEST] UnarmedPawnTest: Job targets wrong weapon - expected: {testWeapon?.Label}, got: {job.targetA.Thing?.Label}");
                return TestResult.Failure($"Job targets wrong weapon: {job.targetA.Thing?.Label}");
            }

            return TestResult.Pass();
        }

        public void Cleanup()
        {
            // Destroy weapon first to avoid container conflicts
            if (testWeapon != null && !testWeapon.Destroyed)
            {
                testWeapon.Destroy();
                testWeapon = null;
            }
            if (testPawn != null && !testPawn.Destroyed)
            {
                // Stop all jobs first
                testPawn.jobs?.StopAll();
                // Clear equipment tracker references
                testPawn.equipment?.DestroyAllEquipment();
                // Destroy the pawn
                testPawn.Destroy();
                testPawn = null;
            }
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

            // Clear all systems before test
            TestRunnerFix.ResetAllSystems();

            var testArea = CellRect.CenteredOn(map.Center, 10);
            var existingWeapons = map.listerThings.ThingsInGroup(ThingRequestGroup.Weapon)
                .Where(t => testArea.Contains(t.Position))
                .ToList();

            foreach (var weapon in existingWeapons)
            {
                weapon.Destroy();
            }

            AutoArmDebug.Log("[TEST] Starting weapon upgrade test setup");

            testPawn = TestHelpers.CreateTestPawn(map);
            if (testPawn == null)
            {
                Log.Error("[TEST] Failed to create test pawn!");
                return;
            }

            AutoArmDebug.Log($"[TEST] Created pawn: {testPawn.Name} at {testPawn.Position}");

            // Prepare pawn for testing
            TestRunnerFix.PreparePawnForTest(testPawn);
            testPawn.equipment?.DestroyAllEquipment();

            var pistolDef = VanillaWeaponDefOf.Gun_Autopistol;
            var rifleDef = VanillaWeaponDefOf.Gun_AssaultRifle;

            AutoArmDebug.Log($"[TEST] Pistol def: {pistolDef?.defName ?? "NULL"}");
            AutoArmDebug.Log($"[TEST] Rifle def: {rifleDef?.defName ?? "NULL"}");

            if (pistolDef == null)
            {
                pistolDef = DefDatabase<ThingDef>.GetNamedSilentFail("Gun_Revolver");
                AutoArmDebug.Log($"[TEST] Using revolver instead: {pistolDef?.defName ?? "NULL"}");
            }
            if (rifleDef == null)
            {
                rifleDef = DefDatabase<ThingDef>.GetNamedSilentFail("Gun_BoltActionRifle");
                AutoArmDebug.Log($"[TEST] Using bolt rifle instead: {rifleDef?.defName ?? "NULL"}");
            }

            if (pistolDef != null && rifleDef != null)
            {
                currentWeapon = ThingMaker.MakeThing(pistolDef) as ThingWithComps;
                if (currentWeapon != null)
                {
                    var compQuality = currentWeapon.TryGetComp<CompQuality>();
                    if (compQuality != null)
                    {
                        compQuality.SetQuality(QualityCategory.Poor, ArtGenerationContext.Colony);
                    }

                    if (testPawn.equipment != null)
                    {
                        // Ensure pawn is unarmed first
                        testPawn.equipment.DestroyAllEquipment();

                        testPawn.equipment.AddEquipment(currentWeapon);
                    }
                    AutoArmDebug.Log($"[TEST] Equipped pawn with {currentWeapon.Label}");
                }

                var weaponPos = testPawn.Position + new IntVec3(3, 0, 0);
                if (!weaponPos.InBounds(map) || !weaponPos.Standable(map))
                {
                    weaponPos = testPawn.Position + new IntVec3(0, 0, 3);
                }

                betterWeapon = ThingMaker.MakeThing(rifleDef) as ThingWithComps;
                if (betterWeapon != null)
                {
                    var compQuality = betterWeapon.TryGetComp<CompQuality>();
                    if (compQuality != null)
                    {
                        compQuality.SetQuality(QualityCategory.Good, ArtGenerationContext.Colony);
                    }

                    GenSpawn.Spawn(betterWeapon, weaponPos, map);
                    betterWeapon.SetForbidden(false, false);

                    if (testPawn.outfits?.CurrentApparelPolicy?.filter != null)
                    {
                        testPawn.outfits.CurrentApparelPolicy.filter.SetAllow(pistolDef, true);
                        testPawn.outfits.CurrentApparelPolicy.filter.SetAllow(rifleDef, true);

                        var weaponsCat = DefDatabase<ThingCategoryDef>.GetNamedSilentFail("Weapons");
                        if (weaponsCat != null)
                            testPawn.outfits.CurrentApparelPolicy.filter.SetAllow(weaponsCat, true);

                        var rangedCat = DefDatabase<ThingCategoryDef>.GetNamedSilentFail("WeaponsRanged");
                        if (rangedCat != null)
                            testPawn.outfits.CurrentApparelPolicy.filter.SetAllow(rangedCat, true);
                    }

                    AutoArmDebug.Log($"[TEST] Created {betterWeapon.Label} at {betterWeapon.Position}");

                    // Force cache rebuild after spawning the weapon
                    ImprovedWeaponCacheManager.InvalidateCache(map);

                    // Manually add the weapon to cache to ensure it's registered
                    ImprovedWeaponCacheManager.AddWeaponToCache(betterWeapon);

                    var nearbyWeapons = ImprovedWeaponCacheManager.GetWeaponsNear(map, testPawn.Position, 50f);
                    AutoArmDebug.Log($"[TEST] Weapons in cache after rebuild: {nearbyWeapons.Count()}");
                    foreach (var w in nearbyWeapons)
                    {
                        AutoArmDebug.Log($"[TEST] - {w.Label} at {w.Position}, destroyed: {w.Destroyed}, spawned: {w.Spawned}");
                    }
                }
            }
        }

        public TestResult Run()
        {
            if (testPawn == null)
            {
                AutoArmDebug.LogError("[TEST] WeaponUpgradeTest: Test pawn is null");
                return TestResult.Failure("Test pawn is null");
            }

            // Ensure pawn is ready for test
            TestRunnerFix.PreparePawnForTest(testPawn);

            if (testPawn.equipment?.Primary == null)
            {
                AutoArmDebug.LogError($"[TEST] WeaponUpgradeTest: Pawn has no equipped weapon - equipment tracker exists: {testPawn.equipment != null}");
                return TestResult.Failure($"Pawn has no equipped weapon (equipment tracker: {testPawn.equipment != null})");
            }

            if (betterWeapon == null || !betterWeapon.Spawned)
            {
                AutoArmDebug.LogError($"[TEST] WeaponUpgradeTest: Better weapon null or not spawned - null: {betterWeapon == null}, spawned: {betterWeapon?.Spawned ?? false}");
                return TestResult.Failure($"Better weapon null or not spawned (null: {betterWeapon == null})");
            }

            var jobGiver = new JobGiver_PickUpBetterWeapon();

            var currentScore = WeaponScoreCache.GetCachedScore(testPawn, currentWeapon);
            var betterScore = WeaponScoreCache.GetCachedScore(testPawn, betterWeapon);

            var result = new TestResult { Success = true };
            result.Data["Current Score"] = currentScore;
            result.Data["Better Score"] = betterScore;

            if (betterScore <= currentScore * 1.1f)
            {
                AutoArmDebug.LogError($"[TEST] WeaponUpgradeTest: Better weapon score not high enough - expected: >{currentScore * 1.1f}, got: {betterScore} (current: {currentScore})");
                return TestResult.Failure($"Better weapon score not high enough ({betterScore} vs {currentScore * 1.1f} required)");
            }

            var job = jobGiver.TestTryGiveJob(testPawn);

            if (job == null)
            {
                string reason;
                bool isValidPawn = JobGiverHelpers.IsValidPawnForAutoEquip(testPawn, out reason);
                bool isValidWeapon = JobGiverHelpers.IsValidWeaponCandidate(betterWeapon, testPawn, out reason);

                AutoArmDebug.LogError($"[TEST] WeaponUpgradeTest: No upgrade job created - pawn valid: {isValidPawn}, weapon valid: {isValidWeapon}, reason: {reason}");
                AutoArmDebug.LogError($"[TEST] WeaponUpgradeTest: Current weapon: {currentWeapon?.Label}, Better weapon: {betterWeapon?.Label} at distance {testPawn.Position.DistanceTo(betterWeapon.Position)}");

                return TestResult.Failure("No upgrade job created");
            }

            if (job.def != JobDefOf.Equip)
            {
                AutoArmDebug.LogError($"[TEST] WeaponUpgradeTest: Wrong job type - expected: Equip, got: {job.def.defName}");
                return TestResult.Failure($"Wrong job type: {job.def.defName}");
            }

            if (job.targetA.Thing != betterWeapon)
            {
                AutoArmDebug.LogError($"[TEST] WeaponUpgradeTest: Job targets wrong weapon - expected: {betterWeapon?.Label}, got: {job.targetA.Thing?.Label}");
                return TestResult.Failure("Job targets wrong weapon");
            }

            return result;
        }

        public void Cleanup()
        {
            // Clean up weapons first to avoid container conflicts
            // Don't destroy equipped weapons directly - let the pawn destruction handle it
            if (betterWeapon != null && !betterWeapon.Destroyed && betterWeapon.ParentHolder is Map)
            {
                betterWeapon.Destroy();
                betterWeapon = null;
            }

            // Destroy pawn (which will also destroy their equipped weapon)
            if (testPawn != null && !testPawn.Destroyed)
            {
                // Stop all jobs first
                testPawn.jobs?.StopAll();
                // Clear equipment tracker references
                testPawn.equipment?.DestroyAllEquipment();
                // Destroy the pawn
                testPawn.Destroy();
                testPawn = null;
            }

            // Only destroy current weapon if it somehow wasn't destroyed with the pawn
            if (currentWeapon != null && !currentWeapon.Destroyed)
            {
                currentWeapon.Destroy();
                currentWeapon = null;
            }
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
                {
                    var rifle = TestHelpers.CreateWeapon(map, rifleDef, pos + new IntVec3(2, 0, 0));
                    if (rifle != null)
                    {
                        weapons.Add(rifle);
                        ImprovedWeaponCacheManager.AddWeaponToCache(rifle);
                    }
                }
                if (swordDef != null)
                {
                    var sword = TestHelpers.CreateWeapon(map, swordDef, pos + new IntVec3(-2, 0, 0));
                    if (sword != null)
                    {
                        weapons.Add(sword);
                        ImprovedWeaponCacheManager.AddWeaponToCache(sword);
                    }
                }
            }
        }

        public TestResult Run()
        {
            if (testPawn == null)
            {
                AutoArmDebug.LogError("[TEST] OutfitFilterTest: Test pawn creation failed");
                return TestResult.Failure("Test pawn creation failed");
            }

            var jobGiver = new JobGiver_PickUpBetterWeapon();
            var job = jobGiver.TestTryGiveJob(testPawn);

            // This test validates that outfit filters are respected
            // If a job is created, it should be for an allowed weapon
            if (job != null && job.targetA.Thing is ThingWithComps weapon)
            {
                var filter = testPawn.outfits?.CurrentApparelPolicy?.filter;
                if (filter != null && !filter.Allows(weapon.def))
                {
                    AutoArmDebug.LogError($"[TEST] OutfitFilterTest: Job created for disallowed weapon - weapon: {weapon.Label}, allowed by filter: false");
                    return TestResult.Failure($"Job created for weapon not allowed by outfit filter: {weapon.Label}");
                }
            }

            return TestResult.Pass();
        }

        public void Cleanup()
        {
            // Destroy weapons first to avoid container conflicts
            foreach (var weapon in weapons)
            {
                if (weapon != null && !weapon.Destroyed)
                {
                    weapon.Destroy();
                }
            }
            weapons.Clear();

            if (testPawn != null && !testPawn.Destroyed)
            {
                testPawn.Destroy();
            }
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
                    forcedWeapon = ThingMaker.MakeThing(pistolDef) as ThingWithComps;

                    if (forcedWeapon != null)
                    {
                        // Ensure pawn is unarmed first
                        testPawn.equipment?.DestroyAllEquipment();

                        testPawn.equipment?.AddEquipment(forcedWeapon);
                        ForcedWeaponHelper.SetForced(testPawn, forcedWeapon);
                    }

                    betterWeapon = TestHelpers.CreateWeapon(map, rifleDef, pos + new IntVec3(3, 0, 0));
                    if (betterWeapon != null)
                    {
                        ImprovedWeaponCacheManager.AddWeaponToCache(betterWeapon);
                    }
                }
            }
        }

        public TestResult Run()
        {
            if (testPawn == null || forcedWeapon == null)
            {
                AutoArmDebug.LogError($"[TEST] ForcedWeaponTest: Test setup failed - pawn null: {testPawn == null}, weapon null: {forcedWeapon == null}");
                return TestResult.Failure("Test setup failed");
            }

            if (!ForcedWeaponHelper.IsForced(testPawn, forcedWeapon))
            {
                AutoArmDebug.LogError($"[TEST] ForcedWeaponTest: Weapon not marked as forced - weapon: {forcedWeapon.Label}");
                return TestResult.Failure("Weapon not marked as forced");
            }

            var jobGiver = new JobGiver_PickUpBetterWeapon();
            var job = jobGiver.TestTryGiveJob(testPawn);

            if (job != null)
            {
                AutoArmDebug.LogError($"[TEST] ForcedWeaponTest: Pawn tried to replace forced weapon - forced weapon: {forcedWeapon.Label}, target weapon: {job.targetA.Thing?.Label}");
                return TestResult.Failure("Pawn tried to replace forced weapon");
            }

            return TestResult.Pass();
        }

        public void Cleanup()
        {
            ForcedWeaponHelper.ClearForced(testPawn);

            // Destroy map weapons first
            if (betterWeapon != null && !betterWeapon.Destroyed && betterWeapon.ParentHolder is Map)
            {
                betterWeapon.Destroy();
            }

            // Destroy pawn (which will also destroy their equipped weapon)
            if (testPawn != null && !testPawn.Destroyed)
            {
                testPawn.Destroy();
            }

            // Only destroy forced weapon if it somehow wasn't destroyed with the pawn
            if (forcedWeapon != null && !forcedWeapon.Destroyed)
            {
                forcedWeapon.Destroy();
            }
        }
    }
}