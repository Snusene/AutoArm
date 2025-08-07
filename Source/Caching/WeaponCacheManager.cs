// AutoArm RimWorld 1.5+ mod - automatic weapon management
// This file: Simplified weapon cache with real-time tracking
// 
// DESIGN PRINCIPLE: Since we track EVERY weapon state change in real-time
// via Harmony patches, the cache is ALWAYS up-to-date. No rebuilds needed!
//
// The only times we need to populate the cache:
// 1. Initial map load (new game or loading save)
// 2. Manual rebuild for debugging/recovery (dev mode only)

using AutoArm.Definitions;
using AutoArm.Logging;
using AutoArm.Testing;
using AutoArm.Weapons;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace AutoArm.Caching
{
    public static class ImprovedWeaponCacheManager
    {
        public enum WeaponCategory
        {
            MeleeBasic,      // Low quality melee
            MeleeAdvanced,   // High quality melee
            RangedShort,     // < 20 range
            RangedMedium,    // 20-35 range
            RangedLong       // > 35 range
        }

        private class WeaponCacheEntry
        {
            public HashSet<ThingWithComps> Weapons { get; set; }
            public Dictionary<IntVec3, List<ThingWithComps>> SpatialIndex { get; set; }
            public Dictionary<ThingWithComps, IntVec3> WeaponToGrid { get; set; }
            public Dictionary<WeaponCategory, List<ThingWithComps>> CategorizedWeapons { get; set; }
            public int LastChangeDetectedTick { get; set; }

            public WeaponCacheEntry()
            {
                Weapons = new HashSet<ThingWithComps>();
                SpatialIndex = new Dictionary<IntVec3, List<ThingWithComps>>();
                WeaponToGrid = new Dictionary<ThingWithComps, IntVec3>();
                CategorizedWeapons = new Dictionary<WeaponCategory, List<ThingWithComps>>();
                LastChangeDetectedTick = 0;

                // Initialize categories
                foreach (WeaponCategory cat in Enum.GetValues(typeof(WeaponCategory)))
                {
                    CategorizedWeapons[cat] = new List<ThingWithComps>();
                }
            }
        }

        private static Dictionary<Map, WeaponCacheEntry> weaponCache = new Dictionary<Map, WeaponCacheEntry>();

        // Object pools to reduce allocations
        private static Stack<HashSet<ThingWithComps>> weaponSetPool = new Stack<HashSet<ThingWithComps>>();
        private static Stack<List<ThingWithComps>> weaponListPool = new Stack<List<ThingWithComps>>();

        // Tracking for potential memory leaks
        private static Dictionary<Map, int> cacheHighWaterMark = new Dictionary<Map, int>();
        private static Dictionary<Map, int> lastCacheSizeLogTick = new Dictionary<Map, int>();
        private const int CacheSizeLogInterval = 6000; // Log every 100 seconds
        private const float CacheGrowthWarningThreshold = 1.5f; // Warn if cache grows 50% beyond high water mark

        /// <summary>
        /// Add a weapon to the cache in real-time
        /// Called by Harmony patches when weapons spawn or change state
        /// </summary>
        public static void AddWeaponToCache(ThingWithComps weapon)
        {
            if (weapon?.Map == null || !IsValidWeapon(weapon))
                return;

            var cache = GetOrCreateCache(weapon.Map);
            
            if (cache.Weapons.Count >= Constants.MaxWeaponsPerCache)
            {
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.Warn($"Weapon cache full ({Constants.MaxWeaponsPerCache} weapons) for map {weapon.Map.uniqueID}");
                }
                return;
            }

            if (cache.Weapons.Contains(weapon))
                return;

            // Only warn if cache is getting full
            if (cache.Weapons.Count >= Constants.MaxWeaponsPerCache * 0.9f)
            {
                AutoArmLogger.Warn($"Weapon cache approaching limit ({cache.Weapons.Count}/{Constants.MaxWeaponsPerCache}) for map {weapon.Map.uniqueID}");
            }

            cache.Weapons.Add(weapon);

            // For weapons in containers, use the container's position
            IntVec3 position = weapon.Position;
            if (weapon.ParentHolder is Building building && building.Position.IsValid)
            {
                position = building.Position;
            }

            var gridPos = GetGridPosition(position);
            
            if (!cache.SpatialIndex.ContainsKey(gridPos))
            {
                cache.SpatialIndex[gridPos] = new List<ThingWithComps>();
            }
            cache.SpatialIndex[gridPos].Add(weapon);
            cache.WeaponToGrid[weapon] = gridPos;

            // Categorize the weapon
            var category = GetWeaponCategory(weapon);
            cache.CategorizedWeapons[category].Add(weapon);
            
            cache.LastChangeDetectedTick = Find.TickManager.TicksGame;
            
            // Track cache growth for leak detection
            TrackCacheSize(weapon.Map, cache);
        }

        /// <summary>
        /// Remove a weapon from the cache in real-time
        /// Called by Harmony patches when weapons despawn or become unavailable
        /// </summary>
        public static void RemoveWeaponFromCache(ThingWithComps weapon)
        {
            if (weapon?.Map == null)
                return;

            if (!weaponCache.TryGetValue(weapon.Map, out var cache))
                return;

            if (cache.Weapons.Remove(weapon))
            {
                // Remove from spatial index
                if (cache.WeaponToGrid.TryGetValue(weapon, out var gridPos))
                {
                    if (cache.SpatialIndex.TryGetValue(gridPos, out var list))
                    {
                        list.Remove(weapon);
                        if (list.Count == 0)
                        {
                            cache.SpatialIndex.Remove(gridPos);
                        }
                    }
                    cache.WeaponToGrid.Remove(weapon);
                }

                // Remove from category
                var category = GetWeaponCategory(weapon);
                cache.CategorizedWeapons[category].Remove(weapon);

                cache.LastChangeDetectedTick = Find.TickManager.TicksGame;
            }
        }

        /// <summary>
        /// Update weapon position in real-time
        /// Called by Harmony patches when weapons move
        /// </summary>
        public static void UpdateWeaponPosition(ThingWithComps weapon, IntVec3 oldPos, IntVec3 newPos)
        {
            if (weapon?.Map == null || oldPos == newPos)
                return;

            if (!weapon.Spawned || weapon.Destroyed)
                return;

            if (!weaponCache.TryGetValue(weapon.Map, out var cache))
                return;

            if (!cache.Weapons.Contains(weapon))
                return;

            var oldGridPos = GetGridPosition(oldPos);
            var newGridPos = GetGridPosition(newPos);

            if (oldGridPos == newGridPos)
                return;

            // Remove from old position
            if (cache.SpatialIndex.TryGetValue(oldGridPos, out var oldList))
            {
                oldList.Remove(weapon);
                if (oldList.Count == 0)
                {
                    cache.SpatialIndex.Remove(oldGridPos);
                }
            }

            // Add to new position
            if (!cache.SpatialIndex.ContainsKey(newGridPos))
            {
                cache.SpatialIndex[newGridPos] = new List<ThingWithComps>();
            }
            cache.SpatialIndex[newGridPos].Add(weapon);
            cache.WeaponToGrid[weapon] = newGridPos;
            
            cache.LastChangeDetectedTick = Find.TickManager.TicksGame;
        }

        /// <summary>
        /// Mark cache as changed (for state changes that don't add/remove weapons)
        /// </summary>
        public static void MarkCacheAsChanged(Map map)
        {
            if (map == null) return;
            
            if (weaponCache.TryGetValue(map, out var cache))
            {
                cache.LastChangeDetectedTick = Find.TickManager.TicksGame;
            }
        }

        /// <summary>
        /// Track cache size and warn about potential memory leaks
        /// </summary>
        private static void TrackCacheSize(Map map, WeaponCacheEntry cache)
        {
            int currentSize = cache.Weapons.Count;
            int currentTick = Find.TickManager.TicksGame;
            
            // Update high water mark
            if (!cacheHighWaterMark.ContainsKey(map))
                cacheHighWaterMark[map] = currentSize;
            else if (currentSize > cacheHighWaterMark[map])
                cacheHighWaterMark[map] = currentSize;
            
            // Check if we should check for memory leaks
            if (!lastCacheSizeLogTick.ContainsKey(map))
                lastCacheSizeLogTick[map] = currentTick;
            
            if (currentTick - lastCacheSizeLogTick[map] > CacheSizeLogInterval)
            {
                lastCacheSizeLogTick[map] = currentTick;
                
                // Check for potential memory leak
                if (currentSize > cacheHighWaterMark[map] * CacheGrowthWarningThreshold)
                {
                    AutoArmLogger.Warn($"[MEMORY LEAK?] Cache for map {map.uniqueID} has grown to {currentSize} weapons (50% above previous high of {cacheHighWaterMark[map]})");
                    AutoArmLogger.Warn($"This might indicate we're missing weapon removal events. Please report this.");
                    
                    // Validate cache contents
                    ValidateCacheContents(map, cache);
                }
            }
        }

        /// <summary>
        /// Validate cache contents to detect stale entries
        /// </summary>
        private static void ValidateCacheContents(Map map, WeaponCacheEntry cache)
        {
            if (AutoArmMod.settings?.debugLogging != true)
                return;

            int staleCount = 0;
            int destroyedCount = 0;
            int wrongMapCount = 0;
            int notSpawnedCount = 0;
            var staleWeapons = new List<ThingWithComps>();
            
            foreach (var weapon in cache.Weapons)
            {
                bool isStale = false;
                string reason = "";
                
                if (weapon == null)
                {
                    isStale = true;
                    reason = "null";
                }
                else if (weapon.Destroyed)
                {
                    isStale = true;
                    reason = "destroyed";
                    destroyedCount++;
                }
                else if (!weapon.Spawned)
                {
                    isStale = true;
                    reason = "not spawned";
                    notSpawnedCount++;
                }
                else if (weapon.Map != map)
                {
                    isStale = true;
                    reason = $"wrong map (expected {map?.uniqueID ?? -1}, got {weapon.Map?.uniqueID ?? -1})";
                    wrongMapCount++;
                }
                
                if (isStale)
                {
                    staleCount++;
                    staleWeapons.Add(weapon);
                    AutoArmLogger.Warn($"[STALE CACHE ENTRY] {weapon?.Label ?? "null"} at {weapon?.Position ?? IntVec3.Invalid} - {reason}");
                }
            }
            
            if (staleCount > 0)
            {
                AutoArmLogger.Error($"[CACHE VALIDATION FAILED] Found {staleCount} stale entries in cache for map {map.uniqueID}:");
                AutoArmLogger.Error($"  - Destroyed: {destroyedCount}");
                AutoArmLogger.Error($"  - Not spawned: {notSpawnedCount}");
                AutoArmLogger.Error($"  - Wrong map: {wrongMapCount}");
                AutoArmLogger.Error($"Cleaning up stale entries...");
                
                // Clean up stale entries
                foreach (var weapon in staleWeapons)
                {
                    cache.Weapons.Remove(weapon);
                    
                    // Clean up spatial index
                    if (cache.WeaponToGrid.TryGetValue(weapon, out var gridPos))
                    {
                        if (cache.SpatialIndex.TryGetValue(gridPos, out var list))
                        {
                            list.Remove(weapon);
                            if (list.Count == 0)
                                cache.SpatialIndex.Remove(gridPos);
                        }
                        cache.WeaponToGrid.Remove(weapon);
                    }
                    
                    // Clean up categories
                    var category = GetWeaponCategory(weapon);
                    cache.CategorizedWeapons[category].Remove(weapon);
                }
                
                AutoArmLogger.Error($"Cache cleaned. New size: {cache.Weapons.Count} weapons");
            }
            // No need to log when validation passes - only log problems
        }

        /// <summary>
        /// Get weapons near a position - the main query method
        /// </summary>
        public static List<ThingWithComps> GetWeaponsNear(Map map, IntVec3 position, float maxDistance)
        {
            // Early exit if no cache exists
            if (!weaponCache.TryGetValue(map, out var cache) || cache.Weapons.Count == 0)
            {
                return new List<ThingWithComps>();
            }

            // Use pooled collections to avoid allocations
            var result = weaponListPool.Count > 0 ? weaponListPool.Pop() : new List<ThingWithComps>();
            result.Clear();

            var foundWeapons = weaponSetPool.Count > 0 ? weaponSetPool.Pop() : new HashSet<ThingWithComps>();
            foundWeapons.Clear();

            try
            {
                // Progressive search - start close and expand outward
                float[] searchRadii = { Constants.SearchRadiusClose, Constants.SearchRadiusMedium, maxDistance };
                int weaponsNeeded = Constants.CloseWeaponsNeeded;

                foreach (float radius in searchRadii)
                {
                    if (radius > maxDistance)
                        break;

                    var maxDistSquared = radius * radius;
                    SearchWeaponsInRadius(cache, position, maxDistSquared, foundWeapons, result);

                    // Early exit if we found enough close weapons
                    if (result.Count >= weaponsNeeded && radius <= Constants.SearchRadiusClose)
                        break;

                    // If we have a good selection at medium range, stop
                    if (result.Count >= weaponsNeeded * 2 && radius <= Constants.SearchRadiusMedium)
                        break;
                }

                return result;
            }
            finally
            {
                // Return HashSet to pool
                if (weaponSetPool.Count < Constants.PooledCollectionLimit)
                {
                    foundWeapons.Clear();
                    weaponSetPool.Push(foundWeapons);
                }
            }
        }

        /// <summary>
        /// Initialize cache for a map (only needed on map load)
        /// </summary>
        public static void InitializeCacheForMap(Map map)
        {
            if (map == null)
                return;

            // If cache already exists and has weapons, it's probably fine
            if (weaponCache.TryGetValue(map, out var existingCache) && existingCache.Weapons.Count > 0)
            {
                return;
            }

            var cache = GetOrCreateCache(map);
            
            // Find all weapons on the map
            var allWeapons = new List<ThingWithComps>();
            
            // Get weapons from the map
            try
            {
                var mapWeapons = map.listerThings.ThingsInGroup(ThingRequestGroup.Weapon)
                    .OfType<ThingWithComps>()
                    .Where(IsValidWeapon)
                    .ToList();
                allWeapons.AddRange(mapWeapons);
            }
            catch (Exception ex)
            {
                AutoArmLogger.Error($"Error getting weapons from map {map.uniqueID}", ex);
            }

            // Get weapons from storage containers
            try
            {
                var storageBuildings = map.listerBuildings.allBuildingsColonist
                    .Where(b => b != null && !b.Destroyed && b.Spawned && 
                           (b.GetSlotGroup() != null || b.def.building?.fixedStorageSettings != null));

                foreach (var building in storageBuildings)
                {
                    var innerContainer = building.TryGetInnerInteractableThingOwner();
                    if (innerContainer != null)
                    {
                        foreach (var item in innerContainer)
                        {
                            if (item is ThingWithComps weapon && IsValidWeapon(weapon))
                            {
                                allWeapons.Add(weapon);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AutoArmLogger.Error($"Error checking storage containers for map {map.uniqueID}", ex);
            }

            // Add all weapons to cache
            foreach (var weapon in allWeapons)
            {
                AddWeaponToCache(weapon);
            }

            // Check for discrepancies
            if (cache.Weapons.Count != cache.WeaponToGrid.Count)
            {
                AutoArmLogger.Warn($"[CACHE INIT WARNING] Weapon count ({cache.Weapons.Count}) doesn't match grid mappings ({cache.WeaponToGrid.Count})!");
            }
            
            // Set initial high water mark
            cacheHighWaterMark[map] = cache.Weapons.Count;
            lastCacheSizeLogTick[map] = Find.TickManager.TicksGame;
        }

        /// <summary>
        /// Clear cache for a destroyed map
        /// </summary>
        public static void ClearCacheForMap(Map map)
        {
            if (map == null)
                return;

            weaponCache.Remove(map);
            cacheHighWaterMark.Remove(map);
            lastCacheSizeLogTick.Remove(map);
        }

        /// <summary>
        /// Clean up caches for destroyed maps
        /// </summary>
        public static void CleanupDestroyedMaps()
        {
            if (Current.Game == null || Find.Maps == null)
                return;

            var mapsToRemove = weaponCache.Keys.Where(m => m == null || !Find.Maps.Contains(m)).ToList();
            foreach (var map in mapsToRemove)
            {
                weaponCache.Remove(map);
                cacheHighWaterMark.Remove(map);
                lastCacheSizeLogTick.Remove(map);
            }

            // Cleanup complete - only log if there were errors
        }

        /// <summary>
        /// Manual cache rebuild - ONLY for debugging/recovery
        /// Should never be needed if real-time tracking is working
        /// </summary>
        public static void DebugRebuildCache(Map map)
        {
            if (!Prefs.DevMode)
                return;

            AutoArmLogger.Warn($"MANUAL CACHE REBUILD for map {map?.uniqueID ?? -1} - this shouldn't be needed!");
            
            if (map == null)
                return;

            // Log current cache state before rebuild
            if (weaponCache.TryGetValue(map, out var oldCache))
            {
                AutoArmLogger.Warn($"Old cache had {oldCache.Weapons.Count} weapons");
                ValidateCacheContents(map, oldCache);
            }

            // Clear existing cache
            weaponCache.Remove(map);
            cacheHighWaterMark.Remove(map);
            lastCacheSizeLogTick.Remove(map);
            
            // Reinitialize
            InitializeCacheForMap(map);
            
            if (weaponCache.TryGetValue(map, out var newCache))
            {
                AutoArmLogger.Warn($"New cache has {newCache.Weapons.Count} weapons");
            }
        }
        
        /// <summary>
        /// Get cache statistics for debugging (dev mode only)
        /// </summary>
        public static void LogCacheStatistics()
        {
            if (!Prefs.DevMode)
                return;
                
            AutoArmLogger.Debug("[CACHE STATISTICS]");
            foreach (var kvp in weaponCache)
            {
                var map = kvp.Key;
                var cache = kvp.Value;
                int highWater = cacheHighWaterMark.ContainsKey(map) ? cacheHighWaterMark[map] : cache.Weapons.Count;
                
                AutoArmLogger.Debug($"  Map {map.uniqueID}: {cache.Weapons.Count} weapons (high water: {highWater})");
            }
        }

        // ========== HELPER METHODS ==========

        private static WeaponCacheEntry GetOrCreateCache(Map map)
        {
            if (!weaponCache.TryGetValue(map, out var cache))
            {
                cache = new WeaponCacheEntry();
                weaponCache[map] = cache;
            }
            return cache;
        }

        private static IntVec3 GetGridPosition(IntVec3 position)
        {
            return new IntVec3(
                position.x / Constants.GridCellSize, 
                0, 
                position.z / Constants.GridCellSize
            );
        }

        private static void SearchWeaponsInRadius(WeaponCacheEntry cache, IntVec3 position, float maxDistSquared, 
            HashSet<ThingWithComps> foundWeapons, List<ThingWithComps> result)
        {
            int searchRadius = Mathf.CeilToInt(Mathf.Sqrt(maxDistSquared));
            int minX = (position.x - searchRadius) / Constants.GridCellSize;
            int maxX = (position.x + searchRadius) / Constants.GridCellSize;
            int minZ = (position.z - searchRadius) / Constants.GridCellSize;
            int maxZ = (position.z + searchRadius) / Constants.GridCellSize;
            
            // Perform grid search
            
            for (int x = minX; x <= maxX; x++)
            {
                for (int z = minZ; z <= maxZ; z++)
                {
                    var gridKey = new IntVec3(x, 0, z);
                    if (cache.SpatialIndex.TryGetValue(gridKey, out var weapons))
                    {
                        foreach (var weapon in weapons)
                        {
                            // Skip if already found
                            if (!foundWeapons.Add(weapon))
                                continue;

                            // Check distance
                            IntVec3 weaponPos = weapon.Position;
                            if (weapon.ParentHolder is Building building && building.Position.IsValid)
                            {
                                weaponPos = building.Position;
                            }

                            float distSq = weaponPos.DistanceToSquared(position);
                            if (distSq <= maxDistSquared)
                            {
                                result.Add(weapon);
                            }
                        }
                    }
                }
            }
        }

        private static bool IsValidWeapon(ThingWithComps weapon)
        {
            if (weapon == null || weapon.def == null)
                return false;

            if (weapon.Destroyed || !weapon.Spawned)
                return false;

            return WeaponValidation.IsProperWeapon(weapon);
        }

        private static WeaponCategory GetWeaponCategory(ThingWithComps weapon)
        {
            if (weapon?.def == null)
                return WeaponCategory.MeleeBasic;

            if (weapon.def.IsMeleeWeapon)
            {
                if (weapon.TryGetQuality(out QualityCategory qc) && (int)qc >= (int)QualityCategory.Good)
                    return WeaponCategory.MeleeAdvanced;
                return WeaponCategory.MeleeBasic;
            }
            else if (weapon.def.IsRangedWeapon)
            {
                if (weapon.def.Verbs?.Count > 0 && weapon.def.Verbs[0] != null)
                {
                    float range = weapon.def.Verbs[0].range;
                    if (range < Constants.CategoryRangeShort)
                        return WeaponCategory.RangedShort;
                    else if (range <= Constants.CategoryRangeMedium)
                        return WeaponCategory.RangedMedium;
                    else
                        return WeaponCategory.RangedLong;
                }
            }

            return WeaponCategory.MeleeBasic;
        }

        /// <summary>
        /// Get weapons by category (for specialized queries)
        /// </summary>
        public static List<ThingWithComps> GetWeaponsByCategory(Map map, WeaponCategory category)
        {
            if (!weaponCache.TryGetValue(map, out var cache))
                return new List<ThingWithComps>();

            return new List<ThingWithComps>(cache.CategorizedWeapons[category]);
        }

        /// <summary>
        /// Force process any pending weapon operations (legacy compatibility)
        /// </summary>
        public static void ForceProcessPendingWeapons()
        {
            // No-op - real-time tracking doesn't have pending operations
        }

        /// <summary>
        /// Clear and rebuild cache (legacy compatibility)
        /// </summary>
        public static void ClearAndRebuildCache(Map map)
        {
            if (map == null)
                return;
                
            // Clear existing cache
            ClearCacheForMap(map);
            
            // Reinitialize
            InitializeCacheForMap(map);
        }

        /// <summary>
        /// Invalidate cache for a map (legacy compatibility)
        /// </summary>
        public static void InvalidateCache(Map map)
        {
            if (map == null)
                return;
                
            // With real-time tracking, we just mark as changed
            MarkCacheAsChanged(map);
            
            // If in dev mode, do a validation
            if (Prefs.DevMode && weaponCache.TryGetValue(map, out var cache))
            {
                ValidateCacheContents(map, cache);
            }
        }

        /// <summary>
        /// Get cached weapon score (delegates to WeaponScoreCache)
        /// </summary>
        public static float GetCachedWeaponScore(Pawn pawn, ThingWithComps weapon)
        {
            return WeaponScoreCache.GetCachedScore(pawn, weapon);
        }
    }
}
