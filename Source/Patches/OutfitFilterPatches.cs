// AutoArm RimWorld 1.5+ mod - automatic weapon management
// This file: Simple outfit filter enforcement - drops weapons when player forbids them
// OPTIMIZED: Fast detection of outfit filters without checking all filters

using AutoArm.Helpers;
using AutoArm.Jobs;
using AutoArm.Logging;
using AutoArm.Weapons;
using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;

namespace AutoArm
{
    /// <summary>
    /// Cache to quickly identify outfit filters without iterating all policies
    /// </summary>
    public static class OutfitFilterCache
    {
        // Map ThingFilter instances to their ApparelPolicy
        private static Dictionary<ThingFilter, ApparelPolicy> filterToPolicyMap = new Dictionary<ThingFilter, ApparelPolicy>();

        // Rebuild the cache when needed
        public static void RebuildCache()
        {
            filterToPolicyMap.Clear();

            var outfitDatabase = Current.Game?.outfitDatabase;
            if (outfitDatabase?.AllOutfits == null)
                return;

            foreach (var policy in outfitDatabase.AllOutfits)
            {
                if (policy?.filter != null)
                {
                    filterToPolicyMap[policy.filter] = policy;
                }
            }

            if (AutoArmMod.settings?.debugLogging == true)
            {
                AutoArmLogger.Debug($"Outfit filter cache rebuilt: {filterToPolicyMap.Count} policies cached");
            }
        }

        // Fast O(1) lookup to check if a filter is an outfit filter
        public static ApparelPolicy GetPolicyForFilter(ThingFilter filter)
        {
            if (filter == null)
                return null;

            // Try to get from cache first
            if (filterToPolicyMap.TryGetValue(filter, out ApparelPolicy policy))
                return policy;

            // Cache might be stale, rebuild once and try again
            RebuildCache();
            filterToPolicyMap.TryGetValue(filter, out policy);
            return policy;
        }

        // Clear cache when game changes
        public static void Clear()
        {
            filterToPolicyMap.Clear();
        }
    }

    /// <summary>
    /// Rebuild cache when game loads or outfits change
    /// </summary>
    [HarmonyPatch(typeof(Game), "LoadGame")]
    public static class Game_LoadGame_RebuildOutfitCache
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            OutfitFilterCache.RebuildCache();
        }
    }

    [HarmonyPatch(typeof(Game), "InitNewGame")]
    public static class Game_InitNewGame_RebuildOutfitCache
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            OutfitFilterCache.RebuildCache();
        }
    }

    [HarmonyPatch(typeof(OutfitDatabase), "MakeNewOutfit")]
    public static class OutfitDatabase_MakeNewOutfit_UpdateCache
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            OutfitFilterCache.RebuildCache();
        }
    }

    [HarmonyPatch(typeof(OutfitDatabase), "TryDelete")]
    public static class OutfitDatabase_TryDelete_UpdateCache
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            OutfitFilterCache.RebuildCache();
        }
    }

    /// <summary>
    /// OPTIMIZED: Simple one-time weapon drop when player forbids a weapon type in outfit policy
    /// Now with O(1) lookup instead of O(n) iteration through all policies
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

            // Only check proper weapons
            if (!WeaponValidation.IsProperWeapon(thingDef))
                return;

            // === OPTIMIZED: O(1) lookup to check if this is an outfit filter ===
            ApparelPolicy affectedPolicy = OutfitFilterCache.GetPolicyForFilter(__instance);

            // Not an outfit filter - exit immediately
            if (affectedPolicy == null)
                return;

            // === Now we know this is an outfit filter, proceed with pawn checks ===

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

                            // Two-step approach: Drop then haul to storage
                            // First, drop the weapon at the pawn's position
                            ThingWithComps droppedWeapon = null;
                            pawn.equipment.TryDropEquipment(primary, out droppedWeapon, pawn.Position, forbid: false);

                            // If drop was successful, queue a haul job to take it to storage
                            if (droppedWeapon != null)
                            {
                                // Create haul job to storage
                                var haulJob = HaulAIUtility.HaulToStorageJob(pawn, droppedWeapon, forced: false);
                                if (haulJob != null)
                                {
                                    pawn.jobs.TryTakeOrderedJob(haulJob, JobTag.Misc);
                                }
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
                        // Check if we have any forbidden weapons in inventory
                        bool hasForbiddenWeapons = false;

                        for (int i = 0; i < inventory.Count; i++)
                        {
                            var item = inventory[i] as ThingWithComps;
                            if (item != null && item.def == thingDef &&
                                WeaponValidation.IsProperWeapon(item) &&
                                !ForcedWeaponHelper.IsForced(pawn, item))
                            {
                                // Mark to prevent re-pickup
                                DroppedItemTracker.MarkAsDropped(item, 1200);
                                hasForbiddenWeapons = true;
                            }
                        }

                        // If we found forbidden weapons, trigger unload job
                        // This will handle all unloadable items including our marked weapons
                        if (hasForbiddenWeapons)
                        {
                            var unloadJob = new Job(JobDefOf.UnloadYourInventory);
                            pawn.jobs.TryTakeOrderedJob(unloadJob, JobTag.Misc);

                            if (AutoArmMod.settings?.debugLogging == true)
                            {
                                AutoArmLogger.Debug($"{pawn.LabelShort}: Unloading inventory with forbidden weapons");
                            }
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// OPTIMIZED: Handle "Clear All" button - drops all non-forced weapons after a 5 second delay
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

            // === OPTIMIZED: O(1) lookup instead of iterating all policies ===
            ApparelPolicy affectedPolicy = OutfitFilterCache.GetPolicyForFilter(__instance);

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

        // Check for pending drops every second instead of every tick
        [HarmonyPatch(typeof(Game), "UpdatePlay")]
        public static class Game_UpdatePlay_CheckPendingDrops
        {
            private static int nextCheckTick = 0;

            [HarmonyPostfix]
            public static void Postfix()
            {
                if (pendingClearAlls.Count == 0)
                    return;

                int currentTick = Find.TickManager.TicksGame;

                // Only check every 60 ticks (1 second)
                if (currentTick < nextCheckTick)
                    return;

                nextCheckTick = currentTick + 60;

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
                        var primary = pawn.equipment.Primary;
                        DroppedItemTracker.MarkAsDropped(primary, 600);

                        // Two-step approach: Drop then haul to storage
                        ThingWithComps droppedWeapon = null;
                        pawn.equipment.TryDropEquipment(primary, out droppedWeapon, pawn.Position, forbid: false);

                        // If drop was successful, queue a haul job to take it to storage
                        if (droppedWeapon != null)
                        {
                            // Create haul job to storage
                            var haulJob = HaulAIUtility.HaulToStorageJob(pawn, droppedWeapon, forced: false);
                            if (haulJob != null)
                            {
                                pawn.jobs.TryTakeOrderedJob(haulJob, JobTag.Misc);
                            }
                        }

                        if (AutoArmMod.settings?.debugLogging == true)
                        {
                            AutoArmLogger.Debug($"{pawn.LabelShort}: Dropping weapon and hauling to storage (Clear All)");
                        }
                    }

                    // Check and drop sidearms that aren't forced
                    if (pawn.inventory?.innerContainer != null)
                    {
                        // Mark all non-forced weapons
                        bool hasWeaponsToUnload = false;
                        foreach (var item in pawn.inventory.innerContainer)
                        {
                            if (item is ThingWithComps weapon && weapon.def.IsWeapon &&
                                WeaponValidation.IsProperWeapon(weapon) &&
                                !ForcedWeaponHelper.IsForced(pawn, weapon))
                            {
                                DroppedItemTracker.MarkAsDropped(weapon, 1200);
                                hasWeaponsToUnload = true;
                            }
                        }

                        if (hasWeaponsToUnload)
                        {
                            var unloadJob = new Job(JobDefOf.UnloadYourInventory);
                            pawn.jobs.TryTakeOrderedJob(unloadJob, JobTag.Misc);

                            if (AutoArmMod.settings?.debugLogging == true)
                            {
                                AutoArmLogger.Debug($"{pawn.LabelShort}: Unloading sidearms to storage (Clear All)");
                            }
                        }
                    }
                }
            }
        }
    }
}