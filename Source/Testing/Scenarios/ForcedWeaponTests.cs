// AutoArm RimWorld 1.5+ mod - automatic weapon management
// This file: Tests for forced weapon handling and protection
// Validates ForcedWeaponHelper and multi-weapon tracking

using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;
using AutoArm.Testing.Helpers;

namespace AutoArm.Testing.Scenarios
{
    /// <summary>
    /// Tests for forced weapon handling - critical for preventing unwanted weapon switches
    /// </summary>
    public class ForcedWeaponBasicTest : ITestScenario
    {
        public string Name => "Forced Weapon Basic Operations";
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

                // Create and equip a weapon that will be forced
                forcedWeapon = TestHelpers.CreateWeapon(map, VanillaWeaponDefOf.Gun_Autopistol,
                    testPawn.Position, QualityCategory.Normal);
                if (forcedWeapon != null)
                {
                    // Use safe equip method
                    SafeTestCleanup.SafeEquipWeapon(testPawn, forcedWeapon);
                }

                // Create a better weapon nearby
                betterWeapon = TestHelpers.CreateWeapon(map, VanillaWeaponDefOf.Gun_AssaultRifle,
                    testPawn.Position + new IntVec3(2, 0, 0), QualityCategory.Excellent);
                if (betterWeapon != null)
                {
                    ImprovedWeaponCacheManager.AddWeaponToCache(betterWeapon);
                }
            }
        }

        public TestResult Run()
        {
            if (testPawn == null || forcedWeapon == null || betterWeapon == null)
                return TestResult.Failure("Test setup failed");

            var result = new TestResult { Success = true };
            var jobGiver = new JobGiver_PickUpBetterWeapon();

            // Test 1: Without forcing, pawn should want the better weapon
            var jobBeforeForce = jobGiver.TestTryGiveJob(testPawn);
            result.Data["JobBeforeForce"] = jobBeforeForce != null;
            result.Data["TargetBeforeForce"] = jobBeforeForce?.targetA.Thing?.Label ?? "none";

            if (jobBeforeForce == null || jobBeforeForce.targetA.Thing != betterWeapon)
            {
                result.Success = false;
                result.FailureReason = "Pawn didn't want better weapon before forcing";
                result.Data["Error"] = "Initial weapon preference test failed";
                result.Data["Error1"] = "Pawn didn't want better weapon before forcing";
                result.Data["JobCreated"] = jobBeforeForce != null;
                result.Data["TargetWeapon"] = jobBeforeForce?.targetA.Thing?.Label ?? "none";
                result.Data["ExpectedTarget"] = betterWeapon?.Label;
                AutoArmLogger.LogError($"[TEST] ForcedWeaponBasicTest: Pawn didn't want better weapon before forcing - job: {jobBeforeForce != null}, target: {jobBeforeForce?.targetA.Thing?.Label}");
                return result;
            }

            // Test 2: Mark weapon as forced
            ForcedWeaponHelper.SetForced(testPawn, forcedWeapon);
            result.Data["IsForced"] = ForcedWeaponHelper.IsForced(testPawn, forcedWeapon);
            result.Data["HasForcedWeapon"] = ForcedWeaponHelper.HasForcedWeapon(testPawn);
            result.Data["ForcedPrimary"] = ForcedWeaponHelper.GetForcedPrimary(testPawn)?.Label ?? "none";

            // Test 3: With forced weapon and upgrades disabled, no job should be created
            bool originalSetting = AutoArmMod.settings?.allowForcedWeaponUpgrades ?? false;
            AutoArmMod.settings.allowForcedWeaponUpgrades = false;

            var jobAfterForce = jobGiver.TestTryGiveJob(testPawn);
            result.Data["JobAfterForce_Disabled"] = jobAfterForce != null;

            if (jobAfterForce != null)
            {
                result.Success = false;
                result.FailureReason = result.FailureReason ?? "Job created for forced weapon with upgrades disabled";
                result.Data["Error"] = result.Data.ContainsKey("Error") ? result.Data["Error"] + "; Forced weapon protection failed" : "Forced weapon protection failed";
                result.Data["Error2"] = "Job created for forced weapon with upgrades disabled";
                result.Data["JobAfterForce"] = true;
                result.Data["ForcedUpgradesSetting"] = false;
                AutoArmLogger.LogError("[TEST] ForcedWeaponBasicTest: Job created for forced weapon with upgrades disabled");
            }

            // Test 4: With forced weapon upgrades enabled, should only look for same type
            AutoArmMod.settings.allowForcedWeaponUpgrades = true;

            var jobWithUpgradesEnabled = jobGiver.TestTryGiveJob(testPawn);
            result.Data["JobAfterForce_Enabled"] = jobWithUpgradesEnabled != null;

            if (jobWithUpgradesEnabled != null && jobWithUpgradesEnabled.targetA.Thing == betterWeapon)
            {
                result.Success = false;
                result.FailureReason = result.FailureReason ?? "Forced weapon tried to upgrade to different weapon type";
                result.Data["Error"] = result.Data.ContainsKey("Error") ? result.Data["Error"] + "; Weapon type restriction failed" : "Weapon type restriction failed";
                result.Data["Error3"] = "Forced weapon tried to upgrade to different weapon type";
                result.Data["CurrentWeaponType"] = forcedWeapon?.def?.defName;
                result.Data["AttemptedUpgradeType"] = betterWeapon?.def?.defName;
                AutoArmLogger.LogError($"[TEST] ForcedWeaponBasicTest: Forced weapon tried to upgrade to different weapon type - current: {forcedWeapon?.def?.defName}, attempted: {betterWeapon?.def?.defName}");
            }

            // Test 5: Add a better version of the same weapon type
            var betterPistol = TestHelpers.CreateWeapon(testPawn.Map, VanillaWeaponDefOf.Gun_Autopistol,
                testPawn.Position + new IntVec3(-2, 0, 0), QualityCategory.Legendary);
            if (betterPistol != null)
            {
                ImprovedWeaponCacheManager.AddWeaponToCache(betterPistol);

                var jobSameType = jobGiver.TestTryGiveJob(testPawn);
                result.Data["JobSameTypeUpgrade"] = jobSameType != null;
                result.Data["TargetSameType"] = jobSameType?.targetA.Thing?.Label ?? "none";

                if (jobSameType != null && jobSameType.targetA.Thing == betterPistol)
                {
                    result.Data["CorrectSameTypeUpgrade"] = true;
                }

                TestHelpers.SafeDestroyWeapon(betterPistol);
            }

            // Test 6: Clear forced status
            ForcedWeaponHelper.ClearForced(testPawn);
            result.Data["IsForcedAfterClear"] = ForcedWeaponHelper.IsForced(testPawn, forcedWeapon);

            if (ForcedWeaponHelper.IsForced(testPawn, forcedWeapon))
            {
                result.Success = false;
                result.FailureReason = result.FailureReason ?? "Weapon still forced after clearing";
                result.Data["Error"] = result.Data.ContainsKey("Error") ? result.Data["Error"] + "; Clear forced status failed" : "Clear forced status failed";
                result.Data["Error4"] = "Weapon still forced after clearing";
                result.Data["WeaponStillForced"] = true;
                result.Data["ExpectedForced"] = false;
                AutoArmLogger.LogError("[TEST] ForcedWeaponBasicTest: Weapon still forced after clearing");
            }

            // Restore original setting
            AutoArmMod.settings.allowForcedWeaponUpgrades = originalSetting;

            return result;
        }

        public void Cleanup()
        {
            ForcedWeaponHelper.ClearForced(testPawn);
            TestHelpers.SafeDestroyWeapon(forcedWeapon);
            TestHelpers.SafeDestroyWeapon(betterWeapon);
            testPawn?.Destroy();
        }
    }

    /// <summary>
    /// Test forced weapon def tracking (multiple weapons of same type)
    /// </summary>
    public class ForcedWeaponDefTrackingTest : ITestScenario
    {
        public string Name => "Forced Weapon Def Tracking";
        private Pawn testPawn;
        private List<ThingWithComps> weapons = new List<ThingWithComps>();

        public void Setup(Map map)
        {
            if (map == null) return;

            testPawn = TestHelpers.CreateTestPawn(map);
            if (testPawn != null)
            {
                testPawn.equipment?.DestroyAllEquipment();

                // Create multiple pistols of different qualities
                var qualities = new[] { QualityCategory.Poor, QualityCategory.Normal, QualityCategory.Excellent };
                foreach (var quality in qualities)
                {
                    var pistol = TestHelpers.CreateWeapon(map, VanillaWeaponDefOf.Gun_Autopistol,
                        testPawn.Position + new IntVec3(weapons.Count * 2, 0, 0), quality);
                    if (pistol != null)
                    {
                        weapons.Add(pistol);
                        ImprovedWeaponCacheManager.AddWeaponToCache(pistol);
                    }
                }
            }
        }

        public TestResult Run()
        {
            if (testPawn == null || weapons.Count < 3)
                return TestResult.Failure("Test setup failed");

            var result = new TestResult { Success = true };

            // Test adding forced defs
            var pistolDef = VanillaWeaponDefOf.Gun_Autopistol;
            ForcedWeaponHelper.AddForcedDef(testPawn, pistolDef);

            result.Data["DefIsForced"] = ForcedWeaponHelper.IsWeaponDefForced(testPawn, pistolDef);
            result.Data["ForcedDefCount"] = ForcedWeaponHelper.GetForcedWeaponDefs(testPawn).Count;

            // Test that all pistols are considered forced
            int forcedCount = 0;
            foreach (var weapon in weapons)
            {
                if (ForcedWeaponHelper.IsWeaponDefForced(testPawn, weapon.def))
                    forcedCount++;
            }
            result.Data["AllPistolsForced"] = forcedCount == weapons.Count;

            if (forcedCount != weapons.Count)
            {
                result.Success = false;
                result.FailureReason = "Not all weapons of forced def marked as forced";
                result.Data["Error"] = $"Def forcing not working correctly - only {forcedCount}/{weapons.Count} weapons marked as forced";
                result.Data["ForcedCount"] = forcedCount;
                result.Data["TotalWeapons"] = weapons.Count;
                result.Data["WeaponDef"] = pistolDef?.defName;
                AutoArmLogger.LogError($"[TEST] ForcedWeaponDefTrackingTest: Not all pistols marked as forced: {forcedCount}/{weapons.Count}");
            }

            // Test removing forced def
            ForcedWeaponHelper.RemoveForcedDef(testPawn, pistolDef);
            result.Data["DefIsForcedAfterRemove"] = ForcedWeaponHelper.IsWeaponDefForced(testPawn, pistolDef);

            if (ForcedWeaponHelper.IsWeaponDefForced(testPawn, pistolDef))
            {
                result.Success = false;
                result.FailureReason = result.FailureReason ?? "Def still forced after removal";
                result.Data["Error"] = result.Data.ContainsKey("Error") ? result.Data["Error"] + "; Def removal failed" : "Def removal failed";
                result.Data["Error2"] = "Def still forced after removal";
                result.Data["DefStillForced"] = true;
                result.Data["ExpectedForced"] = false;
                AutoArmLogger.LogError($"[TEST] ForcedWeaponDefTrackingTest: Def still forced after removal - def: {pistolDef?.defName}");
            }

            return result;
        }

        public void Cleanup()
        {
            ForcedWeaponHelper.ClearForced(testPawn);
            foreach (var weapon in weapons)
            {
                TestHelpers.SafeDestroyWeapon(weapon);
            }
            weapons.Clear();
            testPawn?.Destroy();
        }
    }

    /// <summary>
    /// Test forced weapon save/load functionality
    /// </summary>
    public class ForcedWeaponSaveLoadTest : ITestScenario
    {
        public string Name => "Forced Weapon Save/Load";
        private Pawn testPawn;
        private ThingWithComps weapon;

        public void Setup(Map map)
        {
            if (map == null) return;

            testPawn = TestHelpers.CreateTestPawn(map);
            if (testPawn != null)
            {
                weapon = TestHelpers.CreateWeapon(map, VanillaWeaponDefOf.Gun_AssaultRifle,
                    testPawn.Position);
                if (weapon != null)
                {
                    // Use safe equip method
                    SafeTestCleanup.SafeEquipWeapon(testPawn, weapon);
                }
            }
        }

        public TestResult Run()
        {
            if (testPawn == null || weapon == null)
                return TestResult.Failure("Test setup failed");

            var result = new TestResult { Success = true };

            // Force the weapon
            ForcedWeaponHelper.SetForced(testPawn, weapon);
            ForcedWeaponHelper.AddForcedDef(testPawn, VanillaWeaponDefOf.Gun_Autopistol);

            // Get save data
            var primaryData = ForcedWeaponHelper.GetSaveData();
            var sidearmData = ForcedWeaponHelper.GetSidearmSaveData();

            result.Data["SavedPrimaryCount"] = primaryData.Count;
            result.Data["SavedSidearmCount"] = sidearmData.Count;

            // Clear all data
            ForcedWeaponHelper.ClearForced(testPawn);

            // Verify cleared
            result.Data["ClearedPrimary"] = !ForcedWeaponHelper.HasForcedWeapon(testPawn);
            result.Data["ClearedDefs"] = ForcedWeaponHelper.GetForcedWeaponDefs(testPawn).Count == 0;

            // Load save data back
            ForcedWeaponHelper.LoadSaveData(primaryData);
            
            // Convert List<ThingDef> to HashSet<ThingDef> for loading
            var sidearmDataAsHashSet = new Dictionary<Pawn, HashSet<ThingDef>>();
            foreach (var kvp in sidearmData)
            {
                sidearmDataAsHashSet[kvp.Key] = new HashSet<ThingDef>(kvp.Value);
            }
            ForcedWeaponHelper.LoadSidearmSaveData(sidearmDataAsHashSet);

            // Check that weapon def is restored (not the instance)
            result.Data["RestoredWeaponDef"] = ForcedWeaponHelper.IsWeaponDefForced(testPawn, weapon.def);
            result.Data["RestoredPistolDef"] = ForcedWeaponHelper.IsWeaponDefForced(testPawn, VanillaWeaponDefOf.Gun_Autopistol);

            if (!ForcedWeaponHelper.IsWeaponDefForced(testPawn, weapon.def))
            {
                result.Success = false;
                result.FailureReason = "Weapon def not restored after load";
                result.Data["Error"] = "Save/load functionality failed to restore forced weapon data";
                result.Data["WeaponDef"] = weapon.def?.defName;
                result.Data["RestoredStatus"] = false;
                result.Data["ExpectedStatus"] = true;
                AutoArmLogger.LogError($"[TEST] ForcedWeaponSaveLoadTest: Weapon def not restored after load - def: {weapon.def?.defName}");
            }

            return result;
        }

        public void Cleanup()
        {
            ForcedWeaponHelper.ClearForced(testPawn);
            TestHelpers.SafeDestroyWeapon(weapon);
            testPawn?.Destroy();
        }
    }

    /// <summary>
    /// Test comprehensive work interruption thresholds for all job types
    /// </summary>
    public class ComprehensiveWorkInterruptionTest : ITestScenario
    {
        public string Name => "Comprehensive Work Interruption Thresholds";
        private Pawn testPawn;
        private ThingWithComps currentWeapon;
        private ThingWithComps minorUpgrade;
        private ThingWithComps majorUpgrade;

        public void Setup(Map map)
        {
            if (map == null) return;

            testPawn = TestHelpers.CreateTestPawn(map);
            if (testPawn != null)
            {
                // Give pawn a normal quality pistol
                currentWeapon = TestHelpers.CreateWeapon(map, VanillaWeaponDefOf.Gun_Autopistol,
                    testPawn.Position, QualityCategory.Normal);
                if (currentWeapon != null)
                {
                    // Use safe equip method
                    SafeTestCleanup.SafeEquipWeapon(testPawn, currentWeapon);
                }

                // Create upgrades
                minorUpgrade = TestHelpers.CreateWeapon(map, VanillaWeaponDefOf.Gun_Autopistol,
                    testPawn.Position + new IntVec3(2, 0, 0), QualityCategory.Good);

                majorUpgrade = TestHelpers.CreateWeapon(map, VanillaWeaponDefOf.Gun_SniperRifle,
                    testPawn.Position + new IntVec3(-2, 0, 0), QualityCategory.Excellent);

                if (minorUpgrade != null)
                    ImprovedWeaponCacheManager.AddWeaponToCache(minorUpgrade);
                if (majorUpgrade != null)
                    ImprovedWeaponCacheManager.AddWeaponToCache(majorUpgrade);
            }
        }

        public TestResult Run()
        {
            if (testPawn == null || currentWeapon == null)
                return TestResult.Failure("Test setup failed");

            var result = new TestResult { Success = true };

            // Calculate improvements
            float currentScore = WeaponScoreCache.GetCachedScore(testPawn, currentWeapon);
            float minorScore = minorUpgrade != null ? WeaponScoreCache.GetCachedScore(testPawn, minorUpgrade) : 0f;
            float majorScore = majorUpgrade != null ? WeaponScoreCache.GetCachedScore(testPawn, majorUpgrade) : 0f;

            float minorImprovement = minorScore / currentScore;
            float majorImprovement = majorScore / currentScore;

            result.Data["CurrentScore"] = currentScore;
            result.Data["MinorImprovement"] = minorImprovement;
            result.Data["MajorImprovement"] = majorImprovement;

            // Test job types using the actual thresholds from JobGiverHelpers
            var jobTests = new[]
            {
                // Critical jobs
                JobDefOf.ExtinguishSelf,
                JobDefOf.FleeAndCower,
                JobDefOf.Vomit,
                JobDefOf.Wait_Downed,
                JobDefOf.GotoSafeTemperature,
                JobDefOf.BeatFire,
                
                // Hauling and medical jobs
                JobDefOf.HaulToCell,
                JobDefOf.CarryDownedPawnToExit,
                JobDefOf.Rescue,
                JobDefOf.TendPatient,
                
                // Regular work
                JobDefOf.Mine,
                JobDefOf.Clean,
                JobDefOf.Research,
                JobDefOf.DoBill,
                JobDefOf.DeliverFood,
                JobDefOf.Sow,
                JobDefOf.CutPlant
            };

            int failedTests = 0;
            foreach (var jobDef in jobTests)
            {
                if (jobDef == null) continue;
                
                string jobName = jobDef.defName;
                
                // Test minor upgrade
                bool minorShouldInterrupt = JobGiverHelpers.IsSafeToInterrupt(jobDef, minorImprovement);
                result.Data[$"{jobName}_MinorInterrupt"] = minorShouldInterrupt;
                result.Data[$"{jobName}_MinorImprovement"] = minorImprovement;
                
                // Verify the logic is self-consistent
                // IsSafeToInterrupt should return true if improvement meets threshold
                // We can't test the exact threshold without duplicating the logic,
                // but we can verify behavior is reasonable
                if (minorImprovement >= 1.20f && !minorShouldInterrupt)
                {
                    failedTests++;
                    result.Data[$"{jobName}_MinorError"] = "Should interrupt with 20%+ improvement";
                    AutoArmLogger.LogError($"[TEST] ComprehensiveWorkInterruptionTest: {jobName} should interrupt with {minorImprovement:F2} improvement");
                }
                
                // Test major upgrade
                bool majorShouldInterrupt = JobGiverHelpers.IsSafeToInterrupt(jobDef, majorImprovement);
                result.Data[$"{jobName}_MajorInterrupt"] = majorShouldInterrupt;
                
                // Major upgrades should generally interrupt
                if (majorImprovement >= 1.50f && !majorShouldInterrupt)
                {
                    failedTests++;
                    result.Data[$"{jobName}_MajorError"] = "Should interrupt with 50%+ improvement";
                    AutoArmLogger.LogError($"[TEST] ComprehensiveWorkInterruptionTest: {jobName} should interrupt with {majorImprovement:F2} improvement");
                }
                
                // Verify consistency: if minor interrupts, major must too
                if (minorShouldInterrupt && !majorShouldInterrupt)
                {
                    failedTests++;
                    result.Data[$"{jobName}_ConsistencyError"] = "Minor interrupts but major doesn't";
                    AutoArmLogger.LogError($"[TEST] ComprehensiveWorkInterruptionTest: {jobName} consistency error - minor interrupts but major doesn't");
                }
            }

            result.Data["TotalTests"] = jobTests.Length * 2;
            result.Data["FailedTests"] = failedTests;
            
            if (failedTests > 0)
            {
                result.Success = false;
                result.FailureReason = $"{failedTests} threshold tests failed";
                result.Data["Error"] = $"Work interruption thresholds not working correctly - {failedTests} tests failed";
                result.Data["FailureDetails"] = "See individual job results for details";
                AutoArmLogger.LogError($"[TEST] ComprehensiveWorkInterruptionTest: {failedTests} threshold tests failed");
            }

            return result;
        }

        public void Cleanup()
        {
            TestHelpers.SafeDestroyWeapon(currentWeapon);
            TestHelpers.SafeDestroyWeapon(minorUpgrade);
            TestHelpers.SafeDestroyWeapon(majorUpgrade);
            testPawn?.Destroy();
        }
    }

    /// <summary>
    /// Test SimpleSidearms edge cases including weight limits and duplicate prevention
    /// </summary>
    public class SimpleSidearmsEdgeCasesTest : ITestScenario
    {
        public string Name => "SimpleSidearms Edge Cases (Weight/Duplicates)";
        private Pawn testPawn;
        private List<ThingWithComps> weapons = new List<ThingWithComps>();

        public void Setup(Map map)
        {
            if (map == null) return;

            testPawn = TestHelpers.CreateTestPawn(map);
            if (testPawn != null)
            {
                testPawn.equipment?.DestroyAllEquipment();
                
                // Give pawn some existing sidearms to test weight limits
                if (SimpleSidearmsCompat.IsLoaded())
                {
                    // Add a pistol to inventory
                    var pistol = TestHelpers.CreateWeapon(map, VanillaWeaponDefOf.Gun_Autopistol,
                        testPawn.Position);
                    if (pistol != null)
                    {
                        // Use safe add to inventory method
                        SafeTestCleanup.SafeAddToInventory(testPawn, pistol);
                        weapons.Add(pistol);
                    }
                }

                // Create heavy weapons to test weight limits
                var heavyWeaponDefs = new[] 
                {
                    DefDatabase<ThingDef>.GetNamed("Gun_Minigun", false),
                    DefDatabase<ThingDef>.GetNamed("Gun_LMG", false),
                    DefDatabase<ThingDef>.GetNamed("Gun_ChainShotgun", false)
                };

                foreach (var def in heavyWeaponDefs.Where(d => d != null))
                {
                    var weapon = TestHelpers.CreateWeapon(map, def,
                        testPawn.Position + new IntVec3(weapons.Count * 2, 0, 0));
                    if (weapon != null)
                    {
                        weapons.Add(weapon);
                        ImprovedWeaponCacheManager.AddWeaponToCache(weapon);
                    }
                }

                // Create duplicate weapon types
                for (int i = 0; i < 3; i++)
                {
                    var rifle = TestHelpers.CreateWeapon(map, VanillaWeaponDefOf.Gun_AssaultRifle,
                        testPawn.Position + new IntVec3(-2 - i * 2, 0, 0),
                        (QualityCategory)(i + 2)); // Different qualities
                    if (rifle != null)
                    {
                        weapons.Add(rifle);
                        ImprovedWeaponCacheManager.AddWeaponToCache(rifle);
                    }
                }
            }
        }

        public TestResult Run()
        {
            if (!SimpleSidearmsCompat.IsLoaded())
            {
                var skipResult = TestResult.Pass();
                skipResult.Data["Skipped"] = "SimpleSidearms not loaded";
                return skipResult;
            }

            if (testPawn == null)
                return TestResult.Failure("Test setup failed");

            var result = new TestResult { Success = true };
            var jobGiver = new JobGiver_PickUpBetterWeapon();

            // Test 1: Weight limit checking
            // SimpleSidearms doesn't expose these methods directly
            result.Data["CurrentWeight"] = "N/A - Not exposed by SS API";
            result.Data["WeightLimit"] = "N/A - Not exposed by SS API";

            // Find a heavy weapon
            var heavyWeapon = weapons.FirstOrDefault(w => w.GetStatValue(StatDefOf.Mass) > 5f);
            if (heavyWeapon != null)
            {
                string reason;
                bool canPickupHeavy = SimpleSidearmsCompat.CanPickupSidearmInstance(heavyWeapon, testPawn, out reason);
                result.Data["CanPickupHeavyWeapon"] = canPickupHeavy;
                result.Data["HeavyWeaponReason"] = reason ?? "None";
                result.Data["HeavyWeaponMass"] = heavyWeapon.GetStatValue(StatDefOf.Mass);

                // Verify weight limit is enforced
                if (canPickupHeavy && reason?.Contains("weight") == true)
                {
                    result.Success = false;
                    result.FailureReason = result.FailureReason ?? "Weight limit not properly enforced";
                    result.Data["Error"] = result.Data.ContainsKey("Error") ? result.Data["Error"] + "; Weight validation inconsistent" : "Weight validation inconsistent";
                    result.Data["Error1"] = "Weight limit not properly enforced";
                    result.Data["WeaponAllowed"] = canPickupHeavy;
                    result.Data["ReasonContainsWeight"] = true;
                    AutoArmLogger.LogError($"[TEST] SimpleSidearmsEdgeCasesTest: Weight limit not properly enforced - weapon allowed but reason mentions weight");
                }
            }

            // Test 2: Duplicate weapon type prevention
            var rifles = weapons.Where(w => w.def == VanillaWeaponDefOf.Gun_AssaultRifle).ToList();
            if (rifles.Count >= 2)
            {
                // Pick up first rifle
                var firstRifle = rifles[0];
                if (firstRifle.Spawned)
                {
                    // Use safe add to inventory method
                    SafeTestCleanup.SafeAddToInventory(testPawn, firstRifle);
                }

                // Try to get job for second rifle
                var job = jobGiver.TestTryGiveJob(testPawn);
                
                bool allowDuplicates = SimpleSidearmsCompat.ALLOW_DUPLICATE_WEAPON_TYPES;
                result.Data["AllowDuplicateTypes"] = allowDuplicates;
                
                if (!allowDuplicates)
                {
                    // Should not pick up duplicate weapon type
                    if (job != null && rifles.Contains(job.targetA.Thing))
                    {
                        result.Success = false;
                        result.FailureReason = result.FailureReason ?? "Picked up duplicate weapon type when not allowed";
                        result.Data["Error"] = result.Data.ContainsKey("Error") ? result.Data["Error"] + "; Duplicate prevention failed" : "Duplicate prevention failed";
                        result.Data["Error2"] = "Picked up duplicate weapon type when not allowed";
                        result.Data["DuplicateWeapon"] = job.targetA.Thing?.Label;
                        result.Data["AllowDuplicates"] = allowDuplicates;
                        AutoArmLogger.LogError($"[TEST] SimpleSidearmsEdgeCasesTest: Picked up duplicate weapon type when not allowed - weapon: {job.targetA.Thing?.Label}");
                    }
                }
            }

            // Test 3: Slot limit checking
            int currentSlots = SimpleSidearmsCompat.GetCurrentSidearmCount(testPawn);
            int maxSlots = SimpleSidearmsCompat.GetMaxSidearmsForPawn(testPawn);
            
            result.Data["CurrentSlots"] = currentSlots;
            result.Data["MaxSlots"] = maxSlots;

            // Fill up slots if possible
            while (currentSlots < maxSlots && weapons.Any(w => w.Spawned))
            {
                var weapon = weapons.First(w => w.Spawned);
                if (SafeTestCleanup.SafeAddToInventory(testPawn, weapon))
                {
                    currentSlots++;
                }
                else
                {
                    break;
                }
            }

            // Test slot limit enforcement
            if (currentSlots >= maxSlots)
            {
                var job = jobGiver.TestTryGiveJob(testPawn);
                result.Data["JobWithFullSlots"] = job != null;
                
                if (job != null && weapons.Contains(job.targetA.Thing))
                {
                    result.Success = false;
                    result.FailureReason = result.FailureReason ?? "Created job when sidearm slots are full";
                    result.Data["Error"] = result.Data.ContainsKey("Error") ? result.Data["Error"] + "; Slot limit not enforced" : "Slot limit not enforced";
                    result.Data["Error3"] = "Created job when sidearm slots are full";
                    result.Data["CurrentSlots"] = currentSlots;
                    result.Data["MaxSlots"] = maxSlots;
                    result.Data["TargetWeapon"] = job.targetA.Thing?.Label;
                    AutoArmLogger.LogError($"[TEST] SimpleSidearmsEdgeCasesTest: Created job when sidearm slots are full - current: {currentSlots}, max: {maxSlots}");
                }
            }

            // Test 4: AutoEquipTracker marking
            var testJob = JobHelper.CreateEquipJob(weapons.FirstOrDefault(w => w.Spawned));
            if (testJob != null)
            {
                AutoEquipTracker.MarkAsAutoEquip(testJob, testPawn);
                result.Data["JobMarkedAsAutoEquip"] = AutoEquipTracker.IsAutoEquip(testJob);
                
                if (!AutoEquipTracker.IsAutoEquip(testJob))
                {
                    result.Success = false;
                    result.FailureReason = result.FailureReason ?? "AutoEquipTracker not marking jobs properly";
                    result.Data["Error"] = result.Data.ContainsKey("Error") ? result.Data["Error"] + "; Job marking failed" : "Job marking failed";
                    result.Data["Error4"] = "AutoEquipTracker not marking jobs properly";
                    result.Data["JobMarked"] = false;
                    result.Data["ExpectedMarked"] = true;
                    AutoArmLogger.LogError("[TEST] SimpleSidearmsEdgeCasesTest: AutoEquipTracker not marking jobs properly");
                }
            }

            return result;
        }

        public void Cleanup()
        {
            foreach (var weapon in weapons)
            {
                TestHelpers.SafeDestroyWeapon(weapon);
            }
            weapons.Clear();
            testPawn?.Destroy();
        }
    }

    /// <summary>
    /// Test that pawns don't downgrade from better forced weapons to worse weapons of same quality
    /// Addresses issue: "pawns constantly disregard a forced charge shotgun for an autopistol of the same quality"
    /// </summary>
    public class ForcedWeaponDowngradePreventionTest : ITestScenario
    {
        public string Name => "Forced Weapon Downgrade Prevention";
        private Pawn testPawn;
        private ThingWithComps forcedSuperiorWeapon;
        private ThingWithComps inferiorWeaponSameQuality;
        private ThingWithComps forcedMeleeWeapon;
        private ThingWithComps inferiorMeleeSameQuality;

        public void Setup(Map map)
        {
            if (map == null) return;

            testPawn = TestHelpers.CreateTestPawn(map);
            if (testPawn != null)
            {
                testPawn.equipment?.DestroyAllEquipment();

                // Test case 1: Charge shotgun vs autopistol (same quality)
                var chargeShotgunDef = DefDatabase<ThingDef>.GetNamed("Gun_ChargeShotgun", false);
                if (chargeShotgunDef != null)
                {
                    forcedSuperiorWeapon = TestHelpers.CreateWeapon(map, chargeShotgunDef,
                        testPawn.Position, QualityCategory.Normal);
                    if (forcedSuperiorWeapon != null)
                    {
                        SafeTestCleanup.SafeEquipWeapon(testPawn, forcedSuperiorWeapon);
                    }
                }
                else
                {
                    // Fallback to assault rifle vs autopistol if charge shotgun not available
                    forcedSuperiorWeapon = TestHelpers.CreateWeapon(map, VanillaWeaponDefOf.Gun_AssaultRifle,
                        testPawn.Position, QualityCategory.Normal);
                    if (forcedSuperiorWeapon != null)
                    {
                        SafeTestCleanup.SafeEquipWeapon(testPawn, forcedSuperiorWeapon);
                    }
                }

                // Create inferior ranged weapon with SAME quality
                inferiorWeaponSameQuality = TestHelpers.CreateWeapon(map, VanillaWeaponDefOf.Gun_Autopistol,
                    testPawn.Position + new IntVec3(2, 0, 0), QualityCategory.Normal);
                if (inferiorWeaponSameQuality != null)
                {
                    ImprovedWeaponCacheManager.AddWeaponToCache(inferiorWeaponSameQuality);
                }

                // Test case 2: Monosword vs wooden club (melee)
                var monoswordDef = DefDatabase<ThingDef>.GetNamed("MeleeWeapon_Monosword", false);
                var clubDef = DefDatabase<ThingDef>.GetNamed("MeleeWeapon_Club", false);
                
                if (monoswordDef != null && clubDef != null)
                {
                    forcedMeleeWeapon = TestHelpers.CreateWeapon(map, monoswordDef,
                        testPawn.Position + new IntVec3(0, 0, 2), QualityCategory.Good);
                    inferiorMeleeSameQuality = TestHelpers.CreateWeapon(map, clubDef,
                        testPawn.Position + new IntVec3(0, 0, -2), QualityCategory.Good);
                        
                    if (forcedMeleeWeapon != null)
                        ImprovedWeaponCacheManager.AddWeaponToCache(forcedMeleeWeapon);
                    if (inferiorMeleeSameQuality != null)
                        ImprovedWeaponCacheManager.AddWeaponToCache(inferiorMeleeSameQuality);
                }
            }
        }

        public TestResult Run()
        {
            if (testPawn == null || forcedSuperiorWeapon == null || inferiorWeaponSameQuality == null)
                return TestResult.Failure("Test setup failed - required weapons not created");

            var result = new TestResult { Success = true };
            var jobGiver = new JobGiver_PickUpBetterWeapon();

            // Calculate weapon scores for debugging
            float superiorScore = WeaponScoreCache.GetCachedScore(testPawn, forcedSuperiorWeapon);
            float inferiorScore = WeaponScoreCache.GetCachedScore(testPawn, inferiorWeaponSameQuality);
            
            result.Data["SuperiorWeapon"] = forcedSuperiorWeapon.Label;
            result.Data["SuperiorScore"] = superiorScore;
            result.Data["InferiorWeapon"] = inferiorWeaponSameQuality.Label;
            result.Data["InferiorScore"] = inferiorScore;
            result.Data["ScoreRatio"] = superiorScore / inferiorScore;

            // Verify our "superior" weapon actually scores higher
            if (superiorScore <= inferiorScore)
            {
                result.Success = false;
                result.FailureReason = "Superior weapon doesn't score higher than inferior weapon";
                result.Data["Error"] = "Weapon scoring issue - expected superior weapon to score higher";
                result.Data["ScoreIssue"] = true;
                AutoArmLogger.LogError($"[TEST] ForcedWeaponDowngradePreventionTest: {forcedSuperiorWeapon.Label} ({superiorScore}) doesn't score higher than {inferiorWeaponSameQuality.Label} ({inferiorScore})");
                // Continue test anyway to check forced behavior
            }

            // Test 1: Without forcing, pawn might want the inferior weapon (for whatever reason)
            var jobBeforeForce = jobGiver.TestTryGiveJob(testPawn);
            result.Data["JobBeforeForce"] = jobBeforeForce != null;
            result.Data["TargetBeforeForce"] = jobBeforeForce?.targetA.Thing?.Label ?? "none";

            // Mark superior weapon as forced
            ForcedWeaponHelper.SetForced(testPawn, forcedSuperiorWeapon);
            result.Data["WeaponForced"] = ForcedWeaponHelper.IsForced(testPawn, forcedSuperiorWeapon);

            // Test 2: With forced weapon and upgrades DISABLED, should not pick up inferior weapon
            bool originalSetting = AutoArmMod.settings?.allowForcedWeaponUpgrades ?? false;
            AutoArmMod.settings.allowForcedWeaponUpgrades = false;

            var jobWithForcedDisabled = jobGiver.TestTryGiveJob(testPawn);
            result.Data["JobWithUpgradesDisabled"] = jobWithForcedDisabled != null;

            if (jobWithForcedDisabled != null)
            {
                result.Success = false;
                result.FailureReason = "Created job to pick up weapon when forced weapon upgrades disabled";
                result.Data["Error1"] = "Forced weapon protection failed with upgrades disabled";
                result.Data["TargetWithDisabled"] = jobWithForcedDisabled.targetA.Thing?.Label;
                AutoArmLogger.LogError($"[TEST] ForcedWeaponDowngradePreventionTest: Job created with upgrades disabled - target: {jobWithForcedDisabled.targetA.Thing?.Label}");
            }

            // Test 3: With forced weapon upgrades ENABLED, should still not downgrade
            AutoArmMod.settings.allowForcedWeaponUpgrades = true;

            var jobWithForcedEnabled = jobGiver.TestTryGiveJob(testPawn);
            result.Data["JobWithUpgradesEnabled"] = jobWithForcedEnabled != null;

            if (jobWithForcedEnabled != null && jobWithForcedEnabled.targetA.Thing == inferiorWeaponSameQuality)
            {
                result.Success = false;
                result.FailureReason = "Attempted to downgrade from forced superior weapon";
                result.Data["Error2"] = "Downgrade protection failed - picking up inferior weapon";
                result.Data["TargetWithEnabled"] = jobWithForcedEnabled.targetA.Thing?.Label;
                result.Data["CurrentWeapon"] = forcedSuperiorWeapon.Label;
                result.Data["DowngradeTarget"] = inferiorWeaponSameQuality.Label;
                AutoArmLogger.LogError($"[TEST] ForcedWeaponDowngradePreventionTest: Attempting to downgrade from {forcedSuperiorWeapon.Label} to {inferiorWeaponSameQuality.Label}");
            }

            // Test 4: Melee downgrade prevention (if melee weapons were created)
            if (forcedMeleeWeapon != null && inferiorMeleeSameQuality != null)
            {
                // Unequip ranged, equip melee
                testPawn.equipment?.DestroyAllEquipment();
                SafeTestCleanup.SafeEquipWeapon(testPawn, forcedMeleeWeapon);
                ForcedWeaponHelper.SetForced(testPawn, forcedMeleeWeapon);

                float meleeSupScore = WeaponScoreCache.GetCachedScore(testPawn, forcedMeleeWeapon);
                float meleeInfScore = WeaponScoreCache.GetCachedScore(testPawn, inferiorMeleeSameQuality);
                
                result.Data["MeleeSuperiorWeapon"] = forcedMeleeWeapon.Label;
                result.Data["MeleeSuperiorScore"] = meleeSupScore;
                result.Data["MeleeInferiorWeapon"] = inferiorMeleeSameQuality.Label;
                result.Data["MeleeInferiorScore"] = meleeInfScore;

                var meleeJob = jobGiver.TestTryGiveJob(testPawn);
                result.Data["MeleeDowngradeJob"] = meleeJob != null;
                
                if (meleeJob != null && meleeJob.targetA.Thing == inferiorMeleeSameQuality)
                {
                    result.Success = false;
                    result.FailureReason = result.FailureReason ?? "Attempted to downgrade melee weapon";
                    result.Data["Error3"] = "Melee downgrade protection failed";
                    result.Data["MeleeDowngradeTarget"] = inferiorMeleeSameQuality.Label;
                    AutoArmLogger.LogError($"[TEST] ForcedWeaponDowngradePreventionTest: Attempting melee downgrade from {forcedMeleeWeapon.Label} to {inferiorMeleeSameQuality.Label}");
                }
            }

            // Test 5: Verify an actual upgrade would be allowed (different weapon type but better)
            var actualUpgrade = TestHelpers.CreateWeapon(testPawn.Map, 
                DefDatabase<ThingDef>.GetNamed("Gun_ChargeRifle", false) ?? VanillaWeaponDefOf.Gun_SniperRifle,
                testPawn.Position + new IntVec3(-2, 0, 0), QualityCategory.Legendary);
                
            if (actualUpgrade != null)
            {
                // Re-equip original forced weapon
                testPawn.equipment?.DestroyAllEquipment();
                SafeTestCleanup.SafeEquipWeapon(testPawn, forcedSuperiorWeapon);
                
                ImprovedWeaponCacheManager.AddWeaponToCache(actualUpgrade);
                
                var upgradeJob = jobGiver.TestTryGiveJob(testPawn);
                result.Data["ActualUpgradeJob"] = upgradeJob != null;
                result.Data["ActualUpgradeTarget"] = upgradeJob?.targetA.Thing?.Label ?? "none";
                
                // With allowForcedWeaponUpgrades true, this should create a job
                if (AutoArmMod.settings.allowForcedWeaponUpgrades && upgradeJob == null)
                {
                    result.Data["Warning"] = "No job created for actual upgrade - may be too restrictive";
                }
                
                TestHelpers.SafeDestroyWeapon(actualUpgrade);
            }

            // Restore original setting
            AutoArmMod.settings.allowForcedWeaponUpgrades = originalSetting;

            return result;
        }

        public void Cleanup()
        {
            ForcedWeaponHelper.ClearForced(testPawn);
            TestHelpers.SafeDestroyWeapon(forcedSuperiorWeapon);
            TestHelpers.SafeDestroyWeapon(inferiorWeaponSameQuality);
            TestHelpers.SafeDestroyWeapon(forcedMeleeWeapon);
            TestHelpers.SafeDestroyWeapon(inferiorMeleeSameQuality);
            testPawn?.Destroy();
        }
    }


}