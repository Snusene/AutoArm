
using AutoArm.Helpers;
using System;
using System.Collections.Generic;
using Verse;

namespace AutoArm.Caching
{
    internal sealed class TickExpiringLruCache<TKey, TVal>
    {
        private struct Slot
        {
            public TVal Value;
            public int ExpireTick;
            public long LastAccess;
        }

        private readonly Dictionary<TKey, Slot> entries;
        private readonly int ttl;
        private readonly int maxEntries;
        private readonly int evictionBatch;
        private long accessCounter;

        public TickExpiringLruCache(int initialCapacity, int ttl, int maxEntries, int evictionBatch = 0)
        {
            entries = new Dictionary<TKey, Slot>(initialCapacity);
            this.ttl = ttl;
            this.maxEntries = maxEntries;
            this.evictionBatch = evictionBatch > 0 ? evictionBatch : Math.Max(1, maxEntries / 4);
        }

        public int Count => entries.Count;

        public bool TryGet(TKey key, int currentTick, out TVal value)
        {
            if (entries.TryGetValue(key, out var slot) && currentTick < slot.ExpireTick)
            {
                slot.LastAccess = ++accessCounter;
                entries[key] = slot;
                value = slot.Value;
                return true;
            }
            value = default(TVal);
            return false;
        }

        public void Set(TKey key, TVal value, int currentTick)
        {
            entries[key] = new Slot
            {
                Value = value,
                ExpireTick = currentTick + ttl,
                LastAccess = ++accessCounter
            };
            if (entries.Count > maxEntries)
                EvictOldest();
        }

        public bool Remove(TKey key) => entries.Remove(key);

        public int RemoveWhere(Func<TKey, TVal, bool> predicate)
        {
            var toRemove = ListPool<TKey>.Get();
            foreach (var kvp in entries)
            {
                if (predicate(kvp.Key, kvp.Value.Value))
                    toRemove.Add(kvp.Key);
            }
            for (int i = 0; i < toRemove.Count; i++)
                entries.Remove(toRemove[i]);
            int n = toRemove.Count;
            ListPool<TKey>.Return(toRemove);
            return n;
        }

        public int CleanupExpired(int currentTick)
        {
            var toRemove = ListPool<TKey>.Get();
            foreach (var kvp in entries)
            {
                if (currentTick >= kvp.Value.ExpireTick)
                    toRemove.Add(kvp.Key);
            }
            for (int i = 0; i < toRemove.Count; i++)
                entries.Remove(toRemove[i]);
            int n = toRemove.Count;
            ListPool<TKey>.Return(toRemove);
            return n;
        }

        public void Clear()
        {
            entries.Clear();
            accessCounter = 0;
        }

        private void EvictOldest()
        {
            var pairs = ListPool<KeyValuePair<TKey, Slot>>.Get(entries.Count);
            foreach (var kvp in entries)
                pairs.Add(kvp);
            pairs.SortBy(kvp => kvp.Value.LastAccess);

            int take = Math.Min(evictionBatch, pairs.Count);
            for (int i = 0; i < take; i++)
                entries.Remove(pairs[i].Key);

            ListPool<KeyValuePair<TKey, Slot>>.Return(pairs);
        }
    }
}
