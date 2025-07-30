using RimWorld;
using System;
using System.Linq;
using Verse;

namespace AutoArm
{
    /// <summary>
    /// Test scenario for SimpleSidearms validation fix
    /// </summary>
    public static class TestSimpleSidearmsValidation
    {
        public static void RunTest()
        {
            Log.Message("[AutoArm Test] Starting SimpleSidearms validation test...");

            try
            {
                // Find a test pawn
                var colonists = Find.CurrentMap?.mapPawns?.FreeColonists;
                var pawn = colonists != null && colonists.Count > 0 ? colonists[0] : null;
                if (pawn == null)
                {
                    Log.Warning("[AutoArm Test] No colonist found for testing");
                    AutoArmDebug.Log("[TEST] TestSimpleSidearmsValidation: No colonist found for testing - test cannot proceed");
                    return;
                }

                Log.Message($"[AutoArm Test] Testing with pawn: {pawn.Label}");

                // Check if SimpleSidearms is loaded
                if (!SimpleSidearmsCompat.IsLoaded())
                {
                    Log.Warning("[AutoArm Test] SimpleSidearms not loaded - test cannot proceed");
                    AutoArmDebug.Log("[TEST] TestSimpleSidearmsValidation: SimpleSidearms not loaded - this test requires SimpleSidearms mod");
                    return;
                }

                // Test Case 1: Unarmed pawn
                Log.Message("[AutoArm Test] Case 1: Testing unarmed pawn...");
                var originalPrimary = pawn.equipment?.Primary;
                if (originalPrimary != null)
                {
                    pawn.equipment.Remove(originalPrimary);
                }

                // Find a test weapon
                var testWeapon = Find.CurrentMap?.listerThings?.ThingsInGroup(ThingRequestGroup.Weapon)
                    ?.OfType<ThingWithComps>()
                    ?.FirstOrDefault(w => w.def.IsRangedWeapon && w.Map == pawn.Map);

                if (testWeapon != null)
                {
                    string reason;
                    bool canPickup = ValidationHelper.IsValidWeapon(testWeapon, pawn, out reason);
                    Log.Message($"[AutoArm Test] Unarmed pawn can pickup {testWeapon.Label}: {canPickup} (reason: {reason})");
                    if (!canPickup && pawn.equipment?.Primary == null)
                    {
                        AutoArmDebug.Log($"[TEST] TestSimpleSidearmsValidation: FAILURE - Unarmed pawn {pawn.Label} cannot pickup {testWeapon.Label}: {reason}");
                    }
                }

                // Test Case 2: True primary weapon
                if (originalPrimary != null)
                {
                    pawn.equipment.AddEquipment(originalPrimary);
                    Log.Message("[AutoArm Test] Case 2: Testing with true primary weapon...");

                    bool isRemembered = SimpleSidearmsCompat.IsRememberedSidearm(pawn, originalPrimary);
                    Log.Message($"[AutoArm Test] Current primary {originalPrimary.Label} is remembered sidearm: {isRemembered}");

                    if (testWeapon != null)
                    {
                        string reason;
                        bool canPickup = ValidationHelper.IsValidWeapon(testWeapon, pawn, out reason);
                        Log.Message($"[AutoArm Test] With primary, can pickup {testWeapon.Label}: {canPickup} (reason: {reason})");
                        // Log if SimpleSidearms is incorrectly blocking weapon pickup
                        if (!canPickup && reason != null && reason.Contains("sidearm") && !SimpleSidearmsCompat.IsRememberedSidearm(pawn, originalPrimary))
                        {
                            AutoArmDebug.Log($"[TEST] TestSimpleSidearmsValidation: Potential issue - primary weapon not marked as remembered sidearm, blocking pickup: {reason}");
                        }
                    }
                }

                // Test Case 3: Check if we have any remembered sidearms
                Log.Message("[AutoArm Test] Case 3: Checking for remembered sidearms...");
                if (pawn.inventory?.innerContainer != null)
                {
                    foreach (var item in pawn.inventory.innerContainer)
                    {
                        if (item is ThingWithComps weapon && weapon.def.IsWeapon)
                        {
                            bool isRemembered = SimpleSidearmsCompat.IsRememberedSidearm(pawn, weapon);
                            Log.Message($"[AutoArm Test] Inventory weapon {weapon.Label} is remembered: {isRemembered}");
                        }
                    }
                }

                // Test Case 4: Test weight limit validation
                Log.Message("[AutoArm Test] Case 4: Testing weight limit validation...");

                // Find heavy weapons
                var heavyWeapons = Find.CurrentMap?.listerThings?.ThingsInGroup(ThingRequestGroup.Weapon)
                    ?.OfType<ThingWithComps>()
                    ?.Where(w => w.def.IsWeapon && w.GetStatValue(StatDefOf.Mass) > 3.0f)
                    ?.OrderByDescending(w => w.GetStatValue(StatDefOf.Mass))
                    ?.Take(5);

                if (heavyWeapons != null && heavyWeapons.Any())
                {
                    foreach (var heavyWeapon in heavyWeapons)
                    {
                        string reason;
                        bool canPickup = SimpleSidearmsCompat.CanPickupSidearmInstance(heavyWeapon, pawn, out reason);
                        float weight = heavyWeapon.GetStatValue(StatDefOf.Mass);
                        Log.Message($"[AutoArm Test] {heavyWeapon.Label} ({weight:F1}kg) - SS allows: {canPickup} - Reason: {reason}");
                    }
                }

                // Also log SimpleSidearms settings
                SimpleSidearmsCompat.LogSimpleSidearmsSettings();

                // Log current inventory weight status
                if (pawn.inventory?.innerContainer != null)
                {
                    float totalWeight = 0f;
                    int weaponCount = 0;
                    foreach (var item in pawn.inventory.innerContainer)
                    {
                        if (item is ThingWithComps weapon && weapon.def.IsWeapon)
                        {
                            float weaponWeight = weapon.GetStatValue(StatDefOf.Mass);
                            totalWeight += weaponWeight;
                            weaponCount++;
                            Log.Message($"[AutoArm Test] Current sidearm: {weapon.Label} ({weaponWeight:F1}kg)");
                        }
                    }
                    Log.Message($"[AutoArm Test] Total sidearms: {weaponCount}, Total weight: {totalWeight:F1}kg");
                }

                Log.Message("[AutoArm Test] SimpleSidearms validation test completed");
            }
            catch (Exception ex)
            {
                Log.Error($"[AutoArm Test] Error during test: {ex}");
                AutoArmDebug.Log($"[TEST] TestSimpleSidearmsValidation: Test failed with exception: {ex.Message}");
            }
        }

        public static void TestWeaponValidation(Pawn pawn, ThingWithComps weapon)
        {
            if (pawn == null || weapon == null)
                return;

            Log.Message($"[AutoArm Test] Testing validation for {pawn.Label} with {weapon.Label}");

            string reason;
            bool isValid = ValidationHelper.IsValidWeapon(weapon, pawn, out reason);

            Log.Message($"[AutoArm Test] Weapon valid: {isValid}");
            if (!isValid)
            {
                Log.Message($"[AutoArm Test] Rejection reason: {reason}");
                AutoArmDebug.Log($"[TEST] TestSimpleSidearmsValidation: Weapon {weapon.Label} rejected for {pawn.Label}: {reason}");
            }

            // Additional diagnostics
            if (SimpleSidearmsCompat.IsLoaded())
            {
                string ssReason;
                bool ssAllowed = SimpleSidearmsCompat.CanPickupSidearmInstance(weapon, pawn, out ssReason);
                Log.Message($"[AutoArm Test] SimpleSidearms allows: {ssAllowed} (reason: {ssReason})");

                if (pawn.equipment?.Primary != null)
                {
                    bool primaryIsRemembered = SimpleSidearmsCompat.IsRememberedSidearm(pawn, pawn.equipment.Primary);
                    Log.Message($"[AutoArm Test] Current primary is remembered sidearm: {primaryIsRemembered}");
                }
            }
        }
    }
}