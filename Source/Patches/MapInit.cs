
using AutoArm.Caching;
using HarmonyLib;
using System;
using Verse;

namespace AutoArm.Patches
{
    [HarmonyPatch(typeof(Map), "FinalizeLoading")]
    [HarmonyPatchCategory(PatchCategories.Core)]
    internal static class Map_FinalizeLoading_Patch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Low)]
        public static void Postfix(Map __instance)
        {
            try
            {
                if (__instance == null)
                    return;

                WeaponCache.ForceReinitialize(__instance);

                AutoArmLogger.Debug(() => $"Ensured weapon cache exists for map {__instance.uniqueID} after save load");
            }
            catch (Exception e) { AutoArmLogger.ErrorPatch(e, "Map_FinalizeLoading_Postfix"); }
        }
    }
}
