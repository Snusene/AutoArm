// AutoArm RimWorld 1.5+ mod - automatic weapon management
// This file: Settings value caching to reduce redundant lookups
// Prevents excessive reflection calls for SimpleSidearms settings
// Uses: Timed cache expiration for dynamic setting changes
// Note: Centralized caching prevents duplicate cache implementations

using System;
using System.Collections.Generic;
using Verse;

namespace AutoArm
{
    /// <summary>
    /// Centralized settings cache management (fixes #14)
    /// Prevents duplicate caching systems
    /// </summary>
    public static class SettingsCacheHelper
    {
        private class CachedValue<T>
        {
            public T Value { get; set; }
            public int ExpirationTick { get; set; }
        }

        private static Dictionary<string, object> cache = new Dictionary<string, object>();
        private const int DefaultCacheDuration = 600; // 10 seconds

        /// <summary>
        /// Get or compute a cached value
        /// </summary>
        public static T GetCached<T>(string key, Func<T> computeValue, int cacheDuration = DefaultCacheDuration)
        {
            int currentTick = Find.TickManager.TicksGame;

            if (cache.TryGetValue(key, out object cachedObj) && cachedObj is CachedValue<T> cached)
            {
                if (currentTick < cached.ExpirationTick)
                {
                    return cached.Value;
                }
            }

            // Compute new value
            T value = computeValue();

            // Cache it
            cache[key] = new CachedValue<T>
            {
                Value = value,
                ExpirationTick = currentTick + cacheDuration
            };

            return value;
        }

        /// <summary>
        /// Clear a specific cached value
        /// </summary>
        public static void ClearCache(string key)
        {
            cache.Remove(key);
        }

        /// <summary>
        /// Clear all cached values
        /// </summary>
        public static void ClearAllCaches()
        {
            cache.Clear();
        }

        /// <summary>
        /// Clean up expired cache entries
        /// </summary>
        public static void CleanupExpiredEntries()
        {
            int currentTick = Find.TickManager.TicksGame;
            var toRemove = new List<string>();

            foreach (var kvp in cache)
            {
                if (kvp.Value is CachedValue<object> cached && currentTick >= cached.ExpirationTick)
                {
                    toRemove.Add(kvp.Key);
                }
            }

            foreach (var key in toRemove)
            {
                cache.Remove(key);
            }
        }

        /// <summary>
        /// Get a cached setting value
        /// </summary>
        public static T GetCachedSetting<T>(string key, int cacheDuration = DefaultCacheDuration)
        {
            int currentTick = Find.TickManager.TicksGame;

            if (cache.TryGetValue(key, out object cachedObj) && cachedObj is CachedValue<T> cached)
            {
                if (currentTick < cached.ExpirationTick)
                {
                    return cached.Value;
                }
            }

            return default(T);
        }

        /// <summary>
        /// Set a cached setting value
        /// </summary>
        public static void SetCachedSetting<T>(string key, T value, int cacheDuration = DefaultCacheDuration)
        {
            int currentTick = Find.TickManager.TicksGame;

            cache[key] = new CachedValue<T>
            {
                Value = value,
                ExpirationTick = currentTick + cacheDuration
            };
        }

        // Specific cached settings for SimpleSidearms
        public static bool GetSimpleSidearmsSkipDangerous()
        {
            return GetCached("SS_SkipDangerous", () =>
            {
                if (!SimpleSidearmsCompat.IsLoaded())
                    return false;

                // This would call the reflection-based method to get the actual value
                // For now, return the default
                return true;
            });
        }

        public static bool GetSimpleSidearmsSkipEMP()
        {
            return GetCached("SS_SkipEMP", () =>
            {
                if (!SimpleSidearmsCompat.IsLoaded())
                    return false;

                // This would call the reflection-based method to get the actual value
                // For now, return the default
                return false;
            });
        }

        public static bool GetSimpleSidearmsAllowBlocked()
        {
            return GetCached("SS_AllowBlocked", () =>
            {
                if (!SimpleSidearmsCompat.IsLoaded())
                    return true;

                // This would call the reflection-based method to get the actual value
                // For now, return the default
                return false;
            });
        }
    }
}