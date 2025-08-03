using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using AutoArm.Helpers;
using AutoArm.Logging;
using AutoArm.Caching; using AutoArm.Weapons;

namespace AutoArm.Caching
{
    public static class ImprovedWeaponCacheManager
    {
        // Cache statistics for monitoring performance
        private static int cacheHits = 0;
        private static int cacheMisses = 0;
        private static int lastStatsLogTick = 0;
        
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
            public int LastUpdateTick { get; set; }
            public int LastChangeDetectedTick { get; set; }
            public Dictionary<IntVec3, List<ThingWithComps>> SpatialIndex { get; set; }
            public Dictionary<ThingWithComps, IntVec3> WeaponToGrid { get; set; }
            public Dictionary<WeaponCategory, List<ThingWithComps>> CategorizedWeapons { get; set; }
            
            // Removed - using WeaponScoreCache instead to avoid duplication

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
        private const int BaseCacheLifetime = 10000; // ~167 seconds base
        private const int GridCellSize = 20; // Matching backup version
        
        private static int GetCacheLifetime(Map map)
        {
            // Scale cache lifetime with colony size for performance
            int colonySize = map?.mapPawns?.FreeColonists?.Count() ?? 0;
            if (colonySize >= AutoArmMod.settings.performanceModeColonySize)
            {
                // At 35 pawns: 20000 ticks (~333 seconds)
                // At 50 pawns: 30000 ticks (~500 seconds)
                int extraLifetime = (colonySize - 30) * 600;
                return BaseCacheLifetime + extraLifetime;
            }
            return BaseCacheLifetime;
        }
        
        // Object pool for HashSets to avoid allocations in hot path
        private static Stack<HashSet<ThingWithComps>> weaponSetPool = new Stack<HashSet<ThingWithComps>>();
        private static Stack<List<ThingWithComps>> weaponListPool = new Stack<List<ThingWithComps>>();

        private const int MaxWeaponsPerCache = 1500;
        private const int MaxWeaponsPerRebuild = 100;
        private const int RebuildDelayTicks = 15;

        private static Dictionary<Map, RebuildState> rebuildStates = new Dictionary<Map, RebuildState>();

        private class RebuildState
        {
            public int ProcessedCount { get; set; }
            public int LastProcessTick { get; set; }
            public List<Thing> RemainingWeapons { get; set; }
            public WeaponCacheEntry PartialCache { get; set; }
        }

        public static void AddWeaponToCache(ThingWithComps weapon)
        {
            if (weapon?.Map == null || !IsValidWeapon(weapon))
                return;

            var cache = GetOrCreateCache(weapon.Map);

            if (cache.Weapons.Count >= MaxWeaponsPerCache)
            {
                AutoArmLogger.Warn($"Weapon cache full ({MaxWeaponsPerCache} weapons) for map {weapon.Map.uniqueID}, skipping {weapon.Label}");
                return;
            }

            if (cache.Weapons.Contains(weapon))
                return;

            cache.Weapons.Add(weapon);

            // Removed spammy autopistol logging

            // For weapons in containers, use the container's position
            IntVec3 position = weapon.Position;
            if (weapon.ParentHolder is Building building && building.Position.IsValid)
            {
                position = building.Position;
            }

            var gridPos = new IntVec3(position.x / GridCellSize, 0, position.z / GridCellSize);
            if (!cache.SpatialIndex.ContainsKey(gridPos))
            {
                cache.SpatialIndex[gridPos] = new List<ThingWithComps>();
            }
            cache.SpatialIndex[gridPos].Add(weapon);

            cache.WeaponToGrid[weapon] = gridPos;
            cache.LastChangeDetectedTick = Find.TickManager.TicksGame;

            // Categorize the weapon
            var category = GetWeaponCategory(weapon);
            cache.CategorizedWeapons[category].Add(weapon);

            // Don't log every weapon add - too spammy
        }

        public static void RemoveWeaponFromCache(ThingWithComps weapon)
        {
            if (weapon?.Map == null)
                return;

            if (!weaponCache.TryGetValue(weapon.Map, out var cache))
                return;

            if (cache.Weapons.Remove(weapon))
            {
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

                // Don't log every weapon removal - too spammy
            }
        }

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

            var oldGridPos = new IntVec3(oldPos.x / GridCellSize, 0, oldPos.z / GridCellSize);
            var newGridPos = new IntVec3(newPos.x / GridCellSize, 0, newPos.z / GridCellSize);

            if (oldGridPos == newGridPos)
                return;

            if (cache.SpatialIndex.TryGetValue(oldGridPos, out var oldList))
            {
                oldList.Remove(weapon);
                if (oldList.Count == 0)
                {
                    cache.SpatialIndex.Remove(oldGridPos);
                }
            }

            if (!cache.SpatialIndex.ContainsKey(newGridPos))
            {
                cache.SpatialIndex[newGridPos] = new List<ThingWithComps>();
            }
            cache.SpatialIndex[newGridPos].Add(weapon);

            cache.WeaponToGrid[weapon] = newGridPos;

            // Note: We don't need to update category as weapon type doesn't change

            if (weapon.def.defName == "Gun_AssaultRifle")
            {
                AutoArmLogger.LogWeapon(null, weapon, $"Updated position from {oldPos} to {newPos}");
            }
        }

        private static void LogCacheStats()
        {
            if (!Prefs.DevMode) return;
            
            int currentTick = Find.TickManager.TicksGame;
            if (currentTick - lastStatsLogTick > 6000) // Log every 100 seconds
            {
                int total = cacheHits + cacheMisses;
                if (total > 0)
                {
                    float hitRate = (float)cacheHits / total * 100f;
                    AutoArmLogger.Debug($"WeaponCacheManager stats - Hit rate: {hitRate:F1}% ({cacheHits} hits, {cacheMisses} misses)");
                }
                lastStatsLogTick = currentTick;
                cacheHits = 0;
                cacheMisses = 0;
            }
        }
        
        public static List<ThingWithComps> GetWeaponsNear(Map map, IntVec3 position, float maxDistance)
        {
            // Declare variables outside try block for catch block access
            List<ThingWithComps> result = null;
            HashSet<ThingWithComps> foundWeapons = null;
            
            try
            {
                // Early exit if no cache exists
                if (!weaponCache.TryGetValue(map, out var cache) || cache.Weapons.Count == 0)
                {
                    // Don't log - too spammy
                    return new List<ThingWithComps>();
                }

                // Use pooled collections to avoid allocations
                result = weaponListPool.Count > 0 ? weaponListPool.Pop() : new List<ThingWithComps>();
                result.Clear();
                
                foundWeapons = weaponSetPool.Count > 0 ? weaponSetPool.Pop() : new HashSet<ThingWithComps>();
                foundWeapons.Clear();

                // Removed spammy GetWeaponsNear logging - this happens too frequently

                // Progressive search - start close and expand outward
                float[] searchRadii = { 15f, 30f, maxDistance };
                int weaponsNeeded = 15; // We'll take more if they're closer

                foreach (float radius in searchRadii)
                {
                    if (radius > maxDistance)
                        break;

                    var maxDistSquared = radius * radius;

                    int minX = (position.x - (int)radius) / GridCellSize;
                    int maxX = (position.x + (int)radius) / GridCellSize;
                    int minZ = (position.z - (int)radius) / GridCellSize;
                    int maxZ = (position.z + (int)radius) / GridCellSize;

                    // Search in a spiral pattern from center outward
                    int centerX = position.x / GridCellSize;
                    int centerZ = position.z / GridCellSize;

                    for (int ring = 0; ring <= Math.Max(maxX - centerX, maxZ - centerZ); ring++)
                    {
                        for (int x = centerX - ring; x <= centerX + ring; x++)
                        {
                            for (int z = centerZ - ring; z <= centerZ + ring; z++)
                            {
                                // Skip cells we've already checked
                                if (ring > 0 && x > centerX - ring && x < centerX + ring &&
                                    z > centerZ - ring && z < centerZ + ring)
                                    continue;

                                if (x < minX || x > maxX || z < minZ || z > maxZ)
                                    continue;

                                var gridKey = new IntVec3(x, 0, z);
                                if (cache.SpatialIndex.TryGetValue(gridKey, out var weapons))
                                {
                                    foreach (var weapon in weapons)
                                    {
                                        // For weapons in containers, use the container's position
                                        IntVec3 weaponPos = weapon.Position;
                                        if (weapon.ParentHolder is Building building && building.Position.IsValid)
                                        {
                                            weaponPos = building.Position;
                                        }
                                        
                                        if (weaponPos.DistanceToSquared(position) <= maxDistSquared)
                                        {
                                            // Only add if we haven't seen this weapon yet
                                            if (foundWeapons.Add(weapon))
                                            {
                                                result.Add(weapon);
                                            }
                                        }
                                    }
                                }
                            }
                        }

                        // Early exit if we found enough close weapons
                        if (result.Count >= weaponsNeeded && radius <= 15f)
                        {
                            return result;
                        }
                    }

                    // If we have a good selection at medium range, stop
                    if (result.Count >= weaponsNeeded * 2 && radius <= 30f)
                    {
                        return result;
                    }
                }

                // Return HashSet to pool
                if (weaponSetPool.Count < 10) // Keep pool size reasonable
                {
                    foundWeapons.Clear();
                    weaponSetPool.Push(foundWeapons);
                }
                
                return result; // Caller is responsible for returning list to pool
            }
            catch (Exception ex)
            {
                AutoArmLogger.Error($"GetWeaponsNear failed for map {map?.uniqueID ?? -1} at {position}", ex);
                
                // Return collections to pool on error
                if (weaponSetPool.Count < 10 && foundWeapons != null)
                {
                    foundWeapons.Clear();
                    weaponSetPool.Push(foundWeapons);
                }
                if (result != null && result.Count == 0 && weaponListPool.Count < 10)
                {
                    weaponListPool.Push(result);
                }
                
                return new List<ThingWithComps>(); // Return empty list on error
            }
        }

        public static void InvalidateCache(Map map)
        {
            if (map == null) return;

            bool hadCache = weaponCache.ContainsKey(map);
            int weaponCount = hadCache ? weaponCache[map].Weapons.Count : 0;
            
            if (weaponCache.ContainsKey(map))
            {
                weaponCache.Remove(map);
            }

            if (rebuildStates.ContainsKey(map))
            {
                rebuildStates.Remove(map);
            }

            // Also clear weapon base score cache when map cache is invalidated
            WeaponScoringHelper.ClearWeaponScoreCache();

            if (hadCache)
                AutoArmLogger.Debug($"Invalidated weapon cache for map {map.uniqueID} ({weaponCount} weapons)");
        }

        /// <summary>
        /// Clear all weapon caches for all maps
        /// </summary>
        public static void ClearCache()
        {
            weaponCache.Clear();
            rebuildStates.Clear();
            
            // Also clear pooled collections
            weaponSetPool.Clear();
            weaponListPool.Clear();
            
            // Clear weapon score cache
            WeaponScoringHelper.ClearWeaponScoreCache();
            
            AutoArmLogger.Log("Cleared all weapon caches");
        }
        
        /// <summary>
        /// Clear and rebuild all weapon caches for all maps
        /// </summary>
        public static void ClearAndRebuildCache()
        {
            // First clear everything
            ClearCache();
            
            // Only rebuild if we're in an active game with maps
            if (Current.Game != null && Find.Maps != null)
            {
                // Then trigger rebuild for all active maps
                foreach (var map in Find.Maps)
            {
                if (map != null)
                {
                    AutoArmLogger.Log($"Triggering weapon cache rebuild for {map.uniqueID} ({(map.IsPlayerHome ? "player home" : "other map")})");
                    // This will trigger a rebuild when the cache is accessed
                    // The rebuild is incremental, so it won't freeze the game
                    TriggerCacheRebuild(map);
                }
            }
            
                AutoArmLogger.Log($"Cleared and initiated rebuild of weapon caches for {Find.Maps.Count} maps");
            }
            else
            {
                AutoArmLogger.Log("Cleared weapon caches (no active game to rebuild)");
            }
        }
        
        /// <summary>
        /// Trigger a cache rebuild for a specific map
        /// </summary>
        private static void TriggerCacheRebuild(Map map)
        {
            if (map == null) return;
            
            // Simply accessing the cache will trigger a rebuild if it doesn't exist
            GetOrCreateCache(map);
        }

        public static void CleanupDestroyedMaps()
        {
            // Only clean up if we're in an active game
            if (Current.Game == null || Find.Maps == null)
                return;
                
            var mapsToRemove = weaponCache.Keys.Where(m => m == null || !Find.Maps.Contains(m)).ToList();

            if (mapsToRemove.Any())
            {
                foreach (var map in mapsToRemove)
                {
                    weaponCache.Remove(map);
                    rebuildStates.Remove(map);
                }

                AutoArmLogger.Log($"Cleaned up {mapsToRemove.Count} destroyed map caches");
            }
        }

        private static WeaponCacheEntry GetOrCreateCache(Map map)
        {
            if (!weaponCache.TryGetValue(map, out var cache))
            {
                cacheMisses++;
                LogCacheStats();
                
                if (rebuildStates.TryGetValue(map, out var rebuildState))
                {
                    if (Find.TickManager.TicksGame - rebuildState.LastProcessTick >= RebuildDelayTicks)
                    {
                        ContinueRebuild(map, rebuildState);
                    }
                    return rebuildState.PartialCache;
                }
                else
                {
                    StartRebuild(map);
                    return weaponCache.TryGetValue(map, out cache) ? cache : new WeaponCacheEntry();
                }
            }
            else
            {
                cacheHits++;
            }
            return cache;
        }

        private static void StartRebuild(Map map)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();

            List<Thing> allWeapons;
            try
            {
                allWeapons = map.listerThings.ThingsInGroup(ThingRequestGroup.Weapon).ToList();
                if (Prefs.DevMode)
                    AutoArmLogger.Debug($"StartRebuild: Found {allWeapons.Count} weapons via ThingRequestGroup.Weapon for map {map.uniqueID}");
                
                // Debug: Log what weapons are equipped by colonists
                var equippedWeapons = map.mapPawns.FreeColonists
                    .Where(p => p.equipment?.Primary != null)
                    .Select(p => p.equipment.Primary.Label)
                    .ToList();
                if (equippedWeapons.Any())
                {
                    AutoArmLogger.Log($"Colonists have these weapons equipped: {string.Join(", ", equippedWeapons)}");
                }
                
                // Debug: Log what weapons are in colonist inventories
                var inventoryWeapons = new List<string>();
                foreach (var pawn in map.mapPawns.FreeColonists)
                {
                    if (pawn.inventory?.innerContainer != null)
                    {
                        foreach (var item in pawn.inventory.innerContainer)
                        {
                            if (item is ThingWithComps weapon && weapon.def.IsWeapon)
                            {
                                inventoryWeapons.Add(weapon.Label);
                            }
                        }
                    }
                }
                if (inventoryWeapons.Any())
                {
                    AutoArmLogger.Log($"Colonists have these weapons in inventory: {string.Join(", ", inventoryWeapons)}");
                }
            }
            catch (Exception ex)
            {
                AutoArmLogger.Error($"Critical error getting weapon list from listerThings for map {map.uniqueID}", ex);
                AutoArmLogger.Error($"This might be caused by mods interfering with ThingRequestGroup");
                allWeapons = new List<Thing>(); // Empty list to prevent crash
            }



                // CRITICAL: Also check for weapons inside storage containers
        // The listerThings only tracks items directly on the map, not in containers
        var weaponsInStorage = new List<Thing>();
        try
        {
            // Get all buildings that might be storage
            var potentialStorageBuildings = map.listerBuildings.allBuildingsColonist
                .Where(b => b != null && !b.Destroyed && b.Spawned);

            foreach (var building in potentialStorageBuildings)
            {
                try
                {
                    // Check if this building is likely to be storage
                    bool isStorage = false;
                    
                    // Primary check: Does it have a SlotGroup?
                    if (building.GetSlotGroup() != null)
                    {
                        isStorage = true;
                    }
                    // Secondary check: Does it have storage settings?
                    else if (building.def.building?.fixedStorageSettings != null)
                    {
                        isStorage = true;
                    }
                    // Check common storage building names
                    else if (building.def.defName.IndexOf("shelf", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             building.def.defName.IndexOf("rack", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             building.def.defName.IndexOf("crate", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             building.def.defName.IndexOf("box", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             building.def.defName.IndexOf("container", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             building.def.defName.IndexOf("storage", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             building.def.defName.IndexOf("fridge", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             building.def.defName.IndexOf("freezer", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        isStorage = true;
                    }
                    
                    if (isStorage)
                    {
                        // Try to get the inner container
                        var innerContainer = building.TryGetInnerInteractableThingOwner();
                        if (innerContainer != null)
                        {
                            // Check each item in the container
                            foreach (var item in innerContainer)
                            {
                                if (item != null && 
                                    item is ThingWithComps weapon &&
                                    item.def?.IsWeapon == true &&
                                    item.def?.IsApparel != true)
                                {
                                    weaponsInStorage.Add(item);
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                // Log but continue - don't let one bad building break the whole process
                if (Prefs.DevMode)
                        AutoArmLogger.Debug($"Error checking storage building {building?.Label ?? "unknown"}: {ex.Message}");
            }
            }

            if (weaponsInStorage.Count > 0)
            {
                if (Prefs.DevMode)
                    AutoArmLogger.Debug($"Found {weaponsInStorage.Count} additional weapons in storage containers");
                allWeapons.AddRange(weaponsInStorage);
            }
        }
        catch (Exception ex)
        {
            AutoArmLogger.Error($"Error checking weapons in storage for map {map.uniqueID}", ex);
            // Continue with weapons we already found
        }

        AutoArmLogger.Debug($"Starting incremental rebuild for map {map.uniqueID}: {allWeapons.Count} total weapons ({weaponsInStorage.Count} in storage)");
        
        // Debug: Log first few weapons found
        int debugCount = 0;
        foreach (var weapon in allWeapons.Take(5))
        {
            if (weapon is ThingWithComps twc)
            {
                AutoArmLogger.Log($"[WEAPON CACHE] Sample weapon {++debugCount}: {twc.Label} at {twc.Position} (in storage: {weaponsInStorage.Contains(weapon)})");
            }
        }

            var state = new RebuildState
            {
                ProcessedCount = 0,
                LastProcessTick = Find.TickManager.TicksGame,
                RemainingWeapons = allWeapons,
                PartialCache = new WeaponCacheEntry
                {
                    LastUpdateTick = Find.TickManager.TicksGame
                }
            };

            ProcessRebuildChunk(map, state, MaxWeaponsPerRebuild);

            if (state.RemainingWeapons.Count == 0)
            {
                weaponCache[map] = state.PartialCache;
                rebuildStates.Remove(map);

                sw.Stop();
                AutoArmLogger.Debug($"Completed weapon cache rebuild for map {map.uniqueID}: {state.PartialCache.Weapons.Count} weapons cached in {sw.ElapsedMilliseconds}ms");
            }
            else
            {
                rebuildStates[map] = state;
                AutoArmLogger.Log($"Processed {state.ProcessedCount} weapons, {state.RemainingWeapons.Count} remaining");
            }
        }

        private static void ContinueRebuild(Map map, RebuildState state)
        {
            ProcessRebuildChunk(map, state, MaxWeaponsPerRebuild);
            state.LastProcessTick = Find.TickManager.TicksGame;

            if (state.RemainingWeapons.Count == 0 || state.PartialCache.Weapons.Count >= MaxWeaponsPerCache)
            {
                weaponCache[map] = state.PartialCache;
                rebuildStates.Remove(map);

                AutoArmLogger.Debug($"Completed incremental rebuild for map {map.uniqueID}: {state.PartialCache.Weapons.Count} weapons cached");
            }
        }

        private static void ProcessRebuildChunk(Map map, RebuildState state, int chunkSize)
        {
            int processed = 0;
            var entry = state.PartialCache;

            while (processed < chunkSize && state.RemainingWeapons.Count > 0 && entry.Weapons.Count < MaxWeaponsPerCache)
            {
                var thing = state.RemainingWeapons[0];
                state.RemainingWeapons.RemoveAt(0);

                try
                {
                    var weapon = thing as ThingWithComps;
                    if (weapon?.def == null)
                    {
                        if (thing != null)
                        {
                            AutoArmLogger.Log($"Skipping non-ThingWithComps or null def: {thing.Label}");
                        }
                        continue;
                    }

                    if (!IsValidWeapon(weapon))
                    {
                        // Removed spammy debug log - weapon validation happens frequently
                        continue;
                    }

                    // Removed spammy autopistol logging during rebuild

                    entry.Weapons.Add(weapon);

                    // For weapons in containers, use the container's position
                    IntVec3 position = weapon.Position;
                    if (weapon.ParentHolder is Building building && building.Position.IsValid)
                    {
                        position = building.Position;
                    }

                    var gridPos = new IntVec3(position.x / GridCellSize, 0, position.z / GridCellSize);
                    if (!entry.SpatialIndex.ContainsKey(gridPos))
                    {
                        entry.SpatialIndex[gridPos] = new List<ThingWithComps>();
                    }
                    entry.SpatialIndex[gridPos].Add(weapon);
                    entry.WeaponToGrid[weapon] = gridPos;

                    // Categorize the weapon
                    var category = GetWeaponCategory(weapon);
                    entry.CategorizedWeapons[category].Add(weapon);

                    state.ProcessedCount++;
                    processed++;
                }
                catch (Exception ex)
                {
                    // Catch any exceptions from problematic modded weapons
                    AutoArmLogger.Error($"Failed to process weapon '{thing?.Label ?? "unknown"}' during rebuild", ex);
                    // Continue processing other weapons
                }
            }
        }

        private static bool IsValidWeapon(ThingWithComps weapon)
        {
            if (weapon == null || weapon.def == null)
                return false;

            if (weapon.Destroyed || !weapon.Spawned)
                return false;

            // Use comprehensive weapon validation with improved exception handling
            // This now handles Kiiro Race and other modded items gracefully
            return WeaponValidation.IsProperWeapon(weapon);
        }

        private static WeaponCategory GetWeaponCategory(ThingWithComps weapon)
        {
            if (weapon?.def == null)
                return WeaponCategory.MeleeBasic;

            if (weapon.def.IsMeleeWeapon)
            {
                // Check quality
                if (weapon.TryGetQuality(out QualityCategory qc) && (int)qc >= (int)QualityCategory.Good)
                    return WeaponCategory.MeleeAdvanced;
                return WeaponCategory.MeleeBasic;
            }
            else if (weapon.def.IsRangedWeapon)
            {
                if (weapon.def.Verbs?.Count > 0 && weapon.def.Verbs[0] != null)
                {
                    float range = weapon.def.Verbs[0].range;
                    if (range < 20f)
                        return WeaponCategory.RangedShort;
                    else if (range <= 35f)
                        return WeaponCategory.RangedMedium;
                    else
                        return WeaponCategory.RangedLong;
                }
            }

            return WeaponCategory.MeleeBasic; // Default
        }



        public static List<ThingWithComps> GetWeaponsByCategory(Map map, WeaponCategory category)
        {
            if (!weaponCache.TryGetValue(map, out var cache))
                return new List<ThingWithComps>();

            return new List<ThingWithComps>(cache.CategorizedWeapons[category]);
        }

        public static List<ThingWithComps> GetWeaponsByCategoriesNear(Map map, IntVec3 position,
            float maxDistance, params WeaponCategory[] categories)
        {
            if (!weaponCache.TryGetValue(map, out var cache))
                return new List<ThingWithComps>();

            var result = new List<ThingWithComps>();
            var maxDistSquared = maxDistance * maxDistance;

            foreach (var category in categories)
            {
                foreach (var weapon in cache.CategorizedWeapons[category])
                {
                    // For weapons in containers, use the container's position
                    IntVec3 weaponPos = weapon.Position;
                    if (weapon.ParentHolder is Building building && building.Position.IsValid)
                    {
                        weaponPos = building.Position;
                    }
                    
                    if (weaponPos.DistanceToSquared(position) <= maxDistSquared)
                    {
                        result.Add(weapon);
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Get cached pawn-weapon score using the centralized WeaponScoreCache
        /// </summary>
        public static float GetCachedWeaponScore(Pawn pawn, ThingWithComps weapon)
        {
            // Use the centralized WeaponScoreCache which has better invalidation tracking
            return WeaponScoreCache.GetCachedScore(pawn, weapon);
        }

        /// <summary>
        /// Clear pawn-weapon score cache for a specific pawn (when skills/traits change)
        /// </summary>
        public static void ClearPawnScoreCache(Pawn pawn)
        {
            // Delegate to centralized cache which tracks skill changes properly
            WeaponScoreCache.MarkPawnSkillsChanged(pawn);
        }
    }
}