// AutoArm RimWorld 1.5+ mod - automatic weapon management
// This file: Harmony patches for weapon cache management
// Tracks ESSENTIAL weapon state changes that affect availability for pickup
//
// SIMPLIFIED: Only tracking core state changes that are absolutely necessary
// - Spawn/Despawn/Destroy
// - Equip/Drop
// - Position changes
// - Forbidden status

using AutoArm.Caching;
using AutoArm.Jobs;
using AutoArm.Logging;
using AutoArm.Weapons;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using System.Linq;
using Verse;
using Verse.AI;

namespace AutoArm
{
    // ========================================
    // BASIC EXISTENCE TRACKING
    // ========================================

    /// <summary>
    /// Track when weapons spawn into the world
    /// </summary>
    [HarmonyPatch(typeof(Thing), "SpawnSetup")]
    public static class Thing_SpawnSetup_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Thing __instance)
        {
            // Skip if mod disabled
            if (AutoArmMod.settings?.modEnabled != true)
                return;
                
            if (__instance == null || __instance.def == null)
                return;

            // Use proper weapon validation to match main logic
            if (!WeaponValidation.IsProperWeapon(__instance))
                return;

            if (__instance is ThingWithComps weapon && weapon.Map != null)
            {
                ImprovedWeaponCacheManager.AddWeaponToCache(weapon);
            }
        }
    }

    /// <summary>
    /// Track when weapons despawn from the world
    /// </summary>
    [HarmonyPatch(typeof(Thing), "DeSpawn")]
    public static class Thing_DeSpawn_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(Thing __instance)
        {
            // Skip if mod disabled
            if (AutoArmMod.settings?.modEnabled != true)
                return;
                
            if (__instance == null || __instance.def == null)
                return;

            if (!WeaponValidation.IsProperWeapon(__instance))
                return;

            if (__instance is ThingWithComps weapon)
            {
                ImprovedWeaponCacheManager.RemoveWeaponFromCache(weapon);
            }
        }
    }

    /// <summary>
    /// Track when weapons are destroyed
    /// </summary>
    [HarmonyPatch(typeof(Thing), "Destroy")]
    public static class Thing_Destroy_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(Thing __instance)
        {
            // Skip if mod disabled
            if (AutoArmMod.settings?.modEnabled != true)
                return;
                
            if (__instance == null || __instance.def == null)
                return;

            if (!WeaponValidation.IsProperWeapon(__instance))
                return;

            if (__instance is ThingWithComps weapon)
            {
                ImprovedWeaponCacheManager.RemoveWeaponFromCache(weapon);
            }
        }
    }

    // ========================================
    // POSITION & LOCATION TRACKING
    // ========================================

    /// <summary>
    /// Track when weapons move positions
    /// </summary>
    [HarmonyPatch(typeof(Thing), "set_Position")]
    public static class Thing_SetPosition_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(Thing __instance, IntVec3 value)
        {
            // Skip if mod disabled
            if (AutoArmMod.settings?.modEnabled != true)
                return;
                
            if (__instance == null || __instance.def == null || !__instance.Spawned)
                return;

            if (!WeaponValidation.IsProperWeapon(__instance))
                return;

            if (__instance is ThingWithComps weapon && weapon.Map != null)
            {
                ImprovedWeaponCacheManager.UpdateWeaponPosition(weapon, __instance.Position, value);
            }
        }
    }

    // ========================================
    // FORBIDDEN STATUS TRACKING
    // ========================================

    /// <summary>
    /// Track when weapons change forbidden status
    /// CRITICAL: This ensures the cache stays in sync with forbidden status changes
    /// </summary>
    [HarmonyPatch(typeof(ForbidUtility), "SetForbidden")]
    public static class ForbidUtility_SetForbidden_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Thing t, bool value, bool warnOnFail = true)
        {
            // Skip if mod disabled
            if (AutoArmMod.settings?.modEnabled != true)
                return;
                
            if (t == null || t.Map == null)
                return;

            // Only care about weapons
            if (!WeaponValidation.IsProperWeapon(t))
                return;

            if (t is ThingWithComps weapon)
            {
                // The cache itself doesn't need to do anything special for forbidden status
                // The validation checks will handle forbidden status when evaluating weapons
                // Mark cache as having a change detected to ensure it's refreshed if needed
                ImprovedWeaponCacheManager.MarkCacheAsChanged(weapon.Map);
            }
        }
    }

    /// <summary>
    /// Track mass unforbidding operations
    /// </summary>
    [HarmonyPatch(typeof(Designator_Unforbid), "DesignateSingleCell")]
    public static class Designator_Unforbid_DesignateSingleCell_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(IntVec3 c)
        {
            // Skip if mod disabled
            if (AutoArmMod.settings?.modEnabled != true)
                return;
                
            var map = Find.CurrentMap;
            if (map == null)
                return;

            bool foundWeapon = false;
            var things = c.GetThingList(map);
            foreach (var thing in things)
            {
                if (WeaponValidation.IsProperWeapon(thing))
                {
                    foundWeapon = true;
                    break;
                }
            }
            
            if (foundWeapon)
            {
                // Mark cache as changed for this map
                ImprovedWeaponCacheManager.MarkCacheAsChanged(map);
            }
        }
    }

    /// <summary>
    /// Track mass forbidding operations
    /// </summary>
    [HarmonyPatch(typeof(Designator_Forbid), "DesignateSingleCell")]
    public static class Designator_Forbid_DesignateSingleCell_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(IntVec3 c)
        {
            // Skip if mod disabled
            if (AutoArmMod.settings?.modEnabled != true)
                return;
                
            var map = Find.CurrentMap;
            if (map == null)
                return;

            bool foundWeapon = false;
            var things = c.GetThingList(map);
            foreach (var thing in things)
            {
                if (WeaponValidation.IsProperWeapon(thing))
                {
                    foundWeapon = true;
                    break;
                }
            }
            
            if (foundWeapon)
            {
                // Mark cache as changed for this map
                ImprovedWeaponCacheManager.MarkCacheAsChanged(map);
            }
        }
    }

    // ========================================
    // EQUIPMENT TRACKING
    // ========================================

    /// <summary>
    /// Track when weapons are picked up (removed from ground)
    /// </summary>
    [HarmonyPatch(typeof(Pawn_EquipmentTracker), "AddEquipment")]
    public static class Pawn_EquipmentTracker_AddEquipment_CachePatch
    {
        [HarmonyPostfix]
        public static void Postfix(ThingWithComps newEq)
        {
            // Skip if mod disabled
            if (AutoArmMod.settings?.modEnabled != true)
                return;
                
            if (newEq == null)
                return;

            if (!WeaponValidation.IsProperWeapon(newEq))
                return;

            // Weapon was picked up, remove from cache if it was there
            ImprovedWeaponCacheManager.RemoveWeaponFromCache(newEq);
        }
    }

    /// <summary>
    /// Track when weapons are dropped (added to ground)
    /// </summary>
    [HarmonyPatch(typeof(Pawn_EquipmentTracker), "TryDropEquipment")]
    public static class Pawn_EquipmentTracker_TryDropEquipment_CachePatch
    {
        [HarmonyPostfix]
        public static void Postfix(ThingWithComps eq, bool __result)
        {
            // Skip if mod disabled
            if (AutoArmMod.settings?.modEnabled != true)
                return;
                
            if (!__result || eq == null || eq.Map == null)
                return;

            if (!WeaponValidation.IsProperWeapon(eq))
                return;

            // Weapon was dropped, add to cache
            ImprovedWeaponCacheManager.AddWeaponToCache(eq);
        }
    }

    // ========================================
    // NOTE: REMOVED EDGE CASE PATCHES
    // ========================================
    // The following patches were removed because:
    // 1. They cause compatibility issues with different RimWorld versions
    // 2. They track edge cases that are already covered by spawn/despawn
    // 3. The mod works fine without them (99% functionality retained)
    //
    // Removed patches:
    // - Minified/Unminified tracking (covered by spawn/despawn)
    // - Reservation tracking (not critical)
    // - Fire/burning status (edge case)
    // - Trading (covered by despawn)
    // - Caravan/transport pods (covered by despawn)
    // - Quality changes (score cache handles it)
    // - HP/damage tracking (not critical)
    // - Biocoding (validation checks handle it)
    // - Quest items (validation checks handle it)
    // - Storage containers (still spawned, just in storage)
}
