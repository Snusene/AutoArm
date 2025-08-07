// AutoArm RimWorld 1.5+ mod - automatic weapon management
// This file: Simple outfit filter enforcement - drops weapons when player forbids them
// ONE-TIME drop when player changes policy, no constant checking

using AutoArm.Helpers;
using AutoArm.Jobs;
using AutoArm.Logging;
using AutoArm.Weapons;
using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace AutoArm
{
    /// <summary>
    /// Simple one-time weapon drop when player forbids a weapon type in outfit policy
    /// </summary>
    [HarmonyPatch(typeof(ThingFilter), "SetAllow", new Type[] { typeof(ThingDef), typeof(bool) })]
    public static class ThingFilter_SetAllow_Weapon_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ThingFilter __instance, ThingDef thingDef, bool allow)
        {
            // === EARLY EXITS - FASTEST CHECKS FIRST ===
            // Only act when player FORBIDS a weapon type
            if (allow || thingDef == null || !thingDef.IsWeapon)
                return;
                
            // Check if mod is enabled
            if (AutoArmMod.settings?.modEnabled != true)
                return;
                
            // Only check proper weapons that appear in the filter UI
            if (!WeaponValidation.IsProperWeapon(thingDef))
                return;
                
            // === CRITICAL OPTIMIZATION: Check if this is an outfit filter FIRST ===
            // This avoids checking stockpiles, bills, etc.
            var outfitDatabase = Current.Game?.outfitDatabase;
            if (outfitDatabase == null)
                return;
                
            // Find which outfit policy this filter belongs to (if any)
            ApparelPolicy affectedPolicy = null;
            var outfitPolicies = outfitDatabase.AllOutfits;
            if (outfitPolicies != null)
            {
                for (int i = 0; i < outfitPolicies.Count; i++)
                {
                    if (outfitPolicies[i].filter == __instance)
                    {
                        affectedPolicy = outfitPolicies[i];
                        break; // Found it - stop searching!
                    }
                }
            }
            
            // Not an outfit filter - exit immediately
            if (affectedPolicy == null)
                return;
                
            // === OPTIMIZED: Only check affected pawns ===
            
            foreach (var map in Find.Maps)
            {
                if (map?.mapPawns?.FreeColonists == null)
                    continue;
                    
                // Only check pawns using THIS SPECIFIC policy
                foreach (var pawn in map.mapPawns.FreeColonists)
                {
                    // Skip if not using this policy
                    if (pawn.outfits?.CurrentApparelPolicy != affectedPolicy)
                        continue;
                        
                    // Skip busy pawns
                    if (pawn.Drafted || pawn.InMentalState || pawn.Downed)
                        continue;
                        
                    // Skip if in ritual or hauling (more expensive checks)
                    if (ValidationHelper.IsInRitual(pawn) || JobGiverHelpers.IsHaulingJob(pawn))
                        continue;
                        
                    // Skip temporary colonists
                    if (JobGiverHelpers.IsTemporaryColonist(pawn))
                        continue;
                        
                    // Check primary weapon FIRST (fastest check)
                    var primary = pawn.equipment?.Primary;
                    
                    if (primary != null && primary.def == thingDef)
                    {
                        // Skip if forced
                        if (!ForcedWeaponHelper.IsForced(pawn, primary))
                        {
                            // Mark weapon to prevent immediate re-pickup
                            DroppedItemTracker.MarkAsDropped(primary, 600);
                            
                            // Queue a drop job
                            var dropJob = new Job(JobDefOf.DropEquipment, primary);
                            pawn.jobs.TryTakeOrderedJob(dropJob, JobTag.Misc);
                            
                            if (AutoArmMod.settings?.showNotifications == true && PawnUtility.ShouldSendNotificationAbout(pawn))
                            {
                                Messages.Message("AutoArm_DroppingDisallowed".Translate(
                                    pawn.LabelShort.CapitalizeFirst(),
                                    thingDef.label
                                ), new LookTargets(pawn), MessageTypeDefOf.SilentInput, false);
                            }
                            
                            if (AutoArmMod.settings?.debugLogging == true)
                            {
                                AutoArmLogger.Debug($"{pawn.LabelShort}: Dropping {thingDef.label} (forbidden in outfit)");
                            }
                        }
                    }
                    
                    // Check inventory only if pawn has inventory with items
                    var inventory = pawn.inventory?.innerContainer;
                    if (inventory != null && inventory.Count > 0)
                    {
                        // Manual iteration is faster than LINQ for small collections
                        List<ThingWithComps> weaponsToRemove = null;
                        
                        for (int i = 0; i < inventory.Count; i++)
                        {
                            var item = inventory[i] as ThingWithComps;
                            if (item != null && item.def == thingDef && 
                                WeaponValidation.IsProperWeapon(item) &&
                                !ForcedWeaponHelper.IsForced(pawn, item))
                            {
                                if (weaponsToRemove == null)
                                    weaponsToRemove = new List<ThingWithComps>();
                                weaponsToRemove.Add(item);
                            }
                        }
                        
                        if (weaponsToRemove != null)
                        {
                            foreach (var weapon in weaponsToRemove)
                            {
                                Thing droppedWeapon;
                                if (inventory.TryDrop(weapon, pawn.Position, pawn.Map, 
                                    ThingPlaceMode.Near, out droppedWeapon))
                                {
                                    // Mark to prevent re-pickup
                                    DroppedItemTracker.MarkAsDropped(droppedWeapon, 1200);
                                    
                                    if (AutoArmMod.settings?.debugLogging == true)
                                    {
                                        AutoArmLogger.Debug($"{pawn.LabelShort}: Dropped sidearm {weapon.Label} (forbidden in outfit)");
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
    }
    
    /// <summary>
    /// Handle "Clear All" button - drops all non-forced weapons after a 5 second delay
    /// </summary>
    [HarmonyPatch(typeof(ThingFilter), "SetDisallowAll")]
    public static class ThingFilter_SetDisallowAll_Patch
    {
        // Track pending "Clear All" actions with their scheduled tick
        private static Dictionary<ApparelPolicy, int> pendingClearAlls = new Dictionary<ApparelPolicy, int>();
        
        [HarmonyPostfix]
        public static void Postfix(ThingFilter __instance)
        {
            if (__instance == null || AutoArmMod.settings?.modEnabled != true)
                return;

            if (Current.Game == null || Find.Maps == null)
                return;

            // Check if this filter belongs to an outfit policy
            var outfitPolicies = Current.Game?.outfitDatabase?.AllOutfits;
            if (outfitPolicies == null)
                return;
                
            ApparelPolicy affectedPolicy = null;
            foreach (var policy in outfitPolicies)
            {
                if (policy.filter == __instance)
                {
                    affectedPolicy = policy;
                    break;
                }
            }
            
            if (affectedPolicy == null)
                return;

            // Schedule this clear-all to execute in 5 seconds (300 ticks)
            int executeTick = Find.TickManager.TicksGame + 300;
            pendingClearAlls[affectedPolicy] = executeTick;
            
            if (AutoArmMod.settings?.debugLogging == true)
            {
                AutoArmLogger.Debug($"Clear All clicked for {affectedPolicy.label} - weapons will drop in 5 seconds");
            }
        }
        
        // Check for pending drops every tick
        [HarmonyPatch(typeof(Game), "UpdatePlay")]
        public static class Game_UpdatePlay_CheckPendingDrops
        {
            [HarmonyPostfix]
            public static void Postfix()
            {
                if (pendingClearAlls.Count == 0)
                    return;
                    
                int currentTick = Find.TickManager.TicksGame;
                var toExecute = new List<ApparelPolicy>();
                
                // Find policies that are ready to execute
                foreach (var kvp in pendingClearAlls)
                {
                    if (currentTick >= kvp.Value)
                    {
                        toExecute.Add(kvp.Key);
                    }
                }
                
                // Execute and remove from pending
                foreach (var policy in toExecute)
                {
                    ExecuteClearAll(policy);
                    pendingClearAlls.Remove(policy);
                }
            }
        }
        
        private static void ExecuteClearAll(ApparelPolicy policy)
        {
            if (policy == null)
                return;
                
            // Re-check that the policy still has everything disallowed
            // (Player might have re-enabled items during the 5 second wait)
            bool hasAnyWeaponsAllowed = false;
            foreach (var weaponDef in DefDatabase<ThingDef>.AllDefs.Where(d => d.IsWeapon && WeaponValidation.IsProperWeapon(d)))
            {
                if (policy.filter.Allows(weaponDef))
                {
                    hasAnyWeaponsAllowed = true;
                    break;
                }
            }
            
            // If player re-enabled some weapons, don't drop anything
            if (hasAnyWeaponsAllowed)
            {
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.Debug($"Cancelled weapon drop for {policy.label} - player re-enabled some weapons");
                }
                return;
            }

            // Drop all weapons from affected colonists
            foreach (var map in Find.Maps)
            {
                if (map?.mapPawns?.FreeColonists == null)
                    continue;

                foreach (var pawn in map.mapPawns.FreeColonists)
                {
                    if (pawn.outfits?.CurrentApparelPolicy != policy)
                        continue;
                        
                    // Don't interfere with busy pawns
                    if (pawn.Drafted || pawn.InMentalState || pawn.Downed || 
                        ValidationHelper.IsInRitual(pawn) || JobGiverHelpers.IsHaulingJob(pawn))
                        continue;
                        
                    // Don't force temporary colonists to drop weapons
                    if (JobGiverHelpers.IsTemporaryColonist(pawn))
                        continue;

                    // Drop primary weapon if not forced
                    if (pawn.equipment?.Primary != null && 
                        WeaponValidation.IsProperWeapon(pawn.equipment.Primary) &&
                        !ForcedWeaponHelper.IsForced(pawn, pawn.equipment.Primary))
                    {
                        DroppedItemTracker.MarkAsDropped(pawn.equipment.Primary, 600);
                        var dropJob = new Job(JobDefOf.DropEquipment, pawn.equipment.Primary);
                        pawn.jobs.TryTakeOrderedJob(dropJob, JobTag.Misc);
                        
                        if (AutoArmMod.settings?.debugLogging == true)
                        {
                            AutoArmLogger.Debug($"{pawn.LabelShort}: Dropping all weapons (Clear All executed after 5 second delay)");
                        }
                    }
                    
                    // Drop all sidearms that aren't forced
                    if (pawn.inventory?.innerContainer != null)
                    {
                        var weaponsToRemove = pawn.inventory.innerContainer
                            .OfType<ThingWithComps>()
                            .Where(w => w.def.IsWeapon && 
                                   WeaponValidation.IsProperWeapon(w) &&
                                   !ForcedWeaponHelper.IsForced(pawn, w))
                            .ToList();
                            
                        foreach (var weapon in weaponsToRemove)
                        {
                            Thing droppedWeapon;
                            if (pawn.inventory.innerContainer.TryDrop(weapon, pawn.Position, pawn.Map, 
                                ThingPlaceMode.Near, out droppedWeapon))
                            {
                                DroppedItemTracker.MarkAsDropped(droppedWeapon, 1200);
                            }
                        }
                    }
                }
            }
        }
    }
}
