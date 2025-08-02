// AutoArm RimWorld 1.5+ mod - automatic weapon management
// This file: Tests for Pick Up and Haul + SimpleSidearms integration
// Validates inventory weapon management with both mods

using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;
using AutoArm.Testing.Helpers;

namespace AutoArm.Testing.Scenarios
{
    /// <summary>
    /// Comprehensive test for Pick Up and Haul + Simple Sidearms integration
    /// Tests the weapon hoarding fix implementation
    /// Note: Outfit filter validation removed - haulers can carry any weapon as cargo
    /// </summary>
    public class PickUpAndHaulWeaponHoardingTest : ITestScenario
    {
        public string Name => "Pick Up and Haul + Simple Sidearms Weapon Hoarding Prevention";
        
        private Pawn testPawn;
        private List<ThingWithComps> weaponsToHaul = new List<ThingWithComps>();
        private List<ThingWithComps> initialWeapons = new List<ThingWithComps>();
        private Map testMap;
        private IntVec3 haulDestination;
        private Zone_Stockpile weaponStockpile;

        public void Setup(Map map)
        {
            // Skip if both mods aren't loaded
            if (!PickUpAndHaulCompat.IsLoaded() || !SimpleSidearmsCompat.IsLoaded())
                return;

            testMap = map;
            testPawn = TestHelpers.CreateTestPawn(map);
            
            if (testPawn == null)
                return;

            // Clear pawn's equipment
            testPawn.equipment?.DestroyAllEquipment();
            
            // Give pawn initial weapons (1 ranged, 1 melee) at poor quality
            var pistol = SafeTestCleanup.SafeCreateWeapon(VanillaWeaponDefOf.Gun_Autopistol, null, QualityCategory.Poor);
            var knife = SafeTestCleanup.SafeCreateWeapon(VanillaWeaponDefOf.MeleeWeapon_Knife, null, QualityCategory.Poor);
            
            if (pistol != null)
            {
                SafeTestCleanup.SafeEquipWeapon(testPawn, pistol);
                initialWeapons.Add(pistol);
            }
            
            if (knife != null)
            {
                SafeTestCleanup.SafeAddToInventory(testPawn, knife);
                initialWeapons.Add(knife);
            }

            // Clear fog in test area
            var fogGrid = map.fogGrid;
            if (fogGrid != null)
            {
                foreach (var cell in GenRadial.RadialCellsAround(testPawn.Position, 20, true))
                {
                    if (cell.InBounds(map))
                    {
                        fogGrid.Unfog(cell);
                    }
                }
            }

            // Create a stockpile for weapons
            haulDestination = testPawn.Position + new IntVec3(10, 0, 0);
            CreateWeaponStockpile(map, haulDestination);

            // Create multiple weapons to haul - mix of types and qualities
            CreateWeaponsToHaul(map, testPawn.Position + new IntVec3(3, 0, 0));
        }

        private void CreateWeaponStockpile(Map map, IntVec3 center)
        {
            try
            {
                // Create a 3x3 stockpile zone
                var cells = new List<IntVec3>();
                for (int x = -1; x <= 1; x++)
                {
                    for (int z = -1; z <= 1; z++)
                    {
                        var cell = center + new IntVec3(x, 0, z);
                        if (cell.InBounds(map) && cell.Standable(map))
                        {
                            cells.Add(cell);
                        }
                    }
                }

                if (cells.Count > 0)
                {
                    weaponStockpile = new Zone_Stockpile(StorageSettingsPreset.DefaultStockpile, map.zoneManager);
                    map.zoneManager.RegisterZone(weaponStockpile);
                    
                    foreach (var cell in cells)
                    {
                        weaponStockpile.AddCell(cell);
                    }

                    // Configure to accept only weapons
                    weaponStockpile.settings.filter.SetDisallowAll();
                    foreach (var weaponDef in WeaponThingFilterUtility.AllWeapons)
                    {
                        weaponStockpile.settings.filter.SetAllow(weaponDef, true);
                    }
                    weaponStockpile.settings.Priority = StoragePriority.Critical;
                    
                    AutoArmLogger.Log($"[TEST] Created weapon stockpile at {center} with {cells.Count} cells");
                }
            }
            catch (Exception e)
            {
                AutoArmLogger.LogError($"[TEST] Failed to create stockpile: {e.Message}");
            }
        }

        private void CreateWeaponsToHaul(Map map, IntVec3 startPos)
        {
            var weaponConfigs = new[]
            {
                // Multiple pistols of varying quality (to test duplicate handling)
                (VanillaWeaponDefOf.Gun_Autopistol, QualityCategory.Normal),
                (VanillaWeaponDefOf.Gun_Autopistol, QualityCategory.Good),
                (VanillaWeaponDefOf.Gun_Autopistol, QualityCategory.Excellent),
                
                // Multiple knives (to test duplicate handling)
                (VanillaWeaponDefOf.MeleeWeapon_Knife, QualityCategory.Good),
                (VanillaWeaponDefOf.MeleeWeapon_Knife, QualityCategory.Excellent),
                
                // Different weapon types to exceed slot limits
                (VanillaWeaponDefOf.Gun_AssaultRifle, QualityCategory.Normal),
                (VanillaWeaponDefOf.Gun_SniperRifle, QualityCategory.Good),
                (VanillaWeaponDefOf.MeleeWeapon_LongSword, QualityCategory.Normal),
                
                // Heavy weapons to test weight limits
                (DefDatabase<ThingDef>.GetNamedSilentFail("Gun_LMG"), QualityCategory.Normal),
                (DefDatabase<ThingDef>.GetNamedSilentFail("Gun_Minigun"), QualityCategory.Poor)
            };

            int offset = 0;
            foreach (var (weaponDef, quality) in weaponConfigs)
            {
                if (weaponDef != null)
                {
                    var pos = startPos + new IntVec3(offset % 3, 0, offset / 3);
                    var weapon = TestHelpers.CreateWeapon(map, weaponDef, pos, quality);
                    
                    if (weapon != null)
                    {
                        weaponsToHaul.Add(weapon);
                        ImprovedWeaponCacheManager.AddWeaponToCache(weapon);
                        AutoArmLogger.Log($"[TEST] Created {weapon.Label} (Q:{quality}) at {pos}");
                    }
                    offset++;
                }
            }

            AutoArmLogger.Log($"[TEST] Created {weaponsToHaul.Count} weapons to haul");
        }

        public TestResult Run()
        {
            // Skip if both mods aren't loaded
            if (!PickUpAndHaulCompat.IsLoaded() || !SimpleSidearmsCompat.IsLoaded())
            {
                return TestResult.Pass(); // Skip test
            }

            if (testPawn == null || testMap == null)
            {
                return TestResult.Failure("Test setup failed");
            }

            var result = new TestResult { Success = true };
            
            // Record initial state
            var initialPrimaryDef = testPawn.equipment?.Primary?.def;
            var initialInventoryCount = testPawn.inventory?.innerContainer?.Count(t => t.def.IsWeapon) ?? 0;
            int maxSidearms = SimpleSidearmsCompat.GetMaxSidearmsForPawn(testPawn);
            
            result.Data["InitialPrimary"] = initialPrimaryDef?.label ?? "none";
            result.Data["InitialSidearmCount"] = initialInventoryCount;
            result.Data["MaxSidearmsAllowed"] = maxSidearms;
            result.Data["WeaponsToHaul"] = weaponsToHaul.Count;

            // Simulate Pick Up and Haul job - simulate multiple haul cycles
            AutoArmLogger.Log("[TEST] Starting haul simulation...");
            
            int hauledCount = 0;
            var weaponsByType = new Dictionary<ThingDef, List<ThingWithComps>>();
            
            // Group weapons by type to simulate how Pick Up and Haul would batch them
            foreach (var weapon in weaponsToHaul)
            {
                if (!weaponsByType.ContainsKey(weapon.def))
                    weaponsByType[weapon.def] = new List<ThingWithComps>();
                weaponsByType[weapon.def].Add(weapon);
            }

            // Simulate hauling each type group (Pick Up and Haul tends to haul similar items together)
            foreach (var weaponGroup in weaponsByType.Values)
            {
                AutoArmLogger.Log($"[TEST] Simulating haul of {weaponGroup.Count} {weaponGroup.First().def.label} weapons");
                
                foreach (var weapon in weaponGroup)
                {
                    // Check if pawn would be allowed to pick this up
                    string reason;
                    bool canPickup = SimpleSidearmsCompat.CanPickupSidearmInstance(weapon, testPawn, out reason);
                    
                    if (canPickup || !WeaponValidation.IsProperWeapon(weapon))
                    {
                        // Simulate picking it up into inventory
                        if (testPawn.inventory.innerContainer.TryAddOrTransfer(weapon))
                        {
                            hauledCount++;
                            AutoArmLogger.Log($"[TEST] Added {weapon.Label} to inventory");
                        }
                    }
                    else
                    {
                        AutoArmLogger.Log($"[TEST] SimpleSidearms prevented pickup of {weapon.Label}: {reason}");
                    }
                }
                
                // After each batch, run validation (simulating end of haul job)
                AutoArmLogger.Log("[TEST] Running post-haul validation...");
                PickUpAndHaulCompat.ValidateInventoryWeapons(testPawn);
            }

            result.Data["WeaponsHauled"] = hauledCount;

            // Check final state
            var finalInventoryWeapons = testPawn.inventory?.innerContainer?
                .Where(t => t.def.IsWeapon)
                .OfType<ThingWithComps>()
                .ToList() ?? new List<ThingWithComps>();
            
            var finalPrimary = testPawn.equipment?.Primary;
            int finalWeaponCount = finalInventoryWeapons.Count + (finalPrimary != null ? 1 : 0);
            
            result.Data["FinalPrimary"] = finalPrimary?.Label ?? "none";
            result.Data["FinalSidearmCount"] = finalInventoryWeapons.Count;
            result.Data["FinalTotalWeapons"] = finalWeaponCount;

            // Log detailed final inventory
            AutoArmLogger.Log("[TEST] Final inventory state:");
            if (finalPrimary != null)
            {
                var score = WeaponScoreCache.GetCachedScore(testPawn, finalPrimary);
                AutoArmLogger.Log($"[TEST]   Primary: {finalPrimary.Label} (Score: {score:F2})");
            }
            
            var weaponTypeCounts = new Dictionary<ThingDef, int>();
            if (finalPrimary != null)
                weaponTypeCounts[finalPrimary.def] = 1;
                
            foreach (var weapon in finalInventoryWeapons)
            {
                var score = WeaponScoreCache.GetCachedScore(testPawn, weapon);
                AutoArmLogger.Log($"[TEST]   Sidearm: {weapon.Label} (Score: {score:F2})");
                
                if (!weaponTypeCounts.ContainsKey(weapon.def))
                    weaponTypeCounts[weapon.def] = 0;
                weaponTypeCounts[weapon.def]++;
            }

            // Verify no hoarding occurred
            bool hasHoarding = false;
            string hoardingDetails = "";

            // Check 1: Total weapon count should not exceed max allowed
            if (finalWeaponCount > maxSidearms + 1) // +1 for primary
            {
                hasHoarding = true;
                hoardingDetails += $"Total weapons ({finalWeaponCount}) exceeds limit ({maxSidearms + 1}). ";
                result.Data["Error_ExceededTotal"] = $"{finalWeaponCount} > {maxSidearms + 1}";
            }

            // Check 2: No duplicate weapon types (if setting disallows)
            if (!SimpleSidearmsCompat.ALLOW_DUPLICATE_WEAPON_TYPES)
            {
                foreach (var kvp in weaponTypeCounts)
                {
                    if (kvp.Value > 1)
                    {
                        hasHoarding = true;
                        hoardingDetails += $"Multiple {kvp.Key.label} ({kvp.Value}). ";
                        result.Data[$"Error_Duplicate_{kvp.Key.defName}"] = kvp.Value;
                    }
                }
            }

            // Check 3 removed: We no longer check outfit filters for hauled weapons
            // Haulers should be able to transport any weapon regardless of outfit settings

            // Check 3: Verify best weapons were kept
            var allAvailableWeapons = new List<ThingWithComps>();
            allAvailableWeapons.AddRange(finalInventoryWeapons);
            if (finalPrimary != null) allAvailableWeapons.Add(finalPrimary);
            
            // Add any weapons that were dropped
            var droppedWeapons = weaponsToHaul.Where(w => !allAvailableWeapons.Contains(w) && w.Spawned).ToList();
            
            foreach (var weaponType in weaponTypeCounts.Keys)
            {
                var keptWeapon = allAvailableWeapons.FirstOrDefault(w => w.def == weaponType);
                var droppedOfSameType = droppedWeapons.Where(w => w.def == weaponType).ToList();
                
                if (keptWeapon != null && droppedOfSameType.Any())
                {
                    var keptScore = WeaponScoreCache.GetCachedScore(testPawn, keptWeapon);
                    foreach (var dropped in droppedOfSameType)
                    {
                        var droppedScore = WeaponScoreCache.GetCachedScore(testPawn, dropped);
                        if (droppedScore > keptScore * 1.1f) // 10% margin
                        {
                            result.Data[$"Warning_KeptWorse_{weaponType.defName}"] = $"Kept {keptWeapon.Label} (score:{keptScore:F2}) instead of {dropped.Label} (score:{droppedScore:F2})";
                        }
                    }
                }
            }

            // Final result
            if (hasHoarding)
            {
                result.Success = false;
                result.FailureReason = "Weapon hoarding detected after Pick Up and Haul operations";
                result.Data["HoardingDetails"] = hoardingDetails.Trim();
                AutoArmLogger.LogError($"[TEST] FAILED: {hoardingDetails}");
            }
            else
            {
                result.Data["Result"] = "Success - No hoarding detected";
                AutoArmLogger.Log("[TEST] SUCCESS: No weapon hoarding detected");
            }

            // Additional validation data
            result.Data["DroppedWeapons"] = droppedWeapons.Count;
            result.Data["PickUpAndHaulLoaded"] = PickUpAndHaulCompat.IsLoaded();
            result.Data["SimpleSidearmsLoaded"] = SimpleSidearmsCompat.IsLoaded();
            result.Data["AllowDuplicateTypes"] = SimpleSidearmsCompat.ALLOW_DUPLICATE_WEAPON_TYPES;

            return result;
        }

        public void Cleanup()
        {
            // Destroy stockpile
            if (weaponStockpile != null && testMap?.zoneManager != null)
            {
                try
                {
                    testMap.zoneManager.DeregisterZone(weaponStockpile);
                }
                catch { }
            }

            // Destroy all test items
            foreach (var weapon in weaponsToHaul)
            {
                weapon?.Destroy();
            }
            
            foreach (var weapon in initialWeapons)
            {
                weapon?.Destroy();
            }
            
            testPawn?.Destroy();
            
            weaponsToHaul.Clear();
            initialWeapons.Clear();
        }
    }

    /// <summary>
    /// Test weapon replacement logic during hauling
    /// </summary>
    public class PickUpAndHaulWeaponReplacementTest : ITestScenario
    {
        public string Name => "Pick Up and Haul Weapon Replacement Logic";
        
        private Pawn testPawn;
        private List<ThingWithComps> testWeapons = new List<ThingWithComps>();
        private bool originalAllowSidearmUpgrades;

        public void Setup(Map map)
        {
            if (!PickUpAndHaulCompat.IsLoaded() || !SimpleSidearmsCompat.IsLoaded())
                return;

            // Save and ensure the setting is enabled for this test
            originalAllowSidearmUpgrades = AutoArmMod.settings.allowSidearmUpgrades;
            AutoArmMod.settings.allowSidearmUpgrades = true;

            testPawn = TestHelpers.CreateTestPawn(map);
            if (testPawn == null)
                return;

            // Fill pawn to slot limit with poor quality weapons
            int maxSlots = SimpleSidearmsCompat.GetMaxSidearmsForPawn(testPawn);
            
            // Give poor quality weapons
            for (int i = 0; i < maxSlots + 1; i++) // +1 for primary
            {
                var weaponDef = i % 2 == 0 ? VanillaWeaponDefOf.Gun_Autopistol : VanillaWeaponDefOf.MeleeWeapon_Knife;
                var weapon = SafeTestCleanup.SafeCreateWeapon(weaponDef, null, QualityCategory.Poor);
                
                if (weapon != null)
                {
                    if (i == 0)
                        SafeTestCleanup.SafeEquipWeapon(testPawn, weapon);
                    else
                        SafeTestCleanup.SafeAddToInventory(testPawn, weapon);
                    testWeapons.Add(weapon);
                }
            }

            // Clear fog
            var fogGrid = map.fogGrid;
            if (fogGrid != null)
            {
                foreach (var cell in GenRadial.RadialCellsAround(testPawn.Position, 15, true))
                {
                    if (cell.InBounds(map))
                        fogGrid.Unfog(cell);
                }
            }

            // Create excellent quality weapons to trigger replacement
            var excellentPistol = TestHelpers.CreateWeapon(map, VanillaWeaponDefOf.Gun_Autopistol,
                testPawn.Position + new IntVec3(2, 0, 0), QualityCategory.Excellent);
            var excellentKnife = TestHelpers.CreateWeapon(map, VanillaWeaponDefOf.MeleeWeapon_Knife,
                testPawn.Position + new IntVec3(3, 0, 0), QualityCategory.Excellent);
            
            if (excellentPistol != null)
            {
                excellentPistol.SetForbidden(false, false);
                testWeapons.Add(excellentPistol);
                ImprovedWeaponCacheManager.AddWeaponToCache(excellentPistol);
            }
            
            if (excellentKnife != null)
            {
                excellentKnife.SetForbidden(false, false);
                testWeapons.Add(excellentKnife);
                ImprovedWeaponCacheManager.AddWeaponToCache(excellentKnife);
            }
        }

        public TestResult Run()
        {
            if (!PickUpAndHaulCompat.IsLoaded() || !SimpleSidearmsCompat.IsLoaded())
                return TestResult.Pass();

            if (testPawn == null)
                return TestResult.Failure("Test setup failed");

            var result = new TestResult { Success = true };
            
            // Get initial state
            var initialWeapons = new List<ThingWithComps>();
            if (testPawn.equipment?.Primary != null)
                initialWeapons.Add(testPawn.equipment.Primary);
            initialWeapons.AddRange(testPawn.inventory?.innerContainer?.OfType<ThingWithComps>()
                .Where(w => w.def.IsWeapon) ?? Enumerable.Empty<ThingWithComps>());
            
            result.Data["InitialWeaponCount"] = initialWeapons.Count;
            result.Data["AllowSidearmUpgrades"] = AutoArmMod.settings.allowSidearmUpgrades;

            // Find the excellent weapons
            var excellentWeapons = testWeapons
                .Where(w => w.Spawned && w.TryGetQuality(out var q) && q == QualityCategory.Excellent)
                .ToList();
            
            result.Data["ExcellentWeaponsAvailable"] = excellentWeapons.Count;

            // Debug: Log current state
            AutoArmLogger.Log($"[TEST] Pawn has {initialWeapons.Count} weapons (max allowed: {SimpleSidearmsCompat.GetMaxSidearmsForPawn(testPawn) + 1})");
            AutoArmLogger.Log($"[TEST] allowSidearmUpgrades setting: {AutoArmMod.settings.allowSidearmUpgrades}");
            foreach (var weapon in excellentWeapons)
            {
                AutoArmLogger.Log($"[TEST] Available excellent weapon: {weapon.Label} at {weapon.Position}");
            }

            // Test SimpleSidearms replacement job creation
            AutoArmLogger.Log("[TEST] Testing SimpleSidearms replacement logic...");
            
            // Debug current state
            var currentSlotCount = SimpleSidearmsCompat.GetCurrentSidearmCount(testPawn);
            var maxSlots = SimpleSidearmsCompat.GetMaxSidearmsForPawn(testPawn);
            AutoArmLogger.Log($"[TEST] Current sidearm slots: {currentSlotCount} / {maxSlots}");
            AutoArmLogger.Log($"[TEST] Is at capacity: {currentSlotCount >= maxSlots}");
            
            // Check if SS is properly initialized
            bool isLoaded = SimpleSidearmsCompat.IsLoaded();
            AutoArmLogger.Log($"[TEST] SimpleSidearms loaded: {isLoaded}");
            
            var ssJob = SimpleSidearmsCompat.TryGetSidearmUpgradeJob(testPawn);
            
            if (ssJob != null)
            {
                result.Data["SS_JobCreated"] = true;
                result.Data["SS_JobTarget"] = ssJob.targetA.Thing?.Label ?? "unknown";
                
                // Execute the job to see if replacement works
                if (ssJob.targetA.Thing is ThingWithComps targetWeapon)
                {
                    // Simulate the replacement
                    AutoArmLogger.Log($"[TEST] Simulating replacement with {targetWeapon.Label}");
                    
                    // The new logic should drop worst weapon first
                    var worstScore = float.MaxValue;
                    ThingWithComps worstWeapon = null;
                    
                    foreach (var weapon in initialWeapons)
                    {
                        if (!ForcedWeaponHelper.IsWeaponDefForced(testPawn, weapon.def))
                        {
                            var score = WeaponScoreCache.GetCachedScore(testPawn, weapon);
                            if (score < worstScore)
                            {
                                worstScore = score;
                                worstWeapon = weapon;
                            }
                        }
                    }
                    
                    if (worstWeapon != null)
                    {
                        result.Data["WorstWeapon"] = $"{worstWeapon.Label} (score:{worstScore:F2})";
                        result.Data["ReplacementWeapon"] = $"{targetWeapon.Label} (score:{WeaponScoreCache.GetCachedScore(testPawn, targetWeapon):F2})";
                    }
                }
            }
            else
            {
                result.Data["SS_JobCreated"] = false;
                
                if (AutoArmMod.settings.allowSidearmUpgrades && currentSlotCount >= maxSlots)
                {
                    // Check what excellent weapons are actually available and reachable
                    var reachableExcellent = excellentWeapons.Where(w => 
                        w.Spawned && 
                        testPawn.CanReserveAndReach(w, PathEndMode.ClosestTouch, Danger.Deadly)).ToList();
                    
                    result.Data["ReachableExcellentWeapons"] = reachableExcellent.Count;
                    
                    if (reachableExcellent.Any())
                    {
                        result.Success = false;
                        result.FailureReason = "SimpleSidearms failed to create replacement job when at capacity";
                        result.Data["CurrentSlots"] = currentSlotCount;
                        result.Data["MaxSlots"] = maxSlots;
                        result.Data["AvailableUpgrades"] = string.Join(", ", reachableExcellent.Select(w => w.Label));
                        AutoArmLogger.LogError("[TEST] No replacement job created despite being at capacity with better weapons available");
                        
                        // Extra debug: check if any of the current weapons would be replaced
                        var worstScore = float.MaxValue;
                        ThingWithComps worstWeapon = null;
                        foreach (var weapon in initialWeapons)
                        {
                            var score = WeaponScoreCache.GetCachedScore(testPawn, weapon);
                            if (score < worstScore)
                            {
                                worstScore = score;
                                worstWeapon = weapon;
                            }
                        }
                        
                        if (worstWeapon != null)
                        {
                            result.Data["WorstCurrentWeapon"] = $"{worstWeapon.Label} (score: {worstScore:F2})";
                            var bestUpgrade = reachableExcellent.OrderByDescending(w => WeaponScoreCache.GetCachedScore(testPawn, w)).FirstOrDefault();
                            if (bestUpgrade != null)
                            {
                                var bestScore = WeaponScoreCache.GetCachedScore(testPawn, bestUpgrade);
                                result.Data["BestAvailableUpgrade"] = $"{bestUpgrade.Label} (score: {bestScore:F2})";
                                result.Data["UpgradeImprovement"] = $"{(bestScore / worstScore - 1) * 100:F1}%";
                            }
                        }
                    }
                    else
                    {
                        result.Data["NoReachableUpgrades"] = "No excellent weapons are reachable";
                    }
                }
            }

            // Now test Pick Up and Haul validation
            AutoArmLogger.Log("[TEST] Testing Pick Up and Haul validation...");
            
            // Manually add an extra weapon to exceed limits
            var extraWeapon = testWeapons.FirstOrDefault(w => w.Spawned);
            if (extraWeapon != null)
            {
                testPawn.inventory.innerContainer.TryAddOrTransfer(extraWeapon);
                result.Data["AddedExtraWeapon"] = extraWeapon.Label;
                
                // Run validation
                var beforeCount = testPawn.inventory.innerContainer.Count(t => t.def.IsWeapon);
                PickUpAndHaulCompat.ValidateInventoryWeapons(testPawn);
                var afterCount = testPawn.inventory.innerContainer.Count(t => t.def.IsWeapon);
                
                result.Data["WeaponsBeforeValidation"] = beforeCount;
                result.Data["WeaponsAfterValidation"] = afterCount;
                
                if (beforeCount > afterCount)
                {
                    result.Data["ValidationWorked"] = "Yes - dropped excess weapons";
                }
                else if (beforeCount <= SimpleSidearmsCompat.GetMaxSidearmsForPawn(testPawn))
                {
                    result.Data["ValidationWorked"] = "Not needed - within limits";
                }
                else
                {
                    result.Success = false;
                    result.FailureReason = "Validation failed to drop excess weapons";
                }
            }

            return result;
        }

        public void Cleanup()
        {
            // Restore original setting
            AutoArmMod.settings.allowSidearmUpgrades = originalAllowSidearmUpgrades;
            
            foreach (var weapon in testWeapons)
            {
                weapon?.Destroy();
            }
            testPawn?.Destroy();
            testWeapons.Clear();
        }
    }

    /// <summary>
    /// Comprehensive test that verifies all weapon hoarding fixes are working correctly
    /// </summary>
    public class ComprehensiveWeaponHoardingFixTest : ITestScenario
    {
        public string Name => "Comprehensive Weapon Hoarding Fix Test (Pick Up and Haul + Simple Sidearms)";
        
        private Pawn testPawn;
        private Map testMap;
        private List<ThingWithComps> allTestWeapons = new List<ThingWithComps>();
        private Zone_Stockpile weaponStockpile;

        public void Setup(Map map)
        {
            // Skip if both mods aren't loaded
            if (!PickUpAndHaulCompat.IsLoaded() || !SimpleSidearmsCompat.IsLoaded())
                return;

            testMap = map;
            testPawn = TestHelpers.CreateTestPawn(map);
            
            if (testPawn == null)
                return;

            // Clear pawn's equipment
            testPawn.equipment?.DestroyAllEquipment();
            
            // Clear fog in test area
            var fogGrid = map.fogGrid;
            if (fogGrid != null)
            {
                foreach (var cell in GenRadial.RadialCellsAround(testPawn.Position, 30, true))
                {
                    if (cell.InBounds(map))
                        fogGrid.Unfog(cell);
                }
            }

            // Setup test scenario with various weapon configurations
            SetupWeaponScenarios(map);
        }

        private void SetupWeaponScenarios(Map map)
        {
            // Scenario 1: Pawn starts with full inventory of poor weapons
            int maxSidearms = SimpleSidearmsCompat.GetMaxSidearmsForPawn(testPawn);
            
            // Give poor quality primary
            var poorPistol = SafeTestCleanup.SafeCreateWeapon(VanillaWeaponDefOf.Gun_Autopistol, null, QualityCategory.Poor);
            if (poorPistol != null)
            {
                SafeTestCleanup.SafeEquipWeapon(testPawn, poorPistol);
                allTestWeapons.Add(poorPistol);
            }
            
            // Fill inventory with poor quality sidearms
            for (int i = 0; i < maxSidearms; i++)
            {
                var weaponDef = i % 2 == 0 ? VanillaWeaponDefOf.MeleeWeapon_Knife : VanillaWeaponDefOf.Gun_Revolver;
                var weapon = SafeTestCleanup.SafeCreateWeapon(weaponDef, null, QualityCategory.Poor);
                if (weapon != null)
                {
                    SafeTestCleanup.SafeAddToInventory(testPawn, weapon);
                    allTestWeapons.Add(weapon);
                }
            }

            // Create stockpile for hauling test
            var stockpilePos = testPawn.Position + new IntVec3(15, 0, 0);
            CreateWeaponStockpile(map, stockpilePos);

            // Create various weapons around the map
            CreateTestWeapons(map);
        }

        private void CreateWeaponStockpile(Map map, IntVec3 center)
        {
            try
            {
                var cells = new List<IntVec3>();
                for (int x = -2; x <= 2; x++)
                {
                    for (int z = -2; z <= 2; z++)
                    {
                        var cell = center + new IntVec3(x, 0, z);
                        if (cell.InBounds(map) && cell.Standable(map))
                            cells.Add(cell);
                    }
                }

                if (cells.Count > 0)
                {
                    weaponStockpile = new Zone_Stockpile(StorageSettingsPreset.DefaultStockpile, map.zoneManager);
                    map.zoneManager.RegisterZone(weaponStockpile);
                    
                    foreach (var cell in cells)
                        weaponStockpile.AddCell(cell);

                    weaponStockpile.settings.filter.SetDisallowAll();
                    foreach (var weaponDef in WeaponThingFilterUtility.AllWeapons)
                        weaponStockpile.settings.filter.SetAllow(weaponDef, true);
                    weaponStockpile.settings.Priority = StoragePriority.Critical;
                }
            }
            catch { }
        }

        private void CreateTestWeapons(Map map)
        {
            var basePos = testPawn.Position + new IntVec3(5, 0, 0);
            
            // Create weapons for different test scenarios
            var weaponConfigs = new[]
            {
                // High quality duplicates (to test duplicate handling)
                (VanillaWeaponDefOf.Gun_Autopistol, QualityCategory.Legendary, "Duplicate test 1"),
                (VanillaWeaponDefOf.Gun_Autopistol, QualityCategory.Masterwork, "Duplicate test 2"),
                (VanillaWeaponDefOf.MeleeWeapon_Knife, QualityCategory.Legendary, "Duplicate test 3"),
                
                // Different weapon types (to test variety limits)
                (VanillaWeaponDefOf.Gun_AssaultRifle, QualityCategory.Excellent, "Variety test 1"),
                (VanillaWeaponDefOf.Gun_SniperRifle, QualityCategory.Good, "Variety test 2"),
                (VanillaWeaponDefOf.MeleeWeapon_LongSword, QualityCategory.Excellent, "Variety test 3"),
                
                // Heavy weapons (to test weight limits)
                (DefDatabase<ThingDef>.GetNamedSilentFail("Gun_LMG"), QualityCategory.Good, "Weight test 1"),
                (DefDatabase<ThingDef>.GetNamedSilentFail("Gun_Minigun"), QualityCategory.Normal, "Weight test 2"),
                
                // Weapons for pickup/drop loop testing
                (VanillaWeaponDefOf.Gun_Revolver, QualityCategory.Good, "Loop test 1"),
                (VanillaWeaponDefOf.Gun_Revolver, QualityCategory.Normal, "Loop test 2")
            };

            int offset = 0;
            foreach (var (weaponDef, quality, purpose) in weaponConfigs)
            {
                if (weaponDef != null)
                {
                    var pos = basePos + new IntVec3(offset % 4, 0, offset / 4);
                    var weapon = TestHelpers.CreateWeapon(map, weaponDef, pos, quality);
                    
                    if (weapon != null)
                    {
                        allTestWeapons.Add(weapon);
                        ImprovedWeaponCacheManager.AddWeaponToCache(weapon);
                        AutoArmLogger.Log($"[TEST] Created {weapon.Label} (Q:{quality}) for {purpose} at {pos}");
                    }
                    offset++;
                }
            }
        }

        public TestResult Run()
        {
            if (!PickUpAndHaulCompat.IsLoaded() || !SimpleSidearmsCompat.IsLoaded())
                return TestResult.Pass();

            if (testPawn == null || testMap == null)
                return TestResult.Failure("Test setup failed");

            var result = new TestResult { Success = true };
            var testLog = new List<string>();
            
            try
            {
                // Test 1: Weapon replacement logic (drops worst before picking up better)
                testLog.Add("=== Test 1: Weapon Replacement Logic ===");
                string test1Error = null;
                bool replacementTestPassed = TestWeaponReplacement(testLog, out test1Error);
                result.Data["Test1_WeaponReplacement"] = replacementTestPassed ? "PASS" : "FAIL";
                if (!replacementTestPassed) 
                {
                    result.Success = false;
                    if (!string.IsNullOrEmpty(test1Error))
                        result.Data["Test1_Error"] = test1Error;
                }

                // Test 2: Pick Up and Haul compatibility (no hoarding during haul)
                testLog.Add("\n=== Test 2: Pick Up and Haul Compatibility ===");
                string test2Error = null;
                bool haulTestPassed = TestPickUpAndHaulCompatibility(testLog, out test2Error);
                result.Data["Test2_HaulCompatibility"] = haulTestPassed ? "PASS" : "FAIL";
                if (!haulTestPassed) 
                {
                    result.Success = false;
                    if (!string.IsNullOrEmpty(test2Error))
                        result.Data["Test2_Error"] = test2Error;
                }

                // Test 3: Post-haul validation (drops excess weapons)
                testLog.Add("\n=== Test 3: Post-Haul Validation ===");
                string test3Error = null;
                bool validationTestPassed = TestPostHaulValidation(testLog, out test3Error);
                result.Data["Test3_PostHaulValidation"] = validationTestPassed ? "PASS" : "FAIL";
                if (!validationTestPassed) 
                {
                    result.Success = false;
                    if (!string.IsNullOrEmpty(test3Error))
                        result.Data["Test3_Error"] = test3Error;
                }

                // Test 4: Cooldown system (prevents pickup/drop loops)
                testLog.Add("\n=== Test 4: Cooldown System ===");
                string test4Error = null;
                bool cooldownTestPassed = TestCooldownSystem(testLog, out test4Error);
                result.Data["Test4_CooldownSystem"] = cooldownTestPassed ? "PASS" : "FAIL";
                if (!cooldownTestPassed) 
                {
                    result.Success = false;
                    if (!string.IsNullOrEmpty(test4Error))
                        result.Data["Test4_Error"] = test4Error;
                }

                if (!result.Success)
                {
                    result.FailureReason = "One or more weapon hoarding fixes failed";
                    AutoArmLogger.LogError($"[TEST] Comprehensive test FAILED.");
                }
                else
                {
                    AutoArmLogger.Log("[TEST] Comprehensive test PASSED - All weapon hoarding fixes working correctly");
                }
                
                // Log details for debugging
                foreach (var line in testLog)
                {
                    AutoArmLogger.Log($"[TEST] {line}");
                }
            }
            catch (Exception e)
            {
                result.Success = false;
                result.FailureReason = $"Test threw exception: {e.Message}";
                AutoArmLogger.LogError($"[TEST] Exception during test: {e}");
            }

            return result;
        }

        private bool TestWeaponReplacement(List<string> log, out string error)
        {
            error = null;
            log.Add("Testing if worst weapons are dropped before picking up better ones...");
            
            // Find a legendary weapon to trigger replacement
            var legendaryWeapon = allTestWeapons.FirstOrDefault(w => w.Spawned && 
                w.TryGetQuality(out var q) && q == QualityCategory.Legendary);
            
            if (legendaryWeapon == null)
            {
                log.Add("ERROR: No legendary weapon found for test");
                error = "No legendary weapon found for test";
                return false;
            }

            var initialWeapons = GetPawnWeapons();
            log.Add($"Initial weapons: {initialWeapons.Count} (Primary + {initialWeapons.Count - 1} sidearms)");

            // Check if SimpleSidearms would create a replacement job
            var job = SimpleSidearmsCompat.TryGetSidearmUpgradeJob(testPawn);
            if (job != null)
            {
                log.Add($"Replacement job created for: {job.targetA.Thing?.Label ?? "unknown"}");
                
                // Verify it targets a high-quality weapon
                if (job.targetA.Thing is ThingWithComps targetWeapon)
                {
                    targetWeapon.TryGetQuality(out var targetQuality);
                    
                    // Find worst current weapon
                    float worstScore = float.MaxValue;
                    ThingWithComps worstWeapon = null;
                    foreach (var weapon in initialWeapons)
                    {
                        var score = WeaponScoreCache.GetCachedScore(testPawn, weapon);
                        if (score < worstScore && !ForcedWeaponHelper.IsWeaponDefForced(testPawn, weapon.def))
                        {
                            worstScore = score;
                            worstWeapon = weapon;
                        }
                    }
                    
                    if (worstWeapon != null)
                    {
                        worstWeapon.TryGetQuality(out var worstQuality);
                        log.Add($"Would replace {worstWeapon.Label} (Q:{worstQuality}, score:{worstScore:F2}) with {targetWeapon.Label} (Q:{targetQuality})");
                        
                        if (targetQuality > worstQuality)
                        {
                            log.Add("PASS: Replacement logic correctly identifies upgrade opportunity");
                            return true;
                        }
                    }
                }
            }
            else if (AutoArmMod.settings.allowSidearmUpgrades)
            {
                log.Add("FAIL: No replacement job created despite better weapons available");
                error = "No replacement job created despite better weapons available";
                return false;
            }
            else
            {
                log.Add("SKIP: Sidearm upgrades disabled in settings");
                return true;
            }

            log.Add("FAIL: Replacement logic not working as expected");
            error = "Replacement logic not working as expected";
            return false;
        }

        private bool TestPickUpAndHaulCompatibility(List<string> log, out string error)
        {
            error = null;
            log.Add("Testing Pick Up and Haul integration during hauling...");
            
            var initialCount = GetPawnWeapons().Count;
            log.Add($"Starting with {initialCount} weapons");

            // Simulate hauling multiple weapons
            var weaponsToHaul = allTestWeapons.Where(w => w.Spawned).Take(5).ToList();
            log.Add($"Simulating haul of {weaponsToHaul.Count} weapons");

            int pickedUp = 0;
            int rejected = 0;
            
            foreach (var weapon in weaponsToHaul)
            {
                string reason;
                if (SimpleSidearmsCompat.CanPickupSidearmInstance(weapon, testPawn, out reason))
                {
                    // Use TryAddOrTransfer which handles items that are already in containers
                    if (testPawn.inventory.innerContainer.TryAddOrTransfer(weapon))
                    {
                        pickedUp++;
                        log.Add($"  Picked up: {weapon.Label}");
                    }
                    else
                    {
                        log.Add($"  Failed to add: {weapon.Label}");
                    }
                }
                else
                {
                    rejected++;
                    log.Add($"  Rejected: {weapon.Label} - {reason}");
                }
            }

            log.Add($"Picked up {pickedUp}, rejected {rejected}");
            
            // Check if limits are respected
            var maxAllowed = SimpleSidearmsCompat.GetMaxSidearmsForPawn(testPawn) + 1; // +1 for primary
            var currentCount = GetPawnWeapons().Count;
            
            if (currentCount <= maxAllowed)
            {
                log.Add($"PASS: Weapon count ({currentCount}) within limit ({maxAllowed})");
                return true;
            }
            else
            {
                log.Add($"FAIL: Weapon count ({currentCount}) exceeds limit ({maxAllowed})");
                error = $"Weapon count ({currentCount}) exceeds limit ({maxAllowed})";
                return false;
            }
        }

        private bool TestPostHaulValidation(List<string> log, out string error)
        {
            error = null;
            log.Add("Testing post-haul validation to drop excess weapons...");
            
            // Force add extra weapons to exceed limits
            var extraWeapons = allTestWeapons.Where(w => w.Spawned).Take(3).ToList();
            foreach (var weapon in extraWeapons)
            {
                testPawn.inventory.innerContainer.TryAddOrTransfer(weapon);
            }
            
            var beforeCount = testPawn.inventory.innerContainer.Count(t => t.def.IsWeapon);
            log.Add($"Forced inventory to {beforeCount} weapons (exceeding limits)");
            
            // Run validation
            PickUpAndHaulCompat.ValidateInventoryWeapons(testPawn);
            
            var afterCount = testPawn.inventory.innerContainer.Count(t => t.def.IsWeapon);
            var maxSidearms = SimpleSidearmsCompat.GetMaxSidearmsForPawn(testPawn);
            
            log.Add($"After validation: {afterCount} weapons (max allowed: {maxSidearms})");
            
            if (afterCount <= maxSidearms)
            {
                log.Add($"PASS: Validation dropped {beforeCount - afterCount} excess weapons");
                return true;
            }
            else
            {
                log.Add($"FAIL: Validation failed to enforce limit (still have {afterCount - maxSidearms} excess)");
                error = $"Validation failed to enforce limit (still have {afterCount - maxSidearms} excess)";
                return false;
            }
        }

        private bool TestCooldownSystem(List<string> log, out string error)
        {
            error = null;
            log.Add("Testing cooldown system to prevent pickup/drop loops...");
            
            // Find a weapon on the ground
            var droppedWeapon = allTestWeapons.FirstOrDefault(w => w.Spawned);
            if (droppedWeapon == null)
            {
                log.Add("ERROR: No weapon available for cooldown test");
                error = "No weapon available for cooldown test";
                return false;
            }

            // Test 1: Basic cooldown functionality
            log.Add($"Simulating drop of {droppedWeapon.Label}");
            int longCooldownTicks = 1200; // 20 seconds
            DroppedItemTracker.MarkAsDropped(droppedWeapon, longCooldownTicks);
            
            // Check if it's on cooldown
            bool isRecentlyDropped = DroppedItemTracker.IsRecentlyDropped(droppedWeapon);
            log.Add($"Is recently dropped: {isRecentlyDropped}");
            
            if (!isRecentlyDropped)
            {
                log.Add("FAIL: Weapon not marked as recently dropped");
                error = "Weapon not marked as recently dropped";
                return false;
            }
            
            // Test 2: Check WasDroppedFromPrimaryUpgrade detection
            bool wasFromPrimaryUpgrade = DroppedItemTracker.WasDroppedFromPrimaryUpgrade(droppedWeapon);
            log.Add($"Detected as primary upgrade drop: {wasFromPrimaryUpgrade}");
            
            if (!wasFromPrimaryUpgrade)
            {
                log.Add("FAIL: Long cooldown weapon not detected as primary upgrade drop");
                error = "Long cooldown weapon not detected as primary upgrade drop";
                return false;
            }
            
            // Test 3: Test shorter cooldown
            var anotherWeapon = allTestWeapons.FirstOrDefault(w => w.Spawned && w != droppedWeapon);
            if (anotherWeapon != null)
            {
                int shortCooldownTicks = 300; // 5 seconds (default)
                DroppedItemTracker.MarkAsDropped(anotherWeapon, shortCooldownTicks);
                
                bool isShortCooldown = DroppedItemTracker.IsRecentlyDropped(anotherWeapon);
                bool isShortFromPrimary = DroppedItemTracker.WasDroppedFromPrimaryUpgrade(anotherWeapon);
                
                log.Add($"Short cooldown weapon - Recently dropped: {isShortCooldown}, From primary: {isShortFromPrimary}");
                
                if (!isShortCooldown || isShortFromPrimary)
                {
                    log.Add("FAIL: Short cooldown not working as expected");
                    error = "Short cooldown not working as expected";
                    return false;
                }
            }
            
            // Test 4: Test pending upgrade tracking
            var upgradeWeapon = allTestWeapons.FirstOrDefault(w => w.Spawned && w != droppedWeapon && w != anotherWeapon);
            if (upgradeWeapon != null)
            {
                DroppedItemTracker.MarkPendingSameTypeUpgrade(upgradeWeapon);
                bool isPending = DroppedItemTracker.IsPendingSameTypeUpgrade(upgradeWeapon);
                
                log.Add($"Pending upgrade tracking: {isPending}");
                
                if (!isPending)
                {
                    log.Add("FAIL: Pending upgrade tracking not working");
                    error = "Pending upgrade tracking not working";
                    return false;
                }
                
                // Clear it
                DroppedItemTracker.ClearPendingUpgrade(upgradeWeapon);
                bool stillPending = DroppedItemTracker.IsPendingSameTypeUpgrade(upgradeWeapon);
                
                if (stillPending)
                {
                    log.Add("FAIL: Clear pending upgrade not working");
                    error = "Clear pending upgrade not working";
                    return false;
                }
            }
            
            log.Add("PASS: All cooldown system tests passed");
            return true;
        }

        private List<ThingWithComps> GetPawnWeapons()
        {
            var weapons = new List<ThingWithComps>();
            if (testPawn.equipment?.Primary != null)
                weapons.Add(testPawn.equipment.Primary);
            weapons.AddRange(testPawn.inventory?.innerContainer?.OfType<ThingWithComps>()
                .Where(w => w.def.IsWeapon) ?? Enumerable.Empty<ThingWithComps>());
            return weapons;
        }

        public void Cleanup()
        {
            // Destroy stockpile
            if (weaponStockpile != null && testMap?.zoneManager != null)
            {
                try { testMap.zoneManager.DeregisterZone(weaponStockpile); } catch { }
            }

            // Destroy all test items
            foreach (var weapon in allTestWeapons)
            {
                weapon?.Destroy();
            }
            
            testPawn?.Destroy();
            allTestWeapons.Clear();
        }
    }

    /// <summary>
    /// Test that properly simulates the actual weapon hoarding issue users reported
    /// Where PUAH successfully picks up 5-9 weapons despite SimpleSidearms limit of 2
    /// </summary>
    public class ActualWeaponHoardingSimulationTest : ITestScenario
    {
        public string Name => "Actual Weapon Hoarding Simulation (PUAH bypassing SS limits)";
        
        private Pawn testPawn;
        private List<ThingWithComps> testWeapons = new List<ThingWithComps>();
        private Map testMap;
        private int originalMaxSidearms;

        public void Setup(Map map)
        {
            if (!PickUpAndHaulCompat.IsLoaded() || !SimpleSidearmsCompat.IsLoaded())
                return;

            testMap = map;
            testPawn = TestHelpers.CreateTestPawn(map);
            
            if (testPawn == null)
                return;

            // Clear existing equipment
            testPawn.equipment?.DestroyAllEquipment();
            
            // Give starting weapons (1 primary + 1 sidearm)
            var primary = SafeTestCleanup.SafeCreateWeapon(VanillaWeaponDefOf.Gun_Autopistol, null, QualityCategory.Normal);
            var sidearm = SafeTestCleanup.SafeCreateWeapon(VanillaWeaponDefOf.MeleeWeapon_Knife, null, QualityCategory.Normal);
            
            if (primary != null)
            {
                SafeTestCleanup.SafeEquipWeapon(testPawn, primary);
                testWeapons.Add(primary);
            }
            
            if (sidearm != null)
            {
                SafeTestCleanup.SafeAddToInventory(testPawn, sidearm);
                testWeapons.Add(sidearm);
            }

            // Create 7 more weapons to simulate user's "9 guns" scenario
            var weaponDefs = new[]
            {
                VanillaWeaponDefOf.Gun_Revolver,
                VanillaWeaponDefOf.Gun_AssaultRifle,
                VanillaWeaponDefOf.Gun_SniperRifle,
                VanillaWeaponDefOf.MeleeWeapon_LongSword,
                VanillaWeaponDefOf.Gun_Autopistol,
                VanillaWeaponDefOf.MeleeWeapon_Knife,
                VanillaWeaponDefOf.Gun_Revolver
            };

            foreach (var def in weaponDefs)
            {
                if (def != null)
                {
                    var weapon = SafeTestCleanup.SafeCreateWeapon(def, null, QualityCategory.Normal);
                    if (weapon != null)
                    {
                        testWeapons.Add(weapon);
                    }
                }
            }

            // Store original max sidearms to ensure we're testing with limit of 2-3
            originalMaxSidearms = SimpleSidearmsCompat.GetMaxSidearmsForPawn(testPawn);
            AutoArmLogger.Log($"[TEST] SimpleSidearms max sidearms: {originalMaxSidearms}");
        }

        public TestResult Run()
        {
            if (!PickUpAndHaulCompat.IsLoaded() || !SimpleSidearmsCompat.IsLoaded())
                return TestResult.Pass();

            if (testPawn == null || testMap == null)
                return TestResult.Failure("Test setup failed");

            var result = new TestResult { Success = true };
            
            // Record initial state
            var initialWeaponCount = GetTotalWeaponCount();
            result.Data["InitialWeaponCount"] = initialWeaponCount;
            result.Data["SimpleSidearmsLimit"] = originalMaxSidearms;
            
            // CRITICAL: Simulate PUAH bypassing SimpleSidearms checks by force-adding weapons
            // This represents the actual bug where PUAH adds weapons without checking SS limits
            AutoArmLogger.Log("[TEST] Simulating PUAH bypassing SimpleSidearms limits...");
            
            int weaponsToAdd = 7; // User reported picking up 9 total (2 starting + 7 more)
            int addedCount = 0;
            
            foreach (var weapon in testWeapons)
            {
                if (weapon == testPawn.equipment?.Primary || 
                    testPawn.inventory?.innerContainer?.Contains(weapon) == true)
                    continue; // Skip already equipped/carried
                    
                if (addedCount >= weaponsToAdd)
                    break;
                    
                // Force add to simulate PUAH behavior
                AutoArmLogger.Log($"[TEST] Force-adding {weapon.Label} to inventory (simulating PUAH)");
                
                // Remove from any existing holder
                if (weapon.holdingOwner != null)
                    weapon.holdingOwner.Remove(weapon);
                    
                // Force add to inventory
                bool added = testPawn.inventory.innerContainer.TryAdd(weapon, true);
                if (added)
                {
                    addedCount++;
                    AutoArmLogger.Log($"[TEST] Successfully force-added {weapon.Label}");
                }
                else
                {
                    AutoArmLogger.Log($"[TEST] Failed to force-add {weapon.Label}");
                }
            }
            
            result.Data["WeaponsForceAdded"] = addedCount;
            
            // Check state after force-adding (should exceed limits)
            var afterHoardingCount = GetTotalWeaponCount();
            result.Data["WeaponCountAfterHoarding"] = afterHoardingCount;
            
            // This should be way over the limit
            bool isHoarding = afterHoardingCount > originalMaxSidearms + 1; // +1 for primary
            result.Data["IsHoarding"] = isHoarding;
            
            if (!isHoarding)
            {
                result.Success = false;
                result.FailureReason = "Failed to simulate hoarding - couldn't add enough weapons";
                result.Data["Error"] = $"Expected > {originalMaxSidearms + 1} weapons, got {afterHoardingCount}";
                return result;
            }
            
            AutoArmLogger.Log($"[TEST] Successfully simulated hoarding: {afterHoardingCount} weapons (limit: {originalMaxSidearms + 1})");
            
            // Now test the fix: Run validation
            AutoArmLogger.Log("[TEST] Running ValidateInventoryWeapons to test the fix...");
            PickUpAndHaulCompat.ValidateInventoryWeapons(testPawn);
            
            // Check final state
            var finalWeaponCount = GetTotalWeaponCount();
            result.Data["WeaponCountAfterValidation"] = finalWeaponCount;
            result.Data["WeaponsDropped"] = afterHoardingCount - finalWeaponCount;
            
            // Verify no duplicates if setting disallows
            if (!SimpleSidearmsCompat.ALLOW_DUPLICATE_WEAPON_TYPES)
            {
                var duplicates = CheckForDuplicates();
                result.Data["DuplicatesFound"] = duplicates.Count > 0 ? string.Join(", ", duplicates) : "None";
                
                if (duplicates.Count > 0)
                {
                    result.Success = false;
                    result.FailureReason = "Validation failed to remove duplicate weapon types";
                    result.Data["Error_Duplicates"] = string.Join(", ", duplicates);
                }
            }
            
            // Main check: Are we within limits after validation?
            bool withinLimits = finalWeaponCount <= originalMaxSidearms + 1;
            result.Data["WithinLimitsAfterValidation"] = withinLimits;
            
            if (!withinLimits)
            {
                result.Success = false;
                result.FailureReason = "Validation failed to enforce SimpleSidearms limits";
                result.Data["Error_StillHoarding"] = $"{finalWeaponCount} weapons > {originalMaxSidearms + 1} limit";
            }
            else
            {
                result.Data["Result"] = "SUCCESS: Validation properly enforced limits and prevented hoarding";
                AutoArmLogger.Log("[TEST] SUCCESS: Hoarding was properly prevented by validation");
            }
            
            return result;
        }
        
        private int GetTotalWeaponCount()
        {
            int count = 0;
            if (testPawn.equipment?.Primary != null)
                count++;
            count += testPawn.inventory?.innerContainer?.Count(t => t.def.IsWeapon) ?? 0;
            return count;
        }
        
        private List<string> CheckForDuplicates()
        {
            var weaponCounts = new Dictionary<ThingDef, int>();
            
            if (testPawn.equipment?.Primary != null)
            {
                var def = testPawn.equipment.Primary.def;
                weaponCounts[def] = weaponCounts.ContainsKey(def) ? weaponCounts[def] + 1 : 1;
            }
            
            foreach (var item in testPawn.inventory?.innerContainer ?? Enumerable.Empty<Thing>())
            {
                if (item.def.IsWeapon)
                {
                    weaponCounts[item.def] = weaponCounts.ContainsKey(item.def) ? weaponCounts[item.def] + 1 : 1;
                }
            }
            
            return weaponCounts.Where(kvp => kvp.Value > 1)
                .Select(kvp => $"{kvp.Key.label} x{kvp.Value}")
                .ToList();
        }

        public void Cleanup()
        {
            foreach (var weapon in testWeapons)
            {
                weapon?.Destroy();
            }
            testPawn?.Destroy();
            testWeapons.Clear();
        }
    }

    /// <summary>
    /// Test that pacifist pawns can still haul weapons as cargo
    /// Verifies that outfit filter checks were removed to allow hauling
    /// </summary>
    public class PacifistWeaponHaulingTest : ITestScenario
    {
        public string Name => "Pacifist Weapon Hauling Test";
        
        private Pawn pacifistPawn;
        private List<ThingWithComps> weaponsToHaul = new List<ThingWithComps>();
        private Zone_Stockpile weaponStockpile;
        private Map testMap;

        public void Setup(Map map)
        {
            if (!PickUpAndHaulCompat.IsLoaded())
                return;

            testMap = map;
            
            // Create a pacifist pawn
            var config = new TestHelpers.TestPawnConfig
            {
                Name = "PacifistHauler",
                Traits = new List<TraitDef> { TraitDefOf.Kind },
                EnsureViolenceCapable = false // Allow creating incapable of violence
            };
            
            pacifistPawn = TestHelpers.CreateTestPawn(map, config);
            
            if (pacifistPawn == null)
                return;

            // Try to add pacifist backstory or trait
            if (pacifistPawn.story?.traits != null)
            {
                // Try to find a trait that disables violence
                var pacifistTrait = DefDatabase<TraitDef>.AllDefs
                    .FirstOrDefault(t => t.disabledWorkTags.HasFlag(WorkTags.Violent));
                    
                if (pacifistTrait != null && !pacifistPawn.story.traits.HasTrait(pacifistTrait))
                {
                    pacifistPawn.story.traits.GainTrait(new Trait(pacifistTrait));
                }
            }

            // Set outfit to disallow all weapons to test that hauling still works
            if (pacifistPawn.outfits?.CurrentApparelPolicy?.filter != null)
            {
                foreach (var weaponDef in WeaponThingFilterUtility.AllWeapons)
                {
                    pacifistPawn.outfits.CurrentApparelPolicy.filter.SetAllow(weaponDef, false);
                }
            }

            // Create weapons to haul
            var basePos = pacifistPawn.Position + new IntVec3(3, 0, 0);
            var weaponDefs = new[] 
            {
                VanillaWeaponDefOf.Gun_Autopistol,
                VanillaWeaponDefOf.MeleeWeapon_Knife,
                VanillaWeaponDefOf.Gun_AssaultRifle
            };

            foreach (var weaponDef in weaponDefs)
            {
                if (weaponDef != null)
                {
                    var weapon = TestHelpers.CreateWeapon(map, weaponDef, basePos, QualityCategory.Normal);
                    if (weapon != null)
                    {
                        weapon.SetForbidden(false, false);
                        weaponsToHaul.Add(weapon);
                        basePos.z += 1;
                    }
                }
            }

            // Create weapon stockpile
            var stockpilePos = pacifistPawn.Position + new IntVec3(10, 0, 0);
            CreateWeaponStockpile(map, stockpilePos);
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
                foreach (var weaponDef in WeaponThingFilterUtility.AllWeapons)
                {
                    weaponStockpile.settings.filter.SetAllow(weaponDef, true);
                }
                weaponStockpile.settings.Priority = StoragePriority.Critical;
            }
            catch (Exception e) 
            {
                AutoArmLogger.LogError($"[TEST] Failed to create stockpile: {e.Message}");
            }
        }

        public TestResult Run()
        {
            if (!PickUpAndHaulCompat.IsLoaded())
                return TestResult.Pass();

            if (pacifistPawn == null || testMap == null)
                return TestResult.Failure("Test setup failed");

            var result = new TestResult { Success = true };
            
            // Verify pawn is incapable of violence
            bool isIncapableOfViolence = pacifistPawn.WorkTagIsDisabled(WorkTags.Violent);
            result.Data["IsIncapableOfViolence"] = isIncapableOfViolence;
            
            // Verify outfit disallows weapons
            bool outfitAllowsWeapons = false;
            if (pacifistPawn.outfits?.CurrentApparelPolicy?.filter != null)
            {
                outfitAllowsWeapons = WeaponThingFilterUtility.AllWeapons
                    .Any(wd => pacifistPawn.outfits.CurrentApparelPolicy.filter.Allows(wd));
            }
            result.Data["OutfitAllowsWeapons"] = outfitAllowsWeapons;

            // Check if inventory is properly initialized
            if (pacifistPawn.inventory == null)
            {
                result.Success = false;
                result.FailureReason = "Pawn inventory is null";
                return result;
            }
            
            AutoArmLogger.Log($"[TEST] Pawn inventory capacity: {pacifistPawn.GetStatValue(StatDefOf.CarryingCapacity)}kg");
            AutoArmLogger.Log($"[TEST] Weapons to haul: {weaponsToHaul.Count}");
            
            // Simulate hauling weapons
            // Note: In actual gameplay, Pick Up and Haul doesn't check outfit filters for cargo
            // But RimWorld's TryAddOrTransfer might. So we'll force add the items to test validation.
            int haulAttempts = 0;
            int successfulHauls = 0;
            
            foreach (var weapon in weaponsToHaul)
            {
                haulAttempts++;
                
                AutoArmLogger.Log($"[TEST] Attempting to haul {weapon.Label}, mass: {weapon.GetStatValue(StatDefOf.Mass)}kg, spawned: {weapon.Spawned}");
                
                // Try the normal way first
                if (pacifistPawn.inventory.innerContainer.TryAddOrTransfer(weapon))
                {
                    successfulHauls++;
                    AutoArmLogger.Log($"[TEST] Pacifist successfully picked up {weapon.Label} for hauling");
                }
                else
                {
                    // If normal pickup fails (possibly due to outfit filter), force add to simulate Pick Up and Haul behavior
                    AutoArmLogger.Log($"[TEST] Normal pickup failed, forcing add to simulate Pick Up and Haul");
                    
                    // Remove from map first
                    if (weapon.Spawned)
                    {
                        weapon.DeSpawn();
                    }
                    
                    // Force add to inventory
                    if (pacifistPawn.inventory.innerContainer.TryAdd(weapon, true))
                    {
                        successfulHauls++;
                        AutoArmLogger.Log($"[TEST] Force-added {weapon.Label} to inventory");
                    }
                    else
                    {
                        AutoArmLogger.Log($"[TEST] Failed to force-add {weapon.Label}");
                        // Re-spawn if we couldn't add it
                        if (!weapon.Spawned && !weapon.Destroyed)
                        {
                            GenSpawn.Spawn(weapon, pacifistPawn.Position + IntVec3.South, testMap);
                        }
                    }
                }
            }
            
            result.Data["HaulAttempts"] = haulAttempts;
            result.Data["SuccessfulHauls"] = successfulHauls;
            
            // Run validation (should not drop weapons for two reasons:
            // 1. We don't check outfit filters anymore
            // 2. ValidateInventoryWeapons skips violence-incapable pawns entirely)
            var beforeValidation = pacifistPawn.inventory.innerContainer.Count(t => t.def.IsWeapon);
            PickUpAndHaulCompat.ValidateInventoryWeapons(pacifistPawn);
            var afterValidation = pacifistPawn.inventory.innerContainer.Count(t => t.def.IsWeapon);
            
            result.Data["WeaponsBeforeValidation"] = beforeValidation;
            result.Data["WeaponsAfterValidation"] = afterValidation;
            
            // Test passes if:
            // 1. Pacifist could pick up weapons for hauling (even if we had to force it)
            // 2. Validation didn't drop them (because ValidateInventoryWeapons should skip violence-incapable pawns)
            if (successfulHauls > 0 && afterValidation == beforeValidation)
            {
                result.Data["Result"] = "SUCCESS: Pacifist can haul weapons and validation correctly skips them";
                
                // Note if we had to force-add
                if (successfulHauls < haulAttempts)
                {
                    result.Data["Note"] = "Some weapons required force-add (RimWorld's TryAddOrTransfer may check outfit filters)";
                }
            }
            else if (successfulHauls == 0)
            {
                result.Success = false;
                result.FailureReason = "Pacifist couldn't pick up any weapons for hauling";
                result.Data["Error"] = "Failed to add weapons to inventory even with force";
            }
            else if (afterValidation < beforeValidation)
            {
                result.Success = false;
                result.FailureReason = "Validation incorrectly dropped weapons from pacifist";
                result.Data["Error"] = $"ValidateInventoryWeapons should skip violence-incapable pawns but dropped {beforeValidation - afterValidation} weapons";
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
            
            pacifistPawn?.Destroy();
            weaponsToHaul.Clear();
        }
    }

    /// <summary>
    /// Test that forced weapons are protected during Pick Up And Haul operations
    /// Addresses issue: "pawns constantly disregard a forced charge shotgun for an autopistol"
    /// </summary>
    public class ForcedWeaponPickUpAndHaulProtectionTest : ITestScenario
    {
        public string Name => "Forced Weapon Protection During Pick Up And Haul";
        
        private Pawn testPawn;
        private ThingWithComps forcedChargeShotgun;
        private ThingWithComps forcedMonosword;
        private List<ThingWithComps> inferiorWeaponsToHaul = new List<ThingWithComps>();
        private Zone_Stockpile weaponStockpile;
        private Map testMap;

        public void Setup(Map map)
        {
            if (!PickUpAndHaulCompat.IsLoaded())
                return;

            testMap = map;
            testPawn = TestHelpers.CreateTestPawn(map);
            
            if (testPawn == null)
                return;

            // Clear existing equipment
            testPawn.equipment?.DestroyAllEquipment();
            
            // Create and equip superior weapons that will be forced
            var chargeShotgunDef = DefDatabase<ThingDef>.GetNamed("Gun_ChargeShotgun", false);
            if (chargeShotgunDef != null)
            {
                forcedChargeShotgun = TestHelpers.CreateWeapon(map, chargeShotgunDef,
                    testPawn.Position, QualityCategory.Normal);
                if (forcedChargeShotgun != null)
                {
                    SafeTestCleanup.SafeEquipWeapon(testPawn, forcedChargeShotgun);
                    // Mark as forced
                    ForcedWeaponHelper.SetForced(testPawn, forcedChargeShotgun);
                    AutoArmLogger.Log($"[TEST] Equipped and forced {forcedChargeShotgun.Label}");
                }
            }
            else
            {
                // Fallback to assault rifle if charge shotgun not available
                forcedChargeShotgun = TestHelpers.CreateWeapon(map, VanillaWeaponDefOf.Gun_AssaultRifle,
                    testPawn.Position, QualityCategory.Excellent);
                if (forcedChargeShotgun != null)
                {
                    SafeTestCleanup.SafeEquipWeapon(testPawn, forcedChargeShotgun);
                    ForcedWeaponHelper.SetForced(testPawn, forcedChargeShotgun);
                    AutoArmLogger.Log($"[TEST] Equipped and forced fallback {forcedChargeShotgun.Label}");
                }
            }

            // Add a forced melee weapon to inventory (if SimpleSidearms is loaded)
            if (SimpleSidearmsCompat.IsLoaded())
            {
                var monoswordDef = DefDatabase<ThingDef>.GetNamed("MeleeWeapon_Monosword", false);
                if (monoswordDef != null)
                {
                    forcedMonosword = TestHelpers.CreateWeapon(map, monoswordDef,
                        testPawn.Position, QualityCategory.Good);
                    if (forcedMonosword != null)
                    {
                        SafeTestCleanup.SafeAddToInventory(testPawn, forcedMonosword);
                        ForcedWeaponHelper.AddForcedDef(testPawn, forcedMonosword.def);
                        AutoArmLogger.Log($"[TEST] Added forced sidearm {forcedMonosword.Label}");
                    }
                }
            }

            // Clear fog
            var fogGrid = map.fogGrid;
            if (fogGrid != null)
            {
                foreach (var cell in GenRadial.RadialCellsAround(testPawn.Position, 20, true))
                {
                    if (cell.InBounds(map))
                        fogGrid.Unfog(cell);
                }
            }

            // Create stockpile for hauling
            var stockpilePos = testPawn.Position + new IntVec3(15, 0, 0);
            CreateWeaponStockpile(map, stockpilePos);

            // Create inferior weapons to haul (same quality as forced to test pure weapon type preference)
            CreateInferiorWeaponsToHaul(map);
        }

        private void CreateWeaponStockpile(Map map, IntVec3 center)
        {
            try
            {
                weaponStockpile = new Zone_Stockpile(StorageSettingsPreset.DefaultStockpile, map.zoneManager);
                map.zoneManager.RegisterZone(weaponStockpile);
                
                for (int x = -2; x <= 2; x++)
                {
                    for (int z = -2; z <= 2; z++)
                    {
                        var cell = center + new IntVec3(x, 0, z);
                        if (cell.InBounds(map) && cell.Standable(map))
                            weaponStockpile.AddCell(cell);
                    }
                }

                weaponStockpile.settings.filter.SetDisallowAll();
                foreach (var weaponDef in WeaponThingFilterUtility.AllWeapons)
                {
                    weaponStockpile.settings.filter.SetAllow(weaponDef, true);
                }
                weaponStockpile.settings.Priority = StoragePriority.Critical;
            }
            catch { }
        }

        private void CreateInferiorWeaponsToHaul(Map map)
        {
            var basePos = testPawn.Position + new IntVec3(5, 0, 0);
            
            // Create multiple autopistols (inferior to charge shotgun) with SAME quality
            for (int i = 0; i < 5; i++)
            {
                var autopistol = TestHelpers.CreateWeapon(map, VanillaWeaponDefOf.Gun_Autopistol,
                    basePos + new IntVec3(i % 3, 0, i / 3), QualityCategory.Normal);
                if (autopistol != null)
                {
                    inferiorWeaponsToHaul.Add(autopistol);
                    ImprovedWeaponCacheManager.AddWeaponToCache(autopistol);
                }
            }

            // Add some wooden clubs (inferior to monosword)
            var clubDef = DefDatabase<ThingDef>.GetNamed("MeleeWeapon_Club", false);
            if (clubDef != null)
            {
                for (int i = 0; i < 3; i++)
                {
                    var club = TestHelpers.CreateWeapon(map, clubDef,
                        basePos + new IntVec3(i, 0, 2), QualityCategory.Good);
                    if (club != null)
                    {
                        inferiorWeaponsToHaul.Add(club);
                        ImprovedWeaponCacheManager.AddWeaponToCache(club);
                    }
                }
            }

            AutoArmLogger.Log($"[TEST] Created {inferiorWeaponsToHaul.Count} inferior weapons to haul");
        }

        public TestResult Run()
        {
            if (!PickUpAndHaulCompat.IsLoaded())
                return TestResult.Pass();

            if (testPawn == null || testMap == null || forcedChargeShotgun == null)
                return TestResult.Failure("Test setup failed");

            var result = new TestResult { Success = true };
            
            // Record initial state
            result.Data["InitialPrimary"] = forcedChargeShotgun.Label;
            result.Data["InitialPrimaryForced"] = ForcedWeaponHelper.IsForced(testPawn, forcedChargeShotgun);
            result.Data["InitialSidearmCount"] = testPawn.inventory?.innerContainer?.Count(t => t.def.IsWeapon) ?? 0;
            
            // Calculate weapon scores for comparison
            float forcedScore = WeaponScoreCache.GetCachedScore(testPawn, forcedChargeShotgun);
            result.Data["ForcedWeaponScore"] = forcedScore;
            
            // Test with both settings
            bool[] settingsToTest = { false, true };
            
            foreach (bool allowUpgrades in settingsToTest)
            {
                AutoArmLogger.Log($"[TEST] Testing with allowForcedWeaponUpgrades = {allowUpgrades}");
                AutoArmMod.settings.allowForcedWeaponUpgrades = allowUpgrades;
                
                string testKey = allowUpgrades ? "WithUpgradesEnabled" : "WithUpgradesDisabled";
                
                // Simulate hauling inferior weapons
                AutoArmLogger.Log("[TEST] Simulating Pick Up And Haul job...");
                
                int hauledCount = 0;
                foreach (var weapon in inferiorWeaponsToHaul.ToList()) // ToList to avoid modification during iteration
                {
                    if (!weapon.Spawned) continue;
                    
                    // Check weapon score
                    float weaponScore = WeaponScoreCache.GetCachedScore(testPawn, weapon);
                    AutoArmLogger.Log($"[TEST] Attempting to haul {weapon.Label} (score: {weaponScore:F2})");
                    
                    // Simulate adding to inventory for hauling
                    if (testPawn.inventory.innerContainer.TryAddOrTransfer(weapon))
                    {
                        hauledCount++;
                        AutoArmLogger.Log($"[TEST] Added {weapon.Label} to inventory for hauling");
                    }
                }
                
                result.Data[$"{testKey}_HauledCount"] = hauledCount;
                
                // Check if primary weapon is still the forced weapon
                var currentPrimary = testPawn.equipment?.Primary;
                result.Data[$"{testKey}_PrimaryAfterHaul"] = currentPrimary?.Label ?? "none";
                result.Data[$"{testKey}_PrimaryStillForced"] = currentPrimary == forcedChargeShotgun;
                
                if (currentPrimary != forcedChargeShotgun)
                {
                    result.Success = false;
                    result.FailureReason = $"Forced weapon was replaced during haul (allowUpgrades={allowUpgrades})";
                    result.Data[$"{testKey}_Error1"] = "Primary weapon changed during haul";
                    result.Data[$"{testKey}_NewPrimary"] = currentPrimary?.Label ?? "none";
                    AutoArmLogger.LogError($"[TEST] FAIL: Forced {forcedChargeShotgun.Label} was replaced by {currentPrimary?.Label ?? "none"}");
                }
                
                // Run post-haul validation
                AutoArmLogger.Log("[TEST] Running post-haul validation...");
                PickUpAndHaulCompat.ValidateInventoryWeapons(testPawn);
                
                // Check state after validation
                var primaryAfterValidation = testPawn.equipment?.Primary;
                result.Data[$"{testKey}_PrimaryAfterValidation"] = primaryAfterValidation?.Label ?? "none";
                
                if (primaryAfterValidation != forcedChargeShotgun)
                {
                    result.Success = false;
                    result.FailureReason = result.FailureReason ?? $"Forced weapon lost after validation (allowUpgrades={allowUpgrades})";
                    result.Data[$"{testKey}_Error2"] = "Validation changed primary weapon";
                    result.Data[$"{testKey}_ValidationNewPrimary"] = primaryAfterValidation?.Label ?? "none";
                    AutoArmLogger.LogError($"[TEST] FAIL: Validation changed primary from {forcedChargeShotgun.Label} to {primaryAfterValidation?.Label ?? "none"}");
                }
                
                // Check if forced status is maintained
                bool stillForced = ForcedWeaponHelper.IsForced(testPawn, forcedChargeShotgun);
                result.Data[$"{testKey}_StillForcedAfterValidation"] = stillForced;
                
                if (!stillForced && primaryAfterValidation == forcedChargeShotgun)
                {
                    result.Success = false;
                    result.FailureReason = result.FailureReason ?? $"Weapon lost forced status (allowUpgrades={allowUpgrades})";
                    result.Data[$"{testKey}_Error3"] = "Forced status was cleared";
                    AutoArmLogger.LogError("[TEST] FAIL: Weapon is still equipped but lost forced status");
                }
                
                // Check if any inferior weapons got auto-equipped
                if (primaryAfterValidation != null && inferiorWeaponsToHaul.Contains(primaryAfterValidation))
                {
                    result.Success = false;
                    result.FailureReason = result.FailureReason ?? $"Inferior hauled weapon was equipped (allowUpgrades={allowUpgrades})";
                    result.Data[$"{testKey}_Error4"] = "Hauled weapon auto-equipped";
                    AutoArmLogger.LogError($"[TEST] FAIL: Inferior weapon {primaryAfterValidation.Label} was equipped over forced weapon");
                }
                
                // Test melee sidearm if available
                if (forcedMonosword != null && SimpleSidearmsCompat.IsLoaded())
                {
                    bool monoswordStillInInventory = testPawn.inventory.innerContainer.Contains(forcedMonosword);
                    bool monoswordStillForced = ForcedWeaponHelper.IsWeaponDefForced(testPawn, forcedMonosword.def);
                    
                    result.Data[$"{testKey}_ForcedMeleeInInventory"] = monoswordStillInInventory;
                    result.Data[$"{testKey}_ForcedMeleeStillForced"] = monoswordStillForced;
                    
                    if (!monoswordStillInInventory)
                    {
                        // Check if it was dropped
                        if (forcedMonosword.Spawned)
                        {
                            result.Success = false;
                            result.FailureReason = result.FailureReason ?? $"Forced melee sidearm was dropped (allowUpgrades={allowUpgrades})";
                            result.Data[$"{testKey}_Error5"] = "Forced sidearm dropped during haul";
                            AutoArmLogger.LogError("[TEST] FAIL: Forced melee sidearm was dropped");
                        }
                    }
                }
                
                // Clean up hauled items for next test iteration
                var itemsToRemove = testPawn.inventory.innerContainer
                    .Where(t => inferiorWeaponsToHaul.Contains(t))
                    .ToList();
                foreach (var item in itemsToRemove)
                {
                    testPawn.inventory.innerContainer.Remove(item);
                    if (item is Thing thing)
                    {
                        GenSpawn.Spawn(thing, testPawn.Position + IntVec3.North, testMap);
                    }
                }
            }
            
            // Additional test: AutoArm job creation check
            AutoArmLogger.Log("[TEST] Testing if AutoArm would create job to switch weapons...");
            var jobGiver = new JobGiver_PickUpBetterWeapon();
            var autoArmJob = jobGiver.TestTryGiveJob(testPawn);
            
            if (autoArmJob != null && inferiorWeaponsToHaul.Contains(autoArmJob.targetA.Thing))
            {
                result.Success = false;
                result.FailureReason = result.FailureReason ?? "AutoArm wants to equip inferior weapon over forced weapon";
                result.Data["AutoArmJobTarget"] = autoArmJob.targetA.Thing?.Label ?? "unknown";
                result.Data["Error6"] = "AutoArm job targets inferior weapon";
                AutoArmLogger.LogError($"[TEST] FAIL: AutoArm created job to equip {autoArmJob.targetA.Thing?.Label} over forced weapon");
            }
            else if (autoArmJob != null)
            {
                result.Data["AutoArmJobTarget"] = autoArmJob.targetA.Thing?.Label ?? "none";
                result.Data["AutoArmJobInfo"] = "Job created but not for inferior weapon";
            }
            else
            {
                result.Data["AutoArmJob"] = "No job created (correct behavior)";
            }
            
            if (result.Success)
            {
                result.Data["Result"] = "SUCCESS: Forced weapons protected during Pick Up And Haul operations";
                AutoArmLogger.Log("[TEST] SUCCESS: All forced weapon protections working correctly");
            }
            
            return result;
        }

        public void Cleanup()
        {
            // Clear forced status
            if (testPawn != null)
            {
                ForcedWeaponHelper.ClearForced(testPawn);
            }
            
            // Destroy stockpile
            if (weaponStockpile != null && testMap?.zoneManager != null)
            {
                try { testMap.zoneManager.DeregisterZone(weaponStockpile); } catch { }
            }

            // Destroy all test weapons
            forcedChargeShotgun?.Destroy();
            forcedMonosword?.Destroy();
            foreach (var weapon in inferiorWeaponsToHaul)
            {
                weapon?.Destroy();
            }
            
            testPawn?.Destroy();
            inferiorWeaponsToHaul.Clear();
        }
    }
}