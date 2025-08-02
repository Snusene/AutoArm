// AutoArm RimWorld 1.5+ mod - automatic weapon management
// This file: Safe test cleanup utilities
// Prevents common test errors with proper cleanup methods

using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;

namespace AutoArm.Testing.Helpers
{
    /// <summary>
    /// Centralized safe cleanup methods to prevent common RimWorld test errors
    /// Based on best practices from the modding community
    /// </summary>
    public static class SafeTestCleanup
    {
        private static HashSet<int> destroyedThingIds = new HashSet<int>();
        private static HashSet<int> processedThingIds = new HashSet<int>();
        
        /// <summary>
        /// Safely destroy a weapon, handling all edge cases
        /// </summary>
        public static void SafeDestroyWeapon(ThingWithComps weapon)
        {
            if (weapon == null) return;
            
            // Check if already processed to prevent multiple attempts
            if (weapon.thingIDNumber != 0 && processedThingIds.Contains(weapon.thingIDNumber))
                return;
                
            // Mark as processed immediately to prevent re-entry
            if (weapon.thingIDNumber != 0)
                processedThingIds.Add(weapon.thingIDNumber);
                
            // Check if already destroyed
            if (weapon.Destroyed)
            {
                destroyedThingIds.Add(weapon.thingIDNumber);
                return;
            }
            
            try
            {
                // If the weapon is equipped to a pawn, don't destroy it separately
                // Let the pawn cleanup handle it
                if (weapon.ParentHolder is Pawn_EquipmentTracker)
                {
                    // Just mark it as processed
                    return;
                }
                
                // If in inventory, remove from container first
                if (weapon.ParentHolder is Pawn_InventoryTracker inventory)
                {
                    inventory.innerContainer.Remove(weapon);
                }
                
                // Remove from any other containers
                if (weapon.holdingOwner != null)
                {
                    weapon.holdingOwner.Remove(weapon);
                }
                
                // Despawn if spawned on map
                if (weapon.Spawned)
                {
                    weapon.DeSpawn(DestroyMode.Vanish);
                }
                
                // Only destroy if not already destroyed
                if (!weapon.Destroyed)
                {
                    weapon.Destroy(DestroyMode.Vanish);
                }
                
                // Track that we've destroyed this
                if (weapon.thingIDNumber != 0)
                    destroyedThingIds.Add(weapon.thingIDNumber);
            }
            catch (Exception ex)
            {
                // Log but don't throw - tests should continue
                if (!ex.Message.Contains("already-destroyed"))
                {
                    Log.Warning($"[AutoArm Test] Error during weapon cleanup: {ex.Message}");
                }
            }
        }
        
        /// <summary>
        /// Safely destroy a pawn and all their equipment
        /// </summary>
        public static void SafeDestroyPawn(Pawn pawn)
        {
            if (pawn == null) return;
            
            // Check if already processed
            if (pawn.thingIDNumber != 0 && processedThingIds.Contains(pawn.thingIDNumber))
                return;
                
            // Mark as processed
            if (pawn.thingIDNumber != 0)
                processedThingIds.Add(pawn.thingIDNumber);
            
            if (pawn.Destroyed) 
            {
                destroyedThingIds.Add(pawn.thingIDNumber);
                return;
            }
            
            try
            {
                // Stop all jobs first to prevent any in-progress actions
                pawn.jobs?.StopAll();
                
                // Clean up equipment - let DestroyAllEquipment handle it
                if (pawn.equipment != null)
                {
                    // Mark all equipment as processed so we don't try to destroy them separately
                    if (pawn.equipment.Primary != null)
                    {
                        processedThingIds.Add(pawn.equipment.Primary.thingIDNumber);
                    }
                    
                    foreach (var eq in pawn.equipment.AllEquipmentListForReading)
                    {
                        processedThingIds.Add(eq.thingIDNumber);
                    }
                    
                    // Destroy all equipment through the tracker
                    pawn.equipment.DestroyAllEquipment();
                }
                
                // Clean up inventory
                if (pawn.inventory?.innerContainer != null)
                {
                    // Mark inventory items as processed
                    foreach (var thing in pawn.inventory.innerContainer)
                    {
                        if (thing is ThingWithComps)
                        {
                            processedThingIds.Add(thing.thingIDNumber);
                        }
                    }
                    
                    pawn.inventory.innerContainer.ClearAndDestroyContents(DestroyMode.Vanish);
                }
                
                // Clean up apparel
                if (pawn.apparel != null)
                {
                    foreach (var apparel in pawn.apparel.WornApparel)
                    {
                        processedThingIds.Add(apparel.thingIDNumber);
                    }
                    
                    pawn.apparel.DestroyAll(DestroyMode.Vanish);
                }
                
                // Remove from any holders
                if (pawn.holdingOwner != null)
                {
                    pawn.holdingOwner.Remove(pawn);
                }
                
                // Despawn if needed
                if (pawn.Spawned)
                {
                    pawn.DeSpawn(DestroyMode.Vanish);
                }
                
                // Finally destroy the pawn
                if (!pawn.Destroyed)
                {
                    pawn.Destroy(DestroyMode.Vanish);
                }
                
                // Mark as destroyed
                destroyedThingIds.Add(pawn.thingIDNumber);
            }
            catch (Exception ex)
            {
                if (!ex.Message.Contains("already-destroyed"))
                {
                    Log.Warning($"[AutoArm Test] Error during pawn cleanup: {ex.Message}");
                }
            }
        }
        
        /// <summary>
        /// Clear the destroyed things tracking between test runs
        /// </summary>
        public static void ClearDestroyedTracking()
        {
            destroyedThingIds.Clear();
            processedThingIds.Clear();
        }
        
        /// <summary>
        /// Safely create a weapon with proper stuff assignment
        /// </summary>
        public static ThingWithComps SafeCreateWeapon(ThingDef weaponDef, ThingDef stuff = null, QualityCategory? quality = null)
        {
            if (weaponDef == null) return null;
            
            try
            {
                // Handle stuff requirement
                ThingDef actualStuff = stuff;
                if (weaponDef.MadeFromStuff && actualStuff == null)
                {
                    // Find appropriate default stuff
                    actualStuff = ThingDefOf.Steel;
                    
                    // Check if weapon can use steel
                    if (!CanUseStuff(weaponDef, actualStuff))
                    {
                        // For knives and similar, try other materials
                        if (weaponDef.defName.Contains("Knife") || weaponDef.defName.Contains("Shiv"))
                        {
                            var materials = new[] { ThingDefOf.Steel, ThingDefOf.Plasteel, ThingDefOf.WoodLog };
                            foreach (var mat in materials.Where(m => m != null))
                            {
                                if (CanUseStuff(weaponDef, mat))
                                {
                                    actualStuff = mat;
                                    break;
                                }
                            }
                        }
                        
                        // If still no valid material, find first valid one
                        if (actualStuff == null || !CanUseStuff(weaponDef, actualStuff))
                        {
                            actualStuff = weaponDef.stuffCategories
                                ?.SelectMany(cat => DefDatabase<ThingDef>.AllDefs
                                    .Where(td => td.stuffProps?.categories?.Contains(cat) == true))
                                .FirstOrDefault();
                        }
                    }
                }
                
                // Create the weapon
                Thing thing = ThingMaker.MakeThing(weaponDef, actualStuff);
                var weapon = thing as ThingWithComps;
                
                if (weapon == null) return null;
                
                // Set quality if specified
                if (quality.HasValue)
                {
                    var comp = weapon.TryGetComp<CompQuality>();
                    comp?.SetQuality(quality.Value, ArtGenerationContext.Colony);
                }
                
                return weapon;
            }
            catch (Exception ex)
            {
                Log.Warning($"[AutoArm Test] Error creating weapon {weaponDef.defName}: {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// Safely transfer a weapon to a pawn's equipment
        /// </summary>
        public static bool SafeEquipWeapon(Pawn pawn, ThingWithComps weapon)
        {
            if (pawn?.equipment == null || weapon == null || weapon.Destroyed)
                return false;
                
            try
            {
                // Check if weapon is already in this pawn's equipment
                if (weapon.ParentHolder == pawn.equipment)
                    return true;
                    
                // First remove from any existing container EXCEPT if it's already equipped to another pawn
                if (weapon.holdingOwner != null && !(weapon.ParentHolder is Pawn_EquipmentTracker))
                {
                    weapon.holdingOwner.Remove(weapon);
                }
                else if (weapon.ParentHolder is Pawn_EquipmentTracker otherEquipment && otherEquipment != pawn.equipment)
                {
                    // If equipped to another pawn, we can't take it
                    Log.Warning($"[AutoArm Test] Cannot equip weapon that is equipped to another pawn");
                    return false;
                }
                
                // Despawn if spawned on ground
                if (weapon.Spawned)
                {
                    weapon.DeSpawn();
                }
                
                // Clear current equipment if any
                if (pawn.equipment.Primary != null)
                {
                    pawn.equipment.DestroyAllEquipment();
                }
                
                // Add the new weapon using TryAddOrTransfer to handle edge cases
                pawn.equipment.GetDirectlyHeldThings().TryAddOrTransfer(weapon);
                
                return true;
            }
            catch (Exception ex)
            {
                if (!ex.Message.Contains("already in another container"))
                {
                    Log.Warning($"[AutoArm Test] Error equipping weapon: {ex.Message}");
                }
                return false;
            }
        }
        
        /// <summary>
        /// Safely transfer a weapon to a pawn's inventory
        /// </summary>
        public static bool SafeAddToInventory(Pawn pawn, ThingWithComps weapon)
        {
            if (pawn?.inventory == null || weapon == null || weapon.Destroyed)
                return false;
                
            try
            {
                // Use TryAddOrTransfer which handles items that are already in containers
                return pawn.inventory.innerContainer.TryAddOrTransfer(weapon);
            }
            catch (Exception ex)
            {
                Log.Warning($"[AutoArm Test] Error adding to inventory: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// Clean up all test-created things on a map
        /// </summary>
        public static void CleanupMap(Map map)
        {
            if (map == null) return;
            
            try
            {
                // Clear all weapon caches for this map
                ImprovedWeaponCacheManager.InvalidateCache(map);
                WeaponScoreCache.ClearAllCaches();
                
                // Note: We don't destroy all pawns/weapons on the map as that might
                // interfere with other tests running in parallel
            }
            catch (Exception ex)
            {
                Log.Warning($"[AutoArm Test] Error during map cleanup: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Check if a weapon can use a specific material
        /// </summary>
        private static bool CanUseStuff(ThingDef weaponDef, ThingDef stuffDef)
        {
            if (weaponDef?.stuffCategories == null || stuffDef?.stuffProps?.categories == null)
                return false;
                
            return weaponDef.stuffCategories.Any(cat => stuffDef.stuffProps.categories.Contains(cat));
        }
    }
}
