// AutoArm RimWorld 1.5+ mod - automatic weapon management
// This file: Runtime weapon category injection for outfit filtering
// Merged version combining best features from all versions

using AutoArm.Logging;
using AutoArm.Weapons;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace AutoArm.UI
{
    [StaticConstructorOnStartup]
    public static class WeaponTabInjector
    {
        // Track which mod moved weapons for debugging
        private static string weaponsMovedBy = null;

        static WeaponTabInjector()
        {
            try
            {
                var apparel = DefDatabase<ThingCategoryDef>.GetNamedSilentFail("Apparel");
                var weapons = DefDatabase<ThingCategoryDef>.GetNamedSilentFail("Weapons");
                var root = DefDatabase<ThingCategoryDef>.GetNamedSilentFail("Root");

                if (apparel == null || weapons == null)
                {
                    AutoArmLogger.Error("CRITICAL: Could not find Apparel or Weapons categories - weapon filtering will not work!");
                    return;
                }

                // Check if another mod already moved weapons
                bool alreadyMoved = weapons.parent == apparel;
                if (alreadyMoved)
                {
                    try
                    {
                        // Get list of active mods that might interact with weapons/categories
                        var suspectMods = GetModsThatMightMoveWeapons();

                        if (suspectMods.Count > 0)
                        {
                            // Take the first suspect mod (likely loaded before us)
                            weaponsMovedBy = suspectMods.First();

                            if (suspectMods.Count > 1)
                            {
                                AutoArmLogger.Debug($"Multiple mods might have moved weapons: {string.Join(", ", suspectMods)}");
                            }
                        }
                        else
                        {
                            weaponsMovedBy = "Unknown mod";
                        }
                    }
                    catch (Exception e)
                    {
                        AutoArmLogger.Error("Failed to detect which mod moved weapons", e);
                        weaponsMovedBy = "Unknown mod (detection failed)";
                    }

                    AutoArmLogger.Debug($"Weapons already under Apparel (likely moved by {weaponsMovedBy})");
                }

                // Always ensure weapons are under apparel
                if (root != null && root.childCategories.Contains(weapons))
                {
                    root.childCategories.Remove(weapons);
                }

                if (!apparel.childCategories.Contains(weapons))
                {
                    apparel.childCategories.Add(weapons);
                    weapons.parent = apparel;
                    weaponsMovedBy = "AutoArm";

                    Log.Message("<color=#4287f5>[AutoArm]</color> Weapons moved under Apparel category");
                }
                else
                {
                    // Already under apparel, but ensure it's properly set up
                    weapons.parent = apparel;
                }

                // Validate the move was successful
                if (weapons.parent != apparel)
                {
                    AutoArmLogger.Error($"CRITICAL: Failed to properly set weapons under apparel! Parent is: {weapons.parent?.defName ?? "null"}");
                    AutoArmLogger.Error("Weapon outfit filtering will not work correctly!");
                }

                // Log weapon counts for debugging
                try
                {
                    int rangedCount = WeaponValidation.RangedWeapons.Count;
                    int meleeCount = WeaponValidation.MeleeWeapons.Count;
                    AutoArmLogger.Debug($"Found {rangedCount} ranged and {meleeCount} melee weapon definitions");
                    AutoArmLogger.Debug($"Weapons category setup complete. Moved by: {weaponsMovedBy}");
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

        private static List<string> GetModsThatMightMoveWeapons()
        {
            var suspectMods = new List<string>();

            try
            {
                // Get all active mods
                var activeMods = ModLister.AllInstalledMods.Where(m => m.Active);

                // Keywords that suggest a mod might interact with weapons/categories
                var weaponKeywords = new[] {
                    "weapon", "sidearm", "equip", "loadout", "outfit",
                    "apparel", "gear", "inventory", "combat", "armory",
                    "arsenal", "equipment"
                };

                foreach (var mod in activeMods)
                {
                    try
                    {
                        // Skip AutoArm itself
                        if (mod.PackageIdPlayerFacing?.IndexOf("autoarm", StringComparison.OrdinalIgnoreCase) >= 0)
                            continue;

                        // Check both package ID and name for keywords
                        string modIdentifier = mod.PackageIdPlayerFacing?.ToLower() ?? "";
                        string modName = mod.Name?.ToLower() ?? "";

                        bool mightInteract = weaponKeywords.Any(keyword =>
                            modIdentifier.Contains(keyword) || modName.Contains(keyword));

                        if (mightInteract)
                        {
                            // Use PackageId if available, otherwise use Name
                            string identifier = !string.IsNullOrEmpty(mod.PackageIdPlayerFacing)
                                ? mod.PackageIdPlayerFacing
                                : mod.Name ?? "Unknown";

                            suspectMods.Add(identifier);
                        }
                    }
                    catch (Exception e)
                    {
                        // Log but continue checking other mods
                        AutoArmLogger.Debug($"Error checking mod {mod.Name}: {e.Message}");
                    }
                }

                // Sort by load order (mods loaded before AutoArm are more likely suspects)
                try
                {
                    // Get AutoArm's position in active mods
                    var activeModsInOrder = ModsConfig.ActiveModsInLoadOrder.ToList();
                    var autoArmIndex = activeModsInOrder.FindIndex(m =>
                        m.PackageIdPlayerFacing?.IndexOf("autoarm", StringComparison.OrdinalIgnoreCase) >= 0);

                    if (autoArmIndex >= 0)
                    {
                        // Only include mods that loaded before AutoArm
                        suspectMods = suspectMods
                            .Where(modId =>
                            {
                                var modIndex = activeModsInOrder.FindIndex(m =>
                                    m.PackageIdPlayerFacing == modId || m.Name == modId);
                                return modIndex >= 0 && modIndex < autoArmIndex;
                            })
                            .ToList();
                    }
                }
                catch (Exception e)
                {
                    // If load order sorting fails, just return unsorted list
                    AutoArmLogger.Debug($"Failed to sort mods by load order: {e.Message}");
                }
            }
            catch (Exception e)
            {
                AutoArmLogger.Error("Failed to get mods that might move weapons", e);
            }

            return suspectMods;
        }
    }

    // Force the tree node database to rebuild after we move categories
    [HarmonyPatch(typeof(ThingCategoryNodeDatabase), "FinalizeInit")]
    public static class ThingCategoryNodeDatabase_FinalizeInit_Patch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            try
            {
                var weapons = DefDatabase<ThingCategoryDef>.GetNamedSilentFail("Weapons");
                var apparel = DefDatabase<ThingCategoryDef>.GetNamedSilentFail("Apparel");

                if (weapons != null && apparel != null && weapons.parent == apparel)
                {
                    // Force the tree node to be properly set up
                    if (weapons.treeNode == null)
                    {
                        AutoArmLogger.Warn("Weapons tree node was null after finalization!");

                        // The tree node being null is a serious issue but we can't force a rebuild
                        // The game will handle this internally
                        AutoArmLogger.Warn("Category tree structure may not display correctly in UI");
                    }
                    else
                    {
                        weapons.treeNode.SetOpen(-1, true);
                        AutoArmLogger.Debug("Weapons tree node finalized under apparel");
                    }
                }
            }
            catch (Exception e)
            {
                AutoArmLogger.Error("Failed in ThingCategoryNodeDatabase finalization", e);
            }
        }
    }

    [HarmonyPatch(typeof(Game), "LoadGame")]
    public static class Game_LoadGame_AddWeaponsToOutfits_Patch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            try
            {
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
                var weaponsCat = DefDatabase<ThingCategoryDef>.GetNamedSilentFail("Weapons");
                if (weaponsCat == null || Current.Game?.outfitDatabase == null)
                    return;

                int outfitsModified = 0;
                int outfitsSkipped = 0;

                foreach (var outfit in Current.Game.outfitDatabase.AllOutfits)
                {
                    try
                    {
                        if (outfit.filter != null)
                        {
                            // IMPORTANT: Use WeaponValidation.AllWeapons which properly filters out problematic items
                            bool alreadyHasWeapons = WeaponValidation.AllWeapons
                                .Any(weapon => outfit.filter.Allows(weapon));

                            if (!alreadyHasWeapons)
                            {
                                // This is a fresh outfit with no weapon configuration
                                
                                // Smart detection: Check if outfit allows most apparel (indicating a general-purpose outfit)
                                bool allowsMostApparel = false;
                                try
                                {
                                    // Get all apparel defs (excluding weapons since we moved them)
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
                                bool isNudist = outfit.label.IndexOf("nudist", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                               outfit.label.IndexOf("nude", StringComparison.OrdinalIgnoreCase) >= 0;
                                
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
                                bool shouldHaveWeapons = !isNudist && (allowsMostApparel || hasWeaponKeyword);

                                if (shouldHaveWeapons)
                                {
                                    outfit.filter.SetAllow(weaponsCat, true);
                                    outfitsModified++;
                                    AutoArmLogger.Debug($"Added default weapons to new outfit: {outfit.label}");
                                }
                                else if (isNudist)
                                {
                                    AutoArmLogger.Debug($"Skipped adding weapons to nudist outfit: {outfit.label}");
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

                // Log results based on what happened
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
            catch (Exception e)
            {
                AutoArmLogger.Error("Failed to add weapons to outfits", e);
            }
        }
    }
}