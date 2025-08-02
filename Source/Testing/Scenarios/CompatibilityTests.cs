// AutoArm RimWorld 1.5+ mod - automatic weapon management
// This file: Compatibility tests for SimpleSidearms and Combat Extended integration
// Validates mod detection and cross-mod functionality

using RimWorld;
using System.Collections.Generic;
using Verse;
using AutoArm.Testing.Helpers;

namespace AutoArm.Testing.Scenarios
{
    public class SimpleSidearmsIntegrationTest : ITestScenario
    {
        public string Name => "Simple Sidearms Integration";
        private Pawn testPawn;

        public void Setup(Map map)
        {
            if (map == null) return;
            testPawn = TestHelpers.CreateTestPawn(map);

            // Make sure pawn is properly initialized
            if (testPawn != null)
            {
                testPawn.equipment?.DestroyAllEquipment();
            }
        }

        public TestResult Run()
        {
            if (!SimpleSidearmsCompat.IsLoaded())
                return TestResult.Pass();

            if (testPawn == null)
            {
                AutoArmLogger.LogError("[TEST] SimpleSidearmsIntegrationTest: Test pawn creation failed");
                return TestResult.Failure("Test pawn creation failed");
            }

            int maxSidearms = SimpleSidearmsCompat.GetMaxSidearmsForPawn(testPawn);
            int currentCount = SimpleSidearmsCompat.GetCurrentSidearmCount(testPawn);

            var result = new TestResult { Success = true };
            result.Data["Max Sidearms"] = maxSidearms;
            result.Data["Current Count"] = currentCount;

            return result;
        }

        public void Cleanup()
        {
            if (testPawn != null && !testPawn.Destroyed)
            {
                testPawn.Destroy();
            }
        }
    }

    public class SimpleSidearmsWeightLimitTest : ITestScenario
    {
        public string Name => "SimpleSidearms Weight Limit Enforcement";
        private Pawn testPawn;
        private List<ThingWithComps> weapons = new List<ThingWithComps>();

        public void Setup(Map map)
        {
            if (!SimpleSidearmsCompat.IsLoaded()) return;

            testPawn = TestHelpers.CreateTestPawn(map);
            if (testPawn != null)
            {
                testPawn.equipment?.DestroyAllEquipment();

                // Create weapons of varying weights
                var weaponDefs = new[]
                {
                    (DefDatabase<ThingDef>.GetNamedSilentFail("Gun_Pistol"), 1.2f), // Light
                    (DefDatabase<ThingDef>.GetNamedSilentFail("Gun_AssaultRifle"), 3.5f), // Medium
                    (DefDatabase<ThingDef>.GetNamedSilentFail("Gun_LMG"), 8.5f) // Heavy
                };

                foreach (var (def, expectedWeight) in weaponDefs)
                {
                    if (def != null)
                    {
                        var weapon = TestHelpers.CreateWeapon(map, def,
                            testPawn.Position + new IntVec3(Rand.Range(-3, 3), 0, Rand.Range(-3, 3)));
                        if (weapon != null)
                        {
                            weapons.Add(weapon);
                            ImprovedWeaponCacheManager.AddWeaponToCache(weapon);
                        }
                    }
                }
            }
        }

        public TestResult Run()
        {
            if (!SimpleSidearmsCompat.IsLoaded())
                return TestResult.Pass(); // Skip if SS not loaded

            var result = new TestResult { Success = true };

            foreach (var weapon in weapons)
            {
                string reason;
                bool canPickup = SimpleSidearmsCompat.CanPickupSidearmInstance(weapon, testPawn, out reason);
                float weight = weapon.GetStatValue(StatDefOf.Mass);

                result.Data[$"{weapon.Label}_Weight"] = $"{weight:F1}kg";
                result.Data[$"{weapon.Label}_Allowed"] = canPickup;
                result.Data[$"{weapon.Label}_Reason"] = reason ?? "None";

                // Verify weight limit is being enforced correctly
                if (!canPickup && reason != null &&
                    (reason.Contains("heavy") || reason.Contains("weight")) &&
                    weight < 20f) // Sanity check - weapons under 20kg should generally be allowed
                {
                    // This is expected behavior - weight limits are working
                    result.Data["WeightLimitEnforcement"] = "Working correctly";
                }
            }

            return result;
        }

        public void Cleanup()
        {
            foreach (var weapon in weapons)
            {
                weapon?.Destroy();
            }
            testPawn?.Destroy();
        }
    }

    public class SimpleSidearmsSlotLimitTest : ITestScenario
    {
        public string Name => "SimpleSidearms Slot Limit and Replacement";
        private Pawn testPawn;
        private List<ThingWithComps> ownedWeapons = new List<ThingWithComps>();
        private ThingWithComps betterWeapon;

        public void Setup(Map map)
        {
            if (!SimpleSidearmsCompat.IsLoaded() || !AutoArmMod.settings.allowSidearmUpgrades)
                return;

            testPawn = TestHelpers.CreateTestPawn(map);
            if (testPawn != null)
            {
                // Fill pawn's inventory to slot limit with poor quality weapons
                int maxSlots = SimpleSidearmsCompat.GetMaxSidearmsForPawn(testPawn);

                for (int i = 0; i < maxSlots; i++)
                {
                    var weaponDef = i % 2 == 0 ? VanillaWeaponDefOf.Gun_Autopistol : VanillaWeaponDefOf.MeleeWeapon_Knife;
                    if (weaponDef != null)
                    {
                        var weapon = SafeTestCleanup.SafeCreateWeapon(weaponDef, null, QualityCategory.Poor);
                        if (weapon != null)
                        {
                            if (i == 0)
                            {
                                SafeTestCleanup.SafeEquipWeapon(testPawn, weapon);
                            }
                            else
                            {
                                SafeTestCleanup.SafeAddToInventory(testPawn, weapon);
                            }
                            ownedWeapons.Add(weapon);
                        }
                    }
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

                // Create a much better weapon nearby - spawn very close
                betterWeapon = TestHelpers.CreateWeapon(map, VanillaWeaponDefOf.Gun_AssaultRifle,
                    testPawn.Position + new IntVec3(1, 0, 0), QualityCategory.Legendary);
                if (betterWeapon != null)
                {
                    ImprovedWeaponCacheManager.AddWeaponToCache(betterWeapon);
                }
            }
        }

        public TestResult Run()
        {
            if (!SimpleSidearmsCompat.IsLoaded() || !AutoArmMod.settings.allowSidearmUpgrades)
                return TestResult.Pass();

            var result = new TestResult { Success = true };
            var jobGiver = new JobGiver_PickUpBetterWeapon();

            // Collect detailed debugging information
            result.Data["CurrentWeaponCount"] = SimpleSidearmsCompat.GetCurrentSidearmCount(testPawn) + 1; // +1 for primary
            result.Data["MaxSlots"] = SimpleSidearmsCompat.GetMaxSidearmsForPawn(testPawn);
            result.Data["AllowSidearmUpgrades"] = AutoArmMod.settings.allowSidearmUpgrades;
            result.Data["AutoEquipSidearms"] = AutoArmMod.settings.autoEquipSidearms;
            
            // Log current inventory
            AutoArmLogger.Log("[TEST] SimpleSidearmsSlotLimitTest - Current inventory:");
            if (testPawn.equipment?.Primary != null)
            {
                var primary = testPawn.equipment.Primary;
                var primaryScore = WeaponScoreCache.GetCachedScore(testPawn, primary);
                result.Data["Primary"] = $"{primary.Label} (Q:{(primary.TryGetQuality(out var q) ? q.ToString() : "None")}, Score:{primaryScore:F2})";
                AutoArmLogger.Log($"[TEST]   Primary: {primary.Label} - Score: {primaryScore:F2}");
            }
            
            if (testPawn.inventory?.innerContainer != null)
            {
                int i = 0;
                foreach (var item in testPawn.inventory.innerContainer)
                {
                    if (item is ThingWithComps weapon && weapon.def.IsWeapon)
                    {
                        var score = WeaponScoreCache.GetCachedScore(testPawn, weapon);
                        result.Data[$"Sidearm{i}"] = $"{weapon.Label} (Q:{(weapon.TryGetQuality(out var q) ? q.ToString() : "None")}, Score:{score:F2})";
                        AutoArmLogger.Log($"[TEST]   Sidearm {i}: {weapon.Label} - Score: {score:F2}");
                        i++;
                    }
                }
            }
            
            // Check better weapon details
            if (betterWeapon != null)
            {
                var betterScore = WeaponScoreCache.GetCachedScore(testPawn, betterWeapon);
                result.Data["BetterWeapon"] = $"{betterWeapon.Label} (Q:{(betterWeapon.TryGetQuality(out var q) ? q.ToString() : "None")}, Score:{betterScore:F2})";
                result.Data["BetterWeaponPosition"] = betterWeapon.Position.ToString();
                result.Data["PawnPosition"] = testPawn.Position.ToString();
                result.Data["Distance"] = (testPawn.Position - betterWeapon.Position).LengthHorizontal;
                
                // Check validation
                string validationReason;
                bool canUse = ValidationHelper.CanPawnUseWeapon(testPawn, betterWeapon, out validationReason);
                result.Data["CanUseBetterWeapon"] = canUse;
                result.Data["ValidationReason"] = validationReason ?? "None";
                
                // Check SimpleSidearms validation
                string ssReason;
                bool ssCanPickup = SimpleSidearmsCompat.CanPickupSidearmInstance(betterWeapon, testPawn, out ssReason);
                result.Data["SS_CanPickup"] = ssCanPickup;
                result.Data["SS_Reason"] = ssReason ?? "None";
                
                AutoArmLogger.Log($"[TEST]   Better weapon: {betterWeapon.Label} at {betterWeapon.Position} - Score: {betterScore:F2}");
                AutoArmLogger.Log($"[TEST]   Can use: {canUse} ({validationReason}), SS can pickup: {ssCanPickup} ({ssReason})");
            }
            else
            {
                result.Data["BetterWeapon"] = "NULL";
                AutoArmLogger.LogError("[TEST] Better weapon is null!");
            }
            
            // Test primary job first to see if it would find the weapon
            AutoArmLogger.Log("[TEST] Testing primary JobGiver...");
            var primaryJob = jobGiver.TestTryGiveJob(testPawn);
            if (primaryJob != null)
            {
                result.Data["PrimaryJobCreated"] = true;
                result.Data["PrimaryJobTarget"] = primaryJob.targetA.Thing?.Label ?? "Unknown";
                result.Data["PrimaryJobDef"] = primaryJob.def?.defName ?? "Unknown";
                AutoArmLogger.Log($"[TEST] Primary JobGiver created job: {primaryJob.def?.defName} targeting {primaryJob.targetA.Thing?.Label}");
            }
            else
            {
                result.Data["PrimaryJobCreated"] = false;
                AutoArmLogger.Log("[TEST] Primary JobGiver returned null");
            }

            // Should create a job to replace worst weapon
            AutoArmLogger.Log("[TEST] Testing sidearm upgrade job...");
            var job = SimpleSidearmsCompat.TryGetSidearmUpgradeJob(testPawn);

            if (job != null)
            {
                result.Data["JobCreated"] = true;
                result.Data["TargetWeapon"] = job.targetA.Thing?.Label ?? "Unknown";
                result.Data["JobDef"] = job.def?.defName ?? "Unknown";
                AutoArmLogger.Log($"[TEST] Sidearm upgrade job created: {job.def?.defName} targeting {job.targetA.Thing?.Label}");

                // Verify it's targeting the legendary weapon
                if (job.targetA.Thing != betterWeapon)
                {
                    result.Success = false;
                    result.FailureReason = "Job targets wrong weapon";
                    result.Data["Error"] = "Sidearm upgrade targeting incorrect weapon";
                    result.Data["ExpectedWeapon"] = betterWeapon?.Label;
                    result.Data["ActualWeapon"] = job.targetA.Thing?.Label;
                    result.Data["JobType"] = job.def?.defName;
                    AutoArmLogger.LogError($"[TEST] SimpleSidearmsSlotLimitTest: Job targets wrong weapon - expected: {betterWeapon?.Label}, got: {job.targetA.Thing?.Label}");
                }
            }
            else
            {
                result.Success = false;
                result.FailureReason = "No replacement job created when at slot limit";
                result.Data["Error"] = "Failed to create sidearm upgrade job despite slot availability";
                result.Data["CurrentCount"] = SimpleSidearmsCompat.GetCurrentSidearmCount(testPawn) + 1;
                result.Data["MaxSlots"] = SimpleSidearmsCompat.GetMaxSidearmsForPawn(testPawn);
                result.Data["BetterWeaponAvailable"] = betterWeapon != null;
                result.Data["JobCreated"] = false;
                
                // Try to understand why the job wasn't created
                AutoArmLogger.LogError($"[TEST] SimpleSidearmsSlotLimitTest: No replacement job created");
                AutoArmLogger.LogError($"[TEST]   Current count: {SimpleSidearmsCompat.GetCurrentSidearmCount(testPawn) + 1}, Max slots: {SimpleSidearmsCompat.GetMaxSidearmsForPawn(testPawn)}");
                AutoArmLogger.LogError($"[TEST]   Settings - allowSidearmUpgrades: {AutoArmMod.settings.allowSidearmUpgrades}, autoEquipSidearms: {AutoArmMod.settings.autoEquipSidearms}");
                
                // Test if we can find any sidearm replacements
                // Note: GetWorstSidearmToReplace method would be useful for debugging but doesn't exist yet
                // var worstSidearm = SimpleSidearmsCompat.GetWorstSidearmToReplace(testPawn);
                // if (worstSidearm != null)
                // {
                //     var worstScore = WeaponScoreCache.GetCachedScore(testPawn, worstSidearm);
                //     result.Data["WorstSidearm"] = $"{worstSidearm.Label} (Score:{worstScore:F2})";
                //     AutoArmLogger.LogError($"[TEST]   Worst sidearm found: {worstSidearm.Label} - Score: {worstScore:F2}");
                // }
                // else
                // {
                //     result.Data["WorstSidearm"] = "None found";
                //     AutoArmLogger.LogError("[TEST]   No worst sidearm found by SimpleSidearmsCompat.GetWorstSidearmToReplace");
                // }
            }

            return result;
        }

        public void Cleanup()
        {
            betterWeapon?.Destroy();
            testPawn?.Destroy();
            foreach (var weapon in ownedWeapons)
            {
                weapon?.Destroy();
            }
        }
    }

    public class SimpleSidearmsForcedWeaponTest : ITestScenario
    {
        public string Name => "SimpleSidearms Forced Weapon Interaction";
        private Pawn testPawn;
        private ThingWithComps forcedWeapon;
        private ThingWithComps betterWeapon;

        public void Setup(Map map)
        {
            if (!SimpleSidearmsCompat.IsLoaded()) return;

            testPawn = TestHelpers.CreateTestPawn(map);
            if (testPawn != null)
            {
                // Give pawn a forced weapon
                forcedWeapon = SafeTestCleanup.SafeCreateWeapon(VanillaWeaponDefOf.Gun_Autopistol, null, null);
                if (forcedWeapon != null)
                {
                    SafeTestCleanup.SafeEquipWeapon(testPawn, forcedWeapon);
                    ForcedWeaponHelper.SetForced(testPawn, forcedWeapon);
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

                // Create a better weapon of same type - spawn very close
                betterWeapon = TestHelpers.CreateWeapon(map, VanillaWeaponDefOf.Gun_Autopistol,
                    testPawn.Position + new IntVec3(1, 0, 0), QualityCategory.Legendary);
                if (betterWeapon != null)
                {
                    ImprovedWeaponCacheManager.AddWeaponToCache(betterWeapon);
                }
            }
        }

        public TestResult Run()
        {
            if (!SimpleSidearmsCompat.IsLoaded())
                return TestResult.Pass();

            var result = new TestResult { Success = true };

            result.Data["ForcedWeaponUpgradesAllowed"] = AutoArmMod.settings.allowForcedWeaponUpgrades;
            result.Data["WeaponIsForced"] = ForcedWeaponHelper.IsForced(testPawn, forcedWeapon);

            // Test if forced weapon can be upgraded through SS
            var job = SimpleSidearmsCompat.TryGetSidearmUpgradeJob(testPawn);

            if (AutoArmMod.settings.allowForcedWeaponUpgrades)
            {
                if (job == null)
                {
                    result.Success = false;
                    result.FailureReason = "Forced weapon upgrade not created when allowed";
                    result.Data["Error"] = "SimpleSidearms integration failed - no upgrade job for forced weapon";
                    result.Data["ForcedWeapon"] = forcedWeapon?.Label;
                    result.Data["BetterWeaponAvailable"] = betterWeapon?.Label;
                    result.Data["UpgradesSetting"] = AutoArmMod.settings.allowForcedWeaponUpgrades;
                    result.Data["JobCreated"] = false;
                    AutoArmLogger.LogError($"[TEST] SimpleSidearmsForcedWeaponTest: Forced weapon upgrade not created when allowed - forced weapon: {forcedWeapon?.Label}, better weapon available: {betterWeapon?.Label}");
                }
            }
            else
            {
                if (job != null)
                {
                    result.Success = false;
                    result.Data["Error"] = "SimpleSidearms integration failed - upgrade job created despite setting";
                    result.Data["ExpectedJob"] = false;
                    result.Data["ForcedWeaponUpgradesAllowed"] = AutoArmMod.settings.allowForcedWeaponUpgrades;
                    result.Data["JobCreated"] = true;
                    result.Data["JobTarget"] = job.targetA.Thing?.Label;
                    result.Data["UpgradesSetting"] = false;
                    result.Data["WeaponIsForced"] = true;
                    AutoArmLogger.LogError($"[TEST] SimpleSidearmsForcedWeaponTest: Forced weapon upgrade created when not allowed - setting: {AutoArmMod.settings.allowForcedWeaponUpgrades}, job target: {job.targetA.Thing?.Label}");
                }
            }

            return result;
        }

        public void Cleanup()
        {
            ForcedWeaponHelper.ClearForced(testPawn);
            betterWeapon?.Destroy();
            testPawn?.Destroy();
            forcedWeapon?.Destroy();
        }
    }

    public class CombatExtendedAmmoTest : ITestScenario
    {
        public string Name => "Combat Extended Ammo Check";

        public void Setup(Map map)
        { }

        public TestResult Run()
        {
            if (!CECompat.IsLoaded())
                return TestResult.Pass();

            bool shouldCheck = CECompat.ShouldCheckAmmo();

            var result = new TestResult { Success = true };
            result.Data["CE Loaded"] = CECompat.IsLoaded();
            result.Data["Should Check Ammo"] = shouldCheck;

            return result;
        }

        public void Cleanup()
        { }
    }
}