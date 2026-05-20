
using System;
using System.Collections.Generic;
using Verse;

namespace AutoArm.Helpers
{
    internal static class ListPoolSize
    {
        private const int MinPoolSize = 10;
        private const int MaxPoolSize = 50;

        private static int _cachedSize = MinPoolSize;
        private static int _cachedTick = int.MinValue;

        public static int Get()
        {
            int now = Find.TickManager?.TicksGame ?? 0;
            if (now == _cachedTick)
                return _cachedSize;

            int colonistCount = 0;
            if (Find.Maps != null)
            {
                foreach (var map in Find.Maps)
                {
                    colonistCount += map.mapPawns?.FreeColonistsSpawnedCount ?? 0;
                }
            }

            _cachedSize = Math.Min(MaxPoolSize, Math.Max(MinPoolSize, colonistCount / 2));
            _cachedTick = now;
            return _cachedSize;
        }
    }

    internal static class ListPool<T>
    {
        private static readonly Stack<List<T>> _pool = new Stack<List<T>>();

        private const int DefaultCapacity = 16;

        public static List<T> Get(int capacity = DefaultCapacity)
        {
            if (_pool.Count > 0)
            {
                var list = _pool.Pop();
                list.Clear();
                return list;
            }
            return new List<T>(capacity);
        }

        public static void Return(List<T> list)
        {
            if (list == null) return;

            list.Clear();

            if (_pool.Count < ListPoolSize.Get())
            {
                _pool.Push(list);
            }
        }

    }

    internal static class HashSetPool<T>
    {
        private static readonly Stack<HashSet<T>> _pool = new Stack<HashSet<T>>();

        public static HashSet<T> Get()
        {
            if (_pool.Count > 0)
            {
                var set = _pool.Pop();
                set.Clear();
                return set;
            }
            return new HashSet<T>();
        }

        public static void Return(HashSet<T> set)
        {
            if (set == null) return;

            set.Clear();

            if (_pool.Count < ListPoolSize.Get())
            {
                _pool.Push(set);
            }
        }
    }
}
