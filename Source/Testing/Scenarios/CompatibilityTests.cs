using AutoArm.Caching;
using AutoArm.Definitions;
using AutoArm.Helpers;
using AutoArm.Jobs;
using AutoArm.Logging;
using AutoArm.Testing.Helpers;
using AutoArm.Weapons;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace AutoArm.Testing.Scenarios
{
    public class SimpleSidearmsIntegrationTest : ITestScenario
    {
        public string Name => "Simple Sidearms Integration";
        private Pawn testPawn;
        private ThingWithComps primaryWeapon;
        private ThingWithComps sidearmWeapon;

        public void Setup(Map map)
        {
            if (map == null) return;
            
            testPawn = TestHelpers.CreateTestPawn(map);
            if (testPawn != null)
            {
                testPawn.equipment?.DestroyAllEquipment();
                
                // Give pawn a primary weapon
                primaryWeapon = ThingMaker.MakeThing(VanillaWeaponDefOf.Gun_AssaultRifle) as ThingWithComps;
                if (primaryWeapon != null)
                {
                    testPawn.equipment?.AddEquipment(primaryWeapon);
                }
                
                // Create a potential sidearm weapon
                sidearmWeapon = TestHelpers.CreateWeapon(map, VanillaWeaponDefOf.Gun_Autopistol,
                    testPawn.Position + new IntVec3(2, 0, 0), QualityCategory.Good);
                
                if (sidearmWeapon != null)
                {
                    ImprovedWeaponCacheManager.AddWeaponToCache(sidearmWeapon);
                }
            }
        }

        public TestResult Run()
        {
            var result = new TestResult { Success = true };
            
            // Check if SimpleSidearms is loaded
            bool ssLoaded = SimpleSidearmsCompat.IsLoaded();
            result.Data["SimpleSidearms Loaded"] = ssLoaded;
            
            if (!ssLoaded)
            {
                result.Data["Note"] = "SimpleSidearms not loaded - test skipped";
                return result;
            }

            if (testPawn == null)
            {
                return TestResult.Failure("Test pawn creation failed");
            }

            // Test basic integration
            try
            {
                // Test if we can check sidearm validity
                string reason;
                bool canPickup = SimpleSidearmsCompat.CanPickupSidearmInstance(sidearmWeapon, testPawn, out reason);
                result.Data["Can Pickup Sidearm"] = canPickup;
                result.Data["Reason"] = reason ?? "None";
                
                // Test sidearm job creation
                var job = SimpleSidearmsCompat.FindBestSidearmJob(testPawn,
                    (p, w) => WeaponScoringHelper.GetTotalScore(p, w), 60);
                
                result.Data["Sidearm Job Created"] = job != null;
                if (job != null)
                {
                    result.Data["Job Target"] = job.targetA.Thing?.Label ?? "Unknown";
                }
                
                // Count current sidearms
                int sidearmCount = 0;
                if (testPawn.inventory?.innerContainer != null)
                {
                    foreach (Thing t in testPawn.inventory.innerContainer)
                    {
                        if (t is ThingWithComps && t.def.IsWeapon)
                            sidearmCount++;
                    }
                }
                result.Data["Current Sidearms"] = sidearmCount;
            }
            catch (Exception e)
            {
                result.Success = false;
                result.Data["Error"] = $"SimpleSidearms integration error: {e.Message}";
                AutoArmLogger.Error("[TEST] SimpleSidearmsIntegrationTest failed", e);
            }

            return result;
        }

        public void Cleanup()
        {
            sidearmWeapon?.Destroy();
            testPawn?.Destroy();
            primaryWeapon?.Destroy();
        }
    }

    public class SimpleSidearmsWeightLimitTest : ITestScenario
    {
        public string Name => "SimpleSidearms Weight Limit Check";
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
                var weaponConfigs = new[]
                {
                    (VanillaWeaponDefOf.Gun_Autopistol, "Light", 1.5f),
                    (VanillaWeaponDefOf.Gun_AssaultRifle, "Medium", 3.5f),
                    (VanillaWeaponDefOf.Gun_ChainShotgun, "Heavy", 5.0f)
                };

                int offset = 1;
                foreach (var (def, label, expectedWeight) in weaponConfigs)
                {
                    if (def != null)
                    {
                        var pos = TestPositions.GetNearbyPosition(testPawn.Position, offset, offset + 1, map);
                        var weapon = TestHelpers.CreateWeapon(map, def, pos);
                        if (weapon != null)
                        {
                            weapons.Add(weapon);
                            ImprovedWeaponCacheManager.AddWeaponToCache(weapon);
                        }
                        offset++;
                    }
                }
            }
        }

        public TestResult Run()
        {
            if (!SimpleSidearmsCompat.IsLoaded())
            {
                return TestResult.Pass(); // Skip if SS not loaded
            }

            if (testPawn == null)
            {
                return TestResult.Failure("Test pawn creation failed");
            }

            var result = new TestResult { Success = true };

            try
            {
                // Test weight limits for each weapon
                foreach (var weapon in weapons)
                {
                    string reason;
                    bool canPickup = SimpleSidearmsCompat.CanPickupSidearmInstance(weapon, testPawn, out reason);
                    float weight = weapon.GetStatValue(StatDefOf.Mass);

                    string weaponKey = $"{weapon.def.defName}";
                    result.Data[$"{weaponKey}_Weight"] = $"{weight:F1}kg";
                    result.Data[$"{weaponKey}_CanPickup"] = canPickup;
                    
                    if (!canPickup && reason != null)
                    {
                        result.Data[$"{weaponKey}_Reason"] = reason;
                        
                        // Check if weight-related rejection
                        if (reason.IndexOf("weight", StringComparison.OrdinalIgnoreCase) >= 0 || 
                            reason.IndexOf("heavy", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            result.Data["WeightLimitActive"] = true;
                        }
                    }
                }

                // GetTotalWeight method no longer exists in SimpleSidearms
                // We'll use a placeholder for the test
                float totalWeight = 0f; // SimpleSidearmsCompat.GetTotalWeight(testPawn);
                result.Data["Current Total Weight"] = $"{totalWeight:F1}kg";
            }
            catch (Exception e)
            {
                result.Success = false;
                result.Data["Error"] = $"Weight limit test failed: {e.Message}";
                AutoArmLogger.Error("[TEST] SimpleSidearmsWeightLimitTest failed", e);
            }

            return result;
        }

        public void Cleanup()
        {
            foreach (var weapon in weapons)
            {
                weapon?.Destroy();
            }
            weapons.Clear();
            testPawn?.Destroy();
        }
    }

    public class SimpleSidearmsSlotLimitTest : ITestScenario
    {
        public string Name => "SimpleSidearms Slot Management";
        private Pawn testPawn;
        private List<ThingWithComps> ownedWeapons = new List<ThingWithComps>();
        private ThingWithComps betterWeapon;
        private bool originalSetting;

        public void Setup(Map map)
        {
            if (!SimpleSidearmsCompat.IsLoaded()) return;

            // Store and enable sidearm upgrades for test
            originalSetting = AutoArmMod.settings.allowSidearmUpgrades;
            AutoArmMod.settings.allowSidearmUpgrades = true;

            testPawn = TestHelpers.CreateTestPawn(map);
            if (testPawn != null)
            {
                testPawn.equipment?.DestroyAllEquipment();

                // Add some weapons to inventory
                for (int i = 0; i < 3; i++)
                {
                    var weaponDef = i % 2 == 0 ? VanillaWeaponDefOf.Gun_Autopistol : VanillaWeaponDefOf.MeleeWeapon_Knife;
                    if (weaponDef != null)
                    {
                        var weapon = ThingMaker.MakeThing(weaponDef) as ThingWithComps;
                        if (weapon != null)
                        {
                            var comp = weapon.TryGetComp<CompQuality>();
                            comp?.SetQuality(QualityCategory.Poor, ArtGenerationContext.Colony);

                            if (i == 0)
                            {
                                // First weapon as primary
                                testPawn.equipment?.AddEquipment(weapon);
                            }
                            else
                            {
                                // Others as sidearms in inventory
                                if (weapon.Spawned) weapon.DeSpawn();
                                testPawn.inventory?.innerContainer?.TryAdd(weapon);
                            }
                            ownedWeapons.Add(weapon);
                        }
                    }
                }

                // Create a much better weapon nearby
                betterWeapon = TestHelpers.CreateWeapon(map, VanillaWeaponDefOf.Gun_AssaultRifle,
                    TestPositions.GetNearbyPosition(testPawn.Position, 2, 4, map), 
                    QualityCategory.Legendary);
                    
                if (betterWeapon != null)
                {
                    ImprovedWeaponCacheManager.AddWeaponToCache(betterWeapon);
                }
            }
        }

        public TestResult Run()
        {
            if (!SimpleSidearmsCompat.IsLoaded())
            {
                return TestResult.Pass();
            }

            if (!AutoArmMod.settings.allowSidearmUpgrades)
            {
                return TestResult.Pass(); // Test requires sidearm upgrades
            }

            if (testPawn == null)
            {
                return TestResult.Failure("Test pawn creation failed");
            }

            var result = new TestResult { Success = true };

            try
            {
                // Count current weapons
                int inventoryWeaponCount = 0;
                if (testPawn.inventory?.innerContainer != null)
                {
                    foreach (Thing t in testPawn.inventory.innerContainer)
                    {
                        if (t is ThingWithComps && t.def.IsWeapon)
                            inventoryWeaponCount++;
                    }
                }
                
                result.Data["Primary Weapon"] = testPawn.equipment?.Primary?.Label ?? "None";
                result.Data["Inventory Weapons"] = inventoryWeaponCount;
                result.Data["Total Weapons"] = inventoryWeaponCount + (testPawn.equipment?.Primary != null ? 1 : 0);

                // Test sidearm replacement logic
                var job = SimpleSidearmsCompat.FindBestSidearmJob(testPawn,
                    (p, w) => WeaponScoringHelper.GetTotalScore(p, w), 60);

                if (job != null)
                {
                    result.Data["Replacement Job Created"] = true;
                    result.Data["Target Weapon"] = job.targetA.Thing?.Label ?? "Unknown";
                    
                    // Verify it's targeting the better weapon
                    if (job.targetA.Thing == betterWeapon)
                    {
                        result.Data["Correct Target"] = true;
                    }
                    else
                    {
                        result.Data["Warning"] = "Job targets unexpected weapon";
                    }
                }
                else
                {
                    result.Data["Replacement Job Created"] = false;
                    result.Data["Note"] = "No sidearm job created - may be at limit or settings prevent it";
                }

                // Test if we can add more sidearms
                string reason;
                bool canAddMore = SimpleSidearmsCompat.CanPickupSidearmInstance(betterWeapon, testPawn, out reason);
                result.Data["Can Add More Sidearms"] = canAddMore;
                if (!canAddMore && reason != null)
                {
                    result.Data["Limit Reason"] = reason;
                }
            }
            catch (Exception e)
            {
                result.Success = false;
                result.Data["Error"] = $"Slot limit test failed: {e.Message}";
                AutoArmLogger.Error("[TEST] SimpleSidearmsSlotLimitTest failed", e);
            }

            return result;
        }

        public void Cleanup()
        {
            // Restore original setting
            AutoArmMod.settings.allowSidearmUpgrades = originalSetting;

            betterWeapon?.Destroy();
            
            // Destroy pawn (which will destroy equipped/inventory weapons)
            testPawn?.Destroy();
            
            // Clean up any orphaned weapons
            foreach (var weapon in ownedWeapons)
            {
                if (weapon != null && !weapon.Destroyed && weapon.ParentHolder is Map)
                {
                    weapon.Destroy();
                }
            }
            ownedWeapons.Clear();
        }
    }

    public class SimpleSidearmsForcedWeaponTest : ITestScenario
    {
        public string Name => "SimpleSidearms Forced Weapon Handling";
        private Pawn testPawn;
        private ThingWithComps forcedWeapon;
        private ThingWithComps betterWeapon;

        public void Setup(Map map)
        {
            if (!SimpleSidearmsCompat.IsLoaded()) return;

            testPawn = TestHelpers.CreateTestPawn(map);
            if (testPawn != null)
            {
                testPawn.equipment?.DestroyAllEquipment();
                
                // Give pawn a forced weapon
                forcedWeapon = ThingMaker.MakeThing(VanillaWeaponDefOf.Gun_Autopistol) as ThingWithComps;
                if (forcedWeapon != null)
                {
                    testPawn.equipment?.AddEquipment(forcedWeapon);
                    ForcedWeaponHelper.SetForced(testPawn, forcedWeapon);
                }

                // Create a better weapon of same type
                betterWeapon = TestHelpers.CreateWeapon(map, VanillaWeaponDefOf.Gun_AssaultRifle,
                    TestPositions.GetNearbyPosition(testPawn.Position, 2, 4, map), 
                    QualityCategory.Legendary);
                    
                if (betterWeapon != null)
                {
                    ImprovedWeaponCacheManager.AddWeaponToCache(betterWeapon);
                }
            }
        }

        public TestResult Run()
        {
            if (!SimpleSidearmsCompat.IsLoaded())
            {
                return TestResult.Pass();
            }

            if (testPawn == null || forcedWeapon == null)
            {
                return TestResult.Failure("Test setup failed");
            }

            var result = new TestResult { Success = true };

            try
            {
                result.Data["Forced Weapon Upgrades Allowed"] = AutoArmMod.settings.allowForcedWeaponUpgrades;
                result.Data["Weapon Is Forced"] = ForcedWeaponHelper.IsForced(testPawn, forcedWeapon);
                result.Data["Current Weapon"] = forcedWeapon.Label;
                result.Data["Better Weapon Available"] = betterWeapon?.Label ?? "None";

                // Test if forced weapon prevents sidearm pickup
                var job = SimpleSidearmsCompat.FindBestSidearmJob(testPawn,
                    (p, w) => WeaponScoringHelper.GetTotalScore(p, w), 60);

                if (AutoArmMod.settings.allowForcedWeaponUpgrades)
                {
                    // Should allow upgrade even for forced weapons
                    result.Data["Upgrade Job Created"] = job != null;
                    if (job != null)
                    {
                        result.Data["Upgrade Target"] = job.targetA.Thing?.Label ?? "Unknown";
                    }
                }
                else
                {
                    // Should not create job for forced weapon
                    if (job != null && job.targetA.Thing == betterWeapon)
                    {
                        result.Success = false;
                        result.Data["Error"] = "Created upgrade job for forced weapon when not allowed";
                    }
                    else
                    {
                        result.Data["Correctly Blocked"] = true;
                    }
                }
            }
            catch (Exception e)
            {
                result.Success = false;
                result.Data["Error"] = $"Forced weapon test failed: {e.Message}";
                AutoArmLogger.Error("[TEST] SimpleSidearmsForcedWeaponTest failed", e);
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
        public string Name => "Combat Extended Integration";
        private Pawn testPawn;
        private ThingWithComps ceWeapon;

        public void Setup(Map map)
        {
            if (!CECompat.IsLoaded()) return;
            
            testPawn = TestHelpers.CreateTestPawn(map);
            if (testPawn != null)
            {
                // Try to create a CE weapon
                var ceWeaponDef = DefDatabase<ThingDef>.AllDefs
                    .FirstOrDefault(d => d.IsWeapon && d.IsRangedWeapon && 
                                   d.defName.Contains("Gun_"));
                
                if (ceWeaponDef != null)
                {
                    ceWeapon = TestHelpers.CreateWeapon(map, ceWeaponDef,
                        TestPositions.GetNearbyPosition(testPawn.Position, 2, 4, map));
                        
                    if (ceWeapon != null)
                    {
                        ImprovedWeaponCacheManager.AddWeaponToCache(ceWeapon);
                    }
                }
            }
        }

        public TestResult Run()
        {
            var result = new TestResult { Success = true };
            
            bool ceLoaded = CECompat.IsLoaded();
            result.Data["CE Loaded"] = ceLoaded;
            
            if (!ceLoaded)
            {
                result.Data["Note"] = "Combat Extended not loaded - test skipped";
                return result;
            }

            try
            {
                // Test CE ammo system detection
                string detectionResult;
                bool ammoSystemEnabled = CECompat.TryDetectAmmoSystemEnabled(out detectionResult);
                result.Data["Ammo System Enabled"] = ammoSystemEnabled;
                result.Data["Detection Result"] = detectionResult;

                // Test ammo checking
                bool shouldCheckAmmo = CECompat.ShouldCheckAmmo();
                result.Data["Should Check Ammo"] = shouldCheckAmmo;

                // If we have a test weapon, check if pawn can use it
                if (testPawn != null && ceWeapon != null)
                {
                    bool hasAmmo = CECompat.HasAmmoForWeapon(testPawn, ceWeapon);
                    result.Data["Test Weapon"] = ceWeapon.Label;
                    result.Data["Has Ammo"] = hasAmmo;
                    
                    if (!hasAmmo && ammoSystemEnabled)
                    {
                        result.Data["Note"] = "Pawn lacks ammo for CE weapon (expected with ammo system)";
                    }
                }
            }
            catch (Exception e)
            {
                result.Success = false;
                result.Data["Error"] = $"CE integration error: {e.Message}";
                AutoArmLogger.Error("[TEST] CombatExtendedAmmoTest failed", e);
            }

            return result;
        }

        public void Cleanup()
        {
            ceWeapon?.Destroy();
            testPawn?.Destroy();
        }
    }
}
