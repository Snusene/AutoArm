
using AutoArm.Caching;
using AutoArm.Compatibility;
using AutoArm.UI;
using HarmonyLib;
using RimWorld;
using System;
using Verse;

namespace AutoArm.Patches
{
    internal static class OutfitCacheInvalidator
    {
        internal static void Invalidate(string source)
        {
            try
            {
                ThingFilter_Allows_Thing_Patch.InvalidateCache();
                OutfitFilterCache.RebuildCache();
            }
            catch (Exception e)
            {
                AutoArmLogger.ErrorPatch(e, source);
            }
        }
    }

    [HarmonyPatch(typeof(OutfitDatabase), "MakeNewOutfit")]
    [HarmonyPatchCategory(PatchCategories.UI)]
    internal static class OutfitDatabase_MakeNewOutfit_Patch
    {
        [HarmonyPostfix]
        public static void Postfix() => OutfitCacheInvalidator.Invalidate("OutfitDatabase_MakeNewOutfit");
    }

    [HarmonyPatch(typeof(OutfitDatabase), "TryDelete")]
    [HarmonyPatchCategory(PatchCategories.UI)]
    internal static class OutfitDatabase_TryDelete_Patch
    {
        [HarmonyPostfix]
        public static void Postfix() => OutfitCacheInvalidator.Invalidate("OutfitDatabase_TryDelete");
    }

    [HarmonyPatch(typeof(Dialog_ModSettings), "PreClose")]
    [HarmonyPatchCategory(PatchCategories.UI)]
    internal static class Dialog_ModSettings_PreClose_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Mod ___mod)
        {
            try
            {
                if (!(___mod is AutoArmMod)) return;

                SimpleSidearmsCompat.InvalidateAllCaches();
                WeaponCache.ClearScoreCache();
                PawnValidation.ClearCache();
                AutoArm.UI.StatusOverviewDataGatherer.ClearTopWeaponsCache();

                AutoArmLogger.Debug(() => "[Settings] Invalidated all caches after AutoArm settings closed");
            }
            catch (Exception e)
            {
                AutoArmLogger.ErrorPatch(e, "Dialog_ModSettings_PostClose");
            }
        }
    }
}
