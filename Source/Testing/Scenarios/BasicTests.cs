// AutoArm RimWorld 1.5+ mod - automatic weapon management
// This file: Basic test scenarios for core weapon equip functionality
// Uses TestHelpers and JobGiver_PickUpBetterWeapon for validation

using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using AutoArm.Testing.Helpers;

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

            testPawn = TestHelpers.CreateTestPawn(map);
            if (testPawn != null)
            {
                // Prepare pawn for testing
                TestRunnerFix.PreparePawnForTest(testPawn);
                testPawn.equipment?.DestroyAllEquipment();
                
                // CRITICAL: Set up outfit to allow all weapons
                if (testPawn.outfits != null)
                {
                    var allWeaponsOutfit = Current.Game.outfitDatabase.AllOutfits
                        .FirstOrDefault(o => o.label == "Anything");
                    
                    if (allWeaponsOutfit == null)
                    {
                        allWeaponsOutfit = Current.Game.outfitDatabase.MakeNewOutfit();
                        allWeaponsOutfit.label = "Test - All Weapons";
                        allWeaponsOutfit.filter.SetAllow(ThingCategoryDefOf.Weapons, true);
                    }
                    
                    testPawn.outfits.CurrentApparelPolicy = allWeaponsOutfit;
                }

                var weaponDef = VanillaWeaponDefOf.Gun_Autopistol;
                if (weaponDef != null)
                {
                    // Ensure weapon spawns very close to pawn for reachability
                    IntVec3 weaponPos = testPawn.Position + new IntVec3(1, 0, 0);
                    
                    // Clear fog around test area to ensure pathfinding works
                    var fogGrid = map.fogGrid;
                    if (fogGrid != null)
                    {
                        foreach (var cell in GenRadial.RadialCellsAround(testPawn.Position, 10, true))
                        {
                            if (cell.InBounds(map))
                            {
                                fogGrid.Unfog(cell);
                            }
                        }
                    }
                    
                    testWeapon = TestHelpers.CreateWeapon(map, weaponDef, weaponPos);

                    if (testWeapon != null)
                    {
                        var compQuality = testWeapon.TryGetComp<CompQuality>();
                        if (compQuality != null)
                        {
                            compQuality.SetQuality(QualityCategory.Excellent, ArtGenerationContext.Colony);
                        }
                        
                        // Ensure weapon is not forbidden
                        testWeapon.SetForbidden(false, false);
                        
                        // Force cache rebuild AFTER spawning weapon
                        ImprovedWeaponCacheManager.InvalidateCache(map);
                        
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
                AutoArmLogger.LogError("[TEST] UnarmedPawnTest: Test pawn creation failed");
                return TestResult.Failure("Test pawn creation failed");
            }

            // Ensure pawn is ready for test and clear all cooldowns again
            TestRunnerFix.PreparePawnForTest(testPawn);
            TestRunnerFix.ClearAllCooldownsForPawn(testPawn);
            
            // Double-check mod is enabled
            if (AutoArmMod.settings?.modEnabled != true)
            {
                AutoArmLogger.LogError("[TEST] UnarmedPawnTest: Mod is not enabled!");
                return TestResult.Failure("Mod is not enabled");
            }

            if (testPawn.equipment?.Primary != null)
            {
                AutoArmLogger.LogError($"[TEST] UnarmedPawnTest: Pawn is not unarmed - expected: null weapon, got: {testPawn.equipment.Primary.Label}");
                return TestResult.Failure("Pawn is not unarmed");
            }

            var jobGiver = new JobGiver_PickUpBetterWeapon();
            var job = jobGiver.TestTryGiveJob(testPawn);

            if (job == null)
            {
                string reason;
                bool isValidPawn = JobGiverHelpers.IsValidPawnForAutoEquip(testPawn, out reason);

                var failureResult = TestResult.Failure("No weapon pickup job created for unarmed pawn");
                failureResult.Data["Error"] = "Failed to create weapon acquisition job";
                failureResult.Data["PawnValid"] = isValidPawn;
                failureResult.Data["InvalidReason"] = reason;
                failureResult.Data["PawnDrafted"] = testPawn.Drafted;
                failureResult.Data["PawnInBed"] = testPawn.InBed();
                failureResult.Data["PawnDowned"] = testPawn.Downed;
                failureResult.Data["CurrentJob"] = testPawn.CurJobDef?.defName ?? "none";

                if (testWeapon != null)
                {
                    bool isValidWeapon = JobGiverHelpers.IsValidWeaponCandidate(testWeapon, testPawn, out reason);
                    var score = WeaponScoreCache.GetCachedScore(testPawn, testWeapon);
                    failureResult.Data["WeaponValid"] = isValidWeapon;
                    failureResult.Data["WeaponScore"] = score;
                    failureResult.Data["WeaponInvalidReason"] = reason;
                    
                    // Check if weapon is in cache
                    var cachedWeapons = ImprovedWeaponCacheManager.GetWeaponsNear(testPawn.Map, testPawn.Position, 60f);
                    bool inCache = cachedWeapons.Contains(testWeapon);
                    failureResult.Data["WeaponInCache"] = inCache;
                    failureResult.Data["TotalCachedWeapons"] = cachedWeapons.Count();
                    
                    // Check outfit filter
                    var outfitAllows = testPawn.outfits?.CurrentApparelPolicy?.filter?.Allows(testWeapon) ?? false;
                    failureResult.Data["OutfitAllowsWeapon"] = outfitAllows;
                }

                AutoArmLogger.LogError($"[TEST] UnarmedPawnTest: No weapon pickup job created - pawn valid: {isValidPawn}, reason: {reason}");
                return failureResult;
            }

            if (job.def != JobDefOf.Equip)
            {
                var wrongJobResult = TestResult.Failure($"Wrong job type: {job.def.defName}");
                wrongJobResult.Data["Error"] = "Job created with wrong type";
            wrongJobResult.Data["ExpectedJob"] = JobDefOf.Equip.defName;
            wrongJobResult.Data["ActualJob"] = job.def.defName;
            AutoArmLogger.LogError($"[TEST] UnarmedPawnTest: Wrong job type - expected: Equip, got: {job.def.defName}");
            return wrongJobResult;
            }

            if (job.targetA.Thing != testWeapon)
            {
                var wrongTargetResult = TestResult.Failure("");
                wrongTargetResult.Data["Error"] = "Job targets incorrect weapon";
                wrongTargetResult.Data["ActualWeapon"] = job.targetA.Thing?.Label;
                wrongTargetResult.Data["ExpectedWeapon"] = testWeapon?.Label;
                AutoArmLogger.LogError($"[TEST] UnarmedPawnTest: Job targets wrong weapon - expected: {testWeapon?.Label}, got: {job.targetA.Thing?.Label}");
                return wrongTargetResult;
            }

            return TestResult.Pass();
        }

        public void Cleanup()
        {
            // Use safe cleanup methods
            TestHelpers.SafeDestroyWeapon(testWeapon);
            testWeapon = null;
            
            SafeTestCleanup.SafeDestroyPawn(testPawn);
            testPawn = null;
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
                SafeTestCleanup.SafeDestroyWeapon(weapon as ThingWithComps);
            }

            AutoArmLogger.Log("[TEST] Starting weapon upgrade test setup");

            testPawn = TestHelpers.CreateTestPawn(map);
            if (testPawn == null)
            {
                Log.Error("[TEST] Failed to create test pawn!");
                return;
            }

            AutoArmLogger.Log($"[TEST] Created pawn: {testPawn.Name} at {testPawn.Position}");

            // Prepare pawn for testing
            TestRunnerFix.PreparePawnForTest(testPawn);
            testPawn.equipment?.DestroyAllEquipment();
            
            // CRITICAL: Set up outfit first to allow all weapons
            if (testPawn.outfits != null)
            {
                var allWeaponsOutfit = Current.Game.outfitDatabase.AllOutfits
                    .FirstOrDefault(o => o.label == "Anything");
                
                if (allWeaponsOutfit == null)
                {
                    allWeaponsOutfit = Current.Game.outfitDatabase.MakeNewOutfit();
                    allWeaponsOutfit.label = "Test - All Weapons";
                }
                
                // Ensure all weapon categories are allowed
                allWeaponsOutfit.filter.SetAllow(ThingCategoryDefOf.Weapons, true);
                var rangedCat = DefDatabase<ThingCategoryDef>.GetNamedSilentFail("WeaponsRanged");
                if (rangedCat != null)
                    allWeaponsOutfit.filter.SetAllow(rangedCat, true);
                    
                testPawn.outfits.CurrentApparelPolicy = allWeaponsOutfit;
            }

            var pistolDef = VanillaWeaponDefOf.Gun_Autopistol;
            var rifleDef = VanillaWeaponDefOf.Gun_AssaultRifle;

            AutoArmLogger.Log($"[TEST] Pistol def: {pistolDef?.defName ?? "NULL"}");
            AutoArmLogger.Log($"[TEST] Rifle def: {rifleDef?.defName ?? "NULL"}");

            if (pistolDef == null)
            {
                pistolDef = DefDatabase<ThingDef>.GetNamedSilentFail("Gun_Revolver");
                AutoArmLogger.Log($"[TEST] Using revolver instead: {pistolDef?.defName ?? "NULL"}");
            }
            if (rifleDef == null)
            {
                rifleDef = DefDatabase<ThingDef>.GetNamedSilentFail("Gun_BoltActionRifle");
                AutoArmLogger.Log($"[TEST] Using bolt rifle instead: {rifleDef?.defName ?? "NULL"}");
            }

            if (pistolDef != null && rifleDef != null)
            {
                currentWeapon = SafeTestCleanup.SafeCreateWeapon(pistolDef, null, QualityCategory.Poor);
                if (currentWeapon != null)
                {
                    // Use safe equip method that handles container issues
                    SafeTestCleanup.SafeEquipWeapon(testPawn, currentWeapon);
                    AutoArmLogger.Log($"[TEST] Equipped pawn with {currentWeapon.Label}");
                }

                var weaponPos = testPawn.Position + new IntVec3(2, 0, 0);
                if (!weaponPos.InBounds(map) || !weaponPos.Standable(map))
                {
                    weaponPos = testPawn.Position + new IntVec3(0, 0, 2);
                }
                
                // Clear fog around test area
                var fogGrid = map.fogGrid;
                if (fogGrid != null)
                {
                    foreach (var cell in GenRadial.RadialCellsAround(testPawn.Position, 10, true))
                    {
                        if (cell.InBounds(map))
                        {
                            fogGrid.Unfog(cell);
                        }
                    }
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

                    AutoArmLogger.Log($"[TEST] Created {betterWeapon.Label} at {betterWeapon.Position}");

                    // Force cache rebuild after spawning the weapon
                    ImprovedWeaponCacheManager.InvalidateCache(map);

                    // Manually add the weapon to cache to ensure it's registered
                    ImprovedWeaponCacheManager.AddWeaponToCache(betterWeapon);

                    var nearbyWeapons = ImprovedWeaponCacheManager.GetWeaponsNear(map, testPawn.Position, 50f);
                    AutoArmLogger.Log($"[TEST] Weapons in cache after rebuild: {nearbyWeapons.Count()}");
                    foreach (var w in nearbyWeapons)
                    {
                        AutoArmLogger.Log($"[TEST] - {w.Label} at {w.Position}, destroyed: {w.Destroyed}, spawned: {w.Spawned}");
                    }
                }
            }
        }

        public TestResult Run()
        {
            if (testPawn == null)
            {
                AutoArmLogger.LogError("[TEST] WeaponUpgradeTest: Test pawn is null");
                return TestResult.Failure("Test pawn is null");
            }

            // Ensure pawn is ready for test and clear cooldowns
            TestRunnerFix.PreparePawnForTest(testPawn);
            TestRunnerFix.ClearAllCooldownsForPawn(testPawn);
            
            // Clear any score caches to ensure fresh calculations
            WeaponScoreCache.ClearAllCaches();
            
            // Double-check mod is enabled
            if (AutoArmMod.settings?.modEnabled != true)
            {
                AutoArmLogger.LogError("[TEST] WeaponUpgradeTest: Mod is not enabled!");
                return TestResult.Failure("Mod is not enabled");
            }

            if (testPawn.equipment?.Primary == null)
            {
                AutoArmLogger.LogError($"[TEST] WeaponUpgradeTest: Pawn has no equipped weapon - equipment tracker exists: {testPawn.equipment != null}");
                return TestResult.Failure($"Pawn has no equipped weapon (equipment tracker: {testPawn.equipment != null})");
            }

            if (betterWeapon == null || !betterWeapon.Spawned)
            {
                AutoArmLogger.LogError($"[TEST] WeaponUpgradeTest: Better weapon null or not spawned - null: {betterWeapon == null}, spawned: {betterWeapon?.Spawned ?? false}");
                return TestResult.Failure($"Better weapon null or not spawned (null: {betterWeapon == null})");
            }

            var jobGiver = new JobGiver_PickUpBetterWeapon();

            var currentScore = WeaponScoreCache.GetCachedScore(testPawn, currentWeapon);
            var betterScore = WeaponScoreCache.GetCachedScore(testPawn, betterWeapon);

            var result = new TestResult { Success = true };
            result.Data["Current Score"] = currentScore;
            result.Data["Better Score"] = betterScore;
            result.Data["Score Threshold"] = currentScore * 1.05f; // JobGiver uses 5% threshold

            if (betterScore <= currentScore * 1.05f) // Changed from 1.1f to match actual threshold
            {
                AutoArmLogger.LogError($"[TEST] WeaponUpgradeTest: Better weapon score not high enough - expected: >{currentScore * 1.05f}, got: {betterScore} (current: {currentScore})");
                return TestResult.Failure($"Better weapon score not high enough ({betterScore} vs {currentScore * 1.05f} required)");
            }

            var job = jobGiver.TestTryGiveJob(testPawn);

            if (job == null)
            {
                string reason;
                bool isValidPawn = JobGiverHelpers.IsValidPawnForAutoEquip(testPawn, out reason);
                bool isValidWeapon = JobGiverHelpers.IsValidWeaponCandidate(betterWeapon, testPawn, out reason);

                var failureResult = TestResult.Failure("No upgrade job created");
                failureResult.Data["Error"] = "Failed to create weapon upgrade job";
                failureResult.Data["PawnValid"] = isValidPawn;
                failureResult.Data["WeaponValid"] = isValidWeapon;
                failureResult.Data["InvalidReason"] = reason;
                failureResult.Data["CurrentWeapon"] = currentWeapon?.Label;
                failureResult.Data["BetterWeapon"] = betterWeapon?.Label;
                failureResult.Data["Distance"] = testPawn.Position.DistanceTo(betterWeapon.Position);
                
                // Additional debug info
                var outfitAllows = testPawn.outfits?.CurrentApparelPolicy?.filter?.Allows(betterWeapon) ?? false;
                failureResult.Data["OutfitAllowsBetterWeapon"] = outfitAllows;
                
                // Check if better weapon is in cache
                var cachedWeapons = ImprovedWeaponCacheManager.GetWeaponsNear(testPawn.Map, testPawn.Position, 60f);
                bool inCache = cachedWeapons.Contains(betterWeapon);
                failureResult.Data["BetterWeaponInCache"] = inCache;
                failureResult.Data["TotalCachedWeapons"] = cachedWeapons.Count();

                AutoArmLogger.LogError($"[TEST] WeaponUpgradeTest: No upgrade job created - pawn valid: {isValidPawn}, weapon valid: {isValidWeapon}, reason: {reason}");
                return failureResult;
            }

            if (job.def != JobDefOf.Equip)
            {
                var wrongJobResult = TestResult.Failure($"Wrong job type: {job.def.defName}");
                wrongJobResult.Data["Error"] = "Job created with wrong type";
            wrongJobResult.Data["ExpectedJob"] = JobDefOf.Equip.defName;
            wrongJobResult.Data["ActualJob"] = job.def.defName;
            AutoArmLogger.LogError($"[TEST] WeaponUpgradeTest: Wrong job type - expected: Equip, got: {job.def.defName}");
            return wrongJobResult;
            }

            if (job.targetA.Thing != betterWeapon)
            {
                var wrongTargetResult = TestResult.Failure("");
                wrongTargetResult.Data["Error"] = "Job targets incorrect weapon for upgrade";
                wrongTargetResult.Data["ActualWeapon"] = job.targetA.Thing?.Label;
                wrongTargetResult.Data["ExpectedWeapon"] = betterWeapon?.Label;
                AutoArmLogger.LogError($"[TEST] WeaponUpgradeTest: Job targets wrong weapon - expected: {betterWeapon?.Label}, got: {job.targetA.Thing?.Label}");
                return wrongTargetResult;
            }

            return result;
        }

        public void Cleanup()
        {
            // Use safe cleanup methods
            TestHelpers.SafeDestroyWeapon(betterWeapon);
            betterWeapon = null;
            
            // Pawn cleanup will handle equipped weapon
            SafeTestCleanup.SafeDestroyPawn(testPawn);
            testPawn = null;
            
            // currentWeapon should be destroyed with pawn
            currentWeapon = null;
        }
    }

    public class OutfitFilterTest : ITestScenario
    {
        public string Name => "Outfit Filter Weapon Restrictions (Quality & HP)";
        private Pawn testPawn;
        private List<ThingWithComps> weapons = new List<ThingWithComps>();
        private ApparelPolicy testOutfit;

        public void Setup(Map map)
        {
            if (map == null) return;
            testPawn = TestHelpers.CreateTestPawn(map);

            if (testPawn == null) return;

            // Clear fog for testing
            var fogGrid = map.fogGrid;
            if (fogGrid != null)
            {
                foreach (var cell in GenRadial.RadialCellsAround(testPawn.Position, 15, true))
                {
                    if (cell.InBounds(map))
                        fogGrid.Unfog(cell);
                }
            }

            // Create a custom outfit with quality and HP restrictions
            testOutfit = Current.Game.outfitDatabase.MakeNewOutfit();
            testOutfit.label = "Test - Quality & HP Restricted";
            
            // Allow all weapon types by default
            testOutfit.filter.SetAllow(ThingCategoryDefOf.Weapons, true);
            
            // Set quality range to Good or better
            testOutfit.filter.AllowedQualityLevels = new QualityRange(QualityCategory.Good, QualityCategory.Legendary);
            
            // Set HP range to 50% or better
            testOutfit.filter.AllowedHitPointsPercents = new FloatRange(0.5f, 1.0f);
            
            testPawn.outfits.CurrentApparelPolicy = testOutfit;

            var pos = testPawn.Position;
            var pistolDef = VanillaWeaponDefOf.Gun_Autopistol;
            var rifleDef = VanillaWeaponDefOf.Gun_AssaultRifle;
            var swordDef = VanillaWeaponDefOf.MeleeWeapon_LongSword;

            // Create weapons with various quality levels and HP values
            // Weapon 1: Poor quality, 100% HP (should be rejected - poor quality)
            if (pistolDef != null)
            {
                var poorPistol = TestHelpers.CreateWeapon(map, pistolDef, pos + new IntVec3(2, 0, 0), QualityCategory.Poor);
                if (poorPistol != null)
                {
                    poorPistol.HitPoints = poorPistol.MaxHitPoints; // 100% HP
                    weapons.Add(poorPistol);
                    ImprovedWeaponCacheManager.AddWeaponToCache(poorPistol);
                    AutoArmLogger.Log($"[TEST] Created {poorPistol.Label} - Quality: Poor, HP: 100% (should be rejected)");
                }
            }

            // Weapon 2: Good quality, 30% HP (should be rejected - low HP)
            if (rifleDef != null)
            {
                var damagedRifle = TestHelpers.CreateWeapon(map, rifleDef, pos + new IntVec3(-2, 0, 0), QualityCategory.Good);
                if (damagedRifle != null)
                {
                    damagedRifle.HitPoints = (int)(damagedRifle.MaxHitPoints * 0.3f); // 30% HP
                    weapons.Add(damagedRifle);
                    ImprovedWeaponCacheManager.AddWeaponToCache(damagedRifle);
                    AutoArmLogger.Log($"[TEST] Created {damagedRifle.Label} - Quality: Good, HP: 30% (should be rejected)");
                }
            }

            // Weapon 3: Good quality, 60% HP (should be accepted)
            if (swordDef != null)
            {
                var goodSword = TestHelpers.CreateWeapon(map, swordDef, pos + new IntVec3(0, 0, 2), QualityCategory.Good);
                if (goodSword != null)
                {
                    goodSword.HitPoints = (int)(goodSword.MaxHitPoints * 0.6f); // 60% HP
                    weapons.Add(goodSword);
                    ImprovedWeaponCacheManager.AddWeaponToCache(goodSword);
                    AutoArmLogger.Log($"[TEST] Created {goodSword.Label} - Quality: Good, HP: 60% (should be accepted)");
                }
            }

            // Weapon 4: Excellent quality, 100% HP (should be accepted)
            if (pistolDef != null)
            {
                var excellentPistol = TestHelpers.CreateWeapon(map, pistolDef, pos + new IntVec3(0, 0, -2), QualityCategory.Excellent);
                if (excellentPistol != null)
                {
                    excellentPistol.HitPoints = excellentPistol.MaxHitPoints; // 100% HP
                    weapons.Add(excellentPistol);
                    ImprovedWeaponCacheManager.AddWeaponToCache(excellentPistol);
                    AutoArmLogger.Log($"[TEST] Created {excellentPistol.Label} - Quality: Excellent, HP: 100% (should be accepted)");
                }
            }

            // Weapon 5: Normal quality, 80% HP (should be rejected - normal quality)
            if (rifleDef != null)
            {
                var normalRifle = TestHelpers.CreateWeapon(map, rifleDef, pos + new IntVec3(3, 0, 3), QualityCategory.Normal);
                if (normalRifle != null)
                {
                    normalRifle.HitPoints = (int)(normalRifle.MaxHitPoints * 0.8f); // 80% HP
                    weapons.Add(normalRifle);
                    ImprovedWeaponCacheManager.AddWeaponToCache(normalRifle);
                    AutoArmLogger.Log($"[TEST] Created {normalRifle.Label} - Quality: Normal, HP: 80% (should be rejected)");
                }
            }
        }

        public TestResult Run()
        {
            if (testPawn == null)
            {
                AutoArmLogger.LogError("[TEST] OutfitFilterTest: Test pawn creation failed");
                return TestResult.Failure("Test pawn creation failed");
            }

            var result = new TestResult { Success = true };
            result.Data["OutfitPolicy"] = testOutfit?.label ?? "Unknown";
            result.Data["QualityRange"] = $"{testOutfit?.filter.AllowedQualityLevels.min} - {testOutfit?.filter.AllowedQualityLevels.max}";
            result.Data["HPRange"] = $"{testOutfit?.filter.AllowedHitPointsPercents.min * 100}% - {testOutfit?.filter.AllowedHitPointsPercents.max * 100}%";

            // Track which weapons pass the filter
            var acceptedWeapons = new List<string>();
            var rejectedWeapons = new List<string>();
            int poorQualityRejected = 0;
            int lowHPRejected = 0;
            int normalQualityRejected = 0;

            foreach (var weapon in weapons)
            {
                if (weapon == null || !weapon.Spawned) continue;

                bool passesFilter = testOutfit.filter.Allows(weapon);
                
                weapon.TryGetQuality(out var quality);
                float hpPercent = (float)weapon.HitPoints / weapon.MaxHitPoints;
                
                string weaponInfo = $"{weapon.Label} (Q:{quality}, HP:{hpPercent * 100:F0}%)";
                
                if (passesFilter)
                {
                    acceptedWeapons.Add(weaponInfo);
                    AutoArmLogger.Log($"[TEST] {weaponInfo} - PASSES filter");
                }
                else
                {
                    rejectedWeapons.Add(weaponInfo);
                    AutoArmLogger.Log($"[TEST] {weaponInfo} - REJECTED by filter");
                    
                    // Track rejection reasons
                    if (quality < QualityCategory.Good)
                        poorQualityRejected++;
                    else if (hpPercent < 0.5f)
                        lowHPRejected++;
                    else if (quality == QualityCategory.Normal)
                        normalQualityRejected++;
                }
            }

            result.Data["AcceptedWeapons"] = string.Join(", ", acceptedWeapons);
            result.Data["RejectedWeapons"] = string.Join(", ", rejectedWeapons);
            result.Data["PoorQualityRejected"] = poorQualityRejected;
            result.Data["LowHPRejected"] = lowHPRejected;
            result.Data["NormalQualityRejected"] = normalQualityRejected;

            // Now test if job creation respects the filter
            var jobGiver = new JobGiver_PickUpBetterWeapon();
            var job = jobGiver.TestTryGiveJob(testPawn);

            if (job != null && job.targetA.Thing is ThingWithComps targetWeapon)
            {
                targetWeapon.TryGetQuality(out var targetQuality);
                float targetHPPercent = (float)targetWeapon.HitPoints / targetWeapon.MaxHitPoints;
                
                result.Data["JobCreated"] = true;
                result.Data["TargetWeapon"] = targetWeapon.Label;
                result.Data["TargetQuality"] = targetQuality.ToString();
                result.Data["TargetHP"] = $"{targetHPPercent * 100:F0}%";

                // Verify the target weapon passes the filter
                if (!testOutfit.filter.Allows(targetWeapon))
                {
                    result.Success = false;
                    result.FailureReason = $"Job created for weapon that doesn't pass filter: {targetWeapon.Label}";
                    result.Data["Error"] = "Filter bypass detected";
                    
                    // Diagnose why it doesn't pass
                    if (targetQuality < QualityCategory.Good)
                        result.Data["RejectionReason"] = "Quality too low";
                    else if (targetHPPercent < 0.5f)
                        result.Data["RejectionReason"] = "HP too low";
                    else
                        result.Data["RejectionReason"] = "Unknown";
                }
                else
                {
                    // Verify it's one of the acceptable weapons
                    if (targetQuality < QualityCategory.Good || targetHPPercent < 0.5f)
                    {
                        result.Success = false;
                        result.FailureReason = "Job created for weapon that shouldn't pass quality/HP requirements";
                        result.Data["Error"] = "Filter requirements not properly enforced";
                    }
                }
            }
            else
            {
                result.Data["JobCreated"] = false;
                // This is OK if there are no acceptable weapons or pawn already has a good weapon
                if (acceptedWeapons.Count > 0 && testPawn.equipment?.Primary == null)
                {
                    result.Data["Warning"] = "No job created despite acceptable weapons available";
                }
            }

            AutoArmLogger.Log($"[TEST] OutfitFilterTest completed - Success: {result.Success}");
            return result;
        }

        public void Cleanup()
        {
            // Remove test outfit from database
            if (testOutfit != null && Current.Game?.outfitDatabase != null)
            {
                Current.Game.outfitDatabase.AllOutfits.Remove(testOutfit);
            }

            // Use safe cleanup methods
            foreach (var weapon in weapons)
            {
                TestHelpers.SafeDestroyWeapon(weapon);
            }
            weapons.Clear();

            SafeTestCleanup.SafeDestroyPawn(testPawn);
            testPawn = null;
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
                    forcedWeapon = SafeTestCleanup.SafeCreateWeapon(pistolDef, null, QualityCategory.Normal);

                    if (forcedWeapon != null)
                    {
                        // Use safe equip method
                        SafeTestCleanup.SafeEquipWeapon(testPawn, forcedWeapon);
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
                AutoArmLogger.LogError($"[TEST] ForcedWeaponTest: Test setup failed - pawn null: {testPawn == null}, weapon null: {forcedWeapon == null}");
                return TestResult.Failure("Test setup failed");
            }

            if (!ForcedWeaponHelper.IsForced(testPawn, forcedWeapon))
            {
                var notForcedResult = TestResult.Failure("Weapon not marked as forced");
                notForcedResult.Data["Error"] = "Failed to mark weapon as forced";
                notForcedResult.Data["Weapon"] = forcedWeapon.Label;
                notForcedResult.Data["ForcedStatus"] = false;
                notForcedResult.Data["ExpectedStatus"] = true;
                AutoArmLogger.LogError($"[TEST] ForcedWeaponTest: Weapon not marked as forced - weapon: {forcedWeapon.Label}");
                return notForcedResult;
            }

            var jobGiver = new JobGiver_PickUpBetterWeapon();
            var job = jobGiver.TestTryGiveJob(testPawn);

            if (job != null)
            {
                var replacedResult = TestResult.Failure("Pawn tried to replace forced weapon");
                replacedResult.Data["Error"] = "Forced weapon protection failed";
                replacedResult.Data["ForcedWeapon"] = forcedWeapon.Label;
                replacedResult.Data["TargetWeapon"] = job.targetA.Thing?.Label;
                replacedResult.Data["JobCreated"] = true;
                replacedResult.Data["ExpectedJobCreated"] = false;
                AutoArmLogger.LogError($"[TEST] ForcedWeaponTest: Pawn tried to replace forced weapon - forced weapon: {forcedWeapon.Label}, target weapon: {job.targetA.Thing?.Label}");
                return replacedResult;
            }

            return TestResult.Pass();
        }

        public void Cleanup()
        {
            ForcedWeaponHelper.ClearForced(testPawn);

            // Use safe cleanup methods
            TestHelpers.SafeDestroyWeapon(betterWeapon);
            betterWeapon = null;

            // Pawn cleanup will handle equipped weapon
            SafeTestCleanup.SafeDestroyPawn(testPawn);
            testPawn = null;

            // forcedWeapon should be destroyed with pawn
            forcedWeapon = null;
        }
    }
}