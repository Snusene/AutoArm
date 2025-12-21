using System.Collections.Generic;
using Verse;

namespace AutoArm.Testing.Framework
{
    /// <summary>
    /// Tracks objects that have been destroyed during test cleanup to prevent double-destroy errors
    /// </summary>
    public static class CleanupTracker
    {
        private static HashSet<Thing> destroyedThings = new HashSet<Thing>();
        private static HashSet<Pawn> destroyedPawns = new HashSet<Pawn>();

        /// <summary>
        /// Check if a thing has already been destroyed in this cleanup cycle
        /// </summary>
        public static bool IsDestroyed(Thing thing)
        {
            if (thing == null) return true;
            return thing.Destroyed || destroyedThings.Contains(thing);
        }

        /// <summary>
        /// Check if a pawn has already been destroyed in this cleanup cycle
        /// </summary>
        public static bool IsDestroyed(Pawn pawn)
        {
            if (pawn == null) return true;
            return pawn.Destroyed || destroyedPawns.Contains(pawn);
        }

        /// <summary>
        /// Mark a thing as destroyed
        /// </summary>
        public static void MarkDestroyed(Thing thing)
        {
            if (thing != null)
            {
                destroyedThings.Add(thing);
            }
        }

        /// <summary>
        /// Mark a pawn as destroyed
        /// </summary>
        public static void MarkDestroyed(Pawn pawn)
        {
            if (pawn != null)
            {
                destroyedPawns.Add(pawn);
            }
        }

        /// <summary>
        /// Clear the tracking lists (call at start of each test)
        /// </summary>
        public static void Reset()
        {
            destroyedThings.Clear();
            destroyedPawns.Clear();
        }
    }
}
