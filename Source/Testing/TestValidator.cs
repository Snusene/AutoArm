using System;
using Verse;

namespace AutoArm.Testing
{
    public static class TestValidator
    {
        public static void ValidateTests()
        {
            try
            {
                // Test 1: Check if TestRunner can be instantiated
                var testScenarios = TestRunner.GetAllTests();
                Log.Message($"[AutoArm] Found {testScenarios.Count} test scenarios");

                // Test 2: List all test names
                foreach (var test in testScenarios)
                {
                    Log.Message($"[AutoArm] Test: {test.Name}");
                }

                // Test 3: Check if TestHelpers can create a test pawn config
                var config = new TestHelpers.TestPawnConfig
                {
                    Name = "TestPawn",
                    MakeNoble = true,
                    Conceited = true
                };
                Log.Message($"[AutoArm] Created test config for: {config.Name}");

                // Test 4: Check VanillaWeaponDefOf
                var pistol = VanillaWeaponDefOf.Gun_Autopistol;
                Log.Message($"[AutoArm] Autopistol def: {pistol?.defName ?? "null"}");

                // Test 5: Check if JobGiver_PickUpBetterWeapon exists
                var jobGiver = new JobGiver_PickUpBetterWeapon();
                Log.Message($"[AutoArm] JobGiver_PickUpBetterWeapon created successfully");

                // Test 6: Check compatibility modules
                Log.Message($"[AutoArm] Simple Sidearms loaded: {SimpleSidearmsCompat.IsLoaded()}");
                Log.Message($"[AutoArm] Combat Extended loaded: {CECompat.IsLoaded()}");
                Log.Message($"[AutoArm] Infusion 2 loaded: {InfusionCompat.IsLoaded()}");

                Log.Message("[AutoArm] Test validation completed successfully");
            }
            catch (Exception e)
            {
                Log.Error($"[AutoArm] Test validation failed: {e}");
            }
        }
    }
}