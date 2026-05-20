using System.Collections.Generic;
using Verse;

namespace AutoArm.Testing.Helpers
{
    internal static class CleanupTracker
    {
        private static readonly HashSet<Thing> destroyedThings = new HashSet<Thing>();
        private static readonly HashSet<Pawn> destroyedPawns = new HashSet<Pawn>();
        private static readonly HashSet<Thing> createdThings = new HashSet<Thing>();
        private static readonly HashSet<Pawn> createdPawns = new HashSet<Pawn>();

        public static bool IsDestroyed(Thing thing)
        {
            if (thing == null) return true;
            return thing.Destroyed || destroyedThings.Contains(thing);
        }

        public static bool IsDestroyed(Pawn pawn)
        {
            if (pawn == null) return true;
            return pawn.Destroyed || destroyedPawns.Contains(pawn);
        }

        public static void MarkDestroyed(Thing thing)
        {
            if (thing != null) destroyedThings.Add(thing);
        }

        public static void MarkDestroyed(Pawn pawn)
        {
            if (pawn != null) destroyedPawns.Add(pawn);
        }

        public static void MarkCreated(Thing thing)
        {
            if (thing != null) createdThings.Add(thing);
        }

        public static void MarkCreated(Pawn pawn)
        {
            if (pawn != null) createdPawns.Add(pawn);
        }

        public static bool IsCreatedForTest(Thing thing)
            => thing != null && createdThings.Contains(thing);

        public static bool IsCreatedForTest(Pawn pawn)
            => pawn != null && createdPawns.Contains(pawn);

        public static IReadOnlyCollection<Thing> CreatedThings => createdThings;
        public static IReadOnlyCollection<Pawn> CreatedPawns => createdPawns;

        public static void Reset()
        {
            destroyedThings.Clear();
            destroyedPawns.Clear();
            createdThings.Clear();
            createdPawns.Clear();
        }
    }
}
