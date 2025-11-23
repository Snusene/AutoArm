
using System;
using System.Collections.Generic;
using Verse;

namespace AutoArm.Helpers
{
    /// <summary>
    /// Generic object pool for List&lt;T&gt; instances.
    /// Reduce GC pressure
    /// Dynamic pool
    /// </summary>
    public static class ListPool<T>
    {
        private static readonly Stack<List<T>> _pool = new Stack<List<T>>();

        private const int DefaultCapacity = 16;

        private const int MinPoolSize = 10;
        private const int MaxPoolSize = 50;

        /// <summary>
        /// Get/allocate list
        /// Returned list is guaranteed to be empty (Clear() already called).
        /// </summary>
        /// <param name="capacity">Initial capacity if a new list must be allocated</param>
        /// <returns>An empty List&lt;T&gt; ready for use</returns>
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

        /// <summary>
        /// Return a list to the pool for reuse.
        /// List will be cleared and may be reused by future Get() calls.
        /// If pool is full, list is discarded (not added to pool).
        /// </summary>
        /// <param name="list">The list to return (null is ignored)</param>
        public static void Return(List<T> list)
        {
            if (list == null) return;

            list.Clear();

            if (_pool.Count < GetDynamicPoolSize())
            {
                _pool.Push(list);
            }
        }

        /// <summary>
        /// Pool size
        /// </summary>
        public static int CurrentPoolSize => _pool.Count;

        /// <summary>
        /// Max pool size
        /// </summary>
        public static int MaximumPoolSize => GetDynamicPoolSize();

        /// <summary>
        /// Clear the entire pool (for cleanup or testing)
        /// </summary>
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
                    colonistCount += map.mapPawns?.FreeColonistsCount ?? 0;
                }
            }

            return Math.Min(MaxPoolSize, Math.Max(MinPoolSize, colonistCount / 2));
        }
    }
}
