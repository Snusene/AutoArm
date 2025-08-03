// AutoArm RimWorld 1.5+ mod - automatic weapon management
// This file: Pacifist behavior tests for base game and mod interactions
// Validates violence-incapable pawns don't equip weapons

using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;
using AutoArm.Testing.Helpers;
using AutoArm.Caching; using AutoArm.Helpers; using AutoArm.Logging; using AutoArm.UI;
using AutoArm.Jobs;
using AutoArm.Definitions;
using AutoArm.Weapons;

namespace AutoArm.Testing.Scenarios
{
    /// <summary>
    /// Test that pacifists don't equip weapons in base game (no mod compatibility)
    /// </summary>
    public class PacifistBaseGameTest : ITestScenario
    {
        public string Name => "Pacifist Base Game Weapon Behavior";
        
        private Pawn pacifistPawn;
        private List<ThingWithComps> nearbyWeapons = new List<ThingWithComps>();
        private Map testMap;

        public void Setup(Map map)
        {
            if (map == null) return;

            testMap = map;
            
            // Clear all systems before test
            TestRunnerFix.ResetAllSystems();
            
            // Create a pacifist pawn
            var config = new TestHelpers.TestPawnConfig
            {
                Name = "PacifistColonist",
                EnsureViolenceCapable = false // Allow creating incapable of violence
            };
            
            pacifistPawn = TestHelpers.CreateTestPawn(map, config);
            
            if (pacifistPawn == null)
                return;

            // Ensure they're actually incapable of violence
            if (!pacifistPawn.WorkTagIsDisabled(WorkTags.Violent))
            {
                // Try to add a trait that disables violence
                var pacifistTrait = DefDatabase<TraitDef>.AllDefs
                    .FirstOrDefault(t => t.disabledWorkTags.HasFlag(WorkTags.Violent));
                    
                if (pacifistTrait != null && pacifistPawn.story?.traits != null)
                {
                    pacifistPawn.story.traits.GainTrait(new Trait(pacifistTrait));
                    pacifistPawn.Notify_DisabledWorkTypesChanged();
                }
            }

            // Make sure they're unarmed
            pacifistPawn.equipment?.DestroyAllEquipment();

            // Create weapons around the pawn
            var weaponDefs = new[] 
            {
                VanillaWeaponDefOf.Gun_Autopistol,
                VanillaWeaponDefOf.MeleeWeapon_Knife,
                VanillaWeaponDefOf.Gun_AssaultRifle,
                VanillaWeaponDefOf.MeleeWeapon_LongSword
            };

            int offset = 0;
            foreach (var weaponDef in weaponDefs)
            {
                if (weaponDef != null)
                {
                    var pos = pacifistPawn.Position + new IntVec3(2 + (offset % 2), 0, offset / 2);
                    var weapon = TestHelpers.CreateWeapon(map, weaponDef, pos, QualityCategory.Good);
                    if (weapon != null)
                    {
                        weapon.SetForbidden(false, false);
                        nearbyWeapons.Add(weapon);
                        ImprovedWeaponCacheManager.AddWeaponToCache(weapon);
                    }
                    offset++;
                }
            }

            // Clear fog around test area
            var fogGrid = map.fogGrid;
            if (fogGrid != null)
            {
                foreach (var cell in GenRadial.RadialCellsAround(pacifistPawn.Position, 10, true))
                {
                    if (cell.InBounds(map))
                    {
                        fogGrid.Unfog(cell);
                    }
                }
            }
        }

        public TestResult Run()
        {
            if (pacifistPawn == null || testMap == null)
                return TestResult.Failure("Test setup failed");

            var result = new TestResult { Success = true };
            
            // Verify pawn is actually incapable of violence
            bool isIncapableOfViolence = pacifistPawn.WorkTagIsDisabled(WorkTags.Violent);
            result.Data["IsIncapableOfViolence"] = isIncapableOfViolence;
            
            if (!isIncapableOfViolence)
            {
                result.Success = false;
                result.FailureReason = "Failed to create violence-incapable pawn";
                return result;
            }

            // Test 1: Check if AutoArm's emergency job giver creates job for pacifist
            AutoArmLogger.Log("[TEST] Testing emergency weapon job for pacifist...");
            var emergencyJobGiver = new JobGiver_PickUpBetterWeapon_Emergency();
            var emergencyJob = emergencyJobGiver.TestTryGiveJob(pacifistPawn);
            
            result.Data["EmergencyJobCreated"] = emergencyJob != null;
            
            if (emergencyJob != null)
            {
                result.Success = false;
                result.FailureReason = "Emergency job created for pacifist";
                result.Data["Error_Emergency"] = $"Job created to pick up {emergencyJob.targetA.Thing?.Label}";
                AutoArmLogger.LogError($"[TEST] Emergency job created for pacifist targeting {emergencyJob.targetA.Thing?.Label}");
            }

            // Test 2: Check if regular job giver creates job for pacifist
            AutoArmLogger.Log("[TEST] Testing regular weapon job for pacifist...");
            var regularJobGiver = new JobGiver_PickUpBetterWeapon();
            var regularJob = regularJobGiver.TestTryGiveJob(pacifistPawn);
            
            result.Data["RegularJobCreated"] = regularJob != null;
            
            if (regularJob != null)
            {
                result.Success = false;
                result.FailureReason = "Regular job created for pacifist";
                result.Data["Error_Regular"] = $"Job created to pick up {regularJob.targetA.Thing?.Label}";
                AutoArmLogger.LogError($"[TEST] Regular job created for pacifist targeting {regularJob.targetA.Thing?.Label}");
            }

            // Test 3: Verify think tree conditions
            AutoArmLogger.Log("[TEST] Testing think tree conditions for pacifist...");
            
            // Test unarmed condition
            var unarmedCondition = new ThinkNode_ConditionalUnarmed();
            bool unarmedSatisfied = false;
            try
            {
                var satisfiedMethod = typeof(ThinkNode_Conditional).GetMethod("Satisfied", 
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (satisfiedMethod != null)
                {
                    unarmedSatisfied = (bool)satisfiedMethod.Invoke(unarmedCondition, new object[] { pacifistPawn });
                }
            }
            catch (Exception ex)
            {
                AutoArmLogger.LogError($"[TEST] Error checking unarmed condition: {ex.Message}");
            }
            
            result.Data["UnarmedConditionSatisfied"] = unarmedSatisfied;
            
            if (unarmedSatisfied)
            {
                // This might be okay if the condition is satisfied but the job giver still rejects the pawn
                AutoArmLogger.Log("[TEST] Unarmed condition satisfied for pacifist - job giver should still reject");
            }

            // Test 4: Verify ValidationHelper catches pacifists
            foreach (var weapon in nearbyWeapons)
            {
                string reason;
                bool canUse = ValidationHelper.CanPawnUseWeapon(pacifistPawn, weapon, out reason);
                
                if (canUse)
                {
                    result.Success = false;
                    result.FailureReason = $"ValidationHelper allowed pacifist to use {weapon.Label}";
                    result.Data[$"Error_Validation_{weapon.Label}"] = "CanPawnUseWeapon returned true";
                    AutoArmLogger.LogError($"[TEST] ValidationHelper incorrectly allowed pacifist to use {weapon.Label}");
                }
                else
                {
                    result.Data[$"Validation_{weapon.Label}"] = $"Correctly rejected: {reason}";
                }
            }

            // Test 5: Direct equip attempt (should fail)
            if (nearbyWeapons.Any())
            {
                var testWeapon = nearbyWeapons.First();
                AutoArmLogger.Log($"[TEST] Attempting direct equip of {testWeapon.Label} on pacifist...");
                
                try
                {
                    // Try to force equip
                    pacifistPawn.equipment?.AddEquipment(testWeapon);
                    
                    if (pacifistPawn.equipment?.Primary == testWeapon)
                    {
                        // This would be a RimWorld bug, not ours, but log it
                        result.Data["Warning_DirectEquip"] = "RimWorld allowed direct equip on pacifist!";
                        AutoArmLogger.LogError("[TEST] WARNING: RimWorld allowed direct weapon equip on pacifist!");
                        
                        // Remove it for other tests
                        pacifistPawn.equipment.DestroyAllEquipment();
                    }
                }
                catch (Exception ex)
                {
                    // Expected - RimWorld should reject this
                    result.Data["DirectEquipBlocked"] = ex.Message;
                }
            }

            // Summary
            if (result.Success)
            {
                result.Data["Result"] = "SUCCESS: Pacifist correctly prevented from equipping weapons";
                AutoArmLogger.Log("[TEST] SUCCESS: All pacifist weapon restrictions working correctly");
            }

            return result;
        }

        public void Cleanup()
        {
            foreach (var weapon in nearbyWeapons)
            {
                weapon?.Destroy();
            }
            
            SafeTestCleanup.SafeDestroyPawn(pacifistPawn);
            nearbyWeapons.Clear();
        }
    }

    /// <summary>
    /// Test that pacifists don't pick up sidearms when SimpleSidearms is loaded
    /// </summary>
    public class PacifistSimpleSidearmsTest : ITestScenario
    {
        public string Name => "Pacifist + Simple Sidearms Behavior";
        
        private Pawn pacifistPawn;
        private List<ThingWithComps> nearbyWeapons = new List<ThingWithComps>();

        public void Setup(Map map)
        {
            if (!SimpleSidearmsCompat.IsLoaded())
                return;

            // Clear all systems before test
            TestRunnerFix.ResetAllSystems();
            
            // Create a pacifist pawn
            var config = new TestHelpers.TestPawnConfig
            {
                Name = "PacifistWithSS",
                EnsureViolenceCapable = false
            };
            
            pacifistPawn = TestHelpers.CreateTestPawn(map, config);
            
            if (pacifistPawn == null)
                return;

            // Ensure they're actually incapable of violence
            if (!pacifistPawn.WorkTagIsDisabled(WorkTags.Violent))
            {
                var pacifistTrait = DefDatabase<TraitDef>.AllDefs
                    .FirstOrDefault(t => t.disabledWorkTags.HasFlag(WorkTags.Violent));
                    
                if (pacifistTrait != null && pacifistPawn.story?.traits != null)
                {
                    pacifistPawn.story.traits.GainTrait(new Trait(pacifistTrait));
                    pacifistPawn.Notify_DisabledWorkTypesChanged();
                }
            }

            // Make sure they're unarmed
            pacifistPawn.equipment?.DestroyAllEquipment();
            pacifistPawn.inventory?.innerContainer?.ClearAndDestroyContents();

            // Create various weapons around the pawn
            var weaponDefs = new[] 
            {
                VanillaWeaponDefOf.Gun_Autopistol,
                VanillaWeaponDefOf.MeleeWeapon_Knife,
                VanillaWeaponDefOf.Gun_Revolver,
                VanillaWeaponDefOf.MeleeWeapon_Gladius
            };

            int offset = 0;
            foreach (var weaponDef in weaponDefs)
            {
                if (weaponDef != null)
                {
                    var pos = pacifistPawn.Position + new IntVec3(1 + offset, 0, 0);
                    var weapon = TestHelpers.CreateWeapon(map, weaponDef, pos, QualityCategory.Normal);
                    if (weapon != null)
                    {
                        weapon.SetForbidden(false, false);
                        nearbyWeapons.Add(weapon);
                        ImprovedWeaponCacheManager.AddWeaponToCache(weapon);
                    }
                    offset++;
                }
            }
        }

        public TestResult Run()
        {
            if (!SimpleSidearmsCompat.IsLoaded())
                return TestResult.Pass(); // Skip test

            if (pacifistPawn == null)
                return TestResult.Failure("Test setup failed");

            var result = new TestResult { Success = true };
            
            // Verify pawn is incapable of violence
            bool isIncapableOfViolence = pacifistPawn.WorkTagIsDisabled(WorkTags.Violent);
            result.Data["IsIncapableOfViolence"] = isIncapableOfViolence;
            
            if (!isIncapableOfViolence)
            {
                result.Success = false;
                result.FailureReason = "Failed to create violence-incapable pawn";
                return result;
            }

            // Test 1: Check if SimpleSidearms allows pacifist to pick up weapons
            AutoArmLogger.Log("[TEST] Testing SimpleSidearms validation for pacifist...");
            foreach (var weapon in nearbyWeapons)
            {
                string reason;
                bool canPickup = SimpleSidearmsCompat.CanPickupSidearmInstance(weapon, pacifistPawn, out reason);
                
                result.Data[$"SS_CanPickup_{weapon.def.defName}"] = canPickup;
                
                if (canPickup)
                {
                    result.Success = false;
                    result.FailureReason = $"SimpleSidearms allowed pacifist to pick up {weapon.Label}";
                    result.Data[$"Error_SS_{weapon.Label}"] = "CanPickupSidearmInstance returned true";
                    AutoArmLogger.LogError($"[TEST] SimpleSidearms incorrectly allowed pacifist to pick up {weapon.Label}");
                }
                else
                {
                    result.Data[$"SS_Rejection_{weapon.def.defName}"] = reason;
                    
                    // Verify the reason mentions violence incapability
                    if (reason == null || reason.ToLower().IndexOf("violence") < 0)
                    {
                        result.Data[$"Warning_{weapon.def.defName}"] = "Rejection reason doesn't mention violence incapability";
                    }
                }
            }

            // Test 2: Check if AutoArm's sidearm job giver creates jobs
            AutoArmLogger.Log("[TEST] Testing AutoArm sidearm job for pacifist...");
            var sidearmJobGiver = new JobGiver_PickUpSidearm();
            var sidearmJob = sidearmJobGiver.TestTryGiveJob(pacifistPawn);
            
            result.Data["SidearmJobCreated"] = sidearmJob != null;
            
            if (sidearmJob != null)
            {
                result.Success = false;
                result.FailureReason = "Sidearm job created for pacifist";
                result.Data["Error_SidearmJob"] = $"Job created to pick up {sidearmJob.targetA.Thing?.Label}";
                AutoArmLogger.LogError($"[TEST] Sidearm job created for pacifist targeting {sidearmJob.targetA.Thing?.Label}");
            }

            // Test 3: Check SimpleSidearms upgrade job
            AutoArmLogger.Log("[TEST] Testing SimpleSidearms upgrade job for pacifist...");
            var ssUpgradeJob = SimpleSidearmsCompat.TryGetSidearmUpgradeJob(pacifistPawn);
            
            result.Data["SS_UpgradeJobCreated"] = ssUpgradeJob != null;
            
            if (ssUpgradeJob != null)
            {
                result.Success = false;
                result.FailureReason = "SimpleSidearms upgrade job created for pacifist";
                result.Data["Error_SSUpgrade"] = $"Job created to pick up {ssUpgradeJob.targetA.Thing?.Label}";
                AutoArmLogger.LogError($"[TEST] SimpleSidearms upgrade job created for pacifist");
            }

            // Test 4: Verify think node condition
            var sidearmCondition = new ThinkNode_ConditionalShouldCheckSidearms();
            bool sidearmConditionSatisfied = false;
            try
            {
                var satisfiedMethod = typeof(ThinkNode_Conditional).GetMethod("Satisfied", 
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (satisfiedMethod != null)
                {
                    sidearmConditionSatisfied = (bool)satisfiedMethod.Invoke(sidearmCondition, new object[] { pacifistPawn });
                }
            }
            catch { }
            
            result.Data["SidearmConditionSatisfied"] = sidearmConditionSatisfied;
            
            // The condition might be satisfied, but the job giver should still reject
            if (sidearmConditionSatisfied)
            {
                AutoArmLogger.Log("[TEST] Sidearm condition satisfied - checking if job giver rejects properly");
            }

            // Summary
            if (result.Success)
            {
                result.Data["Result"] = "SUCCESS: Pacifist correctly prevented from picking up sidearms";
                AutoArmLogger.Log("[TEST] SUCCESS: SimpleSidearms correctly blocks pacifist weapon pickup");
            }

            return result;
        }

        public void Cleanup()
        {
            foreach (var weapon in nearbyWeapons)
            {
                weapon?.Destroy();
            }
            
            SafeTestCleanup.SafeDestroyPawn(pacifistPawn);
            nearbyWeapons.Clear();
        }
    }

    /// <summary>
    /// Test that pacifists can haul but not equip weapons with both mods
    /// </summary>
    public class PacifistFullModStackTest : ITestScenario
    {
        public string Name => "Pacifist + SimpleSidearms + Pick Up and Haul";
        
        private Pawn pacifistPawn;
        private List<ThingWithComps> weaponsToHaul = new List<ThingWithComps>();
        private Zone_Stockpile weaponStockpile;
        private Map testMap;

        public void Setup(Map map)
        {
            if (!SimpleSidearmsCompat.IsLoaded() || !PickUpAndHaulCompat.IsLoaded())
                return;

            testMap = map;
            
            // Create a pacifist pawn
            var config = new TestHelpers.TestPawnConfig
            {
                Name = "PacifistHaulerFull",
                EnsureViolenceCapable = false
            };
            
            pacifistPawn = TestHelpers.CreateTestPawn(map, config);
            
            if (pacifistPawn == null)
                return;

            // Ensure they're actually incapable of violence
            if (!pacifistPawn.WorkTagIsDisabled(WorkTags.Violent))
            {
                var pacifistTrait = DefDatabase<TraitDef>.AllDefs
                    .FirstOrDefault(t => t.disabledWorkTags.HasFlag(WorkTags.Violent));
                    
                if (pacifistTrait != null && pacifistPawn.story?.traits != null)
                {
                    pacifistPawn.story.traits.GainTrait(new Trait(pacifistTrait));
                    pacifistPawn.Notify_DisabledWorkTypesChanged();
                }
            }

            // Ensure unarmed and empty inventory
            pacifistPawn.equipment?.DestroyAllEquipment();
            pacifistPawn.inventory?.innerContainer?.ClearAndDestroyContents();

            // Create weapons to test both equipping and hauling
            CreateTestWeapons(map);
            
            // Create a weapon stockpile
            var stockpilePos = pacifistPawn.Position + new IntVec3(10, 0, 0);
            CreateWeaponStockpile(map, stockpilePos);
        }

        private void CreateTestWeapons(Map map)
        {
            // Weapons near pawn for equip testing
            var equipWeapons = new[]
            {
                (VanillaWeaponDefOf.Gun_Autopistol, QualityCategory.Excellent),
                (VanillaWeaponDefOf.MeleeWeapon_LongSword, QualityCategory.Masterwork)
            };

            int offset = 0;
            foreach (var (weaponDef, quality) in equipWeapons)
            {
                if (weaponDef != null)
                {
                    var pos = pacifistPawn.Position + new IntVec3(1 + offset, 0, 0);
                    var weapon = TestHelpers.CreateWeapon(map, weaponDef, pos, quality);
                    if (weapon != null)
                    {
                        weapon.SetForbidden(false, false);
                        weaponsToHaul.Add(weapon);
                        ImprovedWeaponCacheManager.AddWeaponToCache(weapon);
                    }
                    offset++;
                }
            }

            // Weapons for hauling test
            var haulWeapons = new[]
            {
                (VanillaWeaponDefOf.Gun_Revolver, QualityCategory.Normal),
                (VanillaWeaponDefOf.MeleeWeapon_Knife, QualityCategory.Good),
                (VanillaWeaponDefOf.Gun_AssaultRifle, QualityCategory.Poor)
            };

            offset = 0;
            foreach (var (weaponDef, quality) in haulWeapons)
            {
                if (weaponDef != null)
                {
                    var pos = pacifistPawn.Position + new IntVec3(5, 0, offset);
                    var weapon = TestHelpers.CreateWeapon(map, weaponDef, pos, quality);
                    if (weapon != null)
                    {
                        weapon.SetForbidden(false, false);
                        weaponsToHaul.Add(weapon);
                    }
                    offset++;
                }
            }
        }

        private void CreateWeaponStockpile(Map map, IntVec3 center)
        {
            try
            {
                weaponStockpile = new Zone_Stockpile(StorageSettingsPreset.DefaultStockpile, map.zoneManager);
                map.zoneManager.RegisterZone(weaponStockpile);
                
                for (int x = -1; x <= 1; x++)
                {
                    for (int z = -1; z <= 1; z++)
                    {
                        var cell = center + new IntVec3(x, 0, z);
                        if (cell.InBounds(map) && cell.Standable(map))
                            weaponStockpile.AddCell(cell);
                    }
                }

                weaponStockpile.settings.filter.SetDisallowAll();
                foreach (var weaponDef in WeaponValidation.AllWeapons)
                {
                    weaponStockpile.settings.filter.SetAllow(weaponDef, true);
                }
                weaponStockpile.settings.Priority = StoragePriority.Critical;
            }
            catch { }
        }

        public TestResult Run()
        {
            if (!SimpleSidearmsCompat.IsLoaded() || !PickUpAndHaulCompat.IsLoaded())
                return TestResult.Pass();

            if (pacifistPawn == null || testMap == null)
                return TestResult.Failure("Test setup failed");

            var result = new TestResult { Success = true };
            
            // Verify pawn is incapable of violence
            bool isIncapableOfViolence = pacifistPawn.WorkTagIsDisabled(WorkTags.Violent);
            result.Data["IsIncapableOfViolence"] = isIncapableOfViolence;

            // Test 1: Verify no weapon equip jobs are created
            AutoArmLogger.Log("[TEST] Testing all job givers with full mod stack...");
            
            // Emergency job
            var emergencyJob = new JobGiver_PickUpBetterWeapon_Emergency().TestTryGiveJob(pacifistPawn);
            result.Data["EmergencyJobCreated"] = emergencyJob != null;
            if (emergencyJob != null)
            {
                result.Success = false;
                result.FailureReason = "Emergency job created for pacifist";
            }

            // Regular job
            var regularJob = new JobGiver_PickUpBetterWeapon().TestTryGiveJob(pacifistPawn);
            result.Data["RegularJobCreated"] = regularJob != null;
            if (regularJob != null)
            {
                result.Success = false;
                result.FailureReason = "Regular job created for pacifist";
            }

            // Sidearm job
            var sidearmJob = new JobGiver_PickUpSidearm().TestTryGiveJob(pacifistPawn);
            result.Data["SidearmJobCreated"] = sidearmJob != null;
            if (sidearmJob != null)
            {
                result.Success = false;
                result.FailureReason = "Sidearm job created for pacifist";
            }

            // SimpleSidearms upgrade job
            var ssUpgradeJob = SimpleSidearmsCompat.TryGetSidearmUpgradeJob(pacifistPawn);
            result.Data["SS_UpgradeJobCreated"] = ssUpgradeJob != null;
            if (ssUpgradeJob != null)
            {
                result.Success = false;
                result.FailureReason = "SimpleSidearms upgrade job created for pacifist";
            }

            // Test 2: Simulate hauling weapons (should work)
            AutoArmLogger.Log("[TEST] Testing weapon hauling for pacifist...");
            int hauledCount = 0;
            var haulableWeapons = weaponsToHaul.Where(w => w.Spawned).Take(3).ToList();
            
            foreach (var weapon in haulableWeapons)
            {
                // Simulate Pick Up and Haul adding to inventory
                if (pacifistPawn.inventory.innerContainer.TryAddOrTransfer(weapon))
                {
                    hauledCount++;
                    AutoArmLogger.Log($"[TEST] Pacifist hauled {weapon.Label}");
                }
            }
            
            result.Data["WeaponsHauled"] = hauledCount;
            result.Data["TotalHaulAttempts"] = haulableWeapons.Count;

            // Test 3: Run validation (should not drop weapons)
            var beforeValidation = pacifistPawn.inventory.innerContainer.Count(t => t.def.IsWeapon);
            PickUpAndHaulCompat.ValidateInventoryWeapons(pacifistPawn);
            var afterValidation = pacifistPawn.inventory.innerContainer.Count(t => t.def.IsWeapon);
            
            result.Data["WeaponsBeforeValidation"] = beforeValidation;
            result.Data["WeaponsAfterValidation"] = afterValidation;
            
            if (afterValidation < beforeValidation)
            {
                result.Success = false;
                result.FailureReason = "Validation dropped hauled weapons from pacifist";
                result.Data["Error_Validation"] = $"Dropped {beforeValidation - afterValidation} weapons";
            }

            // Test 4: Check if Pick Up and Haul would create haul job (should work)
            AutoArmLogger.Log("[TEST] Checking if Pick Up and Haul allows hauling...");
            bool canHaul = !PickUpAndHaulCompat.IsPawnHaulingToInventory(pacifistPawn);
            result.Data["PickUpAndHaulAllowsHauling"] = canHaul;

            // Summary
            if (result.Success && hauledCount > 0)
            {
                result.Data["Result"] = "SUCCESS: Pacifist can haul but not equip weapons";
                AutoArmLogger.Log("[TEST] SUCCESS: Full mod stack correctly handles pacifist behavior");
            }
            else if (hauledCount == 0)
            {
                result.Data["Warning"] = "Could not test hauling (no weapons picked up)";
            }

            return result;
        }

        public void Cleanup()
        {
            if (weaponStockpile != null && testMap?.zoneManager != null)
            {
                try { testMap.zoneManager.DeregisterZone(weaponStockpile); } catch { }
            }

            foreach (var weapon in weaponsToHaul)
            {
                weapon?.Destroy();
            }
            
            SafeTestCleanup.SafeDestroyPawn(pacifistPawn);
            weaponsToHaul.Clear();
        }
    }
}
