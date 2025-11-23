
using AutoArm.Definitions;
using AutoArm.Helpers;
using AutoArm.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace AutoArm.Caching
{
    /// <summary>
    /// Generic LRU cache
    /// </summary>
    public static class GenericCache
    {
        private static Dictionary<string, object> cache = new Dictionary<string, object>();

        private static Dictionary<string, int> expiration = new Dictionary<string, int>();
        private static Dictionary<string, int> lastAccess = new Dictionary<string, int>();

        private const int DefaultCacheDuration = Constants.DefaultGenericCacheDuration;

        private const int MaxCacheSize = 500;
        private const int EvictionBatchSize = 50;

        private static int _monotonicTick = 0;

        /// <summary>
        /// Cached value
        /// </summary>
        public static T GetCached<T>(string key, Func<T> computeValue, int cacheDuration = DefaultCacheDuration)
        {
            int currentTick = Find.TickManager?.TicksGame ?? (++_monotonicTick);

            if (cache.TryGetValue(key, out object cachedObj) &&
                expiration.TryGetValue(key, out int exp) &&
                currentTick < exp &&
                cachedObj is T cachedValue)
            {
                lastAccess[key] = currentTick;
                return cachedValue;
            }

            T value = computeValue();

            if (cache.Count >= MaxCacheSize)
            {
                EvictOldestEntries();
            }

            cache[key] = value;
            expiration[key] = currentTick + cacheDuration;
            lastAccess[key] = currentTick;

            return value;
        }


        private static void EvictOldestEntries()
        {
            int sampleSize = Math.Min(lastAccess.Count, EvictionBatchSize * 4);

            var sampleEntries = ListPool<KeyValuePair<string, int>>.Get(sampleSize);
            int count = 0;
            foreach (var kvp in lastAccess)
            {
                if (count++ >= sampleSize) break;
                sampleEntries.Add(kvp);
            }

            sampleEntries.SortBy(kvp => kvp.Value);

            var sample = ListPool<string>.Get();
            int entriesToTake = Math.Min(EvictionBatchSize, sampleEntries.Count);
            for (int i = 0; i < entriesToTake; i++)
            {
                sample.Add(sampleEntries[i].Key);
            }

            ListPool<KeyValuePair<string, int>>.Return(sampleEntries);

            foreach (var key in sample)
            {
                cache.Remove(key);
                expiration.Remove(key);
                lastAccess.Remove(key);
            }

            int sampleCount = sample.Count;
            ListPool<string>.Return(sample);

            AutoArmLogger.Debug(() => $"GenericCache: Evicted {sampleCount} oldest entries (sampled {sampleSize} from {MaxCacheSize})");
        }

        /// <summary>
        /// Clear value
        /// </summary>
        public static void ClearCache(string key)
        {
            cache.Remove(key);
            expiration.Remove(key);
            lastAccess.Remove(key);
        }

        /// <summary>
        /// Clear all
        /// </summary>
        public static void ClearAll()
        {
            int count = cache.Count;
            cache.Clear();
            expiration.Clear();
            lastAccess.Clear();

            if (count > 0 && AutoArmMod.settings?.debugLogging == true)
            {
                AutoArmLogger.Debug(() => $"GenericCache: Cleared {count} cached values");
            }
        }

        /// <summary>
        /// Cleanup expired entries
        /// </summary>
        public static int CleanupExpired()
        {
            int currentTick = Find.TickManager?.TicksGame ?? 0;
            var expiredKeys = ListPool<string>.Get();

            foreach (var kvp in expiration)
            {
                if (currentTick >= kvp.Value)
                {
                    expiredKeys.Add(kvp.Key);
                }
            }

            foreach (var key in expiredKeys)
            {
                cache.Remove(key);
                expiration.Remove(key);
                lastAccess.Remove(key);
            }

            int removedCount = expiredKeys.Count;
            ListPool<string>.Return(expiredKeys);

            if (cache.Count > MaxCacheSize)
            {
                EvictOldestEntries();
            }

            return removedCount;
        }

        /// <summary>
        /// Cache stats
        /// </summary>
        public static (int count, int maxSize) GetCacheStats()
        {
            return (cache.Count, MaxCacheSize);
        }
    }
}
