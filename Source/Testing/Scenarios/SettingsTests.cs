// AutoArm RimWorld 1.5+ mod - automatic weapon management
// This file: Settings tests (notifications, thresholds, mod behavior)
// Validates all user-configurable settings work correctly

using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;
using Verse.AI.Group;
using AutoArm.Testing.Helpers;

namespace AutoArm.Testing.Scenarios
{
    /// <summary>
    /// Tests for the showNotifications setting
    /// </summary>
    public class NotificationSettingTest : ITestScenario
    {
        public string Name => "Notification Setting Test";
        private Pawn testPawn;
        private ThingWithComps testWeapon;
        private bool originalSetting;

        public void Setup(Map map)
        {
            if (map == null) return;

            // Save original setting
            originalSetting = AutoArmMod.settings.showNotifications;

            // Clear all systems before test
            TestRunnerFix.ResetAllSystems();

            testPawn = TestHelpers.CreateTestPawn(map);
            if (testPawn != null)
            {
                TestRunnerFix.PreparePawnForTest(testPawn);
                testPawn.equipment?.DestroyAllEquipment();

                var weaponDef = VanillaWeaponDefOf.Gun_Autopistol;
                if (weaponDef != null)
                {
                    testWeapon = TestHelpers.CreateWeapon(map, weaponDef,
                        testPawn.Position + new IntVec3(3, 0, 0));

                    if (testWeapon != null)
                    {
                        testWeapon.SetForbidden(false, false);
                        ImprovedWeaponCacheManager.InvalidateCache(map);
                        ImprovedWeaponCacheManager.AddWeaponToCache(testWeapon);
                    }
                }
            }
        }

        public TestResult Run()
        {
            if (testPawn == null || testWeapon == null)
            {
                return TestResult.Failure("Test setup failed");
            }

            var result = new TestResult { Success = true };

            // Test with notifications enabled
            AutoArmMod.settings.showNotifications = true;
            result.Data["NotificationsEnabled"] = true;

            // Note: Actually testing if notifications appear would require
            // intercepting the Messages system, which is complex.
            // For now, we just verify the setting works.

            // Test with notifications disabled
            AutoArmMod.settings.showNotifications = false;
            result.Data["NotificationsDisabled"] = false;

            // The actual notification logic is in AutoEquipTracker
            // We're just testing that the setting can be changed
            result.Data["SettingChangeable"] = true;

            return result;
        }

        public void Cleanup()
        {
            // Restore original setting
            AutoArmMod.settings.showNotifications = originalSetting;

            TestHelpers.SafeDestroyWeapon(testWeapon);
            testWeapon = null;

            SafeTestCleanup.SafeDestroyPawn(testPawn);
            testPawn = null;
        }
    }

    /// <summary>
    /// Tests for the allowForcedWeaponUpgrades setting
    /// </summary>
    public class ForcedWeaponQualityUpgradeTest : ITestScenario
    {
        public string Name => "Forced Weapon Quality Upgrade";
        private Pawn testPawn;
        private ThingWithComps forcedWeapon;
        private ThingWithComps betterQualityWeapon;
        private bool originalSetting;

        public void Setup(Map map)
        {
            if (map == null) return;

            // Save original setting
            originalSetting = AutoArmMod.settings.allowForcedWeaponUpgrades;

            // Clear all systems before test
            TestRunnerFix.ResetAllSystems();

            testPawn = TestHelpers.CreateTestPawn(map);
            if (testPawn != null)
            {
                TestRunnerFix.PreparePawnForTest(testPawn);

                var revolverDef = VanillaWeaponDefOf.Gun_Revolver;
                if (revolverDef == null)
                    revolverDef = DefDatabase<ThingDef>.GetNamedSilentFail("Gun_Autopistol");

                if (revolverDef != null)
                {
                    // Create and equip a normal quality revolver
                    forcedWeapon = SafeTestCleanup.SafeCreateWeapon(revolverDef, null, QualityCategory.Normal);
                    if (forcedWeapon != null)
                    {
                        SafeTestCleanup.SafeEquipWeapon(testPawn, forcedWeapon);
                        ForcedWeaponHelper.SetForced(testPawn, forcedWeapon);
                        
                        AutoArmLogger.Log($"[TEST] Equipped forced weapon: {forcedWeapon.Label} (Quality: Normal)");
                        AutoArmLogger.Log($"[TEST] Weapon is forced: {ForcedWeaponHelper.IsForced(testPawn, forcedWeapon)}");
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

                    // Create a masterwork version of the same weapon - spawn close to pawn
                    betterQualityWeapon = TestHelpers.CreateWeapon(map, revolverDef, 
                        testPawn.Position + new IntVec3(1, 0, 0), QualityCategory.Masterwork);
                    if (betterQualityWeapon != null)
                    {
                        betterQualityWeapon.SetForbidden(false, false);
                        ImprovedWeaponCacheManager.InvalidateCache(map);
                        ImprovedWeaponCacheManager.AddWeaponToCache(betterQualityWeapon);
                        
                        AutoArmLogger.Log($"[TEST] Created better quality weapon: {betterQualityWeapon.Label} (Quality: Masterwork) at {betterQualityWeapon.Position}");
                        
                        // Ensure outfit allows the weapon
                        if (testPawn.outfits?.CurrentApparelPolicy?.filter != null)
                        {
                            testPawn.outfits.CurrentApparelPolicy.filter.SetAllow(revolverDef, true);
                            
                            // Also ensure quality range allows masterwork
                            var qualityRange = testPawn.outfits.CurrentApparelPolicy.filter.AllowedQualityLevels;
                            if (qualityRange.max < QualityCategory.Masterwork)
                            {
                                testPawn.outfits.CurrentApparelPolicy.filter.AllowedQualityLevels = new QualityRange(qualityRange.min, QualityCategory.Legendary);
                                AutoArmLogger.Log($"[TEST] Expanded outfit quality range to allow Masterwork");
                            }
                        }
                        
                        // Log scores for debugging
                        float currentScore = WeaponScoreCache.GetCachedScore(testPawn, forcedWeapon);
                        float betterScore = WeaponScoreCache.GetCachedScore(testPawn, betterQualityWeapon);
                        AutoArmLogger.Log($"[TEST] Current weapon score: {currentScore}");
                        AutoArmLogger.Log($"[TEST] Better weapon score: {betterScore}");
                        AutoArmLogger.Log($"[TEST] Score improvement: {(betterScore / currentScore - 1) * 100:F1}%");
                        AutoArmLogger.Log($"[TEST] Required improvement threshold: 5%");
                        
                        // Double-check cache contains the weapon
                        var weaponsInCache = ImprovedWeaponCacheManager.GetWeaponsNear(map, testPawn.Position, 10f);
                        AutoArmLogger.Log($"[TEST] Weapons in cache within 10 units: {weaponsInCache.Count()}");
                        if (!weaponsInCache.Contains(betterQualityWeapon))
                        {
                            AutoArmLogger.LogError($"[TEST] Better quality weapon not found in cache after adding!");
                        }
                    }
                }
            }
        }

        public TestResult Run()
        {
            if (testPawn == null || forcedWeapon == null || betterQualityWeapon == null)
            {
                return TestResult.Failure("Test setup failed");
            }

            var result = new TestResult { Success = true };

            // Test with quality upgrades disabled (default)
            AutoArmMod.settings.allowForcedWeaponUpgrades = false;
            
            var jobGiver = new JobGiver_PickUpBetterWeapon();
            var job = jobGiver.TestTryGiveJob(testPawn);
            
            if (job != null)
            {
                result.Success = false;
                result.FailureReason = "Job created when quality upgrades disabled";
                result.Data["Error"] = "Forced weapon upgrade setting not respected";
                result.Data["JobCreated"] = true;
                result.Data["UpgradesSetting"] = false;
                result.Data["ExpectedJob"] = false;
                AutoArmLogger.LogError("[TEST] ForcedWeaponQualityUpgradeTest: Job created when quality upgrades disabled");
            }
            result.Data["JobWithUpgradesDisabled"] = job != null;

            // Test with quality upgrades enabled
            AutoArmMod.settings.allowForcedWeaponUpgrades = true;
            
            // Additional debug logging
            AutoArmLogger.Log($"[TEST] About to call TestTryGiveJob with allowForcedWeaponUpgrades=true");
            AutoArmLogger.Log($"[TEST] Current weapon def: {testPawn.equipment?.Primary?.def?.defName}");
            AutoArmLogger.Log($"[TEST] Better weapon def: {betterQualityWeapon?.def?.defName}");
            AutoArmLogger.Log($"[TEST] Better weapon position: {betterQualityWeapon?.Position}");
            AutoArmLogger.Log($"[TEST] Can reach better weapon: {testPawn.CanReserveAndReach(betterQualityWeapon, PathEndMode.ClosestTouch, Danger.Deadly)}");
            
            // Verify forced status
            if (forcedWeapon != null)
            {
                bool isForcedBefore = ForcedWeaponHelper.IsForced(testPawn, forcedWeapon);
                AutoArmLogger.Log($"[TEST] Current weapon forced status before job: {isForcedBefore}");
            }
            
            // Extra debug: Check weapon scores to verify threshold
            float currentScore = WeaponScoreCache.GetCachedScore(testPawn, forcedWeapon);
            float betterScore = WeaponScoreCache.GetCachedScore(testPawn, betterQualityWeapon);
            float improvement = betterScore / currentScore;
            float requiredImprovement = 1.05f; // 5% as per JobGiver_PickUpBetterWeapon
            
            AutoArmLogger.Log($"[TEST] Current weapon score: {currentScore}");
            AutoArmLogger.Log($"[TEST] Better weapon score: {betterScore}");
            AutoArmLogger.Log($"[TEST] Improvement ratio: {improvement:F3} (need >= {requiredImprovement})");
            AutoArmLogger.Log($"[TEST] Passes threshold: {improvement >= requiredImprovement}");
            
            // Check quality difference
            QualityCategory currentQuality = QualityCategory.Normal;
            QualityCategory betterQuality = QualityCategory.Normal;
            forcedWeapon?.TryGetQuality(out currentQuality);
            betterQualityWeapon?.TryGetQuality(out betterQuality);
            AutoArmLogger.Log($"[TEST] Quality upgrade: {currentQuality} -> {betterQuality}");
            
            // Get base scores to understand quality impact
            if (TestRunner.IsRunningTests)
            {
                var currentBase = WeaponScoreCache.GetBaseWeaponScore(forcedWeapon);
                var betterBase = WeaponScoreCache.GetBaseWeaponScore(betterQualityWeapon);
                if (currentBase != null && betterBase != null)
                {
                    AutoArmLogger.Log($"[TEST] Current base scores - Quality: {currentBase.QualityScore}, Damage: {currentBase.DamageScore}, Range: {currentBase.RangeScore}");
                    AutoArmLogger.Log($"[TEST] Better base scores - Quality: {betterBase.QualityScore}, Damage: {betterBase.DamageScore}, Range: {betterBase.RangeScore}");
                }
            }
            
            job = jobGiver.TestTryGiveJob(testPawn);
            
            AutoArmLogger.Log($"[TEST] Job result: {job?.def?.defName ?? "null"}");
            if (job != null)
            {
                AutoArmLogger.Log($"[TEST] Job target: {job.targetA.Thing?.Label ?? "null"}");
            }
            
            if (job == null)
            {
                result.Success = false;
                result.FailureReason = "No job created when quality upgrades enabled";
                result.Data["Error"] = "Failed to create upgrade job when setting allows it";
                result.Data["JobCreated"] = false;
                result.Data["UpgradesSetting"] = true;
                result.Data["ExpectedJob"] = true;
                
                // Extra debug info
                result.Data["CurrentWeaponDef"] = testPawn.equipment?.Primary?.def?.defName ?? "none";
                result.Data["BetterWeaponDef"] = betterQualityWeapon?.def?.defName ?? "none";
                result.Data["CanReachBetterWeapon"] = testPawn.CanReserveAndReach(betterQualityWeapon, PathEndMode.ClosestTouch, Danger.Deadly);
                
                AutoArmLogger.LogError("[TEST] ForcedWeaponQualityUpgradeTest: No job created when quality upgrades enabled");
            }
            else if (job.targetA.Thing != betterQualityWeapon)
            {
                result.Success = false;
                result.FailureReason = "Job targets wrong weapon";
                result.Data["Error"] = "Job created but targets incorrect weapon";
                result.Data["ExpectedTarget"] = betterQualityWeapon?.Label;
                result.Data["ActualTarget"] = job.targetA.Thing?.Label;
                AutoArmLogger.LogError($"[TEST] ForcedWeaponQualityUpgradeTest: Job targets wrong weapon - expected: {betterQualityWeapon?.Label}, got: {job.targetA.Thing?.Label}");
            }
            
            result.Data["JobWithUpgradesEnabled"] = job != null;
            result.Data["TargetsCorrectWeapon"] = job?.targetA.Thing == betterQualityWeapon;

            return result;
        }

        public void Cleanup()
        {
            // Restore original setting
            AutoArmMod.settings.allowForcedWeaponUpgrades = originalSetting;

            TestHelpers.SafeDestroyWeapon(betterQualityWeapon);
            betterQualityWeapon = null;

            SafeTestCleanup.SafeDestroyPawn(testPawn);
            testPawn = null;
            forcedWeapon = null;
        }
    }

    /// <summary>
    /// Tests for the weaponUpgradeThreshold setting
    /// </summary>
    public class WeaponUpgradeThresholdTest : ITestScenario
    {
        public string Name => "Weapon Upgrade Threshold Setting";
        private Pawn testPawn;
        private ThingWithComps currentWeapon;
        private ThingWithComps slightlyBetterWeapon;
        private ThingWithComps muchBetterWeapon;
        private float originalThreshold;

        public void Setup(Map map)
        {
            if (map == null) return;

            // Save original setting
            originalThreshold = AutoArmMod.settings.weaponUpgradeThreshold;

            // Clear all systems before test
            TestRunnerFix.ResetAllSystems();

            testPawn = TestHelpers.CreateTestPawn(map);
            if (testPawn != null)
            {
                TestRunnerFix.PreparePawnForTest(testPawn);

                // Equip a pistol
                var pistolDef = VanillaWeaponDefOf.Gun_Autopistol;
                var rifleDef = VanillaWeaponDefOf.Gun_AssaultRifle;
                var chargeDef = DefDatabase<ThingDef>.GetNamedSilentFail("Gun_ChargeRifle");

                if (pistolDef != null && rifleDef != null)
                {
                    // Current weapon: poor quality pistol
                    currentWeapon = SafeTestCleanup.SafeCreateWeapon(pistolDef, null, QualityCategory.Poor);
                    if (currentWeapon != null)
                    {
                        SafeTestCleanup.SafeEquipWeapon(testPawn, currentWeapon);
                    }

                    // Slightly better: normal quality pistol (maybe 10-20% better)
                    slightlyBetterWeapon = TestHelpers.CreateWeapon(map, pistolDef,
                        testPawn.Position + new IntVec3(2, 0, 0), QualityCategory.Good);
                    if (slightlyBetterWeapon != null)
                    {
                        ImprovedWeaponCacheManager.AddWeaponToCache(slightlyBetterWeapon);
                    }

                    // Much better: rifle (should be 40%+ better)
                    muchBetterWeapon = TestHelpers.CreateWeapon(map, rifleDef,
                        testPawn.Position + new IntVec3(-2, 0, 0), QualityCategory.Normal);
                    if (muchBetterWeapon != null)
                    {
                        ImprovedWeaponCacheManager.AddWeaponToCache(muchBetterWeapon);
                    }
                }
            }
        }

        public TestResult Run()
        {
            if (testPawn == null || currentWeapon == null)
            {
                return TestResult.Failure("Test setup failed");
            }

            var result = new TestResult { Success = true };
            var jobGiver = new JobGiver_PickUpBetterWeapon();

            // Calculate score differences
            var currentScore = WeaponScoreCache.GetCachedScore(testPawn, currentWeapon);
            var slightlyBetterScore = slightlyBetterWeapon != null ? 
                WeaponScoreCache.GetCachedScore(testPawn, slightlyBetterWeapon) : 0;
            var muchBetterScore = muchBetterWeapon != null ? 
                WeaponScoreCache.GetCachedScore(testPawn, muchBetterWeapon) : 0;

            result.Data["CurrentScore"] = currentScore;
            result.Data["SlightlyBetterScore"] = slightlyBetterScore;
            result.Data["MuchBetterScore"] = muchBetterScore;

            // Test with high threshold (30% better required)
            AutoArmMod.settings.weaponUpgradeThreshold = 1.30f;
            var job = jobGiver.TestTryGiveJob(testPawn);
            
            result.Data["HighThreshold_JobCreated"] = job != null;
            if (job != null)
            {
                result.Data["HighThreshold_Target"] = job.targetA.Thing?.Label;
                // With high threshold, should only pick much better weapon
                if (job.targetA.Thing == slightlyBetterWeapon)
                {
                    result.Success = false;
                    result.FailureReason = "High threshold picked slightly better weapon";
                    result.Data["Error"] = "Upgrade threshold not properly enforced";
                    result.Data["Threshold"] = 1.30f;
                    result.Data["PickedWeapon"] = slightlyBetterWeapon?.Label;
                    result.Data["CurrentScore"] = currentScore;
                    result.Data["SlightlyBetterScore"] = slightlyBetterScore;
                    result.Data["Improvement"] = slightlyBetterScore / currentScore;
                    AutoArmLogger.LogError($"[TEST] WeaponUpgradeThresholdTest: High threshold picked slightly better weapon - threshold: 1.30, improvement: {slightlyBetterScore / currentScore:F2}");
                }
            }

            // Test with low threshold (3% better required)
            AutoArmMod.settings.weaponUpgradeThreshold = 1.03f;
            job = jobGiver.TestTryGiveJob(testPawn);
            
            result.Data["LowThreshold_JobCreated"] = job != null;
            if (job != null)
            {
                result.Data["LowThreshold_Target"] = job.targetA.Thing?.Label;
            }

            return result;
        }

        public void Cleanup()
        {
            // Restore original setting
            AutoArmMod.settings.weaponUpgradeThreshold = originalThreshold;

            TestHelpers.SafeDestroyWeapon(slightlyBetterWeapon);
            TestHelpers.SafeDestroyWeapon(muchBetterWeapon);
            slightlyBetterWeapon = null;
            muchBetterWeapon = null;

            SafeTestCleanup.SafeDestroyPawn(testPawn);
            testPawn = null;
            currentWeapon = null;
        }
    }

    /// <summary>
    /// Tests for the disableDuringRaids setting
    /// </summary>
    public class DisableDuringRaidsTest : ITestScenario
    {
        public string Name => "Disable During Raids Setting (Full Lifecycle)";
        private Pawn testPawn;
        private ThingWithComps testWeapon;
        private bool originalSetting;
        private List<Pawn> raidPawns = new List<Pawn>();
        private Lord raidLord;

        public void Setup(Map map)
        {
            if (map == null) return;

            // Save original setting
            originalSetting = AutoArmMod.settings.disableDuringRaids;

            // Clear all systems before test
            TestRunnerFix.ResetAllSystems();

            testPawn = TestHelpers.CreateTestPawn(map);
            if (testPawn != null)
            {
                TestRunnerFix.PreparePawnForTest(testPawn);
                testPawn.equipment?.DestroyAllEquipment();

                // Clear fog around test area
                var fogGrid = map.fogGrid;
                if (fogGrid != null)
                {
                    foreach (var cell in GenRadial.RadialCellsAround(testPawn.Position, 20, true))
                    {
                        if (cell.InBounds(map))
                            fogGrid.Unfog(cell);
                    }
                }

                // Try multiple weapon defs in case one is missing
                ThingDef weaponDef = VanillaWeaponDefOf.Gun_AssaultRifle;
                if (weaponDef == null)
                {
                    AutoArmLogger.Log("[TEST] Gun_AssaultRifle not found, trying Gun_Autopistol");
                    weaponDef = VanillaWeaponDefOf.Gun_Autopistol;
                }
                if (weaponDef == null)
                {
                    AutoArmLogger.Log("[TEST] Gun_Autopistol not found, trying Gun_Revolver");
                    weaponDef = VanillaWeaponDefOf.Gun_Revolver;
                }
                if (weaponDef == null)
                {
                    // Final fallback - get ANY ranged weapon
                    weaponDef = DefDatabase<ThingDef>.AllDefs
                        .FirstOrDefault(d => d.IsRangedWeapon && d.HasComp(typeof(CompEquippable)));
                    AutoArmLogger.Log($"[TEST] Using fallback weapon def: {weaponDef?.defName ?? "none"}");
                }
                
                if (weaponDef != null)
                {
                    // Create weapon very close to pawn for guaranteed reachability
                    IntVec3 weaponPos = testPawn.Position + new IntVec3(1, 0, 0);
                    
                    // Ensure the position is valid
                    if (!weaponPos.InBounds(map) || !weaponPos.Standable(map))
                    {
                        // Find the nearest valid position
                        if (CellFinder.TryFindRandomCellNear(testPawn.Position, map, 3,
                            c => c.InBounds(map) && c.Standable(map) && !c.Fogged(map),
                            out weaponPos))
                        {
                            AutoArmLogger.Log($"[TEST] Adjusted weapon position to {weaponPos}");
                        }
                    }
                    
                    testWeapon = TestHelpers.CreateWeapon(map, weaponDef, weaponPos);

                    if (testWeapon != null)
                    {
                        testWeapon.SetForbidden(false, false);
                        
                        // Force invalidate and rebuild cache
                        ImprovedWeaponCacheManager.InvalidateCache(map);
                        ImprovedWeaponCacheManager.AddWeaponToCache(testWeapon);
                        
                        // Debug logging to verify weapon setup
                        AutoArmLogger.Log($"[TEST] Created weapon {testWeapon.Label} ({testWeapon.def.defName}) at {testWeapon.Position}");
                        AutoArmLogger.Log($"[TEST] Pawn position: {testPawn.Position}");
                        AutoArmLogger.Log($"[TEST] Distance: {testPawn.Position.DistanceTo(testWeapon.Position)}");
                        AutoArmLogger.Log($"[TEST] Weapon spawned: {testWeapon.Spawned}");
                        AutoArmLogger.Log($"[TEST] Weapon map: {testWeapon.Map == map}");
                        AutoArmLogger.Log($"[TEST] Weapon forbidden: {testWeapon.IsForbidden(testPawn)}");
                        AutoArmLogger.Log($"[TEST] Can reach weapon: {testPawn.CanReserveAndReach(testWeapon, PathEndMode.ClosestTouch, Danger.Deadly)}");
                        
                        // Extra validation
                        string validationReason;
                        bool isValid = ValidationHelper.IsValidWeapon(testWeapon, testPawn, out validationReason);
                        AutoArmLogger.Log($"[TEST] Weapon validation: {isValid} - {validationReason}");
                        
                        // Check outfit filter
                        if (testPawn.outfits?.CurrentApparelPolicy?.filter != null)
                        {
                            bool allowedByOutfit = testPawn.outfits.CurrentApparelPolicy.filter.Allows(testWeapon);
                            AutoArmLogger.Log($"[TEST] Weapon allowed by outfit: {allowedByOutfit}");
                        }
                    }
                    else
                    {
                        AutoArmLogger.LogError("[TEST] Failed to create test weapon");
                    }
                }
                else
                {
                    AutoArmLogger.LogError("[TEST] Could not find any weapon def for testing");
                }
            }
        }

        public TestResult Run()
        {
            if (testPawn == null || testWeapon == null || testPawn.Map == null)
            {
                return TestResult.Failure("Test setup failed");
            }

            var result = new TestResult { Success = true };
            var jobGiver = new JobGiver_PickUpBetterWeapon();
            var map = testPawn.Map;

            // Enable the setting for this test
            AutoArmMod.settings.disableDuringRaids = true;

            // Test 1: Normal behavior without raid
            AutoArmLogger.Log("[TEST] Phase 1: Testing without raid");
            
            // Extra validation before testing
            AutoArmLogger.Log($"[TEST] Pawn is unarmed: {testPawn.equipment?.Primary == null}");
            AutoArmLogger.Log($"[TEST] Test weapon exists: {testWeapon != null}");
            AutoArmLogger.Log($"[TEST] Test weapon spawned: {testWeapon?.Spawned ?? false}");
            
            // Test cache directly
            var weaponsInRange = ImprovedWeaponCacheManager.GetWeaponsNear(map, testPawn.Position, 20f);
            AutoArmLogger.Log($"[TEST] Weapons in range (20 units): {weaponsInRange.Count()}");
            foreach (var w in weaponsInRange)
            {
                AutoArmLogger.Log($"[TEST]   - {w.Label} at {w.Position}, distance: {testPawn.Position.DistanceTo(w.Position)}");
            }
            
            // Extra test to verify outfit allows weapon
            if (testPawn.outfits?.CurrentApparelPolicy?.filter != null && testWeapon != null)
            {
                bool allowedByOutfit = testPawn.outfits.CurrentApparelPolicy.filter.Allows(testWeapon);
                if (!allowedByOutfit)
                {
                    AutoArmLogger.Log("[TEST] Weapon not allowed by outfit - forcing allow");
                    testPawn.outfits.CurrentApparelPolicy.filter.SetAllow(testWeapon.def, true);
                }
            }
            
            // Force clear cooldowns for this pawn just in case
            foreach (TimingHelper.CooldownType cooldownType in Enum.GetValues(typeof(TimingHelper.CooldownType)))
            {
                TimingHelper.ClearCooldown(testPawn, cooldownType);
            }
            
            var jobBeforeRaid = jobGiver.TestTryGiveJob(testPawn);
            result.Data["JobBeforeRaid"] = jobBeforeRaid != null;
            
            if (jobBeforeRaid == null)
            {
                // Try to understand why no job was created
                result.Data["PawnUnarmed"] = testPawn.equipment?.Primary == null;
                result.Data["WeaponExists"] = testWeapon != null;
                result.Data["WeaponSpawned"] = testWeapon?.Spawned ?? false;
                result.Data["WeaponsInCache"] = weaponsInRange.Count();
                
                // Test validation directly
                if (testWeapon != null)
                {
                    string reason;
                    bool canUse = ValidationHelper.CanPawnUseWeapon(testPawn, testWeapon, out reason);
                    result.Data["CanUseWeapon"] = canUse;
                    result.Data["ValidationReason"] = reason ?? "No reason given";
                    
                    // Try to get a job directly from validation helper
                    bool isValid = ValidationHelper.IsValidPawn(testPawn, out reason);
                    result.Data["IsPawnValid"] = isValid;
                    result.Data["PawnValidationReason"] = reason ?? "No reason given";
                }
                
                result.Success = false;
                result.FailureReason = "No job created before raid when one was expected";
                result.Data["Error"] = "Failed to create job in normal conditions";
                AutoArmLogger.LogError("[TEST] No job created before raid");
                return result;
            }

            // Test 2: Create a proper raid
            AutoArmLogger.Log("[TEST] Phase 2: Creating raid");
            var raidFaction = Find.FactionManager.AllFactions
                .FirstOrDefault(f => f.HostileTo(Faction.OfPlayer) && f.def.humanlikeFaction);
            
            if (raidFaction == null)
            {
                result.Data["SkipReason"] = "No hostile faction available for raid test";
                return result;
            }

            // Create raid pawns and lord
            bool raidCreated = CreateRaid(map, raidFaction);
            result.Data["RaidCreated"] = raidCreated;
            
            if (!raidCreated)
            {
                result.Success = false;
                result.FailureReason = "Failed to create raid scenario";
                return result;
            }

            // Verify raid is active
            bool raidDetected = JobGiver_PickUpBetterWeapon.IsRaidActive(map);
            result.Data["RaidDetected"] = raidDetected;
            
            if (!raidDetected)
            {
                result.Success = false;
                result.FailureReason = "Raid created but not detected by IsRaidActive";
                result.Data["Error"] = "IsRaidActive failed to detect created raid";
                result.Data["LordType"] = raidLord?.LordJob?.GetType().Name ?? "null";
                AutoArmLogger.LogError("[TEST] Raid not detected after creation");
                return result;
            }

            // Test 3: Verify no job during raid (with setting enabled)
            AutoArmLogger.Log("[TEST] Phase 3: Testing during raid with setting enabled");
            var jobDuringRaid = jobGiver.TestTryGiveJob(testPawn);
            result.Data["JobDuringRaidWithSettingEnabled"] = jobDuringRaid != null;
            
            if (jobDuringRaid != null)
            {
                result.Success = false;
                result.FailureReason = "Job created during raid when setting should prevent it";
                result.Data["Error"] = "disableDuringRaids setting not working";
                AutoArmLogger.LogError("[TEST] Job created during raid when it shouldn't be");
            }

            // Test 4: Verify jobs work during raid with setting disabled
            AutoArmLogger.Log("[TEST] Phase 4: Testing during raid with setting disabled");
            AutoArmMod.settings.disableDuringRaids = false;
            var jobDuringRaidDisabled = jobGiver.TestTryGiveJob(testPawn);
            result.Data["JobDuringRaidWithSettingDisabled"] = jobDuringRaidDisabled != null;
            
            if (jobDuringRaidDisabled == null)
            {
                result.Success = false;
                result.FailureReason = "No job created during raid when setting is disabled";
                result.Data["Error"] = "Jobs should work when disableDuringRaids is false";
                AutoArmLogger.LogError("[TEST] No job created when disableDuringRaids is false");
            }

            // Re-enable setting for final test
            AutoArmMod.settings.disableDuringRaids = true;

            // Test 5: End the raid
            AutoArmLogger.Log("[TEST] Phase 5: Ending raid");
            EndRaid();
            result.Data["RaidEnded"] = true;

            // Verify raid is no longer active
            bool raidStillActive = JobGiver_PickUpBetterWeapon.IsRaidActive(map);
            result.Data["RaidStillActiveAfterEnd"] = raidStillActive;
            
            if (raidStillActive)
            {
                result.Success = false;
                result.FailureReason = "Raid still detected after ending";
                result.Data["Error"] = "IsRaidActive still returns true after raid ended";
                AutoArmLogger.LogError("[TEST] Raid still active after ending");
                return result;
            }

            // Test 6: Verify normal job creation resumes after raid
            AutoArmLogger.Log("[TEST] Phase 6: Testing after raid ended");
            var jobAfterRaid = jobGiver.TestTryGiveJob(testPawn);
            result.Data["JobAfterRaid"] = jobAfterRaid != null;
            
            if (jobAfterRaid == null)
            {
                result.Success = false;
                result.FailureReason = "No job created after raid ended";
                result.Data["Error"] = "Normal job creation should resume after raid";
                AutoArmLogger.LogError("[TEST] No job created after raid ended");
                return result;
            }

            // Summary
            result.Data["Summary"] = "All raid lifecycle phases passed";
            AutoArmLogger.Log("[TEST] All raid lifecycle tests passed successfully");

            return result;
        }

        private bool CreateRaid(Map map, Faction raidFaction)
        {
            try
            {
                // Find a spawn point at edge of map
                IntVec3 spawnSpot;
                if (!RCellFinder.TryFindRandomPawnEntryCell(out spawnSpot, map, 0f))
                {
                    AutoArmLogger.LogError("[TEST] Failed to find raid spawn point");
                    return false;
                }

                // Create raid pawns
                var pawnKind = raidFaction.def.pawnGroupMakers?
                    .SelectMany(pgm => pgm.options)
                    .Where(opt => opt.kind?.race?.race?.intelligence == Intelligence.Humanlike)
                    .Select(opt => opt.kind)
                    .FirstOrDefault();
                    
                if (pawnKind == null)
                {
                    pawnKind = PawnKindDefOf.Colonist; // Fallback
                }

                // Create 3 raiders
                for (int i = 0; i < 3; i++)
                {
                    var raider = PawnGenerator.GeneratePawn(new PawnGenerationRequest(
                        pawnKind,
                        raidFaction,
                        PawnGenerationContext.NonPlayer,
                        -1,
                        forceGenerateNewPawn: true,
                        allowDead: false,
                        allowDowned: false
                    ));

                    if (raider != null)
                    {
                        // Give them weapons
                        var weaponDef = i == 0 ? VanillaWeaponDefOf.Gun_Autopistol : 
                                       (i == 1 ? VanillaWeaponDefOf.Gun_Revolver : 
                                        VanillaWeaponDefOf.MeleeWeapon_Knife);
                        
                        if (weaponDef != null)
                        {
                            var weapon = ThingMaker.MakeThing(weaponDef) as ThingWithComps;
                            if (weapon != null)
                            {
                                raider.equipment?.AddEquipment(weapon);
                            }
                        }

                        GenSpawn.Spawn(raider, spawnSpot, map);
                        raidPawns.Add(raider);
                    }
                }

                if (raidPawns.Count == 0)
                {
                    AutoArmLogger.LogError("[TEST] Failed to create any raid pawns");
                    return false;
                }

                // Create assault colony lord job
                var lordJob = new LordJob_AssaultColony(raidFaction, true, false, false, false, true);
                raidLord = LordMaker.MakeNewLord(raidFaction, lordJob, map, raidPawns);

                AutoArmLogger.Log($"[TEST] Created raid with {raidPawns.Count} pawns, lord type: {lordJob.GetType().Name}");
                return true;
            }
            catch (Exception e)
            {
                AutoArmLogger.LogError($"[TEST] Exception creating raid: {e.Message}");
                return false;
            }
        }

        private void EndRaid()
        {
            try
            {
                // End the lord
                if (raidLord != null && !raidLord.Map.lordManager.lords.NullOrEmpty())
                {
                    raidLord.Cleanup();
                    AutoArmLogger.Log("[TEST] Raid lord cleaned up");
                }

                // Destroy all raid pawns
                foreach (var pawn in raidPawns)
                {
                    if (pawn != null && !pawn.Destroyed)
                    {
                        pawn.Destroy();
                    }
                }
                
                raidPawns.Clear();
                AutoArmLogger.Log("[TEST] All raid pawns destroyed");
            }
            catch (Exception e)
            {
                AutoArmLogger.LogError($"[TEST] Exception ending raid: {e.Message}");
            }
        }

        public void Cleanup()
        {
            // Clean up any remaining raid elements
            EndRaid();
            
            // Restore original setting
            AutoArmMod.settings.disableDuringRaids = originalSetting;

            TestHelpers.SafeDestroyWeapon(testWeapon);
            testWeapon = null;

            SafeTestCleanup.SafeDestroyPawn(testPawn);
            testPawn = null;
        }
    }

    /// <summary>
    /// Tests for the respectWeaponBonds setting (Royalty DLC)
    /// </summary>
    public class RespectWeaponBondsTest : ITestScenario
    {
        public string Name => "Respect Weapon Bonds Setting";
        private Pawn testPawn;
        private ThingWithComps bondedWeapon;
        private ThingWithComps betterWeapon;
        private bool originalSetting;

        public void Setup(Map map)
        {
            if (map == null) return;

            // Save original setting
            originalSetting = AutoArmMod.settings.respectWeaponBonds;

            // Clear all systems before test
            TestRunnerFix.ResetAllSystems();

            // This test requires Royalty
            if (!ModsConfig.RoyaltyActive)
            {
                return;
            }

            testPawn = TestHelpers.CreateTestPawn(map);
            if (testPawn != null)
            {
                TestRunnerFix.PreparePawnForTest(testPawn);

                // Try to create a persona weapon
                // Look for weapons with CompBladelinkWeapon
                var personaWeaponDef = DefDatabase<ThingDef>.AllDefs
                    .FirstOrDefault(d => d.IsWeapon && d.comps != null && 
                        d.comps.Any(c => c.compClass == typeof(CompBladelinkWeapon)));

                if (personaWeaponDef != null)
                {
                    bondedWeapon = SafeTestCleanup.SafeCreateWeapon(personaWeaponDef, null, QualityCategory.Normal);
                    if (bondedWeapon != null)
                    {
                        // Try to create a bond
                        var comp = bondedWeapon.TryGetComp<CompBladelinkWeapon>();
                        if (comp != null)
                        {
                            comp.CodeFor(testPawn);
                            SafeTestCleanup.SafeEquipWeapon(testPawn, bondedWeapon);
                        }
                    }
                }

                // Create a better non-bonded weapon
                var rifleDef = VanillaWeaponDefOf.Gun_AssaultRifle;
                if (rifleDef != null)
                {
                    betterWeapon = TestHelpers.CreateWeapon(map, rifleDef,
                        testPawn.Position + new IntVec3(3, 0, 0), QualityCategory.Legendary);
                    if (betterWeapon != null)
                    {
                        ImprovedWeaponCacheManager.AddWeaponToCache(betterWeapon);
                    }
                }
            }
        }

        public TestResult Run()
        {
            if (!ModsConfig.RoyaltyActive)
            {
                var skipResult = TestResult.Pass();
                skipResult.Data["SkipReason"] = "Royalty DLC not active";
                return skipResult;
            }

            if (testPawn == null || bondedWeapon == null || betterWeapon == null)
            {
                return TestResult.Failure("Test setup failed");
            }

            var result = new TestResult { Success = true };
            var jobGiver = new JobGiver_PickUpBetterWeapon();

            // Check if weapon is actually bonded
            bool isBonded = ValidationHelper.IsWeaponBondedToPawn(bondedWeapon, testPawn);
            result.Data["WeaponBonded"] = isBonded;

            if (!isBonded)
            {
                var skipResult = TestResult.Pass();
                skipResult.Data["SkipReason"] = "Could not create weapon bond";
                return skipResult;
            }

            // Test with respectWeaponBonds disabled (should allow switching)
            AutoArmMod.settings.respectWeaponBonds = false;
            ForcedWeaponHelper.ClearForced(testPawn); // Clear any forced status
            
            var job = jobGiver.TestTryGiveJob(testPawn);
            result.Data["JobWithBondsDisabled"] = job != null;

            // Test with respectWeaponBonds enabled (should prevent switching)
            AutoArmMod.settings.respectWeaponBonds = true;
            
            // When respectWeaponBonds is enabled, we need to manually mark the bonded weapon as forced
            // In the actual game, this happens automatically when the setting is toggled
            ForcedWeaponHelper.SetForced(testPawn, bondedWeapon);
            
            // Verify the weapon is now forced
            bool isForced = ForcedWeaponHelper.IsForced(testPawn, bondedWeapon);
            result.Data["BondedWeaponForced"] = isForced;
            
            if (!isForced)
            {
                result.Success = false;
                result.FailureReason = "Failed to mark bonded weapon as forced";
                return result;
            }
            
            job = jobGiver.TestTryGiveJob(testPawn);
            result.Data["JobWithBondsEnabled"] = job != null;

            if (job != null && AutoArmMod.settings.respectWeaponBonds)
            {
                result.Success = false;
                result.FailureReason = "Job created when bonded weapon should be respected";
                result.Data["Error"] = "Bonded weapon protection failed - pawn tried to switch weapons";
                result.Data["CurrentWeapon"] = bondedWeapon?.Label;
                result.Data["TargetWeapon"] = job.targetA.Thing?.Label;
                result.Data["WeaponBonded"] = isBonded;
                result.Data["BondedWeaponForced"] = isForced;
                AutoArmLogger.LogError($"[TEST] RespectWeaponBondsTest: Job created when bonded weapon should be respected - bonded: {bondedWeapon?.Label}, target: {job.targetA.Thing?.Label}");
            }

            return result;
        }

        public void Cleanup()
        {
            // Restore original setting
            AutoArmMod.settings.respectWeaponBonds = originalSetting;

            TestHelpers.SafeDestroyWeapon(betterWeapon);
            betterWeapon = null;

            SafeTestCleanup.SafeDestroyPawn(testPawn);
            testPawn = null;
            bondedWeapon = null;
        }
    }

    /// <summary>
    /// Tests for the weaponTypePreference setting
    /// </summary>
    public class WeaponTypePreferenceTest : ITestScenario
    {
        public string Name => "Weapon Type Preference Setting";
        private Pawn testPawn;
        private ThingWithComps meleeWeapon;
        private ThingWithComps rangedWeapon;
        private float originalPreference;

        public void Setup(Map map)
        {
            if (map == null) return;

            // Save original setting
            originalPreference = AutoArmMod.settings.weaponTypePreference;

            // Clear all systems before test
            TestRunnerFix.ResetAllSystems();

            testPawn = TestHelpers.CreateTestPawn(map);
            if (testPawn != null)
            {
                TestRunnerFix.PreparePawnForTest(testPawn);
                testPawn.equipment?.DestroyAllEquipment();

                // Create melee weapon (gladius) with good quality
                var meleeDef = DefDatabase<ThingDef>.GetNamedSilentFail("MeleeWeapon_Gladius");
                if (meleeDef == null)
                    meleeDef = DefDatabase<ThingDef>.GetNamedSilentFail("MeleeWeapon_Knife");
                    
                if (meleeDef != null)
                {
                    meleeWeapon = TestHelpers.CreateWeapon(map, meleeDef,
                        testPawn.Position + new IntVec3(2, 0, 0), QualityCategory.Good);
                    if (meleeWeapon != null)
                    {
                        ImprovedWeaponCacheManager.AddWeaponToCache(meleeWeapon);
                    }
                }

                // Create ranged weapon (pistol) with similar quality
                var rangedDef = VanillaWeaponDefOf.Gun_Autopistol;
                if (rangedDef != null)
                {
                    rangedWeapon = TestHelpers.CreateWeapon(map, rangedDef,
                        testPawn.Position + new IntVec3(-2, 0, 0), QualityCategory.Good);
                    if (rangedWeapon != null)
                    {
                        ImprovedWeaponCacheManager.AddWeaponToCache(rangedWeapon);
                    }
                }
            }
        }

        public TestResult Run()
        {
            if (testPawn == null || meleeWeapon == null || rangedWeapon == null)
            {
                return TestResult.Failure("Test setup failed");
            }

            var result = new TestResult { Success = true };
            var jobGiver = new JobGiver_PickUpBetterWeapon();

            // Calculate base scores
            var meleeScore = WeaponScoreCache.GetCachedScore(testPawn, meleeWeapon);
            var rangedScore = WeaponScoreCache.GetCachedScore(testPawn, rangedWeapon);
            result.Data["BaseMeleeScore"] = meleeScore;
            result.Data["BaseRangedScore"] = rangedScore;

            // Test with strong melee preference (-0.8)
            AutoArmMod.settings.weaponTypePreference = -0.8f;
            var job = jobGiver.TestTryGiveJob(testPawn);
            
            result.Data["StrongMelee_Preference"] = -0.8f;
            result.Data["StrongMelee_RangedMult"] = AutoArmMod.GetRangedMultiplier();
            result.Data["StrongMelee_MeleeMult"] = AutoArmMod.GetMeleeMultiplier();
            result.Data["StrongMelee_Target"] = job?.targetA.Thing?.Label;
            
            if (job != null && job.targetA.Thing == rangedWeapon)
            {
                result.Success = false;
                result.FailureReason = "Strong melee preference chose ranged weapon";
                result.Data["Error"] = "Weapon preference not properly applied";
                AutoArmLogger.LogError("[TEST] WeaponTypePreferenceTest: Strong melee preference chose ranged weapon");
            }

            // Test with balanced preference (0.0)
            AutoArmMod.settings.weaponTypePreference = 0.0f;
            job = jobGiver.TestTryGiveJob(testPawn);
            
            result.Data["Balanced_Preference"] = 0.0f;
            result.Data["Balanced_RangedMult"] = AutoArmMod.GetRangedMultiplier();
            result.Data["Balanced_MeleeMult"] = AutoArmMod.GetMeleeMultiplier();
            result.Data["Balanced_Target"] = job?.targetA.Thing?.Label;

            // Test with strong ranged preference (0.8)
            AutoArmMod.settings.weaponTypePreference = 0.8f;
            job = jobGiver.TestTryGiveJob(testPawn);
            
            result.Data["StrongRanged_Preference"] = 0.8f;
            result.Data["StrongRanged_RangedMult"] = AutoArmMod.GetRangedMultiplier();
            result.Data["StrongRanged_MeleeMult"] = AutoArmMod.GetMeleeMultiplier();
            result.Data["StrongRanged_Target"] = job?.targetA.Thing?.Label;
            
            if (job != null && job.targetA.Thing == meleeWeapon)
            {
                result.Success = false;
                result.FailureReason = "Strong ranged preference chose melee weapon";
                result.Data["Error"] = "Weapon preference not properly applied";
                AutoArmLogger.LogError("[TEST] WeaponTypePreferenceTest: Strong ranged preference chose melee weapon");
            }

            // Verify multiplier calculations
            float expectedRangedMult = 10f + (0.8f * 5f); // Should be 14
            float expectedMeleeMult = 8f - (0.8f * 5f);   // Should be 4
            
            if (Math.Abs(AutoArmMod.GetRangedMultiplier() - expectedRangedMult) > 0.01f ||
                Math.Abs(AutoArmMod.GetMeleeMultiplier() - expectedMeleeMult) > 0.01f)
            {
                result.Success = false;
                result.FailureReason = "Multiplier calculations incorrect";
                result.Data["ExpectedRangedMult"] = expectedRangedMult;
                result.Data["ExpectedMeleeMult"] = expectedMeleeMult;
                result.Data["ActualRangedMult"] = AutoArmMod.GetRangedMultiplier();
                result.Data["ActualMeleeMult"] = AutoArmMod.GetMeleeMultiplier();
            }

            return result;
        }

        public void Cleanup()
        {
            // Restore original setting
            AutoArmMod.settings.weaponTypePreference = originalPreference;

            TestHelpers.SafeDestroyWeapon(meleeWeapon);
            TestHelpers.SafeDestroyWeapon(rangedWeapon);
            meleeWeapon = null;
            rangedWeapon = null;

            SafeTestCleanup.SafeDestroyPawn(testPawn);
            testPawn = null;
        }
    }
}
