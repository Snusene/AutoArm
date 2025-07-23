using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Verse;

namespace AutoArm
{
    public class ImprovedWeaponCacheManager
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
            public int LastUpdateTick { get; set; }
            public int LastChangeDetectedTick { get; set; }
            public Dictionary<IntVec3, List<ThingWithComps>> SpatialIndex { get; set; }
            public Dictionary<ThingWithComps, IntVec3> WeaponToGrid { get; set; }
            public Dictionary<WeaponCategory, List<ThingWithComps>> CategorizedWeapons { get; set; }

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
        private const int CacheLifetime = 10000;
        private const int GridCellSize = 20; // Matching backup version

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
                AutoArmDebug.Log($"Cache full ({MaxWeaponsPerCache} weapons), skipping {weapon.Label}");
                return;
            }

            if (cache.Weapons.Contains(weapon))
                return;

            cache.Weapons.Add(weapon);
            
            // Removed spammy autopistol logging

            var gridPos = new IntVec3(weapon.Position.x / GridCellSize, 0, weapon.Position.z / GridCellSize);
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
                AutoArmDebug.LogWeapon(null, weapon, $"Updated position from {oldPos} to {newPos}");
            }
        }

        public static List<ThingWithComps> GetWeaponsNear(Map map, IntVec3 position, float maxDistance)
        {
            try
            {
                // Early exit if no cache exists
                if (!weaponCache.TryGetValue(map, out var cache) || cache.Weapons.Count == 0)
                {
                    // Don't log - too spammy
                    return new List<ThingWithComps>();
                }
                
                var result = new List<ThingWithComps>();
                var foundWeapons = new HashSet<ThingWithComps>(); // Track found weapons to avoid duplicates
                
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
                                        if (weapon.Position.DistanceToSquared(position) <= maxDistSquared)
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

                return result;
            }
            catch (Exception ex)
            {
                AutoArmDebug.Log($"ERROR in GetWeaponsNear for map {map?.uniqueID ?? -1}: {ex.Message}\nStack trace: {ex.StackTrace}");
                return new List<ThingWithComps>(); // Return empty list on error
            }
        }


        public static void InvalidateCache(Map map)
        {
            if (map == null) return;

            if (weaponCache.ContainsKey(map))
            {
                weaponCache.Remove(map);
            }

            if (rebuildStates.ContainsKey(map))
            {
                rebuildStates.Remove(map);
            }

            AutoArmDebug.Log($"Invalidated weapon cache for {map}");
        }

        public static void CleanupDestroyedMaps()
        {
            var mapsToRemove = weaponCache.Keys.Where(m => m == null || !Find.Maps.Contains(m)).ToList();

            if (mapsToRemove.Any())
            {
                foreach (var map in mapsToRemove)
                {
                    weaponCache.Remove(map);
                    rebuildStates.Remove(map); 
                }

                AutoArmDebug.Log($"Cleaned up {mapsToRemove.Count} destroyed map caches");
            }
        }

        private static WeaponCacheEntry GetOrCreateCache(Map map)
        {
            if (!weaponCache.TryGetValue(map, out var cache))
            {
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
            return cache;
        }

        private static void StartRebuild(Map map)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            
            List<Thing> allWeapons;
            try
            {
                allWeapons = map.listerThings.ThingsInGroup(ThingRequestGroup.Weapon).ToList();
            }
            catch (Exception ex)
            {
                Log.Error($"[AutoArm] Critical error getting weapon list from listerThings: {ex.Message}");
                Log.Error($"[AutoArm] This might be caused by Kiiro Race or other mods interfering with ThingRequestGroup");
                allWeapons = new List<Thing>(); // Empty list to prevent crash
            }

            // Check if ThingRequestGroup.Weapon is broken (common mod conflict)
            if (allWeapons.Count <= 2) // Suspiciously low for any real colony
            {
                // Alternative detection method
                List<Thing> actualWeapons;
                try
                {
                    actualWeapons = map.listerThings.AllThings
                        .Where(t => t != null && 
                                   t.def != null && 
                                   SafeIsWeapon(t) && 
                                   !SafeIsApparel(t) && 
                                   t is ThingWithComps &&
                                   t.Spawned && 
                                   (t.ParentHolder == null || t.ParentHolder == t.Map))
                        .ToList();
                }
                catch (Exception ex)
                {
                    Log.Error($"[AutoArm] Alternative weapon detection also failed: {ex.Message}");
                    actualWeapons = new List<Thing>();
                }
                    
                if (actualWeapons.Count > allWeapons.Count)
                {
                    Log.Warning($"[AutoArm] ThingRequestGroup.Weapon is broken - found only {allWeapons.Count} weapons, but {actualWeapons.Count} ground weapons exist.");
                    Log.Warning($"[AutoArm] Using alternative weapon detection. This is likely caused by a mod conflict.");
                    Log.Warning($"[AutoArm] Consider checking your mod load order or reporting this to the conflicting mod's author.");
                    allWeapons = actualWeapons;
                }
            }
            
            AutoArmDebug.Log($"Starting incremental rebuild for {map} - {allWeapons.Count} total weapons");
            
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
                AutoArmDebug.Log($"Completed rebuild immediately with {state.PartialCache.Weapons.Count} weapons in {sw.ElapsedMilliseconds}ms");
            }
            else
            {
                rebuildStates[map] = state;
                AutoArmDebug.Log($"Processed {state.ProcessedCount} weapons, {state.RemainingWeapons.Count} remaining");
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

                AutoArmDebug.Log($"Completed incremental rebuild with {state.PartialCache.Weapons.Count} weapons");
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
                            AutoArmDebug.Log($"Skipping non-ThingWithComps or null def: {thing.Label}");
                        }
                        continue;
                    }
                    
                    if (!IsValidWeapon(weapon))
                    {
                        AutoArmDebug.LogWeapon(null, weapon, $"Skipping invalid weapon ({weapon.def.defName}) - " +
                                   $"IsWeapon={weapon.def.IsWeapon}, IsApparel={weapon.def.IsApparel}, " +
                                   $"Destroyed={weapon.Destroyed}, Spawned={weapon.Spawned}");
                        continue;
                    }
                    
                    // Removed spammy autopistol logging during rebuild

                    entry.Weapons.Add(weapon);

                    var gridPos = new IntVec3(weapon.Position.x / GridCellSize, 0, weapon.Position.z / GridCellSize);
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
                    // Catch any exceptions from problematic weapons (like Kiiro Race items)
                    AutoArmDebug.Log($"ERROR processing weapon during rebuild: {ex.Message}\nWeapon: {thing?.Label ?? "unknown"}");
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
        
        private static bool SafeIsWeapon(Thing thing)
        {
            try
            {
                return thing?.def?.IsWeapon ?? false;
            }
            catch
            {
                return false;
            }
        }
        
        private static bool SafeIsApparel(Thing thing)
        {
            try
            {
                return thing?.def?.IsApparel ?? false;
            }
            catch
            {
                return false;
            }
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
                    if (weapon.Position.DistanceToSquared(position) <= maxDistSquared)
                    {
                        result.Add(weapon);
                    }
                }
            }
            
            return result;
        }
    }
}
