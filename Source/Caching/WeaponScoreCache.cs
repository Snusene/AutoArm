// AutoArm RimWorld 1.5+ mod - automatic weapon management
// This file: Simplified weapon restriction tracking
// 
// SIMPLIFIED: Instead of a separate blacklist system, we track
// "cannot equip" status directly in weapon scores as a special value

using AutoArm.Definitions;
using AutoArm.Helpers;
using AutoArm.Logging;
using AutoArm.Weapons;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace AutoArm.Caching
{
    /// <summary>
    /// Caches weapon scores per pawn to avoid recalculation
    /// Also tracks weapons that cannot be equipped due to mod restrictions
    /// </summary>
    public static class WeaponScoreCache
    {
        // Special score values
        public const float CANNOT_EQUIP = -1f;  // Weapon has mod restrictions
        public const float SCORE_EXPIRED = -2f; // Score needs recalculation
        
        private class ScoreEntry
        {
            public float Score { get; set; }
            public int LastUpdateTick { get; set; }
            public int PawnSkillHash { get; set; }
        }

        // Main cache: (pawn, weapon) -> score
        private static Dictionary<Pawn, Dictionary<ThingWithComps, ScoreEntry>> scoreCache = 
            new Dictionary<Pawn, Dictionary<ThingWithComps, ScoreEntry>>();

        // Track when pawn skills changed
        private static Dictionary<Pawn, int> pawnSkillHashes = new Dictionary<Pawn, int>();

        /// <summary>
        /// Get cached score for a pawn-weapon combination
        /// Returns CANNOT_EQUIP if weapon has mod restrictions
        /// </summary>
        public static float GetCachedScore(Pawn pawn, ThingWithComps weapon)
        {
            if (pawn == null || weapon == null)
                return 0f;

            // Check if we have a cached score
            if (scoreCache.TryGetValue(pawn, out var weaponScores))
            {
                if (weaponScores.TryGetValue(weapon, out var entry))
                {
                    int currentTick = Find.TickManager.TicksGame;
                    
                    // Check if score is for "cannot equip" - these expire after 60 seconds
                    if (entry.Score == CANNOT_EQUIP)
                    {
                        if (currentTick - entry.LastUpdateTick < Constants.WeaponBlacklistDuration)
                        {
                            return CANNOT_EQUIP; // Still blacklisted
                        }
                        // Expired - remove and recalculate
                        weaponScores.Remove(weapon);
                    }
                    // Check if normal score is still valid
                    else if (currentTick - entry.LastUpdateTick < Constants.WeaponScoreCacheLifetime)
                    {
                        // Check if pawn's skills changed
                        int currentSkillHash = GetPawnSkillHash(pawn);
                        if (entry.PawnSkillHash == currentSkillHash)
                        {
                            return entry.Score;
                        }
                    }
                }
            }

            // Calculate new score
            float score = CalculateWeaponScore(pawn, weapon);
            
            // Cache the result
            CacheScore(pawn, weapon, score);
            
            return score;
        }

        /// <summary>
        /// Calculate weapon score, checking for mod restrictions first
        /// </summary>
        private static float CalculateWeaponScore(Pawn pawn, ThingWithComps weapon)
        {
            // First check if pawn can even equip this weapon (mod restrictions)
            try
            {
                if (!EquipmentUtility.CanEquip(weapon, pawn))
                {
                    return CANNOT_EQUIP;
                }
            }
            catch (Exception ex)
            {
                // Some mods throw exceptions - treat as cannot equip
                AutoArmLogger.Error($"Exception checking CanEquip for {weapon.Label} on {pawn.LabelShort}: {ex.Message}");
                return CANNOT_EQUIP;
            }

            // Calculate actual score
            return WeaponScoringHelper.GetTotalScore(pawn, weapon);
        }

        /// <summary>
        /// Cache a score for a pawn-weapon combination
        /// </summary>
        private static void CacheScore(Pawn pawn, ThingWithComps weapon, float score)
        {
            if (!scoreCache.ContainsKey(pawn))
            {
                scoreCache[pawn] = new Dictionary<ThingWithComps, ScoreEntry>();
            }

            scoreCache[pawn][weapon] = new ScoreEntry
            {
                Score = score,
                LastUpdateTick = Find.TickManager.TicksGame,
                PawnSkillHash = GetPawnSkillHash(pawn)
            };
        }

        /// <summary>
        /// Get hash of pawn's combat skills for change detection
        /// </summary>
        private static int GetPawnSkillHash(Pawn pawn)
        {
            if (pawn?.skills == null)
                return 0;

            // Check cached hash
            if (pawnSkillHashes.TryGetValue(pawn, out int cachedHash))
            {
                return cachedHash;
            }

            // Calculate new hash
            int hash = 17;
            hash = hash * 31 + (int)(pawn.skills.GetSkill(SkillDefOf.Shooting)?.Level ?? 0);
            hash = hash * 31 + (int)(pawn.skills.GetSkill(SkillDefOf.Melee)?.Level ?? 0);
            
            // Include traits that affect weapon preference
            if (pawn.story?.traits != null)
            {
                hash = hash * 31 + (pawn.story.traits.HasTrait(TraitDefOf.Brawler) ? 1 : 0);
            }

            pawnSkillHashes[pawn] = hash;
            return hash;
        }

        /// <summary>
        /// Mark that a pawn's skills have changed
        /// </summary>
        public static void MarkPawnSkillsChanged(Pawn pawn)
        {
            if (pawn == null)
                return;

            // Remove cached skill hash to force recalculation
            pawnSkillHashes.Remove(pawn);
            
            // Invalidate all weapon scores for this pawn
            if (scoreCache.ContainsKey(pawn))
            {
                scoreCache[pawn].Clear();
            }
        }

        /// <summary>
        /// Get cached score with Combat Extended compatibility
        /// </summary>
        public static float GetCachedScoreWithCE(Pawn pawn, ThingWithComps weapon)
        {
            // Simply delegate to GetCachedScore - CE integration is now handled in WeaponScoringHelper
            return GetCachedScore(pawn, weapon);
        }

        /// <summary>
        /// Clean up cache periodically
        /// </summary>
        public static int CleanupCache()
        {
            int removedCount = 0;
            int currentTick = Find.TickManager.TicksGame;

            // Clean up dead pawns
            var deadPawns = scoreCache.Keys.Where(p => p == null || p.Destroyed || p.Dead).ToList();
            foreach (var pawn in deadPawns)
            {
                removedCount += scoreCache[pawn]?.Count ?? 0;
                scoreCache.Remove(pawn);
                pawnSkillHashes.Remove(pawn);
            }

            // Clean up destroyed weapons and expired scores
            foreach (var pawnEntry in scoreCache.ToList())
            {
                var weaponsToRemove = pawnEntry.Value
                    .Where(kvp => kvp.Key == null || 
                                 kvp.Key.Destroyed || 
                                 (kvp.Value.Score != CANNOT_EQUIP && 
                                  currentTick - kvp.Value.LastUpdateTick > Constants.WeaponScoreCacheLifetime * 2))
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var weapon in weaponsToRemove)
                {
                    pawnEntry.Value.Remove(weapon);
                    removedCount++;
                }

                // Remove pawn entry if empty
                if (pawnEntry.Value.Count == 0)
                {
                    scoreCache.Remove(pawnEntry.Key);
                    pawnSkillHashes.Remove(pawnEntry.Key);
                }
            }

            // Limit total cache size
            if (scoreCache.Sum(kvp => kvp.Value.Count) > Constants.MaxScoreCacheEntries)
            {
                // Remove oldest entries
                var allEntries = scoreCache.SelectMany(kvp => 
                    kvp.Value.Select(w => new { Pawn = kvp.Key, Weapon = w.Key, Entry = w.Value }))
                    .OrderBy(x => x.Entry.LastUpdateTick)
                    .Take(Constants.MaxScoreCacheEntries / 2)
                    .ToList();

                foreach (var entry in allEntries)
                {
                    if (scoreCache.ContainsKey(entry.Pawn))
                    {
                        scoreCache[entry.Pawn].Remove(entry.Weapon);
                        removedCount++;
                    }
                }
            }

            return removedCount;
        }

        /// <summary>
        /// Clear all cached scores
        /// </summary>
        public static void ClearCache()
        {
            scoreCache.Clear();
            pawnSkillHashes.Clear();
        }

        /// <summary>
        /// Clear all cached scores (alias for compatibility)
        /// </summary>
        public static void ClearAllCaches()
        {
            ClearCache();
        }
    }
}
