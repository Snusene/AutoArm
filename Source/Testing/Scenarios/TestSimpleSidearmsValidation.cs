using AutoArm.Caching;
using AutoArm.Compatibility;
using AutoArm.Definitions;
using AutoArm.Helpers;
using AutoArm.Logging;
using AutoArm.Testing.Helpers;
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
            if (map == null || !SimpleSidearmsCompat.IsLoaded) return;

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
                originalPrimary = testPawn.equipment?.Primary;

                var weaponConfigs = new[]
                {
                    (AutoArmDefOf.Gun_Autopistol, QualityCategory.Normal, "Pistol"),
                    (AutoArmDefOf.Gun_AssaultRifle, QualityCategory.Good, "Rifle"),
                    (AutoArmDefOf.MeleeWeapon_Knife, QualityCategory.Normal, "Knife"),
                    (AutoArmDefOf.MeleeWeapon_LongSword, QualityCategory.Excellent, "Sword")
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
                            WeaponCacheManager.AddWeaponToCache(weapon);
                        }
                        offset += 2;
                    }
                }
            }
        }

        public TestResult Run()
        {
            if (!SimpleSidearmsCompat.IsLoaded)
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
                TestUnarmedValidation(testData);

                TestPrimaryWeaponValidation(testData);

                TestSidearmCapacity(testData);

                TestWeightLimits(testData);

                TestUpgradeLogic(testData);

                TestInventoryWeapons(testData);

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
                    if (!reason.Contains("SimpleSidearms") && !reason.Contains("weight"))
                    {
                        AutoArmLogger.Warn($"[TEST] Unarmed pawn cannot pickup {weapon.Label}: {reason}");
                    }
                }
            }
        }

        private void TestPrimaryWeaponValidation(Dictionary<string, object> data)
        {
            var primaryWeapon = testWeapons.FirstOrDefault(w => w.def.IsRangedWeapon);
            if (primaryWeapon != null)
            {
                if (primaryWeapon.Spawned)
                    primaryWeapon.DeSpawn();
                testPawn.equipment?.AddEquipment(primaryWeapon);

                data["Test2_HasPrimary"] = true;
                data["Test2_PrimaryWeapon"] = primaryWeapon.Label;

                foreach (var weapon in testWeapons.Where(w => w != primaryWeapon && w.Spawned).Take(2))
                {
                    string reason;
                    bool canAdd = SimpleSidearmsCompat.CanPickupSidearm(weapon, testPawn, out reason);
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

            var testWeapon = testWeapons.FirstOrDefault(w => w.Spawned);
            if (testWeapon != null)
            {
                string reason;
                bool canAdd = SimpleSidearmsCompat.CanPickupSidearm(testWeapon, testPawn, out reason);
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

            float totalWeight = 0f;
            data["Test4_TotalWeight"] = $"{totalWeight:F1}kg";

            var heavyWeapon = testWeapons.OrderByDescending(w => w.GetStatValue(StatDefOf.Mass)).FirstOrDefault();
            if (heavyWeapon != null && heavyWeapon.Spawned)
            {
                float weight = heavyWeapon.GetStatValue(StatDefOf.Mass);
                data["Test4_HeavyWeapon"] = $"{heavyWeapon.Label} ({weight:F1}kg)";

                string reason;
                bool canPickup = SimpleSidearmsCompat.CanPickupSidearm(heavyWeapon, testPawn, out reason);
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

            var betterWeapon = testWeapons
                .Where(w => w.Spawned && w.def.IsRangedWeapon)
                .OrderByDescending(w => w.GetStatValue(StatDefOf.RangedWeapon_Cooldown))
                .FirstOrDefault();

            if (betterWeapon != null && testPawn.equipment?.Primary != null)
            {
                float currentScore = WeaponCacheManager.GetCachedScore(testPawn, testPawn.equipment.Primary);
                float betterScore = WeaponCacheManager.GetCachedScore(testPawn, betterWeapon);

                data["Test5_CurrentScore"] = currentScore;
                data["Test5_BetterScore"] = betterScore;
                data["Test5_ShouldUpgrade"] = betterScore > currentScore * Constants.WeaponUpgradeThreshold;
            }

            var betterSidearm = testWeapons.Where(w => w.Spawned && w != testPawn.equipment?.Primary)
                .OrderByDescending(w => WeaponCacheManager.GetCachedScore(testPawn, w))
                .FirstOrDefault();
            var sidearmJob = betterSidearm != null ? SimpleSidearmsCompat.TryGetWeaponJob(testPawn, betterSidearm) : null;

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

            foreach (var weapon in testWeapons)
            {
                if (weapon != null && !weapon.Destroyed && weapon.ParentHolder is Map)
                {
                    TestHelpers.SafeDestroyWeapon(weapon);
                }
            }
            testWeapons.Clear();

            if (testPawn != null && !testPawn.Destroyed)
            {
                TestHelpers.SafeDestroyPawn(testPawn);
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
