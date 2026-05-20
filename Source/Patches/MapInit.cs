
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

                if (__instance.mapPawns?.FreeColonistsSpawned != null)
                {
                    foreach (var pawn in __instance.mapPawns.FreeColonistsSpawned)
                    {
                        if (pawn != null && !pawn.Dead && !pawn.Downed
                            && pawn.health?.capacities != null && pawn.skills != null)
                        {
                            WeaponCache.PreWarmColonistScore(pawn, true);
                            WeaponCache.PreWarmColonistScore(pawn, false);
                        }
                    }
                }

                AutoArmLogger.Debug(() => $"Ensured weapon cache exists and pre-warmed for map {__instance.uniqueID} after save load");
            }
            catch (Exception e) { AutoArmLogger.ErrorPatch(e, "Map_FinalizeLoading_Postfix"); }
        }
    }
}
