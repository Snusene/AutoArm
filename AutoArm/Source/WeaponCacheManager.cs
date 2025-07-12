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
            public HashSet<ThingWithComps> Weapons { get; set; }
            public int LastUpdateTick { get; set; }
            public int LastChangeDetectedTick { get; set; }
            public Dictionary<IntVec3, List<ThingWithComps>> SpatialIndex { get; set; }
            public Dictionary<ThingWithComps, IntVec3> WeaponToGrid { get; set; }

            public WeaponCacheEntry()
            {
                Weapons = new HashSet<ThingWithComps>();
                SpatialIndex = new Dictionary<IntVec3, List<ThingWithComps>>();
                WeaponToGrid = new Dictionary<ThingWithComps, IntVec3>();
                LastChangeDetectedTick = 0;
            }
        }

        private static Dictionary<Map, WeaponCacheEntry> weaponCache = new Dictionary<Map, WeaponCacheEntry>();
        private const int CacheLifetime = 10000;
        private const int GridCellSize = 20;

        // NEW: Add configurable limits for performance
        private const int MaxWeaponsPerCache = 1500; // Increased from 500 for better mod compatibility
        private const int MaxWeaponsPerRebuild = 100; // Process in chunks during rebuild
        private const int RebuildDelayTicks = 5; // Spread rebuild over multiple ticks

        // NEW: Track incomplete rebuilds
        private static Dictionary<Map, RebuildState> rebuildStates = new Dictionary<Map, RebuildState>();

        private class RebuildState
        {
            public int ProcessedCount { get; set; }
            public int LastProcessTick { get; set; }
            public List<Thing> RemainingWeapons { get; set; }
            public WeaponCacheEntry PartialCache { get; set; }
        }

        // Add a single weapon to existing cache
        public static void AddWeaponToCache(ThingWithComps weapon)
        {
            if (weapon?.Map == null || !IsValidWeapon(weapon))
                return;

            var cache = GetOrCreateCache(weapon.Map);

            // NEW: Check cache size limit
            if (cache.Weapons.Count >= MaxWeaponsPerCache)
            {
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    Log.Message($"[AutoArm] Cache full ({MaxWeaponsPerCache} weapons), skipping {weapon.Label}");
                }
                return;
            }

            if (cache.Weapons.Contains(weapon))
                return;

            cache.Weapons.Add(weapon);

            var gridPos = new IntVec3(weapon.Position.x / GridCellSize, 0, weapon.Position.z / GridCellSize);
            if (!cache.SpatialIndex.ContainsKey(gridPos))
            {
                cache.SpatialIndex[gridPos] = new List<ThingWithComps>();
            }
            cache.SpatialIndex[gridPos].Add(weapon);

            cache.WeaponToGrid[weapon] = gridPos;
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

            int minX = (position.x - (int)maxDistance) / GridCellSize;
            int maxX = (position.x + (int)maxDistance) / GridCellSize;
            int minZ = (position.z - (int)maxDistance) / GridCellSize;
            int maxZ = (position.z + (int)maxDistance) / GridCellSize;

            for (int x = minX; x <= maxX; x++)
            {
                for (int z = minZ; z <= maxZ; z++)
                {
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

            if (AutoArmMod.settings?.debugLogging == true && result.Count == 0)
            {
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
            }

            // NEW: Also remove rebuild state
            if (rebuildStates.ContainsKey(map))
            {
                rebuildStates.Remove(map);
            }

            if (AutoArmMod.settings?.debugLogging == true)
            {
                Log.Message($"[AutoArm] Invalidated weapon cache for {map}");
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
                    rebuildStates.Remove(map); // NEW: Clean rebuild states too
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
                // Check if we're in the middle of a rebuild
                if (rebuildStates.TryGetValue(map, out var rebuildState))
                {
                    // Continue incremental rebuild
                    if (Find.TickManager.TicksGame - rebuildState.LastProcessTick >= RebuildDelayTicks)
                    {
                        ContinueRebuild(map, rebuildState);
                    }
                    return rebuildState.PartialCache;
                }
                else
                {
                    // Start new rebuild
                    StartRebuild(map);
                    return weaponCache.TryGetValue(map, out cache) ? cache : new WeaponCacheEntry();
                }
            }
            return cache;
        }

        // NEW: Start incremental rebuild
        private static void StartRebuild(Map map)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var allWeapons = map.listerThings.ThingsInGroup(ThingRequestGroup.Weapon).ToList();

            if (AutoArmMod.settings?.debugLogging == true)
            {
                Log.Message($"[AutoArm] Starting incremental rebuild for {map} - {allWeapons.Count} total weapons");
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

            // Process first chunk immediately
            ProcessRebuildChunk(map, state, MaxWeaponsPerRebuild);

            // If we processed everything in one go, finalize immediately
            if (state.RemainingWeapons.Count == 0)
            {
                weaponCache[map] = state.PartialCache;
                rebuildStates.Remove(map);

                sw.Stop();
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    Log.Message($"[AutoArm] Completed rebuild immediately with {state.PartialCache.Weapons.Count} weapons in {sw.ElapsedMilliseconds}ms");
                }
            }
            else
            {
                rebuildStates[map] = state;
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    Log.Message($"[AutoArm] Processed {state.ProcessedCount} weapons, {state.RemainingWeapons.Count} remaining");
                }
            }
        }

        // NEW: Continue incremental rebuild
        private static void ContinueRebuild(Map map, RebuildState state)
        {
            ProcessRebuildChunk(map, state, MaxWeaponsPerRebuild);
            state.LastProcessTick = Find.TickManager.TicksGame;

            if (state.RemainingWeapons.Count == 0 || state.PartialCache.Weapons.Count >= MaxWeaponsPerCache)
            {
                // Rebuild complete or cache full
                weaponCache[map] = state.PartialCache;
                rebuildStates.Remove(map);

                if (AutoArmMod.settings?.debugLogging == true)
                {
                    Log.Message($"[AutoArm] Completed incremental rebuild with {state.PartialCache.Weapons.Count} weapons");
                }
            }
        }

        // NEW: Process a chunk of weapons during rebuild
        private static void ProcessRebuildChunk(Map map, RebuildState state, int chunkSize)
        {
            int processed = 0;
            var entry = state.PartialCache;

            while (processed < chunkSize && state.RemainingWeapons.Count > 0 && entry.Weapons.Count < MaxWeaponsPerCache)
            {
                var thing = state.RemainingWeapons[0];
                state.RemainingWeapons.RemoveAt(0);

                var weapon = thing as ThingWithComps;
                if (weapon?.def == null || !IsValidWeapon(weapon))
                    continue;

                entry.Weapons.Add(weapon);

                var gridPos = new IntVec3(weapon.Position.x / GridCellSize, 0, weapon.Position.z / GridCellSize);
                if (!entry.SpatialIndex.ContainsKey(gridPos))
                {
                    entry.SpatialIndex[gridPos] = new List<ThingWithComps>();
                }
                entry.SpatialIndex[gridPos].Add(weapon);
                entry.WeaponToGrid[weapon] = gridPos;

                state.ProcessedCount++;
                processed++;
            }
        }

        private static bool IsValidWeapon(ThingWithComps weapon)
        {
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