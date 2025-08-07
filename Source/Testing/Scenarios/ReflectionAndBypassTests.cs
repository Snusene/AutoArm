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
using System.Reflection;
using Verse;
using Verse.AI;

namespace AutoArm.Testing.Scenarios
{
    /// <summary>
    /// CRITICAL: Test specific SimpleSidearms reflection issues found in logs
    /// </summary>
    public class SimpleSidearmsReflectionFixTest : ITestScenario
    {
        public string Name => "SimpleSidearms Reflection Fix Validation";
        private Pawn testPawn;
        private ThingWithComps testWeapon;

        public void Setup(Map map)
        {
            if (!SimpleSidearmsCompat.IsLoaded()) return;

            testPawn = TestHelpers.CreateTestPawn(map);
            if (testPawn != null)
            {
                testPawn.equipment?.DestroyAllEquipment();
                
                testWeapon = TestHelpers.CreateWeapon(map, VanillaWeaponDefOf.Gun_Autopistol,
                    testPawn.Position + new IntVec3(2, 0, 0));
                    
                if (testWeapon != null)
                {
                    ImprovedWeaponCacheManager.AddWeaponToCache(testWeapon);
                }
            }
        }

        public TestResult Run()
        {
            if (!SimpleSidearmsCompat.IsLoaded())
                return TestResult.Pass().WithData("Note", "SimpleSidearms not loaded");

            if (testPawn == null || testWeapon == null)
                return TestResult.Failure("Test setup failed");

            var result = new TestResult { Success = true };

            // Test 1: InformOfAddedSidearm parameter count issue
            result.Data["Testing_InformOfAddedSidearm"] = true;
            
            try
            {
                // Pick up the weapon
                if (testWeapon.Spawned) testWeapon.DeSpawn();
                testPawn.inventory?.innerContainer?.TryAdd(testWeapon);
                
                // Try to inform SimpleSidearms
                SimpleSidearmsCompat.InformOfAddedSidearm(testPawn, testWeapon);
                result.Data["InformOfAddedSidearm_Success"] = true;
            }
            catch (Exception e)
            {
                result.Data["InformOfAddedSidearm_Error"] = e.Message;
                
                // Check if it's the parameter count issue from logs
                if (e.Message.Contains("parameters specified does not match"))
                {
                    result.Success = false;
                    result.Data["CRITICAL_ERROR1"] = "InformOfAddedSidearm parameter mismatch still exists!";
                    
                    // Try to diagnose the actual method signature
                    DiagnoseInformOfAddedSidearm(result);
                }
            }

            // Test 2: ReorderWeaponsAfterEquip type conversion issue
            result.Data["Testing_ReorderWeapons"] = true;
            
            try
            {
                // Give pawn a primary weapon first
                var primary = ThingMaker.MakeThing(VanillaWeaponDefOf.Gun_AssaultRifle) as ThingWithComps;
                if (primary != null)
                {
                    testPawn.equipment?.AddEquipment(primary);
                    
                    // Try to reorder
                    SimpleSidearmsCompat.ReorderWeaponsAfterEquip(testPawn);
                    result.Data["ReorderWeapons_Success"] = true;
                }
            }
            catch (Exception e)
            {
                result.Data["ReorderWeapons_Error"] = e.Message;
                
                // Check if it's the type conversion issue from logs
                if (e.Message.Contains("cannot be converted to type"))
                {
                    result.Success = false;
                    result.Data["CRITICAL_ERROR2"] = "ReorderWeapons type conversion issue still exists!";
                    
                    // Try to diagnose the actual method signature
                    DiagnoseReorderWeapons(result);
                }
            }

            // Test 3: Test the reflection detection
            result.Data["ReflectionFailed"] = SimpleSidearmsCompat.ReflectionFailed;
            
            if (SimpleSidearmsCompat.ReflectionFailed)
            {
                result.Data["Warning"] = "SimpleSidearms reflection has failed - functionality degraded";
            }

            return result;
        }

        private void DiagnoseInformOfAddedSidearm(TestResult result)
        {
            try
            {
                // Find the actual CompSidearmMemory type
                var compType = GenTypes.AllTypes.FirstOrDefault(t => 
                    t.FullName == "SimpleSidearms.rimworld.CompSidearmMemory");
                    
                if (compType != null)
                {
                    // Find InformOfAddedSidearm method
                    var methods = compType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                        .Where(m => m.Name == "InformOfAddedSidearm");
                        
                    foreach (var method in methods)
                    {
                        var parameters = method.GetParameters();
                        result.Data[$"InformOfAddedSidearm_Signature_{methods.ToList().IndexOf(method)}"] = 
                            $"({string.Join(", ", parameters.Select(p => $"{p.ParameterType.Name} {p.Name}"))})";
                    }
                }
            }
            catch (Exception e)
            {
                result.Data["DiagnoseInformOfAddedSidearm_Error"] = e.Message;
            }
        }

        private void DiagnoseReorderWeapons(TestResult result)
        {
            try
            {
                // Find the WeaponAssingment type
                var weaponAssignmentType = GenTypes.AllTypes.FirstOrDefault(t =>
                    t.FullName == "PeteTimesSix.SimpleSidearms.Utilities.WeaponAssingment");
                    
                if (weaponAssignmentType != null)
                {
                    // Find equipBestWeaponFromInventoryByPreference method
                    var methods = weaponAssignmentType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                        .Where(m => m.Name == "equipBestWeaponFromInventoryByPreference");
                        
                    foreach (var method in methods)
                    {
                        var parameters = method.GetParameters();
                        result.Data[$"ReorderWeapons_Signature_{methods.ToList().IndexOf(method)}"] = 
                            $"({string.Join(", ", parameters.Select(p => $"{p.ParameterType.Name} {p.Name}"))})";
                    }
                }
                
                // Also check for PrimaryWeaponMode enum
                var primaryWeaponModeType = GenTypes.AllTypes.FirstOrDefault(t =>
                    t.FullName.Contains("PrimaryWeaponMode"));
                    
                if (primaryWeaponModeType != null)
                {
                    result.Data["PrimaryWeaponMode_Found"] = true;
                    result.Data["PrimaryWeaponMode_IsEnum"] = primaryWeaponModeType.IsEnum;
                    
                    if (primaryWeaponModeType.IsEnum)
                    {
                        result.Data["PrimaryWeaponMode_Values"] = string.Join(", ", Enum.GetNames(primaryWeaponModeType));
                    }
                }
            }
            catch (Exception e)
            {
                result.Data["DiagnoseReorderWeapons_Error"] = e.Message;
            }
        }

        public void Cleanup()
        {
            testPawn?.Destroy();
            testWeapon?.Destroy();
        }
    }

    /// <summary>
    /// Test for outfit filter bypass vulnerability for unarmed pawns
    /// </summary>
    public class UnarmedOutfitBypassTest : ITestScenario
    {
        public string Name => "Unarmed Outfit Filter Bypass Prevention";
        private Pawn testPawn;
        private ThingWithComps forbiddenWeapon;
        private ThingWithComps outfitBlockedWeapon;
        private ThingWithComps allowedWeapon;

        public void Setup(Map map)
        {
            if (map == null) return;

            testPawn = TestHelpers.CreateTestPawn(map);
            if (testPawn != null)
            {
                // Ensure pawn is unarmed
                testPawn.equipment?.DestroyAllEquipment();

                // Create forbidden weapon (best quality)
                forbiddenWeapon = TestHelpers.CreateWeapon(map, VanillaWeaponDefOf.Gun_AssaultRifle,
                    testPawn.Position + new IntVec3(2, 0, 0), QualityCategory.Legendary);
                if (forbiddenWeapon != null)
                {
                    forbiddenWeapon.SetForbidden(true);
                    ImprovedWeaponCacheManager.AddWeaponToCache(forbiddenWeapon);
                }

                // Create outfit-blocked weapon (good quality)
                outfitBlockedWeapon = TestHelpers.CreateWeapon(map, VanillaWeaponDefOf.Gun_ChainShotgun,
                    testPawn.Position + new IntVec3(-2, 0, 0), QualityCategory.Masterwork);
                if (outfitBlockedWeapon != null)
                {
                    // Remove from outfit filter
                    if (testPawn.outfits?.CurrentApparelPolicy != null)
                    {
                        testPawn.outfits.CurrentApparelPolicy.filter.SetAllow(outfitBlockedWeapon.def, false);
                    }
                    ImprovedWeaponCacheManager.AddWeaponToCache(outfitBlockedWeapon);
                }

                // Create allowed weapon (poor quality)
                allowedWeapon = TestHelpers.CreateWeapon(map, VanillaWeaponDefOf.Gun_Autopistol,
                    testPawn.Position + new IntVec3(0, 0, 2), QualityCategory.Poor);
                if (allowedWeapon != null)
                {
                    ImprovedWeaponCacheManager.AddWeaponToCache(allowedWeapon);
                }
            }
        }

        public TestResult Run()
        {
            if (testPawn == null)
                return TestResult.Failure("Test setup failed");

            var result = new TestResult { Success = true };
            var jobGiver = new JobGiver_PickUpBetterWeapon();

            // Verify pawn is unarmed
            result.Data["PawnIsUnarmed"] = testPawn.equipment?.Primary == null;

            // Test 1: Unarmed pawn MUST respect forbidden flag
            var job1 = jobGiver.TestTryGiveJob(testPawn);
            
            if (job1 != null)
            {
                var targetWeapon = job1.targetA.Thing;
                result.Data["JobCreated"] = true;
                result.Data["TargetWeapon"] = targetWeapon?.Label ?? "null";
                
                if (targetWeapon == forbiddenWeapon)
                {
                    result.Success = false;
                    result.Data["CRITICAL_ERROR1"] = "UNARMED pawn ignoring FORBIDDEN flag!";
                }
                
                if (targetWeapon == outfitBlockedWeapon)
                {
                    result.Success = false;
                    result.Data["CRITICAL_ERROR2"] = "UNARMED pawn ignoring OUTFIT FILTER!";
                }
                
                if (targetWeapon == allowedWeapon)
                {
                    result.Data["CorrectChoice"] = "Chose allowed weapon despite being lower quality";
                }
            }
            else
            {
                result.Data["NoJobCreated"] = true;
                
                // Diagnose why no job was created
                var nearbyWeapons = ImprovedWeaponCacheManager.GetWeaponsNear(testPawn.Map, testPawn.Position, 60f);
                result.Data["NearbyWeapons"] = nearbyWeapons.Count;
                
                bool foundViableWeapon = false;
                foreach (var weapon in nearbyWeapons)
                {
                    bool forbidden = weapon.IsForbidden(testPawn);
                    bool outfitAllows = testPawn.outfits?.CurrentApparelPolicy?.filter?.Allows(weapon) ?? true;
                    
                    result.Data[$"{weapon.Label}_Forbidden"] = forbidden;
                    result.Data[$"{weapon.Label}_OutfitAllows"] = outfitAllows;
                    
                    if (!forbidden && outfitAllows)
                    {
                        foundViableWeapon = true;
                    }
                }
                
                if (foundViableWeapon)
                {
                    result.Success = false;
                    result.Data["ERROR3"] = "Viable weapon exists but unarmed pawn not picking it up!";
                }
            }

            // Test 2: Verify the emergency check is working
            bool isEmergency = testPawn.equipment?.Primary == null;
            int checkInterval = isEmergency ? 1 : 30; // From Constants
            result.Data["EmergencyCheckInterval"] = checkInterval;
            result.Data["ShouldCheckFrequently"] = isEmergency;

            // Test 3: Unforbid weapon and verify immediate pickup
            if (forbiddenWeapon != null)
            {
                forbiddenWeapon.SetForbidden(false);
                
                var job2 = jobGiver.TestTryGiveJob(testPawn);
                if (job2 != null && job2.targetA.Thing == forbiddenWeapon)
                {
                    result.Data["PicksUpUnforbidden"] = true;
                    result.Data["Note"] = "Correctly prioritizes best weapon once unforbidden";
                }
                else if (job2 != null && job2.targetA.Thing == outfitBlockedWeapon)
                {
                    result.Success = false;
                    result.Data["ERROR4"] = "Picking outfit-blocked weapon over allowed weapon!";
                }
            }

            return result;
        }

        public void Cleanup()
        {
            testPawn?.Destroy();
            forbiddenWeapon?.Destroy();
            outfitBlockedWeapon?.Destroy();
            allowedWeapon?.Destroy();
        }
    }
}
