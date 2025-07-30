using HarmonyLib;
using System;
using System.Linq;
using Verse;

namespace AutoArm
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
                Log.Message("<color=#4287f5>[AutoArm]</color> Weapons already under Apparel category - ensuring proper setup");
            }

            int rangedCount = WeaponThingFilterUtility.RangedWeapons.Count;
            int meleeCount = WeaponThingFilterUtility.MeleeWeapons.Count;
            Log.Message($"<color=#4287f5>[AutoArm]</color> Found {rangedCount} ranged and {meleeCount} melee weapon definitions");
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
                    Log.Message("[AutoArm] Weapons tree node finalized under apparel");
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
                            Log.Message($"[AutoArm] Added default weapons to new outfit: {outfit.label}");
                        }
                    }
                }
            }
        }
    }
}