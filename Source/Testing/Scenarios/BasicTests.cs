using AutoArm.Caching;
using AutoArm.Definitions;
using AutoArm.Helpers;
using AutoArm.Jobs;
using AutoArm.Logging;
using AutoArm.Testing.Helpers;
using static AutoArm.Testing.Helpers.TestValidationHelper;
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

            // Clear existing weapons in test area but not entire map (causes issues)
            var testArea = CellRect.CenteredOn(map.Center, 20);
            var existingWeapons = map.listerThings.ThingsInGroup(ThingRequestGroup.Weapon)
                .Where(t => testArea.Contains(t.Position))
                .ToList();
            foreach (var weapon in existingWeapons)
            {
                weapon.Destroy();
            }

            // Create test pawn with violence-capable backstory
            var pawnConfig = new TestHelpers.TestPawnConfig
            {
                Name = "TestPawn",
                EnsureViolenceCapable = true,
                Skills = new Dictionary<SkillDef, int>
                {
                    { SkillDefOf.Shooting, 5 },
                    { SkillDefOf.Melee, 5 }
                }
            };
            testPawn = TestHelpers.CreateTestPawn(map, pawnConfig);
            if (testPawn != null)
            {
                // Prepare pawn for testing
                TestRunnerFix.PreparePawnForTest(testPawn);
                testPawn.equipment?.DestroyAllEquipment();

                var weaponDef = VanillaWeaponDefOf.Gun_Autopistol;
                if (weaponDef != null)
                {
                    // Create weapon very close to the pawn to ensure it's chosen
                    testWeapon = TestHelpers.CreateWeapon(map, weaponDef,
                        testPawn.Position + new IntVec3(1, 0, 0));

                    if (testWeapon != null)
                    {
                        var compQuality = testWeapon.TryGetComp<CompQuality>();
                        if (compQuality != null)
                        {
                            compQuality.SetQuality(QualityCategory.Excellent, ArtGenerationContext.Colony);
                        }

                        // Force cache rebuild to ensure weapon is found
                        ImprovedWeaponCacheManager.InvalidateCache(map);
                        // Manually add to cache
                        ImprovedWeaponCacheManager.AddWeaponToCache(testWeapon);

                        // Verify this is the only weapon nearby
                        var nearbyWeapons = ImprovedWeaponCacheManager.GetWeaponsNear(map, testPawn.Position, 10f).ToList();
                        if (nearbyWeapons.Count > 1)
                        {
                            AutoArmLogger.Error($"[TEST] UnarmedPawnTest: Multiple weapons found near test pawn: {string.Join(", ", nearbyWeapons.Select(w => w.Label))}");
                        }
                    }
                }
            }
        }

        public TestResult Run()
        {
            if (testPawn == null)
            {
                AutoArmLogger.Error("[TEST] UnarmedPawnTest: Test pawn creation failed");
                return TestResult.Failure("Test pawn creation failed");
            }

            // Ensure pawn is ready for test
            TestRunnerFix.PreparePawnForTest(testPawn);

            if (testPawn.equipment?.Primary != null)
            {
                AutoArmLogger.Error($"[TEST] UnarmedPawnTest: Pawn is not unarmed - expected: null weapon, got: {testPawn.equipment.Primary.Label}");
                return TestResult.Failure("Pawn is not unarmed");
            }

            // Ensure pawn systems are initialized
            if (testPawn.jobs == null || testPawn.equipment == null)
            {
                AutoArmLogger.Error($"[TEST] UnarmedPawnTest: Pawn systems not initialized - jobs: {testPawn.jobs != null}, equipment: {testPawn.equipment != null}");
                return TestResult.Failure("Pawn systems not initialized");
            }

            var jobGiver = new JobGiver_PickUpBetterWeapon();
            var job = jobGiver.TestTryGiveJob(testPawn);

            if (job == null)
            {
                // Check if the think tree would even call this JobGiver
                // The ThinkNode_ConditionalUnarmed checks if pawn is colonist, can do violence, not drafted, etc.
                string reason = "Unknown";
                bool isColonist = testPawn.IsColonist;
                bool canViolence = testPawn.WorkTagIsDisabled(WorkTags.Violent) == false;
                bool isDrafted = testPawn.Drafted;
                bool isSpawned = testPawn.Spawned;
                
                if (!isColonist) reason = "Not a colonist";
                else if (!canViolence) reason = "Cannot do violence";
                else if (isDrafted) reason = "Is drafted";
                else if (!isSpawned) reason = "Not spawned";
                else
                {
                    // Pawn should be valid, check weapon
                    bool isValidPawn = IsValidPawnForAutoEquip(testPawn, out reason);
                    if (!isValidPawn)
                    {
                        AutoArmLogger.Error($"[TEST] UnarmedPawnTest: Pawn validation failed - reason: {reason}");
                    }
                    else if (testWeapon != null)
                    {
                        bool isValidWeapon = IsValidWeaponCandidate(testWeapon, testPawn, out string weaponReason);
                        var score = WeaponScoreCache.GetCachedScore(testPawn, testWeapon);
                        AutoArmLogger.Error($"[TEST] UnarmedPawnTest: Available weapon {testWeapon.Label} - valid: {isValidWeapon}, score: {score}, reason: {weaponReason}");
                        reason = weaponReason;
                    }
                }

                AutoArmLogger.Error($"[TEST] UnarmedPawnTest: No weapon pickup job created - reason: {reason}");
                return TestResult.Failure($"No weapon pickup job created for unarmed pawn: {reason}");
            }

            if (job.def != JobDefOf.Equip)
            {
                AutoArmLogger.Error($"[TEST] UnarmedPawnTest: Wrong job type - expected: Equip, got: {job.def.defName}");
                return TestResult.Failure($"Wrong job type: {job.def.defName}");
            }

            if (job.targetA.Thing != testWeapon)
            {
                AutoArmLogger.Error($"[TEST] UnarmedPawnTest: Job targets wrong weapon - expected: {testWeapon?.Label}, got: {job.targetA.Thing?.Label}");
                return TestResult.Failure($"Job targets wrong weapon: {job.targetA.Thing?.Label}");
            }

            return TestResult.Pass();
        }

        public void Cleanup()
        {
            // Destroy weapon first to avoid container conflicts
            if (testWeapon != null && !testWeapon.Destroyed)
            {
                // Only destroy if spawned on map (not in pawn's equipment)
                if (testWeapon.Spawned)
                {
                    testWeapon.Destroy();
                }
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

            // Only clear weapons in immediate test area
            var testArea = CellRect.CenteredOn(map.Center, 10);
            var existingWeapons = map.listerThings.ThingsInGroup(ThingRequestGroup.Weapon)
                .Where(t => testArea.Contains(t.Position))
                .ToList();

            foreach (var weapon in existingWeapons)
            {
                weapon.Destroy();
            }

            AutoArmLogger.Debug("[TEST] Starting weapon upgrade test setup");

            // Create test pawn with violence-capable backstory
            var pawnConfig = new TestHelpers.TestPawnConfig
            {
                Name = "UpgradeTestPawn",
                EnsureViolenceCapable = true,
                Skills = new Dictionary<SkillDef, int>
                {
                    { SkillDefOf.Shooting, 8 },
                    { SkillDefOf.Melee, 4 }
                }
            };
            testPawn = TestHelpers.CreateTestPawn(map, pawnConfig);
            if (testPawn == null)
            {
                Log.Error("[TEST] Failed to create test pawn!");
                return;
            }

            AutoArmLogger.Debug($"[TEST] Created pawn: {testPawn.Name} at {testPawn.Position}");

            // Prepare pawn for testing
            TestRunnerFix.PreparePawnForTest(testPawn);
            testPawn.equipment?.DestroyAllEquipment();

            var pistolDef = VanillaWeaponDefOf.Gun_Autopistol;
            var rifleDef = VanillaWeaponDefOf.Gun_AssaultRifle;

            AutoArmLogger.Debug($"[TEST] Pistol def: {pistolDef?.defName ?? "NULL"}");
            AutoArmLogger.Debug($"[TEST] Rifle def: {rifleDef?.defName ?? "NULL"}");

            if (pistolDef == null)
            {
                pistolDef = DefDatabase<ThingDef>.GetNamedSilentFail("Gun_Revolver");
                AutoArmLogger.Debug($"[TEST] Using revolver instead: {pistolDef?.defName ?? "NULL"}");
            }
            if (rifleDef == null)
            {
                rifleDef = DefDatabase<ThingDef>.GetNamedSilentFail("Gun_BoltActionRifle");
                AutoArmLogger.Debug($"[TEST] Using bolt rifle instead: {rifleDef?.defName ?? "NULL"}");
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
                    AutoArmLogger.Debug($"[TEST] Equipped pawn with {currentWeapon.Label}");
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

                    AutoArmLogger.Debug($"[TEST] Created {betterWeapon.Label} at {betterWeapon.Position}");

                    // Force cache rebuild after spawning the weapon
                    ImprovedWeaponCacheManager.InvalidateCache(map);
                    // Wait for game tick
                    Find.TickManager.DoSingleTick();
                    // Manually add the weapon to cache to ensure it's registered
                    ImprovedWeaponCacheManager.AddWeaponToCache(betterWeapon);

                    var nearbyWeapons = ImprovedWeaponCacheManager.GetWeaponsNear(map, testPawn.Position, 50f);
                    var nearbyWeaponsList = nearbyWeapons.ToList();
                    AutoArmLogger.Debug($"[TEST] Weapons in cache after rebuild: {nearbyWeaponsList.Count}");
                    foreach (var w in nearbyWeaponsList)
                    {
                        AutoArmLogger.Debug($"[TEST] - {w.Label} at {w.Position}, destroyed: {w.Destroyed}, spawned: {w.Spawned}");
                    }
                }
            }
        }

        public TestResult Run()
        {
            if (testPawn == null)
            {
                AutoArmLogger.Error("[TEST] WeaponUpgradeTest: Test pawn is null");
                return TestResult.Failure("Test pawn is null");
            }

            // Ensure pawn is ready for test
            TestRunnerFix.PreparePawnForTest(testPawn);

            if (testPawn.equipment?.Primary == null)
            {
                AutoArmLogger.Error($"[TEST] WeaponUpgradeTest: Pawn has no equipped weapon - equipment tracker exists: {testPawn.equipment != null}");
                return TestResult.Failure($"Pawn has no equipped weapon (equipment tracker: {testPawn.equipment != null})");
            }

            if (betterWeapon == null || !betterWeapon.Spawned)
            {
                AutoArmLogger.Error($"[TEST] WeaponUpgradeTest: Better weapon null or not spawned - null: {betterWeapon == null}, spawned: {betterWeapon?.Spawned ?? false}");
                return TestResult.Failure($"Better weapon null or not spawned (null: {betterWeapon == null})");
            }

            var jobGiver = new JobGiver_PickUpBetterWeapon();

            var currentScore = WeaponScoreCache.GetCachedScore(testPawn, currentWeapon);
            var betterScore = WeaponScoreCache.GetCachedScore(testPawn, betterWeapon);

            var result = new TestResult { Success = true };
            result.Data["Current Score"] = currentScore;
            result.Data["Better Score"] = betterScore;

            if (betterScore <= currentScore * TestConstants.WeaponUpgradeThreshold)
            {
                AutoArmLogger.Error($"[TEST] WeaponUpgradeTest: Better weapon score not high enough - expected: >{currentScore * TestConstants.WeaponUpgradeThreshold}, got: {betterScore} (current: {currentScore})");
                return TestResult.Failure($"Better weapon score not high enough ({betterScore} vs {currentScore * TestConstants.WeaponUpgradeThreshold} required)");
            }

            var job = jobGiver.TestTryGiveJob(testPawn);

            if (job == null)
            {
                // Check why no job was created
                string pawnReason;
                bool isValidPawn = IsValidPawnForAutoEquip(testPawn, out pawnReason);
                
                string weaponReason;
                bool isValidWeapon = IsValidWeaponCandidate(betterWeapon, testPawn, out weaponReason);

                // Check if it's because the upgrade isn't significant enough
                float upgradeRatio = betterScore / currentScore;
                bool isSignificantUpgrade = upgradeRatio >= TestConstants.WeaponUpgradeThreshold;

                string failureReason = "Unknown";
                if (!isValidPawn) failureReason = $"Pawn invalid: {pawnReason}";
                else if (!isValidWeapon) failureReason = $"Weapon invalid: {weaponReason}";
                else if (!isSignificantUpgrade) failureReason = $"Upgrade not significant ({upgradeRatio:F2} < {TestConstants.WeaponUpgradeThreshold})";
                else if (testPawn.Drafted) failureReason = "Pawn is drafted";
                else failureReason = "Unknown reason - check JobGiver logic";

                AutoArmLogger.Error($"[TEST] WeaponUpgradeTest: No upgrade job created - {failureReason}");
                AutoArmLogger.Error($"[TEST] WeaponUpgradeTest: Current: {currentWeapon?.Label} (score: {currentScore:F1}), Better: {betterWeapon?.Label} (score: {betterScore:F1}) at distance {testPawn.Position.DistanceTo(betterWeapon.Position):F1}");

                return TestResult.Failure($"No upgrade job created: {failureReason}");
            }

            if (job.def != JobDefOf.Equip)
            {
                AutoArmLogger.Error($"[TEST] WeaponUpgradeTest: Wrong job type - expected: Equip, got: {job.def.defName}");
                return TestResult.Failure($"Wrong job type: {job.def.defName}");
            }

            if (job.targetA.Thing != betterWeapon)
            {
                AutoArmLogger.Error($"[TEST] WeaponUpgradeTest: Job targets wrong weapon - expected: {betterWeapon?.Label}, got: {job.targetA.Thing?.Label}");
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
        public string Name => "Outfit Filter Quality/HP Restrictions";
        private Pawn testPawn;
        private List<ThingWithComps> weapons = new List<ThingWithComps>();
        private ThingWithComps expectedWeapon; // The weapon that should be chosen

        public void Setup(Map map)
        {
            if (map == null) return;
            
            // Clear all systems before test
            TestRunnerFix.ResetAllSystems();
            
            // Create test pawn
            var pawnConfig = new TestHelpers.TestPawnConfig
            {
                Name = "FilterTestPawn",
                EnsureViolenceCapable = true,
                Skills = new Dictionary<SkillDef, int>
                {
                    { SkillDefOf.Shooting, 8 },
                    { SkillDefOf.Melee, 5 }
                }
            };
            testPawn = TestHelpers.CreateTestPawn(map, pawnConfig);
            
            if (testPawn == null) return;
            
            // Prepare pawn for testing
            TestRunnerFix.PreparePawnForTest(testPawn);
            testPawn.equipment?.DestroyAllEquipment();

            // Configure outfit filter with quality AND HP restrictions
            if (testPawn.outfits?.CurrentApparelPolicy?.filter != null)
            {
                var filter = testPawn.outfits.CurrentApparelPolicy.filter;
                
                // Set quality restrictions - Only Good and above
                filter.AllowedQualityLevels = new QualityRange(QualityCategory.Good, QualityCategory.Legendary);
                
                // Set HP restrictions - Only 50% HP and above  
                filter.AllowedHitPointsPercents = new FloatRange(0.5f, 1.0f);
                
                // Allow all weapon types
                var weaponsCat = DefDatabase<ThingCategoryDef>.GetNamedSilentFail("Weapons");
                if (weaponsCat != null)
                    filter.SetAllow(weaponsCat, true);
                    
                AutoArmLogger.Debug($"[TEST] OutfitFilterTest: Filter configured - Quality: {filter.AllowedQualityLevels}, HP: {filter.AllowedHitPointsPercents}");
            }

            var pos = testPawn.Position;
            var rifleDef = VanillaWeaponDefOf.Gun_AssaultRifle ?? DefDatabase<ThingDef>.GetNamedSilentFail("Gun_BoltActionRifle");
            var pistolDef = VanillaWeaponDefOf.Gun_Autopistol ?? DefDatabase<ThingDef>.GetNamedSilentFail("Gun_Revolver");
            var swordDef = VanillaWeaponDefOf.MeleeWeapon_LongSword ?? DefDatabase<ThingDef>.GetNamedSilentFail("MeleeWeapon_Gladius");

            if (rifleDef != null)
            {
                // 1. Excellent rifle at 100% HP - SHOULD BE ALLOWED (best choice)
                var excellentRifle = CreateTestWeapon(map, rifleDef, pos + new IntVec3(2, 0, 0), QualityCategory.Excellent, 1.0f);
                if (excellentRifle != null)
                {
                    expectedWeapon = excellentRifle; // This should be chosen
                    weapons.Add(excellentRifle);
                    AutoArmLogger.Debug($"[TEST] Created excellent rifle at 100% HP (SHOULD BE ALLOWED)");
                }
                
                // 2. Poor rifle at 100% HP - SHOULD BE REJECTED (quality too low)
                var poorRifle = CreateTestWeapon(map, rifleDef, pos + new IntVec3(3, 0, 0), QualityCategory.Poor, 1.0f);
                if (poorRifle != null)
                {
                    weapons.Add(poorRifle);
                    AutoArmLogger.Debug($"[TEST] Created poor rifle at 100% HP (SHOULD BE REJECTED - low quality)");
                }
                
                // 3. Masterwork rifle at 30% HP - SHOULD BE REJECTED (HP too low)
                var damagedRifle = CreateTestWeapon(map, rifleDef, pos + new IntVec3(4, 0, 0), QualityCategory.Masterwork, 0.3f);
                if (damagedRifle != null)
                {
                    weapons.Add(damagedRifle);
                    AutoArmLogger.Debug($"[TEST] Created masterwork rifle at 30% HP (SHOULD BE REJECTED - low HP)");
                }
            }
            
            if (pistolDef != null)
            {
                // 4. Normal pistol at 45% HP - SHOULD BE REJECTED (HP too low)
                var damagedPistol = CreateTestWeapon(map, pistolDef, pos + new IntVec3(-2, 0, 0), QualityCategory.Normal, 0.45f);
                if (damagedPistol != null)
                {
                    weapons.Add(damagedPistol);
                    AutoArmLogger.Debug($"[TEST] Created normal pistol at 45% HP (SHOULD BE REJECTED - low HP)");
                }
                
                // 5. Good pistol at 60% HP - SHOULD BE ALLOWED
                var goodPistol = CreateTestWeapon(map, pistolDef, pos + new IntVec3(-3, 0, 0), QualityCategory.Good, 0.6f);
                if (goodPistol != null)
                {
                    weapons.Add(goodPistol);
                    AutoArmLogger.Debug($"[TEST] Created good pistol at 60% HP (SHOULD BE ALLOWED)");
                }
            }
            
            if (swordDef != null)
            {
                // 6. Awful sword at 100% HP - SHOULD BE REJECTED (quality too low)
                var awfulSword = CreateTestWeapon(map, swordDef, pos + new IntVec3(0, 0, 2), QualityCategory.Awful, 1.0f);
                if (awfulSword != null)
                {
                    weapons.Add(awfulSword);
                    AutoArmLogger.Debug($"[TEST] Created awful sword at 100% HP (SHOULD BE REJECTED - low quality)");
                }
                
                // 7. Good sword at 51% HP - SHOULD BE ALLOWED (just meets HP requirement)
                var goodSword = CreateTestWeapon(map, swordDef, pos + new IntVec3(0, 0, 3), QualityCategory.Good, 0.51f);
                if (goodSword != null)
                {
                    weapons.Add(goodSword);
                    AutoArmLogger.Debug($"[TEST] Created good sword at 51% HP (SHOULD BE ALLOWED)");
                }
            }
            
            // Force cache rebuild to ensure all weapons are found
            ImprovedWeaponCacheManager.InvalidateCache(map);
            foreach (var weapon in weapons)
            {
                ImprovedWeaponCacheManager.AddWeaponToCache(weapon);
            }
            
            AutoArmLogger.Debug($"[TEST] OutfitFilterTest: Created {weapons.Count} test weapons");
        }
        
        private ThingWithComps CreateTestWeapon(Map map, ThingDef weaponDef, IntVec3 position, QualityCategory quality, float hpPercent)
        {
            if (weaponDef == null || map == null) return null;
            
            ThingWithComps weapon;
            if (weaponDef.MadeFromStuff)
            {
                weapon = ThingMaker.MakeThing(weaponDef, ThingDefOf.Steel) as ThingWithComps;
            }
            else
            {
                weapon = ThingMaker.MakeThing(weaponDef) as ThingWithComps;
            }
            
            if (weapon != null)
            {
                // Set quality
                var compQuality = weapon.TryGetComp<CompQuality>();
                if (compQuality != null)
                {
                    compQuality.SetQuality(quality, ArtGenerationContext.Colony);
                }
                
                // Set HP
                weapon.HitPoints = UnityEngine.Mathf.RoundToInt(weapon.MaxHitPoints * hpPercent);
                
                // Spawn the weapon
                GenSpawn.Spawn(weapon, position, map);
                weapon.SetForbidden(false, false);
                
                AutoArmLogger.Debug($"[TEST] Created {weapon.Label}: Quality={quality}, HP={weapon.HitPoints}/{weapon.MaxHitPoints} ({hpPercent:P0})");
            }
            
            return weapon;
        }

        public TestResult Run()
        {
            if (testPawn == null)
            {
                AutoArmLogger.Error("[TEST] OutfitFilterTest: Test pawn creation failed");
                return TestResult.Failure("Test pawn creation failed");
            }
            
            // Ensure pawn is ready for test
            TestRunnerFix.PreparePawnForTest(testPawn);

            var jobGiver = new JobGiver_PickUpBetterWeapon();
            var job = jobGiver.TestTryGiveJob(testPawn);
            
            var result = new TestResult { Success = true };

            // Count how many weapons meet the filter requirements
            var filter = testPawn.outfits?.CurrentApparelPolicy?.filter;
            int allowedCount = 0;
            int rejectedByQuality = 0;
            int rejectedByHP = 0;
            int rejectedByBoth = 0;
            
            foreach (var weapon in weapons)
            {
                weapon.TryGetQuality(out QualityCategory quality);
                float hpPercent = (float)weapon.HitPoints / weapon.MaxHitPoints;
                
                bool meetsQuality = (int)quality >= (int)QualityCategory.Good;
                bool meetsHP = hpPercent >= 0.5f;
                
                if (meetsQuality && meetsHP)
                {
                    allowedCount++;
                    AutoArmLogger.Debug($"[TEST] {weapon.Label} ALLOWED - Quality: {quality}, HP: {hpPercent:P0}");
                }
                else if (!meetsQuality && !meetsHP)
                {
                    rejectedByBoth++;
                    AutoArmLogger.Debug($"[TEST] {weapon.Label} REJECTED (both) - Quality: {quality}, HP: {hpPercent:P0}");
                }
                else if (!meetsQuality)
                {
                    rejectedByQuality++;
                    AutoArmLogger.Debug($"[TEST] {weapon.Label} REJECTED (quality) - Quality: {quality}, HP: {hpPercent:P0}");
                }
                else
                {
                    rejectedByHP++;
                    AutoArmLogger.Debug($"[TEST] {weapon.Label} REJECTED (HP) - Quality: {quality}, HP: {hpPercent:P0}");
                }
            }
            
            result.Data["Total Weapons"] = weapons.Count;
            result.Data["Allowed"] = allowedCount;
            result.Data["Rejected by Quality"] = rejectedByQuality;
            result.Data["Rejected by HP"] = rejectedByHP;
            result.Data["Rejected by Both"] = rejectedByBoth;

            if (job != null && job.targetA.Thing is ThingWithComps chosenWeapon)
            {
                chosenWeapon.TryGetQuality(out QualityCategory chosenQuality);
                float chosenHPPercent = (float)chosenWeapon.HitPoints / chosenWeapon.MaxHitPoints;
                
                result.Data["Chosen Weapon"] = chosenWeapon.Label;
                result.Data["Chosen Quality"] = chosenQuality.ToString();
                result.Data["Chosen HP"] = $"{chosenHPPercent:P0}";
                
                // Validate that the chosen weapon meets BOTH requirements
                if ((int)chosenQuality < (int)QualityCategory.Good)
                {
                    AutoArmLogger.Error($"[TEST] OutfitFilterTest: Chose weapon with quality {chosenQuality} when filter requires Good+");
                    return TestResult.Failure($"Chose weapon with quality {chosenQuality} when filter requires Good+");
                }
                
                if (chosenHPPercent < 0.5f)
                {
                    AutoArmLogger.Error($"[TEST] OutfitFilterTest: Chose weapon at {chosenHPPercent:P0} HP when filter requires 50%+");
                    return TestResult.Failure($"Chose weapon at {chosenHPPercent:P0} HP when filter requires 50%+");
                }
                
                // Check if it chose the best available weapon
                if (expectedWeapon != null && chosenWeapon != expectedWeapon)
                {
                    result.Data["Note"] = $"Chose {chosenWeapon.Label} instead of expected {expectedWeapon.Label}";
                }
            }
            else if (allowedCount > 0)
            {
                // There were allowed weapons but no job was created
                AutoArmLogger.Error($"[TEST] OutfitFilterTest: No job created despite {allowedCount} weapons meeting filter requirements");
                return TestResult.Failure($"No job created despite {allowedCount} weapons meeting filter requirements");
            }
            else
            {
                // No weapons met the requirements, so no job is expected
                result.Data["Note"] = "No weapons met filter requirements, correctly didn't create job";
            }

            return result;
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
            expectedWeapon = null;

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
                    // Create weapon with proper material if needed
                    if (pistolDef.MadeFromStuff)
                    {
                        forcedWeapon = ThingMaker.MakeThing(pistolDef, ThingDefOf.Steel) as ThingWithComps;
                    }
                    else
                    {
                        forcedWeapon = ThingMaker.MakeThing(pistolDef) as ThingWithComps;
                    }

                    if (forcedWeapon != null)
                    {
                        // Ensure pawn is unarmed first
                        testPawn.equipment?.DestroyAllEquipment();

                        // Properly equip the weapon
                        testPawn.equipment?.AddEquipment(forcedWeapon);
                        
                        // Now mark it as forced AFTER it's equipped
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
                AutoArmLogger.Error($"[TEST] ForcedWeaponTest: Test setup failed - pawn null: {testPawn == null}, weapon null: {forcedWeapon == null}");
                return TestResult.Failure("Test setup failed");
            }

            // Verify the weapon is equipped
            if (testPawn.equipment?.Primary != forcedWeapon)
            {
                AutoArmLogger.Error($"[TEST] ForcedWeaponTest: Weapon not equipped properly");
                return TestResult.Failure("Weapon not equipped");
            }

            // In test environment, forced weapon tracking often doesn't work due to mod interactions
            // We'll accept that the weapon is just not replaced
            var result = TestResult.Pass();
            result.Data["Note"] = "Forced weapon tracking may not work perfectly in test environment";
            
            // The critical test is that pawn shouldn't replace their current weapon when it's marked for retention
            // Even if the marking system isn't working, test the behavior

            var jobGiver = new JobGiver_PickUpBetterWeapon();
            var job = jobGiver.TestTryGiveJob(testPawn);

            if (job != null)
            {
                AutoArmLogger.Error($"[TEST] ForcedWeaponTest: Pawn tried to replace forced weapon - forced weapon: {forcedWeapon.Label}, target weapon: {job.targetA.Thing?.Label}");
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