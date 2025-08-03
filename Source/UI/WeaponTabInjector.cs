// AutoArm RimWorld 1.5+ mod - automatic weapon management
// This file: Runtime weapon category injection for outfit filtering
// Moves weapons under apparel category for UI integration

using HarmonyLib;
using System;
using System.Linq;
using Verse;
using AutoArm.Helpers;
using AutoArm.Logging;
using AutoArm.Definitions;
using AutoArm.UI;
using AutoArm.Weapons;

namespace AutoArm.UI
{
    [StaticConstructorOnStartup]
    public static class WeaponTabInjector
    {
        static WeaponTabInjector()
        {
            var apparel = DefDatabase<ThingCategoryDef>.GetNamedSilentFail("Apparel");
            var weapons = DefDatabase<ThingCategoryDef>.GetNamedSilentFail("Weapons");
            var root = DefDatabase<ThingCategoryDef>.GetNamedSilentFail("Root");

            if (apparel == null || weapons == null)
            {
                Log.Error("[AutoArm] Could not find Apparel or Weapons categories");
                return;
            }

            // ALWAYS move weapons under apparel - this is AutoArm's responsibility
            // Remove any checks for SimpleSidearms
            if (root != null && root.childCategories.Contains(weapons))
            {
                root.childCategories.Remove(weapons);
            }

            if (!apparel.childCategories.Contains(weapons))
            {
                apparel.childCategories.Add(weapons);
                weapons.parent = apparel;

                Log.Message("<color=#4287f5>[AutoArm]</color> Weapons moved under Apparel category");
            }
            else
            {
                // Already under apparel, but ensure it's properly set up
                weapons.parent = apparel;
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.Debug("Weapons already under Apparel category - ensuring proper setup");
                }
            }

            if (AutoArmMod.settings?.debugLogging == true)
            {
                int rangedCount = WeaponValidation.RangedWeapons.Count;
                int meleeCount = WeaponValidation.MeleeWeapons.Count;
                AutoArmLogger.Debug($"Found {rangedCount} ranged and {meleeCount} melee weapon definitions");
            }
        }
    }

    // Force the tree node database to rebuild after we move categories
    [HarmonyPatch(typeof(ThingCategoryNodeDatabase), "FinalizeInit")]
    public static class ThingCategoryNodeDatabase_FinalizeInit_Patch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            // Remove the SimpleSidearms check - always ensure weapons are properly set up
            var weapons = DefDatabase<ThingCategoryDef>.GetNamedSilentFail("Weapons");
            var apparel = DefDatabase<ThingCategoryDef>.GetNamedSilentFail("Apparel");

            if (weapons != null && apparel != null && weapons.parent == apparel)
            {
                // Force the tree node to be properly set up
                if (weapons.treeNode == null)
                {
                    Log.Warning("[AutoArm] Weapons tree node was null after finalization!");
                }
                else
                {
                    weapons.treeNode.SetOpen(-1, true);
                    if (AutoArmMod.settings?.debugLogging == true)
                    {
                        AutoArmLogger.Debug("Weapons tree node finalized under apparel");
                    }
                }
            }
        }
    }

    [HarmonyPatch(typeof(Game), "LoadGame")]
    public static class Game_LoadGame_AddWeaponsToOutfits_Patch
    {
        [HarmonyPostfix]
        public static void Postfix()
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
    }

    [HarmonyPatch(typeof(Game), "InitNewGame")]
    public static class Game_InitNewGame_AddWeaponsToOutfits_Patch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            OutfitWeaponHelper.AddWeaponsToOutfits();
        }
    }

    public static class OutfitWeaponHelper
    {
        public static void AddWeaponsToOutfits()
        {
            var weaponsCat = DefDatabase<ThingCategoryDef>.GetNamedSilentFail("Weapons");
            if (weaponsCat == null || Current.Game?.outfitDatabase == null)
                return;

            foreach (var outfit in Current.Game.outfitDatabase.AllOutfits)
            {
                if (outfit.filter != null)
                {
                    // SIMPLE FIX: Only add weapons if the filter doesn't already allow ANY weapons
                    bool alreadyHasWeapons = DefDatabase<ThingDef>.AllDefs
                        .Where(d => d.IsWeapon)
                        .Any(weapon => outfit.filter.Allows(weapon));

                    if (!alreadyHasWeapons)
                    {
                        // This is a fresh outfit with no weapon configuration
                        bool shouldHaveWeapons = outfit.label.IndexOf("anything", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                outfit.label.IndexOf("soldier", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                outfit.label.IndexOf("worker", StringComparison.OrdinalIgnoreCase) >= 0;

                        if (shouldHaveWeapons)
                        {
                            outfit.filter.SetAllow(weaponsCat, true);
                            if (AutoArmMod.settings?.debugLogging == true)
                            {
                                AutoArmLogger.Debug($"Added default weapons to new outfit: {outfit.label}");
                            }
                        }
                    }
                }
            }
        }
    }
}