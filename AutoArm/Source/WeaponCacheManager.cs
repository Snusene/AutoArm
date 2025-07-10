using System.Collections.Generic;
using RimWorld;
using Verse;

namespace AutoArm
{
    public static class WeaponCacheManager
    {
        private static Dictionary<Map, List<ThingWithComps>> weaponCache = new Dictionary<Map, List<ThingWithComps>>();
        private static Dictionary<Map, int> weaponCacheAge = new Dictionary<Map, int>();
        private const int CacheLifetime = 500;

        public static List<ThingWithComps> GetCachedWeapons(Map map, Pawn pawn)
        {
            if (!weaponCache.TryGetValue(map, out var cached) ||
                !weaponCacheAge.TryGetValue(map, out var age) ||
                Find.TickManager.TicksGame - age > CacheLifetime)
            {
                RebuildCache(map, pawn);
            }
            return weaponCache[map];
        }

        private static void RebuildCache(Map map, Pawn pawn)
        {
            var weapons = new List<ThingWithComps>(200);
            var allMapWeapons = map.listerThings.ThingsInGroup(ThingRequestGroup.Weapon);

            foreach (var thing in allMapWeapons)
            {
                var weapon = thing as ThingWithComps;
                if (weapon?.def == null) continue;
                if (!weapon.def.IsRangedWeapon && !weapon.def.IsMeleeWeapon) continue;
                if (weapon.def.defName == "WoodLog") continue;
                if (weapon.IsForbidden(Faction.OfPlayer)) continue;

                // Special cases
                if (weapon.def.defName == "ElephantTusk" || weapon.def.defName == "ThrumboHorn" ||
                    WeaponThingFilterUtility.AllWeapons.Contains(weapon.def))
                {
                    weapons.Add(weapon);
                }
            }

            weaponCache[map] = weapons;
            weaponCacheAge[map] = Find.TickManager.TicksGame;
        }

        public static void InvalidateCache(Map map)
        {
            weaponCache.Remove(map);
            weaponCacheAge.Remove(map);
        }

        public static void CleanupCache()
        {
            var mapsToRemove = new List<Map>();
            foreach (var kvp in weaponCache)
            {
                if (kvp.Key == null || kvp.Key.Tile < 0 || !Find.Maps.Contains(kvp.Key))
                    mapsToRemove.Add(kvp.Key);
            }
            foreach (var map in mapsToRemove)
            {
                weaponCache.Remove(map);
                weaponCacheAge.Remove(map);
            }
        }
    }
}