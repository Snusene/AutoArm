// AutoArm RimWorld 1.5+ mod - automatic weapon management
// This file: Patches for map initialization and cleanup
// Purpose: Initialize weapon cache when maps load, clean up when destroyed

using AutoArm.Caching;
using AutoArm.Logging;
using HarmonyLib;
using Verse;

namespace AutoArm.Patches
{
    /// <summary>
    /// Initialize weapon cache when a map is created or loaded
    /// </summary>
    [HarmonyPatch(typeof(Map), "FinalizeInit")]
    public static class Map_FinalizeInit_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Map __instance)
        {
            // Skip if mod disabled
            if (AutoArmMod.settings?.modEnabled != true)
                return;
                
            if (__instance == null)
                return;

            // Initialize the weapon cache for this map
            // Since we track all changes in real-time, we only need to populate
            // the cache once when the map loads
            ImprovedWeaponCacheManager.InitializeCacheForMap(__instance);
            
            if (AutoArmMod.settings?.debugLogging == true)
            {
                AutoArmLogger.Debug($"[CACHE] Initialized weapon cache for map {__instance.uniqueID} on map load");
            }
        }
    }

    /// <summary>
    /// Clean up weapon cache when a map is destroyed
    /// </summary>
    [HarmonyPatch(typeof(Map), "FinalizeLoading")]
    public static class Map_FinalizeLoading_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Map __instance)
        {
            // Skip if mod disabled
            if (AutoArmMod.settings?.modEnabled != true)
                return;
                
            if (__instance == null)
                return;

            // Also initialize after loading a save game
            ImprovedWeaponCacheManager.InitializeCacheForMap(__instance);
            
            if (AutoArmMod.settings?.debugLogging == true)
            {
                AutoArmLogger.Debug($"[CACHE] Initialized weapon cache for map {__instance.uniqueID} after save load");
            }
        }
    }

    /// <summary>
    /// Clean up when map is removed
    /// </summary>
    [HarmonyPatch(typeof(Game), "DeinitAndRemoveMap")]
    public static class Game_DeinitAndRemoveMap_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(Map map)
        {
            // Always clean up to prevent memory leaks, even if mod disabled
            if (map == null)
                return;

            // Clear the cache for this map
            ImprovedWeaponCacheManager.ClearCacheForMap(map);
            
            if (AutoArmMod.settings?.debugLogging == true)
            {
                AutoArmLogger.Debug($"[CACHE] Cleared weapon cache for destroyed map {map.uniqueID}");
            }
        }
    }

    /// <summary>
    /// Periodic cleanup of destroyed maps (safety net)
    /// </summary>
    [HarmonyPatch(typeof(Game), "UpdatePlay")]
    public static class Game_UpdatePlay_Patch
    {
        private static int lastCleanupTick = 0;
        private const int CleanupInterval = 60000; // Every 1000 seconds

        [HarmonyPostfix]
        public static void Postfix()
        {
            // Skip if mod disabled
            if (AutoArmMod.settings?.modEnabled != true)
                return;
                
            int currentTick = Find.TickManager.TicksGame;
            if (currentTick - lastCleanupTick > CleanupInterval)
            {
                lastCleanupTick = currentTick;
                ImprovedWeaponCacheManager.CleanupDestroyedMaps();
            }
        }
    }
}
