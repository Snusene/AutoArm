// 1. Improved Weapon Cache Manager - Fix the existing one
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Verse;

namespace AutoArm
{
    public class ImprovedWeaponCacheManager
    {
        private class WeaponCacheEntry
        {
            public HashSet<ThingWithComps> Weapons { get; set; }  // Changed from List to HashSet
            public int LastUpdateTick { get; set; }
            public int LastChangeDetectedTick { get; set; } // Track when weapons actually changed
            public Dictionary<IntVec3, List<ThingWithComps>> SpatialIndex { get; set; }
            public Dictionary<ThingWithComps, IntVec3> WeaponToGrid { get; set; } // Track weapon grid positions

            public WeaponCacheEntry()
            {
                Weapons = new HashSet<ThingWithComps>();  // Changed to HashSet
                SpatialIndex = new Dictionary<IntVec3, List<ThingWithComps>>();
                WeaponToGrid = new Dictionary<ThingWithComps, IntVec3>();
                LastChangeDetectedTick = 0;
            }
        }

        private static Dictionary<Map, WeaponCacheEntry> weaponCache = new Dictionary<Map, WeaponCacheEntry>();
        private const int CacheLifetime = 10000; // Keep as is
        private const int GridCellSize = 20;  // Removed GridSize - was unused

        // Add a single weapon to existing cache
        public static void AddWeaponToCache(ThingWithComps weapon)
        {
            if (weapon?.Map == null || !IsValidWeapon(weapon))
                return;

            var cache = GetOrCreateCache(weapon.Map);

            // Check if already in cache (HashSet makes this O(1))
            if (cache.Weapons.Contains(weapon))
                return;

            // Add to weapons set
            cache.Weapons.Add(weapon);

            // Add to spatial index
            var gridPos = new IntVec3(weapon.Position.x / GridCellSize, 0, weapon.Position.z / GridCellSize);
            if (!cache.SpatialIndex.ContainsKey(gridPos))
            {
                cache.SpatialIndex[gridPos] = new List<ThingWithComps>();
            }
            cache.SpatialIndex[gridPos].Add(weapon);

            // Track grid position
            cache.WeaponToGrid[weapon] = gridPos;

            // Mark change detected
            cache.LastChangeDetectedTick = Find.TickManager.TicksGame;

            if (AutoArmMod.settings?.debugLogging == true)
            {
                Log.Message($"[AutoArm] Added {weapon.Label} at {weapon.Position} to cache");
            }
        }

        // Remove weapon from cache
        public static void RemoveWeaponFromCache(ThingWithComps weapon)
        {
            if (weapon?.Map == null)
                return;

            if (!weaponCache.TryGetValue(weapon.Map, out var cache))
                return;

            if (cache.Weapons.Remove(weapon))
            {
                // Remove from spatial index using tracked position
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

                // Mark change detected
                cache.LastChangeDetectedTick = Find.TickManager.TicksGame;

                if (AutoArmMod.settings?.debugLogging == true)
                {
                    Log.Message($"[AutoArm] Removed {weapon.Label} from cache");
                }
            }
        }

        // Update weapon position in cache
        public static void UpdateWeaponPosition(ThingWithComps weapon, IntVec3 oldPos, IntVec3 newPos)
        {
            if (weapon?.Map == null || oldPos == newPos)
                return;

            // Safety check - don't update destroyed or despawned weapons
            if (!weapon.Spawned || weapon.Destroyed)
                return;

            if (!weaponCache.TryGetValue(weapon.Map, out var cache))
                return;

            // Only update if weapon is in cache
            if (!cache.Weapons.Contains(weapon))
                return;

            var oldGridPos = new IntVec3(oldPos.x / GridCellSize, 0, oldPos.z / GridCellSize);
            var newGridPos = new IntVec3(newPos.x / GridCellSize, 0, newPos.z / GridCellSize);

            // If grid position hasn't changed, nothing to do
            if (oldGridPos == newGridPos)
                return;

            // Remove from old grid position
            if (cache.SpatialIndex.TryGetValue(oldGridPos, out var oldList))
            {
                oldList.Remove(weapon);
                if (oldList.Count == 0)
                {
                    cache.SpatialIndex.Remove(oldGridPos);
                }
            }

            // Add to new grid position
            if (!cache.SpatialIndex.ContainsKey(newGridPos))
            {
                cache.SpatialIndex[newGridPos] = new List<ThingWithComps>();
            }
            cache.SpatialIndex[newGridPos].Add(weapon);

            // Update tracked position
            cache.WeaponToGrid[weapon] = newGridPos;

            if (AutoArmMod.settings?.debugLogging == true && weapon.def.defName == "Gun_AssaultRifle")
            {
                Log.Message($"[AutoArm] Updated {weapon.Label} position from {oldPos} to {newPos}");
            }
        }

        public static List<ThingWithComps> GetWeaponsNear(Map map, IntVec3 position, float maxDistance)
        {
            var cache = GetOrCreateCache(map);
            var result = new List<ThingWithComps>();
            var maxDistSquared = maxDistance * maxDistance;

            // Calculate grid cells to check
            int minX = (position.x - (int)maxDistance) / GridCellSize;
            int maxX = (position.x + (int)maxDistance) / GridCellSize;
            int minZ = (position.z - (int)maxDistance) / GridCellSize;
            int maxZ = (position.z + (int)maxDistance) / GridCellSize;

            for (int x = minX; x <= maxX; x++)
            {
                for (int z = minZ; z <= maxZ; z++)
                {
                    // IMPORTANT: Grid key should just be (x, 0, z), NOT multiplied by GridCellSize
                    var gridKey = new IntVec3(x, 0, z);
                    if (cache.SpatialIndex.TryGetValue(gridKey, out var weapons))
                    {
                        foreach (var weapon in weapons)
                        {
                            if (weapon.Position.DistanceToSquared(position) <= maxDistSquared)
                            {
                                result.Add(weapon);
                            }
                        }
                    }
                }
            }

            // Only do debug logging if explicitly looking for assault rifles
            if (AutoArmMod.settings?.debugLogging == true && result.Count == 0)
            {
                // Check if assault rifles exist but weren't found
                var assaultRiflesInCache = cache.Weapons.Where(w => w.def.defName == "Gun_AssaultRifle").ToList();
                if (assaultRiflesInCache.Any())
                {
                    var closest = assaultRiflesInCache.MinBy(w => w.Position.DistanceTo(position));
                    Log.Message($"[AutoArm] No weapons found near {position}, but cache has {assaultRiflesInCache.Count} assault rifles. Closest at {closest.Position.DistanceTo(position):F1} cells away");
                }
            }

            return result;
        }

        public static void InvalidateCache(Map map)
        {
            if (map == null) return;

            if (weaponCache.ContainsKey(map))
            {
                weaponCache.Remove(map);

                if (AutoArmMod.settings?.debugLogging == true)
                {
                    Log.Message($"[AutoArm] Invalidated weapon cache for {map}");
                }
            }
        }

        // Cleanup method to prevent memory leaks from destroyed maps
        public static void CleanupDestroyedMaps()
        {
            var mapsToRemove = weaponCache.Keys.Where(m => m == null || !Find.Maps.Contains(m)).ToList();

            if (mapsToRemove.Any())
            {
                foreach (var map in mapsToRemove)
                {
                    weaponCache.Remove(map);
                }

                if (AutoArmMod.settings?.debugLogging == true)
                {
                    Log.Message($"[AutoArm] Cleaned up {mapsToRemove.Count} destroyed map caches");
                }
            }
        }

        private static WeaponCacheEntry GetOrCreateCache(Map map)
        {
            if (!weaponCache.TryGetValue(map, out var cache))
            {
                // First time - do full rebuild
                cache = RebuildCache(map);
                weaponCache[map] = cache;
            }
            // Removed periodic rebuild - incremental updates handle everything
            return cache;
        }

        private static WeaponCacheEntry RebuildCache(Map map)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var entry = new WeaponCacheEntry();
            entry.LastUpdateTick = Find.TickManager.TicksGame;
            var allWeapons = map.listerThings.ThingsInGroup(ThingRequestGroup.Weapon);

            // Move heavy debug logging outside the main processing
            bool isDebugging = AutoArmMod.settings?.debugLogging == true;
            int totalWeaponCount = 0;

            if (isDebugging)
            {
                totalWeaponCount = allWeapons.Count();
                Log.Message($"[AutoArm] Rebuilding weapon cache for {map} - found {totalWeaponCount} total weapons");
            }

            // Process ALL weapons - removed the 100 limit
            int processedCount = 0;

            foreach (var thing in allWeapons)
            {
                var weapon = thing as ThingWithComps;
                if (weapon?.def == null)
                    continue;

                if (!IsValidWeapon(weapon))
                    continue;

                entry.Weapons.Add(weapon);

                // Add to spatial index
                var gridPos = new IntVec3(weapon.Position.x / GridCellSize, 0, weapon.Position.z / GridCellSize);
                if (!entry.SpatialIndex.ContainsKey(gridPos))
                {
                    entry.SpatialIndex[gridPos] = new List<ThingWithComps>();
                }
                entry.SpatialIndex[gridPos].Add(weapon);

                // Track grid position
                entry.WeaponToGrid[weapon] = gridPos;

                processedCount++;
            }

            // Only do expensive debug operations after main processing
            if (isDebugging)
            {
                Log.Message($"[AutoArm] Cache rebuilt with {entry.Weapons.Count} valid weapons");

                // Only count assault rifles if really needed
                if (entry.Weapons.Count < 50) // Only for small caches
                {
                    var assaultRifleCount = entry.Weapons.Count(w => w.def.defName == "Gun_AssaultRifle");
                    if (assaultRifleCount > 0)
                    {
                        Log.Message($"[AutoArm] Cache contains {assaultRifleCount} assault rifles");
                    }
                }
            }

            sw.Stop();
            if (sw.ElapsedMilliseconds > 10)
            {
                Log.Warning($"[AutoArm] Cache rebuild took {sw.ElapsedMilliseconds}ms for {entry.Weapons.Count} weapons");
            }

            return entry;
        }

        private static bool IsValidWeapon(ThingWithComps weapon)
        {
            // Quick checks first
            if (!weapon.def.IsWeapon || weapon.def.IsApparel)
                return false;

            if (weapon.def.defName == "WoodLog")
                return false;

            if (weapon.Destroyed || !weapon.Spawned)
                return false;

            if (weapon.IsForbidden(Faction.OfPlayer))
                return false;

            return true;
        }
    }
}