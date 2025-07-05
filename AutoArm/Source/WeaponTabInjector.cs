using HarmonyLib;
using Verse;
using RimWorld;

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
            if (root != null && weapons != null && root.childCategories.Contains(weapons))
            {
                root.childCategories.Remove(weapons);
            }
            if (apparel != null && weapons != null && !apparel.childCategories.Contains(weapons))
            {
                apparel.childCategories.Add(weapons);
                weapons.parent = apparel;
                Log.Message("[AutoArm] Weapons injected as a child of Apparel.");
            }
        }
    }
}