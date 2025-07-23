using Verse;
using RimWorld;
using HarmonyLib;
using System.Linq;

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

            int rangedCount = WeaponThingFilterUtility.RangedWeapons.Count;
            int meleeCount = WeaponThingFilterUtility.MeleeWeapons.Count;
            Log.Message($"<color=#4287f5>[AutoArm]</color> Found {rangedCount} ranged and {meleeCount} melee weapon definitions");
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
                    bool shouldHaveWeapons = outfit.label.ToLower().Contains("anything") ||
                                           outfit.label.ToLower().Contains("soldier") ||
                                           outfit.label.ToLower().Contains("worker");

                    if (shouldHaveWeapons)
                    {
                        outfit.filter.SetAllow(weaponsCat, true);
                    }
                }
            }
        }
    }
}