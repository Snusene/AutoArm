// AutoArm RimWorld 1.5+ mod - automatic weapon management
// This file: Thread-safe generic caching system
// Used throughout the mod for temporary value caching

using AutoArm.Definitions;
using AutoArm.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace AutoArm.Caching
{
    /// <summary>
    /// Thread-safe generic caching system for temporary values
    /// </summary>
    public static class GenericCache
    {
        private static readonly System.Threading.ReaderWriterLockSlim cacheLock = new System.Threading.ReaderWriterLockSlim();
        private static Dictionary<string, object> cache = new Dictionary<string, object>();
        private static Dictionary<string, int> expiration = new Dictionary<string, int>();
        private const int DefaultCacheDuration = Constants.DefaultGenericCacheDuration; // 10 seconds

        /// <summary>
        /// Get or compute a cached value
        /// </summary>
        public static T GetCached<T>(string key, Func<T> computeValue, int cacheDuration = DefaultCacheDuration)
        {
            int currentTick = Find.TickManager?.TicksGame ?? 0;

            // Try read lock first
            cacheLock.EnterReadLock();
            try
            {
                if (cache.TryGetValue(key, out object cachedObj) &&
                    expiration.TryGetValue(key, out int exp) &&
                    currentTick < exp &&
                    cachedObj is T cachedValue)
                {
                    return cachedValue;
                }
            }
            finally
            {
                cacheLock.ExitReadLock();
            }

            // Compute new value outside of lock
            T value = computeValue();

            // Write lock to update cache
            cacheLock.EnterWriteLock();
            try
            {
                // Double-check in case another thread computed it
                if (cache.TryGetValue(key, out object cachedObj2) &&
                    expiration.TryGetValue(key, out int exp2) &&
                    currentTick < exp2 &&
                    cachedObj2 is T cachedValue2)
                {
                    return cachedValue2;
                }

                cache[key] = value;
                expiration[key] = currentTick + cacheDuration;
            }
            finally
            {
                cacheLock.ExitWriteLock();
            }

            return value;
        }

        /// <summary>
        /// Clear a specific cached value
        /// </summary>
        public static void ClearCache(string key)
        {
            cacheLock.EnterWriteLock();
            try
            {
                cache.Remove(key);
                expiration.Remove(key);
            }
            finally
            {
                cacheLock.ExitWriteLock();
            }
        }

        /// <summary>
        /// Clear all cached values
        /// </summary>
        public static void ClearAll()
        {
            cacheLock.EnterWriteLock();
            try
            {
                int count = cache.Count;
                cache.Clear();
                expiration.Clear();

                if (count > 0 && AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.Debug($"Cleared {count} cached values");
                }
            }
            finally
            {
                cacheLock.ExitWriteLock();
            }
        }

        /// <summary>
        /// Clean up expired entries
        /// </summary>
        public static int CleanupExpired()
        {
            int currentTick = Find.TickManager?.TicksGame ?? 0;
            List<string> expiredKeys = new List<string>();

            cacheLock.EnterWriteLock();
            try
            {
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
                }
            }
            finally
            {
                cacheLock.ExitWriteLock();
            }

            return expiredKeys.Count;
        }
    }
}