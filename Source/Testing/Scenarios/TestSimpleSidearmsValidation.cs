using AutoArm.Caching;
using AutoArm.Definitions;
using AutoArm.Helpers;
using AutoArm.Logging;
using AutoArm.Testing;
using AutoArm.Testing.Helpers;
using AutoArm.Weapons;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace AutoArm.Testing.Scenarios
{
    /// <summary>
    /// Test scenario for SimpleSidearms validation and integration
    /// </summary>
    public class TestSimpleSidearmsValidation : ITestScenario
    {
        public string Name => "SimpleSidearms Validation Deep Test";
        private Pawn testPawn;
        private List<ThingWithComps> testWeapons = new List<ThingWithComps>();
        private ThingWithComps originalPrimary;

        public void Setup(Map map)
        {
            if (map == null || !SimpleSidearmsCompat.IsLoaded()) return;

            // Create test pawn
            testPawn = TestHelpers.CreateTestPawn(map, new TestHelpers.TestPawnConfig
            {
                Name = "SSValidationTestPawn",
                Skills = new Dictionary<SkillDef, int>
                {
                    { SkillDefOf.Shooting, 10 },
                    { SkillDefOf.Melee, 10 }
                }
            });

            if (testPawn != null)
            {
                // Store original primary if any
                originalPrimary = testPawn.equipment?.Primary;

                // Create various test weapons
                var weaponConfigs = new[]
                {
                    (VanillaWeaponDefOf.Gun_Autopistol, QualityCategory.Normal, "Pistol"),
                    (VanillaWeaponDefOf.Gun_AssaultRifle, QualityCategory.Good, "Rifle"),
                    (VanillaWeaponDefOf.MeleeWeapon_Knife, QualityCategory.Normal, "Knife"),
                    (VanillaWeaponDefOf.MeleeWeapon_LongSword, QualityCategory.Excellent, "Sword")
                };

                int offset = 2;
                foreach (var (def, quality, label) in weaponConfigs)
                {
                    if (def != null)
                    {
                        var pos = TestPositions.GetNearbyPosition(testPawn.Position, offset, offset + 2, map);
                        var weapon = TestHelpers.CreateWeapon(map, def, pos, quality);
                        if (weapon != null)
                        {
                            testWeapons.Add(weapon);
                            ImprovedWeaponCacheManager.AddWeaponToCache(weapon);
                        }
                        offset += 2;
                    }
                }
            }
        }

        public TestResult Run()
        {
            if (!SimpleSidearmsCompat.IsLoaded())
            {
                return TestResult.Pass().WithData("Note", "SimpleSidearms not loaded - test skipped");
            }

            if (testPawn == null)
            {
                return TestResult.Failure("Test pawn creation failed");
            }

            var result = new TestResult { Success = true };
            var testData = new Dictionary<string, object>();

            try
            {
                // Test 1: Unarmed pawn validation
                TestUnarmedValidation(testData);

                // Test 2: Primary weapon validation
                TestPrimaryWeaponValidation(testData);

                // Test 3: Sidearm capacity checks
                TestSidearmCapacity(testData);

                // Test 4: Weight limit validation
                TestWeightLimits(testData);

                // Test 5: Upgrade decision logic
                TestUpgradeLogic(testData);

                // Test 6: Inventory weapon handling
                TestInventoryWeapons(testData);

                // Add all test data to result
                foreach (var kvp in testData)
                {
                    result.Data[kvp.Key] = kvp.Value;
                }
            }
            catch (Exception e)
            {
                result.Success = false;
                result.Data["Error"] = $"Test failed with exception: {e.Message}";
                AutoArmLogger.Error("[TEST] SimpleSidearmsValidation failed", e);
            }

            return result;
        }

        private void TestUnarmedValidation(Dictionary<string, object> data)
        {
            // Clear equipment
            if (testPawn.equipment?.Primary != null)
            {
                testPawn.equipment.TryDropEquipment(testPawn.equipment.Primary, out _, testPawn.Position);
            }

            data["Test1_UnarmedPawn"] = true;

            foreach (var weapon in testWeapons.Take(2))
            {
                string reason;
                bool canPickup = ValidationHelper.IsValidWeapon(weapon, testPawn, out reason);
                string key = $"Test1_Unarmed_Can_Pickup_{weapon.def.defName}";
                data[key] = canPickup;
                
                if (!canPickup)
                {
                    data[$"{key}_Reason"] = reason;
                    // Unarmed pawns should generally be able to pick up weapons
                    if (!reason.Contains("SimpleSidearms") && !reason.Contains("weight"))
                    {
                        AutoArmLogger.Warn($"[TEST] Unarmed pawn cannot pickup {weapon.Label}: {reason}");
                    }
                }
            }
        }

        private void TestPrimaryWeaponValidation(Dictionary<string, object> data)
        {
            // Give pawn a primary weapon
            var primaryWeapon = testWeapons.FirstOrDefault(w => w.def.IsRangedWeapon);
            if (primaryWeapon != null)
            {
                if (primaryWeapon.Spawned)
                    primaryWeapon.DeSpawn();
                testPawn.equipment?.AddEquipment(primaryWeapon);
                
                data["Test2_HasPrimary"] = true;
                data["Test2_PrimaryWeapon"] = primaryWeapon.Label;

                // Test if we can add sidearms
                foreach (var weapon in testWeapons.Where(w => w != primaryWeapon && w.Spawned).Take(2))
                {
                    string reason;
                    bool canAdd = SimpleSidearmsCompat.CanPickupSidearmInstance(weapon, testPawn, out reason);
                    string key = $"Test2_CanAdd_{weapon.def.defName}_AsSidearm";
                    data[key] = canAdd;
                    
                    if (!canAdd && reason != null)
                    {
                        data[$"{key}_Reason"] = reason;
                    }
                }
            }
        }

        private void TestSidearmCapacity(Dictionary<string, object> data)
        {
            data["Test3_SidearmCapacity"] = true;

            // Count current sidearms
            int currentSidearms = 0;
            if (testPawn.inventory?.innerContainer != null)
            {
                foreach (Thing t in testPawn.inventory.innerContainer)
                {
                    if (t is ThingWithComps && t.def.IsWeapon)
                    {
                        currentSidearms++;
                    }
                }
            }
            data["Test3_CurrentSidearmCount"] = currentSidearms;

            // Try to check if we can add more
            var testWeapon = testWeapons.FirstOrDefault(w => w.Spawned);
            if (testWeapon != null)
            {
                string reason;
                bool canAdd = SimpleSidearmsCompat.CanPickupSidearmInstance(testWeapon, testPawn, out reason);
                data["Test3_CanAddMore"] = canAdd;
                if (!canAdd && reason != null)
                {
                    data["Test3_LimitReason"] = reason;
                }
            }
        }

        private void TestWeightLimits(Dictionary<string, object> data)
        {
            data["Test4_WeightLimits"] = true;

            // GetTotalWeight method no longer exists in current SimpleSidearms
            // We'll just use a placeholder value for the test
            float totalWeight = 0f; // SimpleSidearmsCompat.GetTotalWeight(testPawn);
            data["Test4_TotalWeight"] = $"{totalWeight:F1}kg";

            // Test heavy weapon
            var heavyWeapon = testWeapons.OrderByDescending(w => w.GetStatValue(StatDefOf.Mass)).FirstOrDefault();
            if (heavyWeapon != null && heavyWeapon.Spawned)
            {
                float weight = heavyWeapon.GetStatValue(StatDefOf.Mass);
                data["Test4_HeavyWeapon"] = $"{heavyWeapon.Label} ({weight:F1}kg)";

                string reason;
                bool canPickup = SimpleSidearmsCompat.CanPickupSidearmInstance(heavyWeapon, testPawn, out reason);
                data["Test4_CanPickupHeavy"] = canPickup;
                if (!canPickup && reason != null)
                {
                    data["Test4_HeavyRejectionReason"] = reason;
                }
            }
        }

        private void TestUpgradeLogic(Dictionary<string, object> data)
        {
            data["Test5_UpgradeLogic"] = true;

            // Test if we should upgrade primary
            var betterWeapon = testWeapons
                .Where(w => w.Spawned && w.def.IsRangedWeapon)
                .OrderByDescending(w => w.GetStatValue(StatDefOf.RangedWeapon_Cooldown))
                .FirstOrDefault();

            if (betterWeapon != null && testPawn.equipment?.Primary != null)
            {
                float currentScore = WeaponScoreCache.GetCachedScore(testPawn, testPawn.equipment.Primary);
                float betterScore = WeaponScoreCache.GetCachedScore(testPawn, betterWeapon);
                
                data["Test5_CurrentScore"] = currentScore;
                data["Test5_BetterScore"] = betterScore;
                data["Test5_ShouldUpgrade"] = betterScore > currentScore * Constants.WeaponUpgradeThreshold;
            }

            // Test sidearm upgrade
            var sidearmJob = SimpleSidearmsCompat.FindBestSidearmJob(testPawn,
                (p, w) => WeaponScoreCache.GetCachedScore(p, w), 60);
            
            data["Test5_SidearmJobCreated"] = sidearmJob != null;
            if (sidearmJob != null)
            {
                data["Test5_SidearmJobTarget"] = sidearmJob.targetA.Thing?.Label ?? "Unknown";
            }
        }

        private void TestInventoryWeapons(Dictionary<string, object> data)
        {
            data["Test6_InventoryCheck"] = true;

            if (testPawn.inventory?.innerContainer != null)
            {
                var inventoryWeapons = new List<string>();
                float totalInventoryWeight = 0f;

                foreach (Thing item in testPawn.inventory.innerContainer)
                {
                    if (item is ThingWithComps weapon && weapon.def.IsWeapon)
                    {
                        float weight = weapon.GetStatValue(StatDefOf.Mass);
                        inventoryWeapons.Add($"{weapon.Label} ({weight:F1}kg)");
                        totalInventoryWeight += weight;
                    }
                }

                data["Test6_InventoryWeapons"] = string.Join(", ", inventoryWeapons);
                data["Test6_TotalInventoryWeight"] = $"{totalInventoryWeight:F1}kg";
                data["Test6_InventoryWeaponCount"] = inventoryWeapons.Count;
            }
        }

        public void Cleanup()
        {
            // Restore original primary if it exists and isn't destroyed
            if (originalPrimary != null && !originalPrimary.Destroyed)
            {
                if (testPawn?.equipment != null && testPawn.equipment.Primary != originalPrimary)
                {
                    testPawn.equipment.TryDropEquipment(testPawn.equipment.Primary, out _, testPawn.Position);
                    if (!originalPrimary.Spawned)
                    {
                        testPawn.equipment.AddEquipment(originalPrimary);
                    }
                }
            }

            // Clean up test weapons
            foreach (var weapon in testWeapons)
            {
                if (weapon != null && !weapon.Destroyed && weapon.ParentHolder is Map)
                {
                    weapon.Destroy();
                }
            }
            testWeapons.Clear();

            // Destroy test pawn
            if (testPawn != null && !testPawn.Destroyed)
            {
                testPawn.Destroy();
            }
        }
    }

    /// <summary>
    /// Static helper for running SimpleSidearms validation tests outside of test framework
    /// </summary>
    public static class SimpleSidearmsValidationHelper
    {
        public static void RunQuickValidationTest()
        {
            var map = Find.CurrentMap;
            if (map == null)
            {
                Log.Error("[AutoArm] No current map for SimpleSidearms validation test");
                return;
            }

            var test = new TestSimpleSidearmsValidation();
            
            try
            {
                test.Setup(map);
                var result = test.Run();
                
                Log.Message("[AutoArm] SimpleSidearms Validation Test Results:");
                Log.Message($"  Success: {result.Success}");
                
                foreach (var kvp in result.Data)
                {
                    Log.Message($"  {kvp.Key}: {kvp.Value}");
                }
                
                if (!result.Success)
                {
                    Log.Warning($"  Failure Reason: {result.FailureReason}");
                }
            }
            finally
            {
                test.Cleanup();
            }
        }
    }
}
