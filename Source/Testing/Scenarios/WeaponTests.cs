// AutoArm RimWorld 1.5+ mod - automatic weapon management
// This file: Weapon tests for drop behavior and blacklist functionality
// Validates weapon filtering and restriction systems

using RimWorld;
using Verse;
using AutoArm.Caching; using AutoArm.Logging; using AutoArm.UI;
using AutoArm.Weapons;
using AutoArm.Jobs;
using AutoArm.Definitions;

namespace AutoArm.Testing.Scenarios
{
    public class WeaponDropTest : ITestScenario
    {
        public string Name => "Weapon Drop on Policy Change";
        private Pawn testPawn;
        private ThingWithComps rangedWeapon;
        private ApparelPolicy originalPolicy;
        private ApparelPolicy restrictivePolicy;

        public void Setup(Map map)
        {
            if (map == null) return;

            testPawn = TestHelpers.CreateTestPawn(map);

            if (testPawn != null)
            {
                // Give pawn a ranged weapon
                var rifleDef = VanillaWeaponDefOf.Gun_AssaultRifle;
                if (rifleDef != null)
                {
                    rangedWeapon = ThingMaker.MakeThing(rifleDef) as ThingWithComps;
                    if (rangedWeapon != null)
                    {
                        testPawn.equipment?.DestroyAllEquipment();
                        testPawn.equipment?.AddEquipment(rangedWeapon);
                    }
                }

                // Store original policy
                originalPolicy = testPawn.outfits?.CurrentApparelPolicy;

                // Create a restrictive policy that doesn't allow ranged weapons
                restrictivePolicy = new ApparelPolicy(testPawn.Map.uniqueID, "Test - Melee Only");

                // Get the weapons category
                var weaponsCat = DefDatabase<ThingCategoryDef>.GetNamedSilentFail("Weapons");
                if (weaponsCat != null)
                {
                    restrictivePolicy.filter.SetAllow(weaponsCat, true);
                }

                // Disallow all ranged weapons
                foreach (var weaponDef in WeaponValidation.RangedWeapons)
                {
                    restrictivePolicy.filter.SetAllow(weaponDef, false);
                }

                // Allow melee weapons
                foreach (var weaponDef in WeaponValidation.MeleeWeapons)
                {
                    restrictivePolicy.filter.SetAllow(weaponDef, true);
                }

                // Add policy to database
                Current.Game.outfitDatabase.AllOutfits.Add(restrictivePolicy);
            }
        }

        public TestResult Run()
        {
            if (testPawn == null || rangedWeapon == null)
                return TestResult.Failure("Test setup failed");

            if (testPawn.equipment?.Primary != rangedWeapon)
                return TestResult.Failure("Pawn doesn't have the test weapon equipped");

            var result = new TestResult { Success = true };
            result.Data["InitialWeapon"] = rangedWeapon.Label;

            // Change to restrictive policy
            if (testPawn.outfits != null)
            {
                testPawn.outfits.CurrentApparelPolicy = restrictivePolicy;
            }

            // The weapon drop check happens in Pawn_EquipmentTracker.Notify_ApparelPolicyChanged
            // which is called by the harmony patch when outfit changes
            // For testing, we'll check if the weapon should be dropped

            bool weaponAllowedByPolicy = restrictivePolicy.filter.Allows(rangedWeapon.def);
            result.Data["WeaponAllowedByNewPolicy"] = weaponAllowedByPolicy;

            if (weaponAllowedByPolicy)
            {
                result.Success = false;
                result.FailureReason = "Ranged weapon is still allowed by restrictive policy";
                result.Data["Error"] = "Policy failed to restrict ranged weapon";
                result.Data["Weapon"] = rangedWeapon.Label;
                result.Data["PolicyName"] = restrictivePolicy.label;
                result.Data["WeaponAllowed"] = true;
                result.Data["ExpectedAllowed"] = false;
                AutoArmLogger.LogError($"[TEST] WeaponDropTest: Ranged weapon is still allowed by restrictive policy - expected: false, got: true (weapon: {rangedWeapon.Label})");
                return result;
            }

            // In the actual game, the harmony patch would trigger the drop
            // For the test, we verify the policy correctly disallows the weapon
            result.Data["PolicyCorrectlyRestrictsWeapon"] = !weaponAllowedByPolicy;

            return result;
        }

        public void Cleanup()
        {
            // Restore original policy
            if (testPawn?.outfits != null && originalPolicy != null)
            {
                testPawn.outfits.CurrentApparelPolicy = originalPolicy;
            }

            // Remove test policy
            if (restrictivePolicy != null && Current.Game?.outfitDatabase?.AllOutfits != null)
            {
                Current.Game.outfitDatabase.AllOutfits.Remove(restrictivePolicy);
            }

            // Destroy pawn (which will also destroy their equipped weapon)
            if (testPawn != null && !testPawn.Destroyed)
            {
                testPawn.Destroy();
            }

            // Only destroy weapon if it somehow wasn't destroyed with the pawn
            if (rangedWeapon != null && !rangedWeapon.Destroyed)
            {
                rangedWeapon.Destroy();
            }
        }
    }

    public class WeaponBlacklistBasicTest : ITestScenario
    {
        public string Name => "Weapon Blacklist Basic Operations";
        private Pawn testPawn;
        private ThingDef testWeaponDef;

        public void Setup(Map map)
        {
            testPawn = TestHelpers.CreateTestPawn(map);
            testWeaponDef = VanillaWeaponDefOf.Gun_BoltActionRifle;
        }

        public TestResult Run()
        {
            var result = new TestResult { Success = true };

            // Test adding to blacklist
            WeaponBlacklist.AddToBlacklist(testWeaponDef, testPawn, "Test restriction");
            result.Data["AddedToBlacklist"] = WeaponBlacklist.IsBlacklisted(testWeaponDef, testPawn);

            if (!WeaponBlacklist.IsBlacklisted(testWeaponDef, testPawn))
            {
                result.Success = false;
                result.FailureReason = "Weapon not blacklisted after adding";
                result.Data["Error"] = "Blacklist add operation failed";
                result.Data["WeaponDef"] = testWeaponDef.defName;
                result.Data["BlacklistStatus"] = false;
                result.Data["ExpectedStatus"] = true;
                AutoArmLogger.LogError($"[TEST] WeaponBlacklistBasicTest: Weapon not blacklisted after adding - expected: true, got: false (weapon: {testWeaponDef.defName})");
            }

            // Test removing from blacklist
            WeaponBlacklist.RemoveFromBlacklist(testWeaponDef, testPawn);
            result.Data["RemovedFromBlacklist"] = !WeaponBlacklist.IsBlacklisted(testWeaponDef, testPawn);

            if (WeaponBlacklist.IsBlacklisted(testWeaponDef, testPawn))
            {
                result.Success = false;
                result.FailureReason = result.FailureReason ?? "Weapon still blacklisted after removing";
                result.Data["Error"] = result.Data.ContainsKey("Error") ? result.Data["Error"] + "; Blacklist remove operation failed" : "Blacklist remove operation failed";
                result.Data["Error2"] = "Weapon still blacklisted after removing";
                result.Data["BlacklistStatusAfterRemove"] = true;
                result.Data["ExpectedStatusAfterRemove"] = false;
                AutoArmLogger.LogError($"[TEST] WeaponBlacklistBasicTest: Weapon still blacklisted after removing - expected: false, got: true (weapon: {testWeaponDef.defName})");
            }

            return result;
        }

        public void Cleanup()
        {
            WeaponBlacklist.ClearBlacklist(testPawn);
            testPawn?.Destroy();
        }
    }

    public class WeaponBlacklistExpirationTest : ITestScenario
    {
        public string Name => "Weapon Blacklist Expiration";
        private Pawn testPawn;
        private ThingDef testWeaponDef;

        public void Setup(Map map)
        {
            testPawn = TestHelpers.CreateTestPawn(map);
            testWeaponDef = VanillaWeaponDefOf.Gun_BoltActionRifle;

            // Add weapon to blacklist
            WeaponBlacklist.AddToBlacklist(testWeaponDef, testPawn, "Expiration test");
        }

        public TestResult Run()
        {
            var result = new TestResult { Success = true };

            result.Data["InitiallyBlacklisted"] = WeaponBlacklist.IsBlacklisted(testWeaponDef, testPawn);

            // Simulate time passing (more than BLACKLIST_DURATION)
            // Note: In real testing, you'd need to actually advance game ticks
            // This is a simplified test
            WeaponBlacklist.CleanupOldEntries();

            result.Data["StillBlacklistedAfterCleanup"] = WeaponBlacklist.IsBlacklisted(testWeaponDef, testPawn);
            result.Data["Note"] = "Full expiration test requires advancing game time";

            return result;
        }

        public void Cleanup()
        {
            WeaponBlacklist.ClearBlacklist(testPawn);
            testPawn?.Destroy();
        }
    }

    public class WeaponBlacklistIntegrationTest : ITestScenario
    {
        public string Name => "Weapon Blacklist Job Integration";
        private Pawn testPawn;
        private ThingWithComps blacklistedWeapon;
        private ThingWithComps normalWeapon;

        public void Setup(Map map)
        {
            testPawn = TestHelpers.CreateTestPawn(map);

            if (testPawn != null)
            {
                testPawn.equipment?.DestroyAllEquipment();

                // Create a weapon that will be blacklisted
                blacklistedWeapon = TestHelpers.CreateWeapon(map, VanillaWeaponDefOf.Gun_BoltActionRifle,
                    testPawn.Position + new IntVec3(2, 0, 0));

                // Create a normal weapon
                normalWeapon = TestHelpers.CreateWeapon(map, VanillaWeaponDefOf.Gun_Autopistol,
                    testPawn.Position + new IntVec3(-2, 0, 0));

                if (blacklistedWeapon != null)
                {
                    ImprovedWeaponCacheManager.AddWeaponToCache(blacklistedWeapon);
                    WeaponBlacklist.AddToBlacklist(blacklistedWeapon.def, testPawn, "Integration test");
                }

                if (normalWeapon != null)
                {
                    ImprovedWeaponCacheManager.AddWeaponToCache(normalWeapon);
                }
            }
        }

        public TestResult Run()
        {
            var result = new TestResult { Success = true };
            var jobGiver = new JobGiver_PickUpBetterWeapon();

            result.Data["BlacklistedWeaponDef"] = blacklistedWeapon?.def?.defName ?? "null";
            result.Data["IsBlacklisted"] = WeaponBlacklist.IsBlacklisted(blacklistedWeapon?.def, testPawn);

            // Should not create job for blacklisted weapon
            var job = jobGiver.TestTryGiveJob(testPawn);

            if (job != null)
            {
                result.Data["JobCreated"] = true;
                result.Data["TargetWeapon"] = job.targetA.Thing?.Label ?? "Unknown";

                // Should target the normal weapon, not the blacklisted one
                if (job.targetA.Thing == blacklistedWeapon)
                {
                    result.Success = false;
                    result.FailureReason = "Job targets blacklisted weapon";
                    result.Data["Error"] = "Blacklist validation failed - job created for blacklisted weapon";
                    result.Data["BlacklistedWeapon"] = blacklistedWeapon?.Label;
                    result.Data["ExpectedTarget"] = normalWeapon?.Label;
                    result.Data["ActualTarget"] = job.targetA.Thing?.Label;
                    AutoArmLogger.LogError($"[TEST] WeaponBlacklistIntegrationTest: Job targets blacklisted weapon - expected: {normalWeapon?.Label}, got: {blacklistedWeapon?.Label}");
                }
            }

            return result;
        }

        public void Cleanup()
        {
            WeaponBlacklist.ClearBlacklist(testPawn);
            blacklistedWeapon?.Destroy();
            normalWeapon?.Destroy();
            testPawn?.Destroy();
        }
    }
}
