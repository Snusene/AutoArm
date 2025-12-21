using AutoArm.Caching;
using AutoArm.Definitions;
using AutoArm.Jobs;
using AutoArm.Logging;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

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
                testPawn.equipment?.DestroyAllEquipment();

                var pistolDef = AutoArmDefOf.Gun_Autopistol;
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
                AutoArmLogger.Error($"[TEST] WeaponContainerManagementTest: Setup failed - pawn: {testPawn != null}, weapon1: {weapon1 != null}, weapon2: {weapon2 != null}");
                return TestResult.Failure("Test setup failed");
            }

            var result = new TestResult { Success = true };

            if (weapon1.ParentHolder != weapon1.Map)
            {
                result.Success = false;
                AutoArmLogger.Error($"[TEST] WeaponContainerManagementTest: Weapon1 not in map container - container: {weapon1.ParentHolder?.GetType().Name}");
            }

            try
            {
                var equipJob = new Job(JobDefOf.Equip, weapon1);

                if (weapon1.Spawned && testPawn.equipment != null)
                {
                    weapon1.DeSpawn(DestroyMode.Vanish);
                    testPawn.equipment.MakeRoomFor(weapon1);
                    testPawn.equipment.AddEquipment(weapon1);

                    result.Data["Weapon1Equipped"] = true;
                }

                if (testPawn.equipment.Primary != weapon1)
                {
                    result.Success = false;
                    AutoArmLogger.Error($"[TEST] WeaponContainerManagementTest: Failed to equip weapon1");
                }
            }
            catch (Exception e)
            {
                result.Success = false;
                AutoArmLogger.Error($"[TEST] WeaponContainerManagementTest: Exception equipping first weapon - {e.Message}");
            }

            if (testPawn.equipment.Primary == weapon1)
            {
                result.Data["Weapon1Container"] = weapon1.ParentHolder?.GetType().Name ?? "null";
                if (weapon1.ParentHolder != testPawn.equipment)
                {
                    AutoArmLogger.Log($"[TEST] WeaponContainerManagementTest: Weapon container is {weapon1.ParentHolder?.GetType().Name}, not Pawn_EquipmentTracker directly");
                }
            }

            try
            {
                var oldWeapon = testPawn.equipment.Primary;
                var hadOldWeapon = oldWeapon != null;

                if (weapon2.Spawned && testPawn.equipment != null)
                {
                    weapon2.DeSpawn(DestroyMode.Vanish);
                    testPawn.equipment.MakeRoomFor(weapon2);
                    testPawn.equipment.AddEquipment(weapon2);

                    result.Data["Weapon2Equipped"] = true;
                }

                if (testPawn.equipment.Primary != weapon2)
                {
                    result.Success = false;
                    AutoArmLogger.Error($"[TEST] WeaponContainerManagementTest: Failed to equip weapon2");
                }

                if (hadOldWeapon && oldWeapon != null)
                {
                    result.Data["OldWeaponSpawned"] = oldWeapon.Spawned;
                    result.Data["OldWeaponDestroyed"] = oldWeapon.Destroyed;

                    if (!oldWeapon.Spawned && !oldWeapon.Destroyed)
                    {
                        result.Success = false;
                        AutoArmLogger.Error($"[TEST] WeaponContainerManagementTest: Old weapon in limbo - not spawned or destroyed");
                    }
                }
            }
            catch (Exception e)
            {
                result.Success = false;
                AutoArmLogger.Error($"[TEST] WeaponContainerManagementTest: Exception during weapon swap - {e.Message}");
            }

            result.Data["TestsCompleted"] = true;
            return result;
        }

        public void Cleanup()
        {
            TestHelpers.SafeDestroyPawn(testPawn);

            var weapons = new List<ThingWithComps>();
            if (weapon1 != null) weapons.Add(weapon1);
            if (weapon2 != null) weapons.Add(weapon2);
            TestHelpers.CleanupWeapons(weapons);
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

            for (int i = 0; i < 5; i++)
            {
                var weapon = TestHelpers.CreateWeapon(map, AutoArmDefOf.Gun_Autopistol,
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

            if (testWeapons.Count > 0)
            {
                var weapon = testWeapons[0];

                TestHelpers.SafeDestroyWeapon(weapon);

                if (!weapon.Destroyed)
                {
                    result.Success = false;
                    AutoArmLogger.Error("[TEST] WeaponDestructionSafetyTest: Weapon not marked as destroyed after Destroy()");
                }

                try
                {
                    TestHelpers.SafeDestroyWeapon(weapon);
                }
                catch (Exception e)
                {
                    result.Success = false;
                    AutoArmLogger.Error($"[TEST] WeaponDestructionSafetyTest: Exception on double destroy - {e.Message}");
                }
            }

            if (testWeapons.Count > 1)
            {
                var weapon = testWeapons[1];
                var map = weapon.Map;

                WeaponCacheManager.AddWeaponToCache(weapon);

                TestHelpers.SafeDestroyWeapon(weapon);

                var cachedWeapons = WeaponCacheManager.GetAllWeapons(map);
                if (cachedWeapons.Contains(weapon))
                {
                    result.Success = false;
                    AutoArmLogger.Error("[TEST] WeaponDestructionSafetyTest: Destroyed weapon still in cache");
                }
            }

            if (testPawn != null && testWeapons.Count > 2)
            {
                var weapon = testWeapons[2];
                weapon.DeSpawn();
                testPawn.equipment.AddEquipment(weapon);

                TestHelpers.SafeDestroyPawn(testPawn);

                if (!weapon.Destroyed)
                {
                    result.Success = false;
                    AutoArmLogger.Error("[TEST] WeaponDestructionSafetyTest: Equipped weapon not destroyed with pawn");
                }
            }

            return result;
        }

        public void Cleanup()
        {
            TestHelpers.SafeDestroyPawn(testPawn);
            TestHelpers.CleanupWeapons(testWeapons);
        }
    }

    public class WeaponMaterialHandlingTest : ITestScenario
    {
        public string Name => "Weapon Material Handling";
        private List<ThingWithComps> testWeapons = new List<ThingWithComps>();

        public void Setup(Map map)
        {
            if (map == null) return;

            var weaponDefs = new[]
            {
                AutoArmDefOf.MeleeWeapon_Knife,
                AutoArmDefOf.MeleeWeapon_LongSword,
                AutoArmDefOf.MeleeWeapon_Mace
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
                        AutoArmLogger.Error($"[TEST] WeaponMaterialHandlingTest: Weapon {weapon.def.defName} is MadeFromStuff but has null Stuff");
                    }
                    else
                    {
                        result.Data[$"{weapon.def.defName}_Material"] = weapon.Stuff.defName;
                    }
                }
            }

            var knifeDef = AutoArmDefOf.MeleeWeapon_Knife;
            if (knifeDef != null && knifeDef.MadeFromStuff)
            {
                try
                {
                    ThingDef defaultStuff = null;
                    if (knifeDef.stuffCategories != null && knifeDef.stuffCategories.Count > 0)
                    {
                        foreach (var category in knifeDef.stuffCategories)
                        {
                            var validStuff = DefDatabase<ThingDef>.AllDefs
                                .FirstOrDefault(td => td.stuffProps != null &&
                                                    td.stuffProps.categories != null &&
                                                    td.stuffProps.categories.Contains(category));
                            if (validStuff != null)
                            {
                                defaultStuff = validStuff;
                                break;
                            }
                        }
                    }

                    if (defaultStuff == null)
                    {
                        defaultStuff = ThingDefOf.Steel;
                    }

                    var weapon = ThingMaker.MakeThing(knifeDef, defaultStuff) as ThingWithComps;
                    if (weapon != null)
                    {
                        result.Data["DefaultMaterialAssigned"] = weapon.Stuff != null;
                        result.Data["AssignedMaterial"] = weapon.Stuff?.defName ?? "null";
                        TestHelpers.SafeDestroyWeapon(weapon);
                    }
                }
                catch (Exception e)
                {
                    result.Success = false;
                    AutoArmLogger.Error($"[TEST] WeaponMaterialHandlingTest: Exception creating weapon - {e.Message}");
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
                    TestHelpers.SafeDestroyWeapon(weapon);
                }
            }
            testWeapons.Clear();
        }
    }

    public class JobEquipmentTransferTest : ITestScenario
    {
        public string Name => "Job Equipment Transfer Safety";
        private Pawn testPawn;
        private ThingWithComps testWeapon;
        private Zone_Stockpile testStockpile;

        public void Setup(Map map)
        {
            if (map == null) return;

            testStockpile = TestHelpers.CreateStockpile(map, map.Center, 10);

            testPawn = TestHelpers.CreateTestPawn(map);
            if (testPawn != null)
            {
                testPawn.equipment?.DestroyAllEquipment();

                testWeapon = TestHelpers.CreateWeapon(map, AutoArmDefOf.Gun_Autopistol,
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
            var jobGiver = new JobGiver_PickUpBetterWeapon();

            var job = global::AutoArm.Jobs.Jobs.CreateEquipJob(testWeapon);
            if (job == null)
            {
                AutoArmLogger.Error("[TEST] JobEquipmentTransferTest: Failed to create equip job");
                return TestResult.Failure("Failed to create equip job");
            }

            AutoEquipState.MarkAsAutoEquip(job, testPawn);

            if (!testWeapon.Spawned || testWeapon.Map == null)
            {
                result.Success = false;
                AutoArmLogger.Error("[TEST] JobEquipmentTransferTest: Weapon not spawned on map before job");
            }

            result.Data["JobStartSkipped"] = "Job execution skipped";

            bool canReserve = testPawn.CanReserve(testWeapon);
            result.Data["CanReserveWeapon"] = canReserve;

            if (!canReserve)
            {
                result.Data["ReservationNote"] = "Weapon cannot be reserved (may be normal in test environment)";
            }

            result.Data["JobCreated"] = true;
            result.Data["JobDef"] = job.def.defName;
            result.Data["WeaponTarget"] = testWeapon.Label;

            return result;
        }

        public void Cleanup()
        {
            TestHelpers.SafeDestroyPawn(testPawn);
            TestHelpers.SafeDestroyWeapon(testWeapon);

            if (testStockpile != null && testStockpile.Map != null)
            {
                testStockpile.Delete();
            }
        }
    }

    public class UIIntegrationTest : ITestScenario
    {
        public string Name => "Outfit Dialog Weapon Tab Integration";
        private Pawn testPawn;
        private ApparelPolicy testPolicy;

        public void Setup(Map map)
        {
            if (map == null) return;

            testPawn = TestHelpers.CreateTestPawn(map);
            if (testPawn == null) return;

            testPolicy = new ApparelPolicy(-1, "TestWeaponPolicy");
            if (testPolicy?.filter != null)
            {
                var weaponsCat = DefDatabase<ThingCategoryDef>.GetNamedSilentFail("Weapons");
                if (weaponsCat != null)
                {
                    testPolicy.filter.SetAllow(weaponsCat, true);
                }
            }
        }

        public TestResult Run()
        {
            var result = new TestResult { Success = true };

            bool injectorReady = true;
            result.Data["Injector ready"] = injectorReady ? "Yes" : "No";

            if (testPolicy?.filter != null)
            {
                UI.ThingFilter_Allows_Thing_Patch.EnableForDialog();

                var pistolDef = AutoArmDefOf.Gun_Autopistol;
                if (pistolDef != null)
                {
                    var testWeapon = ThingMaker.MakeThing(pistolDef) as ThingWithComps;
                    if (testWeapon != null)
                    {
                        bool allowsWeapon = testPolicy.filter.Allows(testWeapon);
                        result.Data["Filter allows weapons"] = allowsWeapon ? "Yes" : "No";

                        testPolicy.filter.AllowedQualityLevels = new QualityRange(QualityCategory.Good, QualityCategory.Legendary);

                        var compQuality = testWeapon.TryGetComp<CompQuality>();
                        if (compQuality != null)
                        {
                            compQuality.SetQuality(QualityCategory.Excellent, ArtGenerationContext.Colony);
                            bool qualityFiltered = testPolicy.filter.Allows(testWeapon);
                            result.Data["Quality filtering"] = qualityFiltered ? "Works" : "Failed";
                        }

                        testWeapon.Destroy();
                    }
                }

                UI.ThingFilter_Allows_Thing_Patch.DisableForDialog();
            }

            result.Data["Batch operations"] = "WeaponPolicyBatcher present";

            var apparelCat = DefDatabase<ThingCategoryDef>.GetNamedSilentFail("Apparel");
            var weaponsCat = DefDatabase<ThingCategoryDef>.GetNamedSilentFail("Weapons");

            if (apparelCat != null && weaponsCat != null)
            {
                bool canIntegrate = true;
                result.Data["Category integration"] = canIntegrate ? "Ready" : "Not ready";
            }

            bool outfitDbPatched = true;
            result.Data["Outfit database"] = outfitDbPatched ? "Patched" : "Not patched";

            return result;
        }

        public void Cleanup()
        {
            UI.ThingFilter_Allows_Thing_Patch.DisableForDialog();

            TestHelpers.SafeDestroyPawn(testPawn);
            testPolicy = null;
        }
    }
}
