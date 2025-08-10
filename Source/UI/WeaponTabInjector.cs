// AutoArm RimWorld 1.5+ mod - automatic weapon management
// This file: Runtime weapon filter injection for outfit filtering
// LIGHTWEIGHT VERSION: Minimal category manipulation for performance

using AutoArm.Logging;
using AutoArm.Weapons;
using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Verse;

namespace AutoArm.UI
{
    [StaticConstructorOnStartup]
    public static class WeaponTabInjector
    {
        private static ThingCategoryDef weaponsCategory;
        private static ThingCategoryDef apparelCategory;
        private static ThingCategoryDef originalWeaponsParent;
        private static bool isMovedForUI = false;
        private static bool isProcessing = false;

        static WeaponTabInjector()
        {
            try
            {
                apparelCategory = DefDatabase<ThingCategoryDef>.GetNamedSilentFail("Apparel");
                weaponsCategory = DefDatabase<ThingCategoryDef>.GetNamedSilentFail("Weapons");

                if (apparelCategory == null || weaponsCategory == null)
                {
                    AutoArmLogger.Error("CRITICAL: Could not find Apparel or Weapons categories - weapon filtering will not work!");
                    return;
                }

                // Store the original parent (should be Root)
                originalWeaponsParent = weaponsCategory.parent;

                // Check if another mod already moved weapons
                if (weaponsCategory.parent == apparelCategory)
                {
                    AutoArmLogger.Warn("WARNING: Weapons category is already under Apparel - another mod may have moved it!");
                    originalWeaponsParent = ThingCategoryDefOf.Root; // Assume it should be at root
                }

                // Log weapon counts for debugging
                try
                {
                    int rangedCount = WeaponValidation.RangedWeapons.Count;
                    int meleeCount = WeaponValidation.MeleeWeapons.Count;
                    AutoArmLogger.Debug($"Found {rangedCount} ranged and {meleeCount} melee weapon definitions");
                }
                catch (Exception e)
                {
                    AutoArmLogger.Error("Failed to count weapon definitions", e);
                }
            }
            catch (Exception e)
            {
                AutoArmLogger.Error($"CRITICAL ERROR in WeaponTabInjector: {e.Message}", e);
            }
        }

        /// <summary>
        /// Move weapons under apparel for UI (lightweight version)
        /// </summary>
        public static void MoveWeaponsForUI()
        {
            if (isProcessing || isMovedForUI || weaponsCategory == null || apparelCategory == null)
                return;

            try
            {
                isProcessing = true;

                if (weaponsCategory.parent != apparelCategory)
                {
                    // Simple parent swap
                    weaponsCategory.parent = apparelCategory;

                    // Update child lists
                    if (!apparelCategory.childCategories.Contains(weaponsCategory))
                    {
                        apparelCategory.childCategories.Add(weaponsCategory);
                    }

                    if (originalWeaponsParent != null && originalWeaponsParent.childCategories.Contains(weaponsCategory))
                    {
                        originalWeaponsParent.childCategories.Remove(weaponsCategory);
                    }

                    isMovedForUI = true;
                    AutoArmLogger.Debug("Moved weapons under apparel for UI");
                }
            }
            catch (Exception e)
            {
                AutoArmLogger.Error($"Failed to move weapons for UI: {e.Message}", e);
            }
            finally
            {
                isProcessing = false;
            }
        }

        /// <summary>
        /// Restore weapons to original position (lightweight version)
        /// </summary>
        public static void RestoreWeaponsPosition()
        {
            if (isProcessing || !isMovedForUI || weaponsCategory == null || originalWeaponsParent == null)
                return;

            try
            {
                isProcessing = true;

                if (weaponsCategory.parent != originalWeaponsParent)
                {
                    // Simple parent swap back
                    weaponsCategory.parent = originalWeaponsParent;

                    // Update child lists
                    if (!originalWeaponsParent.childCategories.Contains(weaponsCategory))
                    {
                        originalWeaponsParent.childCategories.Add(weaponsCategory);
                    }

                    if (apparelCategory != null && apparelCategory.childCategories.Contains(weaponsCategory))
                    {
                        apparelCategory.childCategories.Remove(weaponsCategory);
                    }

                    isMovedForUI = false;
                    AutoArmLogger.Debug("Restored weapons to original position");
                }
            }
            catch (Exception e)
            {
                AutoArmLogger.Error($"Failed to restore weapons position: {e.Message}", e);
            }
            finally
            {
                isProcessing = false;
            }
        }
    }

    // ============================================================================
    // UI PATCHES - Move weapons only when outfit dialog opens/closes
    // ============================================================================

    /// <summary>
    /// Move weapons when outfit dialog opens
    /// </summary>
    [HarmonyPatch(typeof(Dialog_ManageApparelPolicies), "PreOpen")]
    public static class Dialog_ManageApparelPolicies_PreOpen_Patch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            WeaponTabInjector.MoveWeaponsForUI();
        }
    }

    /// <summary>
    /// Restore weapons when any window closes (if it's the apparel dialog)
    /// </summary>
    [HarmonyPatch(typeof(Window), "Close")]
    public static class Window_Close_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(Window __instance, bool doCloseSound = true)
        {
            if (__instance is Dialog_ManageApparelPolicies)
            {
                WeaponTabInjector.RestoreWeaponsPosition();
            }
        }
    }

    /// <summary>
    /// Ensure weapons are restored if dialog is closed abnormally
    /// </summary>
    [HarmonyPatch(typeof(WindowStack), "TryRemove", new Type[] { typeof(Window), typeof(bool) })]
    public static class WindowStack_TryRemove_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Window window, bool doCloseSound)
        {
            if (window is Dialog_ManageApparelPolicies)
            {
                WeaponTabInjector.RestoreWeaponsPosition();
            }
        }
    }

    /// <summary>
    /// Ensure weapons are in correct position for bill filters
    /// </summary>
    [HarmonyPatch(typeof(Dialog_BillConfig), "PreOpen")]
    public static class Dialog_BillConfig_PreOpen_Patch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            // Make sure weapons are NOT under apparel for bill dialogs
            WeaponTabInjector.RestoreWeaponsPosition();
        }
    }

    /// <summary>
    /// Ensure weapons are in correct position for storage filters
    /// </summary>
    [HarmonyPatch(typeof(ITab_Storage), "FillTab")]
    public static class ITab_Storage_FillTab_Patch
    {
        [HarmonyPrefix]
        public static void Prefix()
        {
            // Make sure weapons are NOT under apparel for storage tabs
            WeaponTabInjector.RestoreWeaponsPosition();
        }
    }

    // ============================================================================
    // FILTER PATCHES - Make weapons work in outfit filters
    // ============================================================================

    /// <summary>
    /// Make ThingFilter.Allows work for weapons in outfit filters
    /// </summary>
    [HarmonyPatch(typeof(ThingFilter), "Allows", new Type[] { typeof(Thing) })]
    public static class ThingFilter_Allows_Thing_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ThingFilter __instance, Thing t, ref bool __result)
        {
            // If already allowed, nothing to do
            if (__result)
                return;

            // Only modify for outfit filters
            if (!IsOutfitFilter(__instance))
                return;

            // Check if this is a weapon
            if (t is ThingWithComps weapon && WeaponValidation.IsProperWeapon(weapon))
            {
                // Check if weapons are allowed in this filter
                try
                {
                    var allowedDefsField = typeof(ThingFilter).GetField("allowedDefs",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    var allowedDefs = allowedDefsField?.GetValue(__instance) as HashSet<ThingDef>;

                    if (allowedDefs != null && allowedDefs.Contains(weapon.def))
                    {
                        // Also check quality and hitpoints if configured
                        if (!CheckQualityFilter(__instance, weapon))
                            return;

                        if (!CheckHitPointsFilter(__instance, weapon))
                            return;

                        // Weapon passes all checks
                        __result = true;
                    }
                }
                catch (Exception e)
                {
                    AutoArmLogger.Debug($"Failed to check weapon in outfit filter: {e.Message}");
                }
            }
        }

        private static bool CheckQualityFilter(ThingFilter filter, ThingWithComps weapon)
        {
            try
            {
                var allowedQualities = filter.AllowedQualityLevels;

                if (allowedQualities != QualityRange.All)
                {
                    QualityCategory quality;
                    if (weapon.TryGetQuality(out quality))
                    {
                        if (!allowedQualities.Includes(quality))
                            return false;
                    }
                }
            }
            catch { }

            return true;
        }

        private static bool CheckHitPointsFilter(ThingFilter filter, ThingWithComps weapon)
        {
            try
            {
                var allowedHitPointsPercents = filter.AllowedHitPointsPercents;

                if (allowedHitPointsPercents != FloatRange.ZeroToOne)
                {
                    float hitPointsPercent = (float)weapon.HitPoints / weapon.MaxHitPoints;

                    // ASYMMETRIC FILTER: Respect maximum HP but skip minimum HP for weapons

                    // If filter has a MAXIMUM (e.g., "up to 50%" for slaves)
                    if (allowedHitPointsPercents.max < 1.0f)
                    {
                        // RESPECT the maximum - slaves only get damaged weapons
                        if (hitPointsPercent > allowedHitPointsPercents.max)
                            return false;
                    }

                    // If filter has a MINIMUM (e.g., "51%+" for anti-tainted)
                    // IGNORE the minimum for weapons - they don't have tainted penalties
                    // The penalty will be applied in scoring instead

                    return true;
                }
            }
            catch { }

            return true;
        }

        private static bool IsOutfitFilter(ThingFilter filter)
        {
            if (filter == null)
                return false;

            var outfitDatabase = Current.Game?.outfitDatabase;
            if (outfitDatabase != null)
            {
                foreach (var outfit in outfitDatabase.AllOutfits)
                {
                    if (outfit.filter == filter)
                        return true;
                }
            }

            return false;
        }
    }

    // ============================================================================
    // GAME LOADING PATCHES - Add weapons to outfit filters
    // ============================================================================

    [HarmonyPatch(typeof(Game), "LoadGame")]
    public static class Game_LoadGame_AddWeaponsToOutfits_Patch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            try
            {
                // Ensure weapons are in correct position after loading
                WeaponTabInjector.RestoreWeaponsPosition();

                OutfitWeaponHelper.AddWeaponsToOutfits();

                // Check for bonded weapons if setting is enabled
                if (AutoArmMod.settings?.modEnabled == true &&
                    AutoArmMod.settings?.respectWeaponBonds == true &&
                    ModsConfig.RoyaltyActive)
                {
                    AutoArmMod.MarkAllBondedWeaponsAsForcedOnLoad();
                }
            }
            catch (Exception e)
            {
                AutoArmLogger.Error("Failed in LoadGame postfix", e);
            }
        }
    }

    [HarmonyPatch(typeof(Game), "InitNewGame")]
    public static class Game_InitNewGame_AddWeaponsToOutfits_Patch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            try
            {
                // Ensure weapons are in correct position after init
                WeaponTabInjector.RestoreWeaponsPosition();

                OutfitWeaponHelper.AddWeaponsToOutfits();
            }
            catch (Exception e)
            {
                AutoArmLogger.Error("Failed in InitNewGame postfix", e);
            }
        }
    }

    public static class OutfitWeaponHelper
    {
        public static void AddWeaponsToOutfits()
        {
            try
            {
                if (Current.Game?.outfitDatabase == null)
                    return;

                // Temporarily move weapons to make filter operations work
                WeaponTabInjector.MoveWeaponsForUI();

                try
                {
                    int outfitsModified = 0;
                    int outfitsSkipped = 0;

                    foreach (var outfit in Current.Game.outfitDatabase.AllOutfits)
                    {
                        try
                        {
                            if (outfit.filter != null)
                            {
                                // Check if outfit already has weapon configuration
                                bool alreadyHasWeapons = WeaponValidation.AllWeapons
                                    .Any(weapon => outfit.filter.Allows(weapon));

                                if (!alreadyHasWeapons)
                                {
                                    // Determine if this outfit should have weapons
                                    bool shouldHaveWeapons = ShouldOutfitHaveWeapons(outfit);

                                    if (shouldHaveWeapons)
                                    {
                                        // Add all weapon types to the filter
                                        var weaponsCategory = DefDatabase<ThingCategoryDef>.GetNamedSilentFail("Weapons");
                                        if (weaponsCategory != null)
                                        {
                                            outfit.filter.SetAllow(weaponsCategory, true);
                                            outfitsModified++;
                                            AutoArmLogger.Debug($"Added default weapons to new outfit: {outfit.label}");
                                        }
                                    }
                                }
                                else
                                {
                                    outfitsSkipped++;
                                }
                            }
                        }
                        catch (Exception e)
                        {
                            AutoArmLogger.Error($"Failed to process outfit '{outfit?.label ?? "null"}'", e);
                        }
                    }

                    // Log results
                    if (outfitsModified > 0 || outfitsSkipped > 0)
                    {
                        if (outfitsModified > 0 && outfitsSkipped == 0)
                        {
                            // Likely a new game
                            AutoArmLogger.Debug($"Added default weapons to {outfitsModified} new outfits");
                        }
                        else if (outfitsSkipped > 0 && outfitsModified == 0)
                        {
                            // Likely a loaded save with all outfits configured
                            AutoArmLogger.Debug($"Preserved {outfitsSkipped} player-configured outfit filters (no modifications made)");
                        }
                        else
                        {
                            // Mixed - some new, some existing
                            AutoArmLogger.Debug($"Modified {outfitsModified} new outfits, preserved {outfitsSkipped} player-configured outfit filters");
                        }
                    }
                }
                finally
                {
                    // Always restore weapons position
                    WeaponTabInjector.RestoreWeaponsPosition();
                }
            }
            catch (Exception e)
            {
                AutoArmLogger.Error("Failed to add weapons to outfits", e);
                // Make sure to restore position even on error
                WeaponTabInjector.RestoreWeaponsPosition();
            }
        }

        private static bool ShouldOutfitHaveWeapons(ApparelPolicy outfit)
        {
            // Check for nudist outfits
            bool isNudist = outfit.label.IndexOf("nudist", StringComparison.OrdinalIgnoreCase) >= 0 ||
                           outfit.label.IndexOf("nude", StringComparison.OrdinalIgnoreCase) >= 0;

            if (isNudist)
                return false;

            // Check if outfit allows most apparel (general purpose outfit)
            bool allowsMostApparel = false;
            try
            {
                var apparelDefs = DefDatabase<ThingDef>.AllDefs
                    .Where(def => def.IsApparel && !def.IsWeapon && def.category == ThingCategory.Item)
                    .ToList();

                if (apparelDefs.Count > 0)
                {
                    int allowedCount = apparelDefs.Count(def => outfit.filter.Allows(def));
                    float allowedPercentage = (float)allowedCount / apparelDefs.Count;

                    // If outfit allows more than 70% of apparel, it's probably a general outfit
                    allowsMostApparel = allowedPercentage > 0.7f;

                    if (AutoArmMod.settings?.debugLogging == true && allowsMostApparel)
                    {
                        AutoArmLogger.Debug($"Outfit '{outfit.label}' allows {allowedPercentage:P0} of apparel - adding weapons");
                    }
                }
            }
            catch (Exception e)
            {
                AutoArmLogger.Debug($"Failed to check apparel allowance for outfit '{outfit.label}': {e.Message}");
            }

            // Name-based detection as fallback (or for specific roles)
            bool hasWeaponKeyword = outfit.label.IndexOf("anything", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    outfit.label.IndexOf("soldier", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    outfit.label.IndexOf("worker", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    outfit.label.IndexOf("spacefarer", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    outfit.label.IndexOf("guard", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    outfit.label.IndexOf("fighter", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    outfit.label.IndexOf("battle", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    outfit.label.IndexOf("combat", StringComparison.OrdinalIgnoreCase) >= 0;

            // Add weapons if:
            // 1. Outfit allows most apparel (general purpose outfit) AND not nudist
            // 2. OR outfit name suggests it should have weapons AND not nudist
            return !isNudist && (allowsMostApparel || hasWeaponKeyword);
        }
    }
}