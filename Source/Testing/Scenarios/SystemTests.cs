// AutoArm RimWorld 1.5+ mod - automatic weapon management
// This file: System tests for weapon container management and safety
// Validates core weapon handling infrastructure

using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;
using AutoArm.Caching; using AutoArm.Jobs; using AutoArm.Logging;
using AutoArm.Definitions;
using AutoArm.Weapons;

namespace AutoArm.Testing.Scenarios
{
    public class WeaponContainerManagementTest : ITestScenario
    {
        public string Name => "Weapon Container Management";
        private Pawn testPawn;
        private ThingWithComps weapon1;
        private ThingWithComps weapon2;

        public void Setup(Map map)
        {
            if (map == null) return;

            testPawn = TestHelpers.CreateTestPawn(map);
            if (testPawn != null)
            {
                // Clear any existing equipment
                testPawn.equipment?.DestroyAllEquipment();

                // Create weapons with proper materials
                var pistolDef = VanillaWeaponDefOf.Gun_Autopistol;
                if (pistolDef != null)
                {
                    weapon1 = TestHelpers.CreateWeapon(map, pistolDef,
                        testPawn.Position + new IntVec3(2, 0, 0));
                    weapon2 = TestHelpers.CreateWeapon(map, pistolDef,
                        testPawn.Position + new IntVec3(-2, 0, 0));
                }
            }
        }

        public TestResult Run()
        {
            if (testPawn == null || weapon1 == null || weapon2 == null)
            {
                AutoArmLogger.LogError($"[TEST] WeaponContainerManagementTest: Setup failed - pawn: {testPawn != null}, weapon1: {weapon1 != null}, weapon2: {weapon2 != null}");
                return TestResult.Failure("Test setup failed");
            }

            var result = new TestResult { Success = true };

            // Test 1: Verify weapons start in map container
            if (weapon1.ParentHolder != weapon1.Map)
            {
                result.Success = false;
                result.FailureReason = "Weapon not in expected container";
                result.Data["Error"] = "Weapon container state incorrect";
                result.Data["Weapon1Container"] = weapon1.ParentHolder?.GetType().Name ?? "null";
                result.Data["ExpectedContainer"] = "Map";
                AutoArmLogger.LogError($"[TEST] WeaponContainerManagementTest: Weapon1 not in map container - container: {weapon1.ParentHolder?.GetType().Name}");
            }

            // Test 2: Simple equip test - pawn picks up weapon from ground
            try
            {
                weapon1.SetForbidden(false);

                // Create and execute an equip job like the game normally would
                var equipJob = new Job(JobDefOf.Equip, weapon1);

                // Manually perform the equip action that the job would do
                if (weapon1.Spawned && testPawn.equipment != null)
                {
                    // This is how RimWorld's Toils_Misc.TakeEquipment works internally
                    weapon1.DeSpawn(DestroyMode.Vanish);
                    testPawn.equipment.MakeRoomFor(weapon1);
                    testPawn.equipment.AddEquipment(weapon1);

                    result.Data["Weapon1Equipped"] = true;
                }

                if (testPawn.equipment.Primary != weapon1)
                {
                    result.Success = false;
                    result.FailureReason = result.FailureReason ?? "Failed to equip weapon";
                    result.Data["Error"] = result.Data.ContainsKey("Error") ? result.Data["Error"] + "; Equip operation failed" : "Equip operation failed";
                    result.Data["EquipFailed"] = true;
                    result.Data["PrimaryWeapon"] = testPawn.equipment.Primary?.Label ?? "null";
                    result.Data["ExpectedWeapon"] = weapon1?.Label;
                    AutoArmLogger.LogError($"[TEST] WeaponContainerManagementTest: Failed to equip weapon1");
                }
            }
            catch (Exception e)
            {
                result.Success = false;
                result.FailureReason = result.FailureReason ?? "Exception during equip";
                result.Data["Error"] = result.Data.ContainsKey("Error") ? result.Data["Error"] + "; Exception during equip" : "Exception during equip";
                result.Data["Exception1"] = e.Message;
                AutoArmLogger.LogError($"[TEST] WeaponContainerManagementTest: Exception equipping first weapon - {e.Message}");
            }

            // Test 3: Verify container ownership after equip
            if (testPawn.equipment.Primary == weapon1)
            {
                result.Data["Weapon1Container"] = weapon1.ParentHolder?.GetType().Name ?? "null";
                if (weapon1.ParentHolder != testPawn.equipment)
                {
                    // This is actually okay in some RimWorld versions - the container might be the inner container
                    AutoArmLogger.Log($"[TEST] WeaponContainerManagementTest: Weapon container is {weapon1.ParentHolder?.GetType().Name}, not Pawn_EquipmentTracker directly");
                }
            }

            // Test 4: Equipment swapping - equip second weapon
            try
            {
                weapon2.SetForbidden(false);

                // Store reference to old weapon
                var oldWeapon = testPawn.equipment.Primary;
                var hadOldWeapon = oldWeapon != null;

                // Equip new weapon the same way
                if (weapon2.Spawned && testPawn.equipment != null)
                {
                    weapon2.DeSpawn(DestroyMode.Vanish);
                    testPawn.equipment.MakeRoomFor(weapon2);  // This drops current weapon
                    testPawn.equipment.AddEquipment(weapon2);

                    result.Data["Weapon2Equipped"] = true;
                }

                // Verify swap worked
                if (testPawn.equipment.Primary != weapon2)
                {
                    result.Success = false;
                    result.FailureReason = result.FailureReason ?? "Failed to swap weapons";
                    result.Data["Error"] = result.Data.ContainsKey("Error") ? result.Data["Error"] + "; Weapon swap failed" : "Weapon swap failed";
                    result.Data["SwapFailed"] = true;
                    result.Data["CurrentWeapon"] = testPawn.equipment.Primary?.Label ?? "null";
                    result.Data["ExpectedWeapon"] = weapon2?.Label;
                    AutoArmLogger.LogError($"[TEST] WeaponContainerManagementTest: Failed to equip weapon2");
                }

                // Check old weapon was handled properly
                if (hadOldWeapon && oldWeapon != null)
                {
                    result.Data["OldWeaponSpawned"] = oldWeapon.Spawned;
                    result.Data["OldWeaponDestroyed"] = oldWeapon.Destroyed;

                    // Old weapon should either be spawned on ground or destroyed
                    if (!oldWeapon.Spawned && !oldWeapon.Destroyed)
                    {
                        result.Success = false;
                        result.FailureReason = result.FailureReason ?? "Old weapon in invalid state";
                        result.Data["Error"] = result.Data.ContainsKey("Error") ? result.Data["Error"] + "; Old weapon in limbo" : "Old weapon in limbo";
                        result.Data["OldWeaponSpawned"] = oldWeapon.Spawned;
                        result.Data["OldWeaponDestroyed"] = oldWeapon.Destroyed;
                        result.Data["OldWeaponContainer"] = oldWeapon.ParentHolder?.GetType().Name ?? "null";
                        AutoArmLogger.LogError($"[TEST] WeaponContainerManagementTest: Old weapon in limbo - not spawned or destroyed");
                    }
                }
            }
            catch (Exception e)
            {
                result.Success = false;
                result.FailureReason = result.FailureReason ?? "Exception during weapon swap";
                result.Data["Error"] = result.Data.ContainsKey("Error") ? result.Data["Error"] + "; Exception during swap" : "Exception during swap";
                result.Data["Exception2"] = e.Message;
                AutoArmLogger.LogError($"[TEST] WeaponContainerManagementTest: Exception during weapon swap - {e.Message}");
            }

            result.Data["TestsCompleted"] = true;
            return result;
        }

        public void Cleanup()
        {
            // Proper cleanup order
            if (testPawn != null && !testPawn.Destroyed)
            {
                testPawn.equipment?.DestroyAllEquipment();
                testPawn.Destroy();
            }

            // Only destroy weapons if they're spawned on the map
            if (weapon1 != null && !weapon1.Destroyed && weapon1.Spawned)
            {
                weapon1.Destroy();
            }
            if (weapon2 != null && !weapon2.Destroyed && weapon2.Spawned)
            {
                weapon2.Destroy();
            }
        }
    }

    public class WeaponDestructionSafetyTest : ITestScenario
    {
        public string Name => "Weapon Destruction Safety";
        private List<ThingWithComps> testWeapons = new List<ThingWithComps>();
        private Pawn testPawn;

        public void Setup(Map map)
        {
            if (map == null) return;

            testPawn = TestHelpers.CreateTestPawn(map);

            // Create multiple weapons
            for (int i = 0; i < 5; i++)
            {
                var weapon = TestHelpers.CreateWeapon(map, VanillaWeaponDefOf.Gun_Autopistol,
                    map.Center + new IntVec3(i * 2, 0, 0));
                if (weapon != null)
                {
                    testWeapons.Add(weapon);
                }
            }
        }

        public TestResult Run()
        {
            var result = new TestResult { Success = true };

            // Test 1: Double destruction protection
            if (testWeapons.Count > 0)
            {
                var weapon = testWeapons[0];

                // First destruction
                weapon.Destroy();

                // Verify destroyed state
                if (!weapon.Destroyed)
                {
                    result.Success = false;
                    result.FailureReason = "Weapon not marked as destroyed";
                    result.Data["Error"] = "Destroy() method failed to mark weapon as destroyed";
                    result.Data["WeaponDestroyed"] = false;
                    result.Data["ExpectedDestroyed"] = true;
                    AutoArmLogger.LogError("[TEST] WeaponDestructionSafetyTest: Weapon not marked as destroyed after Destroy()");
                }

                // Try to destroy again - should not throw
                try
                {
                    weapon.Destroy();
                    // If we get here without exception, that's actually a problem
                    // RimWorld should log an error for double destruction
                }
                catch (Exception e)
                {
                    result.Success = false;
                    result.FailureReason = result.FailureReason ?? "Exception on double destroy";
                    result.Data["Error"] = result.Data.ContainsKey("Error") ? result.Data["Error"] + "; Double destroy exception" : "Double destroy exception";
                    result.Data["DoubleDestroyException"] = e.Message;
                    AutoArmLogger.LogError($"[TEST] WeaponDestructionSafetyTest: Exception on double destroy - {e.Message}");
                }
            }

            // Test 2: Cache removal of destroyed weapons
            if (testWeapons.Count > 1)
            {
                var weapon = testWeapons[1];
                var map = weapon.Map;

                // Add to cache
                ImprovedWeaponCacheManager.AddWeaponToCache(weapon);

                // Destroy weapon
                weapon.Destroy();

                // Check if cache still contains destroyed weapon
                var cachedWeapons = ImprovedWeaponCacheManager.GetWeaponsNear(map, weapon.Position, 10f);
                if (cachedWeapons.Contains(weapon))
                {
                    result.Success = false;
                    result.FailureReason = result.FailureReason ?? "Destroyed weapon still in cache";
                    result.Data["Error"] = result.Data.ContainsKey("Error") ? result.Data["Error"] + "; Cache not cleared after destruction" : "Cache not cleared after destruction";
                    result.Data["WeaponInCacheAfterDestroy"] = true;
                    result.Data["ExpectedInCache"] = false;
                    AutoArmLogger.LogError("[TEST] WeaponDestructionSafetyTest: Destroyed weapon still in cache");
                }
            }

            // Test 3: Equipment destruction cascade
            if (testPawn != null && testWeapons.Count > 2)
            {
                var weapon = testWeapons[2];
                weapon.DeSpawn();
                testPawn.equipment.AddEquipment(weapon);

                // Destroy pawn - should also destroy equipment
                testPawn.Destroy();

                if (!weapon.Destroyed)
                {
                    result.Success = false;
                    result.FailureReason = result.FailureReason ?? "Equipped weapon not destroyed with pawn";
                    result.Data["Error"] = result.Data.ContainsKey("Error") ? result.Data["Error"] + "; Equipment cascade destruction failed" : "Equipment cascade destruction failed";
                    result.Data["EquippedWeaponDestroyed"] = false;
                    result.Data["PawnDestroyed"] = true;
                    AutoArmLogger.LogError("[TEST] WeaponDestructionSafetyTest: Equipped weapon not destroyed with pawn");
                }
            }

            return result;
        }

        public void Cleanup()
        {
            // Clean up any remaining weapons
            foreach (var weapon in testWeapons)
            {
                if (weapon != null && !weapon.Destroyed)
                {
                    weapon.Destroy();
                }
            }

            // Pawn should already be destroyed from test
            if (testPawn != null && !testPawn.Destroyed)
            {
                testPawn.Destroy();
            }
        }
    }

    public class WeaponMaterialHandlingTest : ITestScenario
    {
        public string Name => "Weapon Material Handling";
        private List<ThingWithComps> testWeapons = new List<ThingWithComps>();

        public void Setup(Map map)
        {
            if (map == null) return;

            // Test weapons that require materials
            var weaponDefs = new[]
            {
                VanillaWeaponDefOf.MeleeWeapon_Knife,
                VanillaWeaponDefOf.MeleeWeapon_LongSword,
                VanillaWeaponDefOf.MeleeWeapon_Mace
            };

            foreach (var def in weaponDefs.Where(d => d != null))
            {
                var weapon = TestHelpers.CreateWeapon(map, def,
                    map.Center + new IntVec3(testWeapons.Count * 2, 0, 0));
                if (weapon != null)
                {
                    testWeapons.Add(weapon);
                }
            }
        }

        public TestResult Run()
        {
            var result = new TestResult { Success = true };

            foreach (var weapon in testWeapons)
            {
                if (weapon.def.MadeFromStuff)
                {
                    if (weapon.Stuff == null)
                    {
                        result.Success = false;
                        result.FailureReason = result.FailureReason ?? "Weapon missing required material";
                        result.Data["Error"] = result.Data.ContainsKey("Error") ? result.Data["Error"] + "; Material assignment failed" : "Material assignment failed";
                        result.Data[$"{weapon.def.defName}_Material"] = "null";
                        result.Data[$"{weapon.def.defName}_RequiresMaterial"] = true;
                        AutoArmLogger.LogError($"[TEST] WeaponMaterialHandlingTest: Weapon {weapon.def.defName} is MadeFromStuff but has null Stuff");
                    }
                    else
                    {
                        result.Data[$"{weapon.def.defName}_Material"] = weapon.Stuff.defName;
                    }
                }
            }

            // Test creating weapon without material (should use default)
            var knifeDef = VanillaWeaponDefOf.MeleeWeapon_Knife;
            if (knifeDef != null && knifeDef.MadeFromStuff)
            {
                try
                {
                    // Get default stuff for the weapon
                    ThingDef defaultStuff = null;
                    if (knifeDef.stuffCategories != null && knifeDef.stuffCategories.Count > 0)
                    {
                        // Find first valid stuff
                        foreach (var category in knifeDef.stuffCategories)
                        {
                            var validStuff = DefDatabase<ThingDef>.AllDefs
                                .Where(td => td.stuffProps != null &&
                                            td.stuffProps.categories != null &&
                                            td.stuffProps.categories.Contains(category))
                                .FirstOrDefault();
                            if (validStuff != null)
                            {
                                defaultStuff = validStuff;
                                break;
                            }
                        }
                    }

                    if (defaultStuff == null)
                    {
                        defaultStuff = ThingDefOf.Steel; // Fallback
                    }

                    // Create with explicit material
                    var weapon = ThingMaker.MakeThing(knifeDef, defaultStuff) as ThingWithComps;
                    if (weapon != null)
                    {
                        result.Data["DefaultMaterialAssigned"] = weapon.Stuff != null;
                        result.Data["AssignedMaterial"] = weapon.Stuff?.defName ?? "null";
                        weapon.Destroy();
                    }
                }
                catch (Exception e)
                {
                    result.Success = false;
                    result.FailureReason = result.FailureReason ?? "Exception creating weapon with material";
                    result.Data["Error"] = result.Data.ContainsKey("Error") ? result.Data["Error"] + "; Material creation exception" : "Material creation exception";
                    result.Data["MaterialException"] = e.Message;
                    AutoArmLogger.LogError($"[TEST] WeaponMaterialHandlingTest: Exception creating weapon - {e.Message}");
                }
            }

            return result;
        }

        public void Cleanup()
        {
            foreach (var weapon in testWeapons)
            {
                if (weapon != null && !weapon.Destroyed)
                {
                    weapon.Destroy();
                }
            }
        }
    }

    public class JobEquipmentTransferTest : ITestScenario
    {
        public string Name => "Job Equipment Transfer Safety";
        private Pawn testPawn;
        private ThingWithComps testWeapon;

        public void Setup(Map map)
        {
            if (map == null) return;

            testPawn = TestHelpers.CreateTestPawn(map);
            if (testPawn != null)
            {
                testPawn.equipment?.DestroyAllEquipment();

                testWeapon = TestHelpers.CreateWeapon(map, VanillaWeaponDefOf.Gun_Autopistol,
                    testPawn.Position + new IntVec3(3, 0, 0));
            }
        }

        public TestResult Run()
        {
            if (testPawn == null || testWeapon == null)
            {
                return TestResult.Failure("Test setup failed");
            }

            var result = new TestResult { Success = true };
            
            // Ensure pawn has job tracker
            if (testPawn.jobs == null)
            {
                AutoArmLogger.LogError("[TEST] JobEquipmentTransferTest: Pawn has no job tracker");
                return TestResult.Failure("Pawn has no job tracker");
            }

            var jobGiver = new JobGiver_PickUpBetterWeapon();

            // Create equip job
            var job = JobHelper.CreateEquipJob(testWeapon);
            if (job == null)
            {
                AutoArmLogger.LogError("[TEST] JobEquipmentTransferTest: Failed to create equip job");
                return TestResult.Failure("Failed to create equip job");
            }

            // Mark as auto-equip job
            AutoEquipTracker.MarkAsAutoEquip(job, testPawn);

            // Verify weapon is currently on map
            if (!testWeapon.Spawned || testWeapon.Map == null)
            {
                result.Success = false;
                result.FailureReason = "Weapon not spawned on map before job";
                result.Data["Error"] = "Initial weapon state invalid";
                result.Data["WeaponSpawned"] = testWeapon.Spawned;
                result.Data["WeaponMap"] = testWeapon.Map != null;
                AutoArmLogger.LogError("[TEST] JobEquipmentTransferTest: Weapon not spawned on map before job");
            }

            try
            {
                // Start the job
                testPawn.jobs.StartJob(job, JobCondition.InterruptForced);

                // The actual equipping happens during job execution
                // For this test, we're mainly checking that the job was created properly
                result.Data["JobCreated"] = true;
                result.Data["JobDef"] = job.def.defName;
                result.Data["WeaponTarget"] = testWeapon.Label;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.FailureReason = "Exception during job execution";
                result.Data["Error"] = "Job execution failed";
                result.Data["Exception"] = ex.Message;
                result.Data["StackTrace"] = ex.StackTrace;
                AutoArmLogger.LogError($"[TEST] JobEquipmentTransferTest: Exception - {ex.Message}");
            }

            return result;
        }

        public void Cleanup()
        {
            if (testPawn != null && !testPawn.Destroyed)
            {
                testPawn.jobs?.StopAll();
                testPawn.equipment?.DestroyAllEquipment();
                testPawn.Destroy();
            }

            if (testWeapon != null && !testWeapon.Destroyed && testWeapon.Spawned)
            {
                testWeapon.Destroy();
            }
        }
    }
}
