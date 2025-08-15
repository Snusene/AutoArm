// AutoArm RimWorld 1.5+ mod - automatic weapon management
// This file: Weapon filter save/load patch for outfit persistence

using AutoArm.Logging;
using AutoArm.Weapons;
using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;

namespace AutoArm.UI
{
    // Patch ApparelPolicy's ExposeData to save weapon filter states
    [HarmonyPatch(typeof(ApparelPolicy), "ExposeData")]
    public static class ApparelPolicy_ExposeData_Patch
    {
        // Track which outfits have loaded weapon data
        private static HashSet<string> outfitsWithLoadedWeaponData = new HashSet<string>();

        // Temporary storage for filter data during loading
        private static Dictionary<string, OutfitWeaponFilterData> pendingFilterRestoration = new Dictionary<string, OutfitWeaponFilterData>();

        // Persistent storage of loaded weapon data for checking against new weapons
        private static Dictionary<string, OutfitWeaponFilterData> loadedWeaponData = new Dictionary<string, OutfitWeaponFilterData>();

        [HarmonyPostfix]
        public static void Postfix(ApparelPolicy __instance)
        {
            if (__instance?.filter == null)
                return;

            // For saving: collect current weapon filter states
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                List<string> allowedWeapons = new List<string>();
                List<string> disallowedWeapons = new List<string>();
                List<string> allowedCategories = new List<string>();
                List<string> disallowedCategories = new List<string>();

                // Save individual weapon states
                foreach (var weaponDef in WeaponValidation.AllWeapons)
                {
                    if (__instance.filter.Allows(weaponDef))
                    {
                        allowedWeapons.Add(weaponDef.defName);
                    }
                    else
                    {
                        disallowedWeapons.Add(weaponDef.defName);
                    }
                }

                // Save weapon category states
                var weaponCategories = new[] { "Weapons", "WeaponsMelee", "WeaponsRanged", "WeaponsUnique", "WeaponsMeleeBladelink" };
                foreach (var categoryName in weaponCategories)
                {
                    var category = DefDatabase<ThingCategoryDef>.GetNamedSilentFail(categoryName);
                    if (category != null)
                    {
                        // Check if the category is allowed (all its items are allowed)
                        bool categoryAllowed = IsCategoryAllowed(__instance.filter, category);
                        if (categoryAllowed)
                        {
                            allowedCategories.Add(categoryName);
                        }
                        else
                        {
                            disallowedCategories.Add(categoryName);
                        }
                    }
                }

                // Only save if we have weapon data
                if (allowedWeapons.Count > 0 || disallowedWeapons.Count > 0 || allowedCategories.Count > 0 || disallowedCategories.Count > 0)
                {
                    Scribe_Collections.Look(ref allowedWeapons, "autoArmAllowedWeapons", LookMode.Value);
                    Scribe_Collections.Look(ref disallowedWeapons, "autoArmDisallowedWeapons", LookMode.Value);
                    Scribe_Collections.Look(ref allowedCategories, "autoArmAllowedCategories", LookMode.Value);
                    Scribe_Collections.Look(ref disallowedCategories, "autoArmDisallowedCategories", LookMode.Value);

                    if (AutoArmMod.settings?.debugLogging == true)
                    {
                        AutoArmLogger.Debug($"[SAVE] Saved weapon filter for outfit '{__instance.label}': {allowedWeapons.Count} allowed weapons, {disallowedWeapons.Count} disallowed weapons, {allowedCategories.Count} allowed categories, {disallowedCategories.Count} disallowed categories");
                    }
                }
            }
            // For loading: restore weapon filter states
            else if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                List<string> allowedWeapons = null;
                List<string> disallowedWeapons = null;
                List<string> allowedCategories = null;
                List<string> disallowedCategories = null;

                Scribe_Collections.Look(ref allowedWeapons, "autoArmAllowedWeapons", LookMode.Value);
                Scribe_Collections.Look(ref disallowedWeapons, "autoArmDisallowedWeapons", LookMode.Value);
                Scribe_Collections.Look(ref allowedCategories, "autoArmAllowedCategories", LookMode.Value);
                Scribe_Collections.Look(ref disallowedCategories, "autoArmDisallowedCategories", LookMode.Value);

                // Store the loaded data for later restoration in PostLoadInit
                if (allowedWeapons != null || disallowedWeapons != null || allowedCategories != null || disallowedCategories != null)
                {
                    var data = new OutfitWeaponFilterData
                    {
                        AllowedWeapons = allowedWeapons ?? new List<string>(),
                        DisallowedWeapons = disallowedWeapons ?? new List<string>(),
                        AllowedCategories = allowedCategories ?? new List<string>(),
                        DisallowedCategories = disallowedCategories ?? new List<string>(),
                        HasSavedData = true
                    };
                    pendingFilterRestoration[__instance.GetUniqueLoadID()] = data;

                    if (AutoArmMod.settings?.debugLogging == true)
                    {
                        AutoArmLogger.Debug($"[LOAD] Loaded weapon filter data for outfit '{__instance.label}': {data.AllowedWeapons.Count} allowed weapons, {data.DisallowedWeapons.Count} disallowed weapons, {data.AllowedCategories.Count} allowed categories, {data.DisallowedCategories.Count} disallowed categories");
                    }
                }
            }
            // Post-load: apply the restored weapon filter states
            else if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (pendingFilterRestoration.TryGetValue(__instance.GetUniqueLoadID(), out var data))
                {
                    // Mark this outfit as already configured
                    outfitsWithLoadedWeaponData.Add(__instance.label);

                    // Store the loaded data persistently for future reference
                    loadedWeaponData[__instance.label] = data;

                    // Temporarily move weapons to apparel to make SetAllow work
                    WeaponTabInjector.MoveWeaponsForUI();

                    try
                    {
                        // Apply category states first
                        ApplyCategoryStates(__instance, data);

                        // Then apply individual weapon states (these override category states)
                        foreach (var weaponDefName in data.AllowedWeapons)
                        {
                            var weaponDef = DefDatabase<ThingDef>.GetNamedSilentFail(weaponDefName);
                            if (weaponDef != null && WeaponValidation.IsProperWeapon(weaponDef))
                            {
                                __instance.filter.SetAllow(weaponDef, true);
                            }
                        }

                        // Apply disallowed weapons
                        foreach (var weaponDefName in data.DisallowedWeapons)
                        {
                            var weaponDef = DefDatabase<ThingDef>.GetNamedSilentFail(weaponDefName);
                            if (weaponDef != null && WeaponValidation.IsProperWeapon(weaponDef))
                            {
                                __instance.filter.SetAllow(weaponDef, false);
                            }
                        }

                        if (AutoArmMod.settings?.debugLogging == true)
                        {
                            AutoArmLogger.Debug($"[APPLY] Applied weapon filter for outfit '{__instance.label}': {data.AllowedWeapons.Count} allowed weapons, {data.DisallowedWeapons.Count} disallowed weapons, {data.AllowedCategories.Count} allowed categories, {data.DisallowedCategories.Count} disallowed categories");
                        }
                    }
                    finally
                    {
                        // Always restore weapon position
                        WeaponTabInjector.RestoreWeaponsPosition();
                    }

                    pendingFilterRestoration.Remove(__instance.GetUniqueLoadID());
                }
            }
        }

        public static bool HasLoadedWeaponData(ApparelPolicy outfit)
        {
            return outfit != null && outfitsWithLoadedWeaponData.Contains(outfit.label);
        }

        public static void ClearLoadedDataTracking()
        {
            outfitsWithLoadedWeaponData.Clear();
            pendingFilterRestoration.Clear();
            loadedWeaponData.Clear();
        }

        /// <summary>
        /// Gets the list of all weapons that were in the saved data (both allowed and disallowed)
        /// </summary>
        public static HashSet<string> GetSavedWeaponList(ApparelPolicy outfit)
        {
            if (outfit == null)
                return null;

            // Try to get from persistent storage first
            if (loadedWeaponData.TryGetValue(outfit.label, out var data))
            {
                var result = new HashSet<string>();
                if (data.AllowedWeapons != null)
                    result.UnionWith(data.AllowedWeapons);
                if (data.DisallowedWeapons != null)
                    result.UnionWith(data.DisallowedWeapons);
                return result;
            }

            // Fall back to pending restoration (during load process)
            if (pendingFilterRestoration.TryGetValue(outfit.GetUniqueLoadID(), out data))
            {
                var result = new HashSet<string>();
                if (data.AllowedWeapons != null)
                    result.UnionWith(data.AllowedWeapons);
                if (data.DisallowedWeapons != null)
                    result.UnionWith(data.DisallowedWeapons);
                return result;
            }

            return null;
        }

        /// <summary>
        /// Gets the saved category states for an outfit
        /// </summary>
        public static OutfitWeaponFilterData GetSavedCategoryStates(ApparelPolicy outfit)
        {
            if (outfit == null)
                return null;

            // Try to get from persistent storage first
            if (loadedWeaponData.TryGetValue(outfit.label, out var data))
            {
                return data;
            }

            // Fall back to pending restoration (during load process)
            if (pendingFilterRestoration.TryGetValue(outfit.GetUniqueLoadID(), out data))
            {
                return data;
            }

            return null;
        }

        private static bool IsCategoryAllowed(ThingFilter filter, ThingCategoryDef category)
        {
            // A category is considered "allowed" if it would show as checked in the UI
            // This happens when all (or most) of its child items are allowed
            if (category.childThingDefs == null || category.childThingDefs.Count == 0)
                return true;

            int allowedCount = 0;
            int totalCount = 0;

            foreach (var thingDef in category.childThingDefs)
            {
                if (WeaponValidation.IsProperWeapon(thingDef))
                {
                    totalCount++;
                    if (filter.Allows(thingDef))
                    {
                        allowedCount++;
                    }
                }
            }

            // Consider the category allowed if more than 50% of weapons are allowed
            return totalCount > 0 && (float)allowedCount / totalCount > 0.5f;
        }

        private static void ApplyCategoryStates(ApparelPolicy outfit, OutfitWeaponFilterData data)
        {
            // Apply category states using reflection
            try
            {
                var filterType = outfit.filter.GetType();

                // Try the full SetAllow method signature
                var setCategoryAllowMethod = filterType.GetMethod("SetAllow",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                    null,
                    new Type[] { typeof(ThingCategoryDef), typeof(bool), typeof(List<ThingDef>), typeof(List<SpecialThingFilterDef>), typeof(IEnumerable<ThingDef>) },
                    null);

                if (setCategoryAllowMethod == null)
                {
                    // Try simpler overload
                    setCategoryAllowMethod = filterType.GetMethod("SetAllow",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                        null,
                        new Type[] { typeof(ThingCategoryDef), typeof(bool) },
                        null);
                }

                if (setCategoryAllowMethod != null)
                {
                    // Apply allowed categories
                    foreach (var categoryName in data.AllowedCategories)
                    {
                        var category = DefDatabase<ThingCategoryDef>.GetNamedSilentFail(categoryName);
                        if (category != null)
                        {
                            if (setCategoryAllowMethod.GetParameters().Length == 5)
                            {
                                setCategoryAllowMethod.Invoke(outfit.filter, new object[] { category, true, null, null, null });
                            }
                            else
                            {
                                setCategoryAllowMethod.Invoke(outfit.filter, new object[] { category, true });
                            }

                            if (AutoArmMod.settings?.debugLogging == true)
                            {
                                AutoArmLogger.Debug($"  Restored category '{categoryName}' as ALLOWED");
                            }
                        }
                    }

                    // Apply disallowed categories
                    foreach (var categoryName in data.DisallowedCategories)
                    {
                        var category = DefDatabase<ThingCategoryDef>.GetNamedSilentFail(categoryName);
                        if (category != null)
                        {
                            if (setCategoryAllowMethod.GetParameters().Length == 5)
                            {
                                setCategoryAllowMethod.Invoke(outfit.filter, new object[] { category, false, null, null, null });
                            }
                            else
                            {
                                setCategoryAllowMethod.Invoke(outfit.filter, new object[] { category, false });
                            }

                            if (AutoArmMod.settings?.debugLogging == true)
                            {
                                AutoArmLogger.Debug($"  Restored category '{categoryName}' as DISALLOWED");
                            }
                        }
                    }
                }
                else
                {
                    // Fallback: manually set all weapons in the categories
                    foreach (var categoryName in data.DisallowedCategories)
                    {
                        var category = DefDatabase<ThingCategoryDef>.GetNamedSilentFail(categoryName);
                        if (category != null && category.childThingDefs != null)
                        {
                            foreach (var thingDef in category.childThingDefs)
                            {
                                if (WeaponValidation.IsProperWeapon(thingDef))
                                {
                                    outfit.filter.SetAllow(thingDef, false);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                AutoArmLogger.Debug($"Could not apply category states via reflection: {e.Message}");
            }
        }

        public class OutfitWeaponFilterData
        {
            public List<string> AllowedWeapons;
            public List<string> DisallowedWeapons;
            public List<string> AllowedCategories;
            public List<string> DisallowedCategories;
            public bool HasSavedData;
        }
    }
}