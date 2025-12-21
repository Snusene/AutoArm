
using AutoArm.Caching;
using AutoArm.Logging;
using HarmonyLib;
using Verse;

namespace AutoArm.Patches
{
    /// <summary>
    /// Init cache
    /// </summary>
    [HarmonyPatch(typeof(Map), "FinalizeInit")]
    [HarmonyPatchCategory(PatchCategories.Core)]
    public static class Map_FinalizeInit_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Map __instance)
        {
            if (__instance == null)
                return;

            WeaponCacheManager.Initialize(__instance);

            if (__instance.mapPawns?.FreeColonistsSpawned != null)
            {
                int colonistCount = 0;
                foreach (var pawn in __instance.mapPawns.FreeColonistsSpawned)
                {
                    if (pawn != null && !pawn.Dead && !pawn.Downed)
                    {
                        WeaponCacheManager.PreWarmColonistScore(pawn, true);
                        WeaponCacheManager.PreWarmColonistScore(pawn, false);
                        colonistCount++;
                    }
                }
                if (colonistCount > 0)
                {
                    AutoArmLogger.Debug(() => $"Pre-warmed skill caches for {colonistCount} colonists");
                }
            }

            AutoArmLogger.Debug(() => $"Initialized and pre-warmed weapon cache for map {__instance.uniqueID} on map creation/load");
        }
    }

    /// <summary>
    /// Clean cache on destroy
    /// </summary>
    [HarmonyPatch(typeof(Map), "FinalizeLoading")]
    [HarmonyPatchCategory(PatchCategories.Core)]
    public static class Map_FinalizeLoading_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Map __instance)
        {
            if (__instance == null)
                return;

            WeaponCacheManager.ForceReinitialize(__instance);

            if (__instance.mapPawns?.FreeColonistsSpawned != null)
            {
                foreach (var pawn in __instance.mapPawns.FreeColonistsSpawned)
                {
                    if (pawn != null && !pawn.Dead && !pawn.Downed)
                    {
                        WeaponCacheManager.PreWarmColonistScore(pawn, true);
                        WeaponCacheManager.PreWarmColonistScore(pawn, false);
                    }
                }
            }

            AutoArmLogger.Debug(() => $"Ensured weapon cache exists and pre-warmed for map {__instance.uniqueID} after save load");
        }
    }

    /// <summary>
    /// Cleanup on remove
    /// </summary>
    [HarmonyPatch(typeof(Game), "DeinitAndRemoveMap")]
    [HarmonyPatchCategory(PatchCategories.Core)]
    public static class Game_DeinitAndRemoveMap_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(Map map)
        {
            if (map == null)
                return;

            WeaponCacheManager.ClearCacheForMap(map);

            AutoArmLogger.Debug(() => $"Cleared weapon cache for destroyed map {map.uniqueID}");
        }
    }

    /// <summary>
    /// Periodic cleanup
    /// </summary>
    [HarmonyPatch(typeof(Game), "UpdatePlay")]
    [HarmonyPatchCategory(PatchCategories.Core)]
    public static class Game_UpdatePlay_Patch
    {
        private static int lastCleanupTick = 0;
        private const int CleanupInterval = 60000;

        [HarmonyPostfix]
        public static void Postfix()
        {
            if (AutoArmMod.settings?.modEnabled != true)
                return;

            int currentTick = Find.TickManager.TicksGame;

            if (currentTick - lastCleanupTick > CleanupInterval)
            {
                lastCleanupTick = currentTick;
                WeaponCacheManager.CleanupDestroyedMaps();
            }
        }
    }
}
