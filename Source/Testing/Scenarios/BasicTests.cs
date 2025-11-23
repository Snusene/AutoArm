using AutoArm.Caching;
using AutoArm.Definitions;
using AutoArm.Helpers;
using AutoArm.Jobs;
using AutoArm.Logging;
using AutoArm.Testing.Framework;
using AutoArm.Testing.Helpers;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;
using static AutoArm.Testing.Helpers.TestValidationHelper;

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

            TestRunnerFix.ResetAllSystems();

            var allWeapons = map.listerThings.ThingsInGroup(ThingRequestGroup.Weapon)
                .OfType<ThingWithComps>()
                .ToList();
            TestCleanupHelper.DestroyWeapons(allWeapons);

            WeaponCacheManager.EnsureCacheExists(map);

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
                TestRunnerFix.PreparePawnForTest(testPawn);
                testPawn.equipment?.DestroyAllEquipment();

                var weaponDef = AutoArmDefOf.Gun_Autopistol;
                if (weaponDef != null)
                {
                    testWeapon = TestHelpers.CreateWeapon(map, weaponDef,
                        testPawn.Position + new IntVec3(1, 0, 0));

                    if (testWeapon != null)
                    {
                        var compQuality = testWeapon.TryGetComp<CompQuality>();
                        if (compQuality != null)
                        {
                            compQuality.SetQuality(QualityCategory.Excellent, ArtGenerationContext.Colony);
                        }

                        WeaponCacheManager.AddWeaponToCache(testWeapon);

                        var cachedWeapons = WeaponCacheManager.GetAllWeapons(map).ToList();
                        if (!cachedWeapons.Contains(testWeapon))
                        {
                            AutoArmLogger.Error($"[TEST] UnarmedPawnTest: Created weapon not found in cache! Cache size: {cachedWeapons.Count}");
                            AutoArmLogger.Error($"[TEST] Weapon spawned: {testWeapon.Spawned}, destroyed: {testWeapon.Destroyed}, map: {testWeapon.Map?.uniqueID}");
                        }
                        else
                        {
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

            TestRunnerFix.PreparePawnForTest(testPawn);

            if (testPawn.equipment?.Primary != null)
            {
                AutoArmLogger.Error($"[TEST] UnarmedPawnTest: Pawn is not unarmed - expected: null weapon, got: {testPawn.equipment.Primary.Label}");
                return TestResult.Failure("Pawn is not unarmed");
            }

            if (testPawn.jobs == null || testPawn.equipment == null)
            {
                AutoArmLogger.Error($"[TEST] UnarmedPawnTest: Pawn systems not initialized - jobs: {testPawn.jobs != null}, equipment: {testPawn.equipment != null}");
                return TestResult.Failure("Pawn systems not initialized");
            }

            if (testPawn.jobs?.curJob?.def == JobDefOf.Equip &&
                testPawn.jobs.curJob.targetA.Thing == testWeapon)
            {
                AutoArmLogger.Debug(() => $"[TEST] UnarmedPawnTest: Pawn already has equip job for {testWeapon?.Label} - SUCCESS");
                return TestResult.Pass();
            }

            JobGiver_PickUpBetterWeapon.EnableTestMode(true);

            var jobGiver = new JobGiver_PickUpBetterWeapon();
            var job = jobGiver.TestTryGiveJob(testPawn);

            JobGiver_PickUpBetterWeapon.EnableTestMode(false);

            if (job != null)
            {
                if (!TestJobValidator.ValidateJob(job, testPawn, out string validateReason))
                {
                    AutoArmLogger.Error($"[TEST] UnarmedPawnTest: Job validation failed - {validateReason}");
                    return TestResult.Failure($"Job validation failed: {validateReason}");
                }
                return TestResult.Pass();
            }

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
                bool isValidPawn = IsValidPawn(testPawn, out reason);
                if (!isValidPawn)
                {
                    AutoArmLogger.Error($"[TEST] UnarmedPawnTest: Pawn validation failed - reason: {reason}");
                }
                else if (testWeapon != null)
                {
                    bool isValidWeapon = IsValidWeaponCandidate(testWeapon, testPawn, out string weaponReason);
                    var score = WeaponCacheManager.GetCachedScore(testPawn, testWeapon);
                    AutoArmLogger.Error($"[TEST] UnarmedPawnTest: Available weapon {testWeapon.Label} - valid: {isValidWeapon}, score: {score}, reason: {weaponReason}");
                    reason = weaponReason;
                }
            }

            AutoArmLogger.Error($"[TEST] UnarmedPawnTest: No weapon pickup job created - reason: {reason}");
            return TestResult.Failure($"No weapon pickup job created for unarmed pawn: {reason}");
        }

        public void Cleanup()
        {
            TestCleanupHelper.CleanupTest(testPawn, testWeapon);
            testWeapon = null;
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

            TestRunnerFix.ResetAllSystems();

            var allWeapons = map.listerThings.ThingsInGroup(ThingRequestGroup.Weapon)
                .OfType<ThingWithComps>()
                .ToList();
            TestCleanupHelper.DestroyWeapons(allWeapons);

            WeaponCacheManager.ClearScoreCache();


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


            TestRunnerFix.PreparePawnForTest(testPawn);
            testPawn.equipment?.DestroyAllEquipment();

            var pistolDef = AutoArmDefOf.Gun_Autopistol;
            var rifleDef = AutoArmDefOf.Gun_AssaultRifle;


            if (pistolDef == null)
            {
                pistolDef = DefDatabase<ThingDef>.GetNamedSilentFail("Gun_Revolver");
            }
            if (rifleDef == null)
            {
                rifleDef = DefDatabase<ThingDef>.GetNamedSilentFail("Gun_BoltActionRifle");
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
                        testPawn.equipment.DestroyAllEquipment();

                        testPawn.equipment.AddEquipment(currentWeapon);
                    }
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

                        WeaponCacheManager.OnOutfitFilterChanged(testPawn.outfits.CurrentApparelPolicy);
                        WeaponCacheManager.ForceRebuildAllOutfitCaches(map);
                    }


                    WeaponCacheManager.AddWeaponToCache(betterWeapon);

                    WeaponCacheManager.EnsureCacheExists(map);
                    WeaponCacheManager.ValidateCacheIntegrity(map);

                    var nearbyWeapons = WeaponCacheManager.GetAllWeapons(map);
                    var nearbyWeaponsList = nearbyWeapons.ToList();
                    if (nearbyWeaponsList.Count > 1)
                    {
                        AutoArmLogger.Debug(() => $"[TEST] WARNING: Found {nearbyWeaponsList.Count} weapons in cache, expected 1. Extra weapons may interfere with test.");
                        foreach (var w in nearbyWeaponsList)
                        {
                            AutoArmLogger.Debug(() => $"[TEST]   - {w.Label} at {w.Position}");
                        }
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

            TestRunnerFix.PreparePawnForTest(testPawn);
            if (testPawn.jobs != null)
            {
                testPawn.jobs.StopAll(false);
                testPawn.jobs.ClearQueuedJobs();
            }

            ForcedWeapons.ClearForced(testPawn);

            if (betterWeapon?.Map?.reservationManager != null)
            {
                betterWeapon.Map.reservationManager.ReleaseAllForTarget(betterWeapon);
            }

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

            WeaponCacheManager.EnsureCacheExists(testPawn.Map);
            if (!WeaponCacheManager.IsWeaponTracked(testPawn.Map, betterWeapon))
            {
                WeaponCacheManager.AddWeaponToCache(betterWeapon);
                WeaponCacheManager.MarkCacheAsChanged(testPawn.Map);

                AutoArmLogger.Debug(() => $"[TEST] WeaponUpgradeTest: Re-added {betterWeapon.Label} to weapon cache after preparation");
            }

            JobGiver_PickUpBetterWeapon.EnableTestMode(true);

            var jobGiver = new JobGiver_PickUpBetterWeapon();

            var currentScore = WeaponCacheManager.GetCachedScore(testPawn, currentWeapon);
            var betterScore = WeaponCacheManager.GetCachedScore(testPawn, betterWeapon);

            var result = new TestResult { Success = true };
            result.Data["Current Score"] = currentScore;
            result.Data["Better Score"] = betterScore;

            if (betterScore <= currentScore * TestConstants.WeaponUpgradeThreshold)
            {
                AutoArmLogger.Error($"[TEST] WeaponUpgradeTest: Better weapon score not high enough - expected: >{currentScore * TestConstants.WeaponUpgradeThreshold}, got: {betterScore} (current: {currentScore})");
                return TestResult.Failure($"Better weapon score not high enough ({betterScore} vs {currentScore * TestConstants.WeaponUpgradeThreshold} required)");
            }

            var job = jobGiver.TestTryGiveJob(testPawn);

            JobGiver_PickUpBetterWeapon.EnableTestMode(false);

            if (job == null)
            {
                string pawnReason;
                bool isValidPawn = IsValidPawn(testPawn, out pawnReason);

                string weaponReason;
                bool isValidWeapon = IsValidWeaponCandidate(betterWeapon, testPawn, out weaponReason);

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

            string jobName = job.def.defName ?? string.Empty;
            bool isValidEquipJob = job.def == JobDefOf.Equip ||
                                   jobName == "AutoArmSwapPrimary" ||
                                   jobName == "AutoArmSwapSidearm" ||
                                   jobName == "EquipSecondary" ||
                                   jobName.IndexOf("SimpleSidearms", System.StringComparison.OrdinalIgnoreCase) >= 0;

            if (!isValidEquipJob)
            {
                AutoArmLogger.Error($"[TEST] WeaponUpgradeTest: Wrong job type - expected Equip/Swap, got: {jobName}");
                return TestResult.Failure($"Wrong job type: {jobName}");
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
            TestCleanupHelper.CleanupTest(testPawn, currentWeapon, betterWeapon);
            currentWeapon = null;
            betterWeapon = null;
            testPawn = null;
        }
    }

    public class OutfitFilterTest : ITestScenario
    {
        public string Name => "Outfit Filter Quality/HP Restrictions";
        private Pawn testPawn;
        private List<ThingWithComps> weapons = new List<ThingWithComps>();
        private ThingWithComps expectedWeapon;
        private QualityRange originalQualityRange;
        private FloatRange originalHPRange;
        private ApparelPolicy originalPolicy;

        public void Setup(Map map)
        {
            if (map == null) return;

            TestRunnerFix.ResetAllSystems();

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

            TestRunnerFix.PreparePawnForTest(testPawn);
            testPawn.equipment?.DestroyAllEquipment();

            if (testPawn.outfits?.CurrentApparelPolicy?.filter != null)
            {
                var filter = testPawn.outfits.CurrentApparelPolicy.filter;

                originalPolicy = testPawn.outfits.CurrentApparelPolicy;
                originalQualityRange = filter.AllowedQualityLevels;
                originalHPRange = filter.AllowedHitPointsPercents;

                filter.AllowedQualityLevels = new QualityRange(QualityCategory.Good, QualityCategory.Legendary);

                filter.AllowedHitPointsPercents = new FloatRange(0.5f, 1.0f);

                var weaponsCat = DefDatabase<ThingCategoryDef>.GetNamedSilentFail("Weapons");
                if (weaponsCat != null)
                    filter.SetAllow(weaponsCat, true);

                WeaponCacheManager.OnOutfitFilterChanged(testPawn.outfits.CurrentApparelPolicy);
                WeaponCacheManager.ForceRebuildAllOutfitCaches(map);

            }

            var pos = testPawn.Position;
            var rifleDef = AutoArmDefOf.Gun_AssaultRifle ?? DefDatabase<ThingDef>.GetNamedSilentFail("Gun_BoltActionRifle");
            var pistolDef = AutoArmDefOf.Gun_Autopistol ?? DefDatabase<ThingDef>.GetNamedSilentFail("Gun_Revolver");
            var swordDef = AutoArmDefOf.MeleeWeapon_LongSword ?? DefDatabase<ThingDef>.GetNamedSilentFail("MeleeWeapon_Gladius");

            if (rifleDef != null)
            {
                var excellentRifle = CreateTestWeapon(map, rifleDef, pos + new IntVec3(2, 0, 0), QualityCategory.Excellent, 1.0f);
                if (excellentRifle != null)
                {
                    expectedWeapon = excellentRifle;
                    weapons.Add(excellentRifle);
                }

                var poorRifle = CreateTestWeapon(map, rifleDef, pos + new IntVec3(3, 0, 0), QualityCategory.Poor, 1.0f);
                if (poorRifle != null)
                {
                    weapons.Add(poorRifle);
                }

                var damagedRifle = CreateTestWeapon(map, rifleDef, pos + new IntVec3(4, 0, 0), QualityCategory.Masterwork, 0.3f);
                if (damagedRifle != null)
                {
                    weapons.Add(damagedRifle);
                }
            }

            if (pistolDef != null)
            {
                var damagedPistol = CreateTestWeapon(map, pistolDef, pos + new IntVec3(-2, 0, 0), QualityCategory.Normal, 0.45f);
                if (damagedPistol != null)
                {
                    weapons.Add(damagedPistol);
                }

                var goodPistol = CreateTestWeapon(map, pistolDef, pos + new IntVec3(-3, 0, 0), QualityCategory.Good, 0.6f);
                if (goodPistol != null)
                {
                    weapons.Add(goodPistol);
                }
            }

            if (swordDef != null)
            {
                var awfulSword = CreateTestWeapon(map, swordDef, pos + new IntVec3(0, 0, 2), QualityCategory.Awful, 1.0f);
                if (awfulSword != null)
                {
                    weapons.Add(awfulSword);
                }

                var goodSword = CreateTestWeapon(map, swordDef, pos + new IntVec3(0, 0, 3), QualityCategory.Good, 0.51f);
                if (goodSword != null)
                {
                    weapons.Add(goodSword);
                }
            }

            foreach (var weapon in weapons)
            {
                WeaponCacheManager.AddWeaponToCache(weapon);
            }

            UI.ThingFilter_Allows_Thing_Patch.EnableForDialog();

            UI.ThingFilter_Allows_Thing_Patch.InvalidateCache();


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
                var compQuality = weapon.TryGetComp<CompQuality>();
                if (compQuality != null)
                {
                    compQuality.SetQuality(quality, ArtGenerationContext.Colony);
                }

                weapon.HitPoints = UnityEngine.Mathf.RoundToInt(weapon.MaxHitPoints * hpPercent);

                GenSpawn.Spawn(weapon, position, map);
                weapon.SetForbidden(false, false);

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

            TestRunnerFix.PreparePawnForTest(testPawn);

            if (testPawn.jobs?.curJob != null && testPawn.jobs.curJob.def == JobDefOf.Equip)
            {
                AutoArmLogger.Debug(() => $"[TEST] OutfitFilterTest: Clearing existing equip job for {testPawn.LabelShort}");
                testPawn.jobs.EndCurrentJob(JobCondition.InterruptForced, false);
            }

            JobGiver_PickUpBetterWeapon.EnableTestMode(true);

            var jobGiver = new JobGiver_PickUpBetterWeapon();
            var job = jobGiver.TestTryGiveJob(testPawn);

            JobGiver_PickUpBetterWeapon.EnableTestMode(false);

            var result = new TestResult { Success = true };

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
                }
                else if (!meetsQuality && !meetsHP)
                {
                    rejectedByBoth++;
                }
                else if (!meetsQuality)
                {
                    rejectedByQuality++;
                }
                else
                {
                    rejectedByHP++;
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

                if ((int)chosenQuality < (int)QualityCategory.Good)
                {
                    AutoArmLogger.Error($"[TEST] OutfitFilterTest: Chose weapon with quality {chosenQuality} when filter requires Good+");
                    return TestResult.Failure($"Chose weapon with quality {chosenQuality} when filter requires Good+");
                }


                if (expectedWeapon != null && chosenWeapon != expectedWeapon)
                {
                    result.Data["Note"] = $"Chose {chosenWeapon.Label} instead of expected {expectedWeapon.Label}";
                }
            }
            else if (allowedCount > 0)
            {
                AutoArmLogger.Error($"[TEST] OutfitFilterTest: No job created despite {allowedCount} weapons meeting filter requirements");
                return TestResult.Failure($"No job created despite {allowedCount} weapons meeting filter requirements");
            }
            else
            {
                result.Data["Note"] = "No weapons met filter requirements, correctly didn't create job";
            }

            return result;
        }

        public void Cleanup()
        {
            UI.ThingFilter_Allows_Thing_Patch.DisableForDialog();

            if (originalPolicy != null && testPawn?.outfits != null && !testPawn.Destroyed)
            {
                var filter = originalPolicy.filter;
                if (filter != null)
                {
                    filter.AllowedQualityLevels = originalQualityRange;
                    filter.AllowedHitPointsPercents = originalHPRange;
                }
            }

            TestCleanupHelper.DestroyWeapons(weapons);
            weapons.Clear();
            expectedWeapon = null;

            TestCleanupHelper.DestroyPawn(testPawn);
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
                var pistolDef = AutoArmDefOf.Gun_Autopistol;
                var rifleDef = AutoArmDefOf.Gun_AssaultRifle;

                if (pistolDef != null && rifleDef != null)
                {
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
                        testPawn.equipment?.DestroyAllEquipment();

                        testPawn.equipment?.AddEquipment(forcedWeapon);

                        ForcedWeapons.SetForced(testPawn, forcedWeapon);
                    }

                    betterWeapon = TestHelpers.CreateWeapon(map, rifleDef, pos + new IntVec3(3, 0, 0));
                    if (betterWeapon != null)
                    {
                        WeaponCacheManager.AddWeaponToCache(betterWeapon);
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

            if (testPawn.equipment?.Primary != forcedWeapon)
            {
                AutoArmLogger.Error($"[TEST] ForcedWeaponTest: Weapon not equipped properly");
                return TestResult.Failure("Weapon not equipped");
            }

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
            ForcedWeapons.ClearForced(testPawn);

            TestCleanupHelper.CleanupTest(testPawn, forcedWeapon, betterWeapon);
            forcedWeapon = null;
            betterWeapon = null;
            testPawn = null;
        }
    }
}
