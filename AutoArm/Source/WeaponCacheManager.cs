using RimWorld;
using System.Collections.Generic;
using Verse;

public class ImprovedWeaponCacheManager
{
    private class WeaponCacheEntry
    {
        public List<ThingWithComps> Weapons { get; set; }
        public int LastUpdateTick { get; set; }
        public Dictionary<IntVec3, List<ThingWithComps>> SpatialIndex { get; set; }

        public WeaponCacheEntry()
        {
            Weapons = new List<ThingWithComps>();
            SpatialIndex = new Dictionary<IntVec3, List<ThingWithComps>>();
        }
    }

    private static Dictionary<Map, WeaponCacheEntry> weaponCache = new Dictionary<Map, WeaponCacheEntry>();
    private const int CacheLifetime = 250;
    private const int GridSize = 10; // For spatial indexing

    public static List<ThingWithComps> GetWeaponsNear(Map map, IntVec3 position, float maxDistance)
    {
        var cache = GetOrCreateCache(map);

        // Use spatial index for faster lookups
        var result = new List<ThingWithComps>();
        var maxDistSquared = maxDistance * maxDistance;

        // Calculate grid cells to check
        int minX = (position.x - (int)maxDistance) / GridSize;
        int maxX = (position.x + (int)maxDistance) / GridSize;
        int minZ = (position.z - (int)maxDistance) / GridSize;
        int maxZ = (position.z + (int)maxDistance) / GridSize;

        for (int x = minX; x <= maxX; x++)
        {
            for (int z = minZ; z <= maxZ; z++)
            {
                var gridKey = new IntVec3(x * GridSize, 0, z * GridSize);
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

        return result;
    }

    private static WeaponCacheEntry GetOrCreateCache(Map map)
    {
        if (!weaponCache.TryGetValue(map, out var cache) ||
            Find.TickManager.TicksGame - cache.LastUpdateTick > CacheLifetime)
        {
            cache = RebuildCache(map);
            weaponCache[map] = cache;
        }
        return cache;
    }

    private static WeaponCacheEntry RebuildCache(Map map)
    {
        var entry = new WeaponCacheEntry();
        entry.LastUpdateTick = Find.TickManager.TicksGame;

        var allWeapons = map.listerThings.ThingsInGroup(ThingRequestGroup.Weapon);

        foreach (var thing in allWeapons)
        {
            var weapon = thing as ThingWithComps;
            if (weapon?.def == null || !IsValidWeapon(weapon)) continue;

            entry.Weapons.Add(weapon);

            // Add to spatial index
            var gridKey = new IntVec3(
                (weapon.Position.x / GridSize) * GridSize,
                0,
                (weapon.Position.z / GridSize) * GridSize
            );

            if (!entry.SpatialIndex.ContainsKey(gridKey))
                entry.SpatialIndex[gridKey] = new List<ThingWithComps>();

            entry.SpatialIndex[gridKey].Add(weapon);
        }

        return entry;
    }

    private static bool IsValidWeapon(ThingWithComps weapon)
    {
        if (!weapon.def.IsWeapon || weapon.def.IsApparel) return false;
        if (weapon.def.defName == "WoodLog") return false;
        if (weapon.IsForbidden(Faction.OfPlayer)) return false;
        return true;
    }
}