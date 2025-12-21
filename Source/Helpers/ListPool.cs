
using System;
using System.Collections.Generic;
using Verse;

namespace AutoArm.Helpers
{
    public static class ListPool<T>
    {
        private static readonly Stack<List<T>> _pool = new Stack<List<T>>();

        private const int DefaultCapacity = 16;

        private const int MinPoolSize = 10;
        private const int MaxPoolSize = 50;

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

            if (_pool.Count < GetDynamicPoolSize())
            {
                _pool.Push(list);
            }
        }

        public static int CurrentPoolSize => _pool.Count;

        public static int MaximumPoolSize => GetDynamicPoolSize();

        public static void ClearPool()
        {
            _pool.Clear();
        }


        private static int GetDynamicPoolSize()
        {
            int colonistCount = 0;

            if (Find.Maps != null)
            {
                foreach (var map in Find.Maps)
                {
                    colonistCount += map.mapPawns?.FreeColonistsSpawnedCount ?? 0;
                }
            }

            return Math.Min(MaxPoolSize, Math.Max(MinPoolSize, colonistCount / 2));
        }
    }
}
