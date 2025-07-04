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
            if (apparel != null && weapons != null && !apparel.childCategories.Contains(weapons))
            {
                apparel.childCategories.Add(weapons);
                Log.Message("[AutoArm] Weapons injected as a child of Apparel.");
            }
        }
    }
}
