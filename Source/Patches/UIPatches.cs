// AutoArm RimWorld 1.5+ mod - automatic weapon management
// This file: UI patches for forced weapon labels and filter visibility
// Shows "forced" labels and controls weapon filter visibility

using AutoArm.Logging;
using AutoArm.Testing;
using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using ForcedWeaponHelper = AutoArm.Helpers.ForcedWeaponHelper;

namespace AutoArm
{
    // ============================================================================
    // FORCED WEAPON LABEL PATCHES
    // ============================================================================

    /// <summary>
    /// Patches to add ", forced" to weapon labels in the gear tab, similar to forced apparel
    /// </summary>
    [HarmonyPatch(typeof(Thing), "Label", MethodType.Getter)]
    public static class Thing_Label_Patch
    {
        private static Dictionary<int, (string label, int tick)> cache = new Dictionary<int, (string, int)>();

        [HarmonyPrefix]
        public static bool Prefix(Thing __instance, ref string __result)
        {
            if (!__instance.def.IsWeapon)
                return true;

            int tick = Find.TickManager?.TicksGame ?? 0;

            if (cache.TryGetValue(__instance.thingIDNumber, out var cached) && cached.tick == tick)
            {
                __result = cached.label;
                return false;
            }

            return true;
        }

        [HarmonyPostfix]
        public static void Postfix(Thing __instance, ref string __result)
        {
            if (__instance.def.IsWeapon)
            {
                ForcedWeaponLabelHelper.AddForcedText(__instance, ref __result);
                if (cache.Count > 500)
                    cache.Clear();
                cache[__instance.thingIDNumber] = (__result, Find.TickManager?.TicksGame ?? 0);
            }
        }
    }

    [HarmonyPatch(typeof(Thing), "LabelNoCount", MethodType.Getter)]
    public static class Thing_LabelNoCount_Patch
    {
        private static Dictionary<int, (string label, int tick)> cache = new Dictionary<int, (string, int)>();

        [HarmonyPrefix]
        public static bool Prefix(Thing __instance, ref string __result)
        {
            if (!__instance.def.IsWeapon)
                return true;

            int tick = Find.TickManager?.TicksGame ?? 0;

            if (cache.TryGetValue(__instance.thingIDNumber, out var cached) && cached.tick == tick)
            {
                __result = cached.label;
                return false;
            }

            return true;
        }

        [HarmonyPostfix]
        public static void Postfix(Thing __instance, ref string __result)
        {
            if (__instance.def.IsWeapon)
            {
                ForcedWeaponLabelHelper.AddForcedText(__instance, ref __result);
                if (cache.Count > 500)
                    cache.Clear();
                cache[__instance.thingIDNumber] = (__result, Find.TickManager?.TicksGame ?? 0);
            }
        }
    }

    [HarmonyPatch(typeof(Thing), "LabelCap", MethodType.Getter)]
    public static class Thing_LabelCap_Patch
    {
        private static Dictionary<int, (string label, int tick)> cache = new Dictionary<int, (string, int)>();

        [HarmonyPrefix]
        public static bool Prefix(Thing __instance, ref string __result)
        {
            if (!__instance.def.IsWeapon)
                return true;

            int tick = Find.TickManager?.TicksGame ?? 0;

            if (cache.TryGetValue(__instance.thingIDNumber, out var cached) && cached.tick == tick)
            {
                __result = cached.label;
                return false;
            }

            return true;
        }

        [HarmonyPostfix]
        public static void Postfix(Thing __instance, ref string __result)
        {
            if (__instance.def.IsWeapon)
            {
                ForcedWeaponLabelHelper.AddForcedText(__instance, ref __result);
                if (cache.Count > 500)
                    cache.Clear();
                cache[__instance.thingIDNumber] = (__result, Find.TickManager?.TicksGame ?? 0);
            }
        }
    }

    internal static class ForcedWeaponLabelHelper
    {
        // Cache UI state to avoid repeated expensive lookups
        private static int lastUICheckTick = -1;

        private static bool cachedIsGearTabOpen = false;
        private static Pawn cachedSelectedPawn = null;

        // Cache forced weapons when pawn changes
        private static int lastForcedWeaponCheckTick = -1;

        private static Pawn lastForcedWeaponCheckPawn = null;
        private static HashSet<int> cachedForcedWeaponIds = new HashSet<int>();
        private static HashSet<ThingDef> cachedForcedWeaponDefs = new HashSet<ThingDef>();

        // Cache full labels with memory limit
        private static Dictionary<int, string> labelCache = new Dictionary<int, string>();

        private static int labelCacheTick = -1;
        private const int MaxLabelCacheSize = 100;

        // Pre-allocated strings
        private const string ForcedSuffix = ", forced";

        // Mod enabled caching
        private static bool ModEnabledCached = true;

        private static int ModEnabledCheckTick = -1;

        /// <summary>
        /// Adds ", forced" text to weapon labels when appropriate (optimized for gear tab only)
        /// </summary>
        internal static void AddForcedText(Thing thing, ref string label)
        {
            // CRITICAL: Check weapon first - this eliminates 95%+ of calls
            if (thing == null || !thing.def.IsWeapon || !(thing is ThingWithComps weapon))
                return;

            // Skip if already has forced text
            if (label.EndsWith(ForcedSuffix))
                return;

            // CRITICAL FIX: Don't try to access UI during world generation or when no map exists
            // This prevents InvalidCastException when generating faction leaders
            if (Find.CurrentMap == null || Find.Selector == null)
                return;

            int currentTick = Find.TickManager?.TicksGame ?? 0;

            // Cache mod enabled check
            if (ModEnabledCheckTick != currentTick)
            {
                ModEnabledCached = AutoArmMod.settings?.modEnabled == true;
                ModEnabledCheckTick = currentTick;
            }
            if (!ModEnabledCached) return;

            // Cache UI state check (only check once per tick)
            if (lastUICheckTick != currentTick)
            {
                lastUICheckTick = currentTick;
                labelCacheTick = -1; // Clear label cache on new tick

                // Check if viewing a colonist in gear tab
                var selectedPawn = Find.Selector?.SingleSelectedThing as Pawn;
                if (selectedPawn == null || !selectedPawn.IsColonist)
                {
                    cachedIsGearTabOpen = false;
                    cachedSelectedPawn = null;
                    ClearCaches();
                    return;
                }

                // Check if gear tab is open
                if (Find.MainTabsRoot?.OpenTab != MainButtonDefOf.Inspect)
                {
                    cachedIsGearTabOpen = false;
                    cachedSelectedPawn = null;
                    ClearCaches();
                    return;
                }

                var inspectPane = (MainTabWindow_Inspect)MainButtonDefOf.Inspect.TabWindow;
                cachedIsGearTabOpen = inspectPane?.OpenTabType == typeof(ITab_Pawn_Gear);
                cachedSelectedPawn = cachedIsGearTabOpen ? selectedPawn : null;

                if (!cachedIsGearTabOpen)
                    ClearCaches();
            }

            // Use cached results
            if (!cachedIsGearTabOpen || cachedSelectedPawn == null)
                return;

            // Check label cache
            if (labelCacheTick == currentTick && labelCache.TryGetValue(weapon.thingIDNumber, out string cachedLabel))
            {
                label = cachedLabel;
                return;
            }

            // Update forced weapon cache if pawn changed
            if (lastForcedWeaponCheckPawn != cachedSelectedPawn || lastForcedWeaponCheckTick != currentTick)
            {
                lastForcedWeaponCheckPawn = cachedSelectedPawn;
                lastForcedWeaponCheckTick = currentTick;
                BuildForcedWeaponCache(cachedSelectedPawn);
            }

            // Fast lookup using cached data
            bool isForced = false;
            
            if (SimpleSidearmsCompat.IsLoaded())
            {
                // For SimpleSidearms, only show "forced" on the PRIMARY weapon
                // SimpleSidearms marks a type as forced meaning "this should be your primary"
                // So only the equipped weapon should show as forced, not sidearms in inventory
                if (cachedForcedWeaponDefs.Contains(weapon.def))
                {
                    // Only show as forced if this is the equipped primary weapon
                    if (weapon == cachedSelectedPawn.equipment?.Primary)
                    {
                        isForced = true;
                    }
                }
            }
            else
            {
                // AutoArm tracks by specific instance ID
                // But only show "forced" if the weapon is actually owned by the pawn
                if (cachedForcedWeaponIds.Contains(weapon.thingIDNumber))
                {
                    // Verify the weapon is actually owned by the selected pawn
                    // (not just lying on the ground with a cached ID)
                    bool isOwned = false;
                    
                    // Check if it's equipped
                    if (weapon == cachedSelectedPawn.equipment?.Primary)
                    {
                        isOwned = true;
                    }
                    // Check if it's in inventory
                    else if (cachedSelectedPawn.inventory?.innerContainer?.Contains(weapon) == true)
                    {
                        isOwned = true;
                    }
                    
                    if (isOwned)
                    {
                        isForced = true;
                    }
                }
            }

            if (isForced)
            {
                label = label + ForcedSuffix;

                // Add to cache with size limit
                if (labelCache.Count > MaxLabelCacheSize)
                    labelCache.Clear();
                labelCache[weapon.thingIDNumber] = label;
                labelCacheTick = currentTick;
            }
        }

        private static void BuildForcedWeaponCache(Pawn pawn)
        {
            cachedForcedWeaponIds.Clear();
            cachedForcedWeaponDefs.Clear();

            // When SimpleSidearms is loaded, check forced weapon types ONCE
            if (SimpleSidearmsCompat.IsLoaded())
            {
                // Check all weapon defs that could be forced (more efficient than checking each weapon)
                var weaponDefs = new HashSet<ThingDef>();
                
                // Collect unique weapon defs from equipment and inventory
                if (pawn.equipment?.Primary != null)
                {
                    weaponDefs.Add(pawn.equipment.Primary.def);
                }
                
                if (pawn.inventory?.innerContainer != null)
                {
                    foreach (var item in pawn.inventory.innerContainer)
                    {
                        if (item is ThingWithComps invWeapon && invWeapon.def.IsWeapon)
                        {
                            weaponDefs.Add(invWeapon.def);
                        }
                    }
                }
                
                // Check each unique def ONCE
                foreach (var weaponDef in weaponDefs)
                {
                    bool isForced, isPreferred;
                    if (SimpleSidearmsCompat.IsWeaponTypeForced(pawn, weaponDef, out isForced, out isPreferred))
                    {
                        cachedForcedWeaponDefs.Add(weaponDef);
                    }
                }
            }
            else
            {
                // AutoArm's own forced weapon tracking by instance ID
                if (pawn.equipment?.Primary != null)
                {
                    var primary = pawn.equipment.Primary;
                    if (ForcedWeaponHelper.IsForced(pawn, primary))
                    {
                        cachedForcedWeaponIds.Add(primary.thingIDNumber);
                    }
                }

                if (pawn.inventory?.innerContainer != null)
                {
                    foreach (var item in pawn.inventory.innerContainer)
                    {
                        if (item is ThingWithComps invWeapon && invWeapon.def.IsWeapon)
                        {
                            if (ForcedWeaponHelper.IsForced(pawn, invWeapon))
                            {
                                cachedForcedWeaponIds.Add(invWeapon.thingIDNumber);
                            }
                        }
                    }
                }
            }
        }

        private static void ClearCaches()
        {
            labelCache.Clear();
            cachedForcedWeaponIds.Clear();
            cachedForcedWeaponDefs.Clear();
            lastForcedWeaponCheckPawn = null;
        }

        // Call from CleanupHelper
        public static void CleanupDeadPawnCaches()
        {
            if (lastForcedWeaponCheckPawn != null &&
                (lastForcedWeaponCheckPawn.Dead || lastForcedWeaponCheckPawn.Destroyed))
            {
                ClearCaches();
            }
        }

        /// <summary>
        /// Checks if a pawn has a specific weapon marked as forced
        /// </summary>
        private static bool CheckPawnHasForcedWeapon(Pawn pawn, ThingWithComps weapon)
        {
            // ONLY check specific weapon instance, not weapon type
            return ForcedWeaponHelper.IsForced(pawn, weapon);
        }
    }

    // ============================================================================
    // WEAPON FILTER UI VISIBILITY PATCH
    // ============================================================================

    // Note: The weapon filter visibility is controlled by patching the ThingCategoryDef
    // to make the Weapons category report as not having any contents when AutoArm is disabled

    /// <summary>
    /// Hide weapons category from outfit filters when AutoArm is disabled
    /// </summary>
    [HarmonyPatch(typeof(ThingCategoryDef), "DescendantThingDefs", MethodType.Getter)]
    public static class ThingCategoryDef_DescendantThingDefs_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ThingCategoryDef __instance, ref IEnumerable<ThingDef> __result)
        {
            // Skip this patch during tests to avoid breaking test functionality
            if (TestRunner.IsRunningTests)
                return;

            // If AutoArm is disabled and this is the Weapons category, return empty list
            if (AutoArmMod.settings?.modEnabled != true &&
                __instance?.defName == "Weapons" &&
                __instance?.parent?.defName == "Apparel")
            {
                __result = Enumerable.Empty<ThingDef>();
            }
        }
    }

    /// <summary>
    /// Also hide child categories when AutoArm is disabled
    /// </summary>
    [HarmonyPatch(typeof(ThingCategoryDef), "ThisAndChildCategoryDefs", MethodType.Getter)]
    public static class ThingCategoryDef_ThisAndChildCategoryDefs_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ThingCategoryDef __instance, ref IEnumerable<ThingCategoryDef> __result)
        {
            // Skip this patch during tests to avoid breaking test functionality
            if (TestRunner.IsRunningTests)
                return;

            // If AutoArm is disabled and this is looking for Weapons under Apparel, exclude it
            if (AutoArmMod.settings?.modEnabled != true && __instance?.defName == "Apparel")
            {
                __result = __result.Where(cat => cat.defName != "Weapons");
            }
        }
    }
}