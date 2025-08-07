using AutoArm.Helpers;
using AutoArm.Logging;
using AutoArm.Definitions;
using RimWorld;
using Verse;
using Verse.AI;

namespace AutoArm.Testing.Helpers
{
    /// <summary>
    /// Helper class for validating test conditions
    /// </summary>
    public static class TestValidationHelper
    {
        /// <summary>
        /// Check if a pawn is valid for auto-equip
        /// </summary>
        public static bool IsValidPawnForAutoEquip(Pawn pawn, out string reason)
        {
            reason = "Valid";
            
            if (pawn == null)
            {
                reason = "Pawn is null";
                return false;
            }
            
            if (!pawn.Spawned)
            {
                reason = "Pawn not spawned";
                return false;
            }
            
            if (pawn.Dead)
            {
                reason = "Pawn is dead";
                return false;
            }
            
            if (pawn.Downed)
            {
                reason = "Pawn is downed";
                return false;
            }
            
            // Explicitly check for prisoners first
            if (pawn.IsPrisoner || pawn.IsPrisonerOfColony)
            {
                reason = "Is a prisoner";
                return false;
            }
            
            // Check if pawn is a colonist or slave (slaves can equip weapons)
            bool isColonistOrSlave = pawn.IsColonist || (ModsConfig.IdeologyActive && pawn.IsSlaveOfColony);
            if (!isColonistOrSlave)
            {
                reason = "Not a colonist or slave";
                return false;
            }
            
            if (pawn.WorkTagIsDisabled(WorkTags.Violent))
            {
                reason = "Cannot do violence";
                return false;
            }
            
            if (pawn.Drafted)
            {
                reason = "Pawn is drafted";
                return false;
            }
            
            if (pawn.InMentalState)
            {
                reason = "Pawn in mental state";
                return false;
            }
            
            if (pawn.equipment == null)
            {
                reason = "No equipment tracker";
                return false;
            }
            
            if (pawn.Map == null)
            {
                reason = "Pawn has no map";
                return false;
            }
            
            // Check if mod is enabled
            if (AutoArmMod.settings != null && !AutoArmMod.settings.modEnabled)
            {
                reason = "AutoArm mod is disabled";
                return false;
            }
            
            return true;
        }
        
        /// <summary>
        /// Check if a weapon is a valid candidate for the pawn
        /// </summary>
        public static bool IsValidWeaponCandidate(ThingWithComps weapon, Pawn pawn, out string reason)
        {
            reason = "Valid";
            
            if (weapon == null)
            {
                reason = "Weapon is null";
                return false;
            }
            
            if (weapon.Destroyed)
            {
                reason = "Weapon is destroyed";
                return false;
            }
            
            if (!weapon.Spawned)
            {
                reason = "Weapon not spawned";
                return false;
            }
            
            if (weapon.IsForbidden(pawn))
            {
                reason = "Weapon is forbidden";
                return false;
            }
            
            if (!pawn.CanReserve(weapon))
            {
                reason = "Cannot reserve weapon";
                return false;
            }
            
            if (!pawn.CanReach(weapon, PathEndMode.Touch, Danger.Deadly))
            {
                reason = "Cannot reach weapon";
                return false;
            }
            
            // Check outfit filter
            if (pawn.outfits?.CurrentApparelPolicy?.filter != null)
            {
                var filter = pawn.outfits.CurrentApparelPolicy.filter;
                if (!filter.Allows(weapon))
                {
                    reason = "Outfit filter rejects weapon";
                    return false;
                }
            }
            
            // Check if weapon is already equipped by someone else
            foreach (var otherPawn in weapon.Map.mapPawns.FreeColonistsSpawned)
            {
                if (otherPawn != pawn && otherPawn.equipment?.Primary == weapon)
                {
                    reason = "Weapon already equipped by another pawn";
                    return false;
                }
            }
            
            return true;
        }
        
        /// <summary>
        /// Force refresh the weapon cache for testing
        /// </summary>
        public static void ForceWeaponCacheRefresh(Map map)
        {
            if (map == null) return;
            
            try
            {
                // Clear and rebuild cache
                Caching.ImprovedWeaponCacheManager.InvalidateCache(map);
                
                // Re-add all weapons on the map
                var weapons = map.listerThings.ThingsInGroup(ThingRequestGroup.Weapon);
                foreach (var weapon in weapons)
                {
                    if (weapon is ThingWithComps twc && twc.Spawned && !twc.Destroyed)
                    {
                        Caching.ImprovedWeaponCacheManager.AddWeaponToCache(twc);
                    }
                }
                
                AutoArmLogger.Debug($"[TEST] Force refreshed weapon cache for map {map.uniqueID} - {weapons.Count} weapons");
            }
            catch (System.Exception e)
            {
                AutoArmLogger.Error("[TEST] Error force refreshing weapon cache", e);
            }
        }
        
        /// <summary>
        /// Ensure a weapon is properly registered for testing
        /// </summary>
        public static void EnsureWeaponRegistered(ThingWithComps weapon)
        {
            if (weapon == null || !weapon.Spawned || weapon.Destroyed) return;
            
            try
            {
                // Add to cache
                Caching.ImprovedWeaponCacheManager.AddWeaponToCache(weapon);
                
                // Clear any restrictions - RemoveDropped doesn't exist, just ensure not tracked
                // DroppedItemTracker doesn't have a RemoveDropped method
                
                // Ensure not forbidden
                weapon.SetForbidden(false, false);
                
                AutoArmLogger.Debug($"[TEST] Ensured weapon {weapon.Label} is registered for testing");
            }
            catch (System.Exception e)
            {
                AutoArmLogger.Error($"[TEST] Error registering weapon {weapon?.Label}", e);
            }
        }
        
        /// <summary>
        /// Check if SimpleSidearms reflection is working
        /// </summary>
        public static bool IsSimpleSidearmsWorking()
        {
            if (!SimpleSidearmsCompat.IsLoaded())
                return false;
                
            // Check if reflection succeeded
            if (SimpleSidearmsCompat.ReflectionFailed)
            {
                AutoArmLogger.Debug("[TEST] SimpleSidearms is loaded but reflection failed");
                return false;
            }
            
            return true;
        }
        
        /// <summary>
        /// Force mark a weapon as forced (bypassing SimpleSidearms if needed)
        /// </summary>
        public static void ForceMarkWeaponAsForced(Pawn pawn, ThingWithComps weapon)
        {
            if (pawn == null || weapon == null) return;
            
            try
            {
                // Direct force marking
                ForcedWeaponHelper.SetForced(pawn, weapon);
                
                // If SimpleSidearms is loaded but reflection failed, use fallback tracking
                if (SimpleSidearmsCompat.IsLoaded() && SimpleSidearmsCompat.ReflectionFailed)
                {
                    // Use the SetForced method directly - it handles everything
                    // Since SimpleSidearms reflection failed, just use the normal SetForced
                    // It will handle this case appropriately
                    AutoArmLogger.Debug($"[TEST] Force marked weapon {weapon.Label} for {pawn.Name} using fallback tracking");
                }
            }
            catch (System.Exception e)
            {
                AutoArmLogger.Error($"[TEST] Error force marking weapon as forced", e);
            }
        }
    }
}
