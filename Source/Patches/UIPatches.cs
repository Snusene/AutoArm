// AutoArm RimWorld 1.5+ mod - automatic weapon management
// This file: UI patches for forced weapon labels and filter visibility
// Shows "forced" labels and controls weapon filter visibility

using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using AutoArm.Testing;

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
        [HarmonyPostfix]
        public static void Postfix(Thing __instance, ref string __result)
        {
            ForcedWeaponLabelHelper.AddForcedText(__instance, ref __result);
        }
    }

    [HarmonyPatch(typeof(Thing), "LabelNoCount", MethodType.Getter)]
    public static class Thing_LabelNoCount_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Thing __instance, ref string __result)
        {
            ForcedWeaponLabelHelper.AddForcedText(__instance, ref __result);
        }
    }

    [HarmonyPatch(typeof(Thing), "LabelCap", MethodType.Getter)]
    public static class Thing_LabelCap_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Thing __instance, ref string __result)
        {
            ForcedWeaponLabelHelper.AddForcedText(__instance, ref __result);
        }
    }

    internal static class ForcedWeaponLabelHelper
    {
        /// <summary>
        /// Adds ", forced" text to weapon labels when appropriate (optimized for gear tab only)
        /// </summary>
        internal static void AddForcedText(Thing thing, ref string label)
        {
            // Check if mod is enabled
            if (AutoArmMod.settings?.modEnabled != true)
                return;

            if (thing == null || !thing.def.IsWeapon || !(thing is ThingWithComps weapon) || label.EndsWith(", forced"))
                return;

            // Performance optimization: Only run when viewing a colonist
            var selectedPawn = Find.Selector.SingleSelectedThing as Pawn;
            if (selectedPawn == null || !selectedPawn.IsColonist)
                return;

            // Check if the inspect pane is open and showing gear tab
            var inspectPane = (MainTabWindow_Inspect)MainButtonDefOf.Inspect.TabWindow;
            if (inspectPane == null || Find.MainTabsRoot.OpenTab != MainButtonDefOf.Inspect)
                return;

            // Check if the gear tab is the active tab
            var curTab = inspectPane.CurTabs?.FirstOrDefault(t => t is ITab_Pawn_Gear);
            if (curTab == null || inspectPane.OpenTabType != typeof(ITab_Pawn_Gear))
                return;

            // Only check if this specific pawn has this weapon forced
            if (CheckPawnHasForcedWeapon(selectedPawn, weapon))
            {
                label += ", forced";
            }
        }

        /// <summary>
        /// Checks if a pawn has a specific weapon marked as forced
        /// </summary>
        private static bool CheckPawnHasForcedWeapon(Pawn pawn, ThingWithComps weapon)
        {
            // Check if equipped as primary
            if (pawn.equipment?.Primary == weapon && ForcedWeaponHelper.IsForced(pawn, weapon))
                return true;

            // Check if in inventory
            if (pawn.inventory?.innerContainer?.Contains(weapon) == true)
            {
                if (ForcedWeaponHelper.IsForced(pawn, weapon) ||
                    ForcedWeaponHelper.IsWeaponDefForced(pawn, weapon.def))
                    return true;
            }

            // Check if weapon def is forced (for equipped weapons)
            if (pawn.equipment?.Primary == weapon && ForcedWeaponHelper.IsWeaponDefForced(pawn, weapon.def))
                return true;

            return false;
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
