// AutoArm RimWorld 1.5+ mod - automatic weapon management
// This file: Weapon decision logging for debugging
// Tracks recent weapon evaluation decisions per pawn

using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace AutoArm
{
    public static class WeaponDecisionLog
    {
        public struct Decision
        {
            public string weaponName;
            public float score;
            public string reason;
            public int tick;
        }

        private static Dictionary<Pawn, List<Decision>> recentDecisions = new Dictionary<Pawn, List<Decision>>();
        private const int MaxDecisionsPerPawn = 20;

        public static void LogDecision(Pawn pawn, ThingWithComps weapon, float score, string reason = null)
        {
            if (!AutoArmMod.settings.debugLogging) return;

            if (!recentDecisions.ContainsKey(pawn))
                recentDecisions[pawn] = new List<Decision>();

            recentDecisions[pawn].Add(new Decision
            {
                weaponName = weapon.Label,
                score = score,
                reason = reason ?? "Evaluated",
                tick = Find.TickManager.TicksGame
            });

            if (recentDecisions[pawn].Count > MaxDecisionsPerPawn)
                recentDecisions[pawn].RemoveAt(0);
        }

        public static void PrintRecentDecisions(Pawn pawn)
        {
            if (!recentDecisions.TryGetValue(pawn, out var decisions))
            {
                Log.Message($"[AutoArm] No recent weapon decisions for {pawn.Name}");
                return;
            }

            Log.Message($"\n[AutoArm] Recent weapon decisions for {pawn.Name}:");

            int startIndex = Math.Max(0, decisions.Count - 10);
            for (int i = startIndex; i < decisions.Count; i++)
            {
                var decision = decisions[i];
                var ticksAgo = Find.TickManager.TicksGame - decision.tick;
                Log.Message($"  {decision.weaponName}: Score={decision.score:F1}, Reason={decision.reason}, {ticksAgo} ticks ago");
            }
        }

        public static void Cleanup()
        {
            var toRemove = recentDecisions.Keys
                .Where(p => p.DestroyedOrNull() || p.Dead)
                .ToList();

            foreach (var pawn in toRemove)
                recentDecisions.Remove(pawn);
        }
    }
}