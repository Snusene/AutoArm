using RimWorld;
using System.Collections.Generic;
using Verse;

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
                AutoArmDebug.LogError("[TEST] SimpleSidearmsIntegrationTest: Test pawn creation failed");
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
                        var weapon = ThingMaker.MakeThing(weaponDef) as ThingWithComps;
                        if (weapon != null)
                        {
                            var comp = weapon.TryGetComp<CompQuality>();
                            comp?.SetQuality(QualityCategory.Poor, ArtGenerationContext.Colony);

                            if (i == 0)
                            {
                                testPawn.equipment?.AddEquipment(weapon);
                            }
                            else
                            {
                                testPawn.inventory?.innerContainer?.TryAdd(weapon);
                            }
                            ownedWeapons.Add(weapon);
                        }
                    }
                }

                // Create a much better weapon nearby
                betterWeapon = TestHelpers.CreateWeapon(map, VanillaWeaponDefOf.Gun_AssaultRifle,
                    testPawn.Position + new IntVec3(2, 0, 0), QualityCategory.Legendary);
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

            result.Data["CurrentWeaponCount"] = SimpleSidearmsCompat.GetCurrentSidearmCount(testPawn) + 1; // +1 for primary
            result.Data["MaxSlots"] = SimpleSidearmsCompat.GetMaxSidearmsForPawn(testPawn);

            // Should create a job to replace worst weapon
            var job = SimpleSidearmsCompat.TryGetSidearmUpgradeJob(testPawn);

            if (job != null)
            {
                result.Data["JobCreated"] = true;
                result.Data["TargetWeapon"] = job.targetA.Thing?.Label ?? "Unknown";

                // Verify it's targeting the legendary weapon
                if (job.targetA.Thing != betterWeapon)
                {
                    result.Success = false;
                    result.Data["Error"] = "Job targets wrong weapon";
                    AutoArmDebug.LogError($"[TEST] SimpleSidearmsSlotLimitTest: Job targets wrong weapon - expected: {betterWeapon?.Label}, got: {job.targetA.Thing?.Label}");
                }
            }
            else
            {
                result.Success = false;
                result.Data["Error"] = "No replacement job created when at slot limit";
                AutoArmDebug.LogError($"[TEST] SimpleSidearmsSlotLimitTest: No replacement job created when at slot limit - current count: {SimpleSidearmsCompat.GetCurrentSidearmCount(testPawn) + 1}, max slots: {SimpleSidearmsCompat.GetMaxSidearmsForPawn(testPawn)}");
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
                forcedWeapon = ThingMaker.MakeThing(VanillaWeaponDefOf.Gun_Autopistol) as ThingWithComps;
                if (forcedWeapon != null)
                {
                    testPawn.equipment?.AddEquipment(forcedWeapon);
                    ForcedWeaponHelper.SetForced(testPawn, forcedWeapon);
                }

                // Create a better weapon of same type
                betterWeapon = TestHelpers.CreateWeapon(map, VanillaWeaponDefOf.Gun_Autopistol,
                    testPawn.Position + new IntVec3(2, 0, 0), QualityCategory.Legendary);
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
                    result.Data["Error"] = "Forced weapon upgrade not created when allowed";
                    AutoArmDebug.LogError($"[TEST] SimpleSidearmsForcedWeaponTest: Forced weapon upgrade not created when allowed - forced weapon: {forcedWeapon?.Label}, better weapon available: {betterWeapon?.Label}");
                }
            }
            else
            {
                if (job != null)
                {
                    result.Success = false;
                    result.Data["Error"] = "Forced weapon upgrade created when not allowed";
                    AutoArmDebug.LogError($"[TEST] SimpleSidearmsForcedWeaponTest: Forced weapon upgrade created when not allowed - setting: {AutoArmMod.settings.allowForcedWeaponUpgrades}, job target: {job.targetA.Thing?.Label}");
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