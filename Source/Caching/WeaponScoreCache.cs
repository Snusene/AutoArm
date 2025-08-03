// AutoArm RimWorld 1.5+ mod - automatic weapon management
// This file: Performance-optimized weapon scoring cache with pawn-specific calculations
// Uses WeaponScoringHelper for base scores, CECompat for ammo modifiers, InfusionCompat for mod bonuses

using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using AutoArm.Helpers;
using AutoArm.Weapons;
using AutoArm.Logging;
using AutoArm.Caching;

namespace AutoArm.Caching
{
    public static class WeaponScoreCache
    {
        // Thread safety lock
        private static readonly object cacheLock = new object();
        
        // Cache for pawn-specific scores
        private static Dictionary<(int weaponId, int pawnId), CachedScore> pawnScoreCache = new Dictionary<(int, int), CachedScore>();

        // Track when weapons were last modified
        private static Dictionary<int, int> weaponModifiedTick = new Dictionary<int, int>();

        // Track when pawn skills last changed
        private static Dictionary<int, int> pawnSkillChangedTick = new Dictionary<int, int>();

        private const int PawnCacheLifetime = 1200;  // ~20 seconds
        private const int MaxCacheEntries = 5000;

        internal class CachedScore
        {
            public float Score { get; set; }
            public int CachedTick { get; set; }
            public int WeaponModTick { get; set; }
            public int PawnSkillTick { get; set; }
        }

        public static float GetCachedScore(Pawn pawn, ThingWithComps weapon)
        {
            if (weapon == null || pawn == null)
                return 0f;

            int currentTick = Find.TickManager.TicksGame;
            var cacheKey = (weapon.thingIDNumber, pawn.thingIDNumber);

            // Check if we have a valid cached score
            lock (cacheLock)
            {
                if (pawnScoreCache.TryGetValue(cacheKey, out var cached))
                {
                    // Check if cache is still valid
                    if (currentTick - cached.CachedTick < PawnCacheLifetime)
                    {
                        // Check if weapon hasn't been modified
                        int weaponModTick = GetWeaponModifiedTick(weapon);
                        if (weaponModTick <= cached.WeaponModTick)
                        {
                            // Check if pawn skills haven't changed
                            int pawnSkillTick = GetPawnSkillChangedTick(pawn);
                            if (pawnSkillTick <= cached.PawnSkillTick)
                            {
                                if (Prefs.DevMode && Find.TickManager.TicksGame % 600 == 0) // Log every 10 seconds in dev mode
                                    AutoArmLogger.Debug($"WeaponScoreCache hit: {weapon.Label} for {pawn.LabelShort}");
                                return cached.Score;
                            }
                        }
                    }
                }
            }

            float score = 0f;
            try
            {
                // Calculate fresh score using WeaponScoringHelper
                score = WeaponScoringHelper.GetTotalScore(pawn, weapon);

                // Add any mod-specific bonuses that aren't part of base scoring
                score += GetModSpecificBonuses(weapon);
            }
            catch (Exception ex)
            {
                AutoArmLogger.Error($"Failed to calculate weapon score for {weapon.Label}: {ex.Message}", ex);
                return 0f;
            }

            // Cache the result
            lock (cacheLock)
            {
                pawnScoreCache[cacheKey] = new CachedScore
                {
                    Score = score,
                    CachedTick = currentTick,
                    WeaponModTick = GetWeaponModifiedTick(weapon),
                    PawnSkillTick = GetPawnSkillChangedTick(pawn)
                };

                // Clean cache if too large
                if (pawnScoreCache.Count > MaxCacheEntries)
                {
                    AutoArmLogger.Warn($"WeaponScoreCache exceeded {MaxCacheEntries} entries, cleaning up");
                    CleanupCache();
                }
            }

            return score;
        }

        /// <summary>
        /// Get mod-specific bonuses that aren't part of the base weapon scoring
        /// </summary>
        private static float GetModSpecificBonuses(ThingWithComps weapon)
        {
            float bonus = 0f;

            // Infusion 2 mod bonuses
            if (InfusionCompat.IsLoaded())
            {
                bonus += InfusionCompat.GetInfusionScoreBonus(weapon);
            }

            // Odyssey unique weapon bonus
            bonus += GetOdysseyUniqueBonus(weapon);

            return bonus;
        }

        private const float OdysseyUniqueBaseBonus = 50f;
        private const float OdysseyUniqueTraitBonus = 100f; // ~50 points per beneficial trait

        private static float GetOdysseyUniqueBonus(ThingWithComps weapon)
        {
            if (!IsOdysseyUniqueWeapon(weapon))
                return 0f;

            // Base bonus for being unique (they're rare and special)
            float bonus = OdysseyUniqueBaseBonus;

            // Additional bonus for traits (assume average of 2 beneficial traits)
            bonus += OdysseyUniqueTraitBonus;

            return bonus;
        }

        private static bool IsOdysseyUniqueWeapon(ThingWithComps weapon)
        {
            if (weapon?.def == null)
                return false;

            // Check if Odyssey DLC is active
            if (!ModsConfig.OdysseyActive)
                return false;

            // Based on debug output, unique weapons have "_Unique" in their def name
            // Example: "Gun_Revolver_Unique"
            return weapon.def.defName.Contains("_Unique");
        }

        public static float GetCachedScoreWithCE(Pawn pawn, ThingWithComps weapon)
        {
            float baseScore = GetCachedScore(pawn, weapon);

            // Apply Combat Extended ammo score modifier if CE is loaded and ammo checking is enabled
            if (CECompat.ShouldCheckAmmo())
            {
                float ammoModifier = CECompat.GetAmmoScoreModifier(weapon, pawn);
                baseScore *= ammoModifier;
            }

            return baseScore;
        }

        private static int GetWeaponModifiedTick(ThingWithComps weapon)
        {
            lock (cacheLock)
            {
                if (weaponModifiedTick.TryGetValue(weapon.thingIDNumber, out int tick))
                    return tick;
                return 0;
            }
        }

        private static int GetPawnSkillChangedTick(Pawn pawn)
        {
            lock (cacheLock)
            {
                if (pawnSkillChangedTick.TryGetValue(pawn.thingIDNumber, out int tick))
                    return tick;
                return 0;
            }
        }

        public static void MarkWeaponModified(ThingWithComps weapon)
        {
            if (weapon != null)
            {
                lock (cacheLock)
                {
                    weaponModifiedTick[weapon.thingIDNumber] = Find.TickManager.TicksGame;
                }
            }
        }

        public static void MarkPawnSkillsChanged(Pawn pawn)
        {
            if (pawn != null)
            {
                lock (cacheLock)
                {
                    pawnSkillChangedTick[pawn.thingIDNumber] = Find.TickManager.TicksGame;
                }
            }
        }

        public static int CleanupCache()
        {
            int removed = 0;
            
            lock (cacheLock)
            {
                int currentTick = Find.TickManager.TicksGame;
                int lifetimeThreshold = PawnCacheLifetime * 2;

                // Cleanup old pawn cache entries using LINQ
                // Use smart pre-allocation based on cache size
                var pawnKeysToRemove = pawnScoreCache
                    .Where(kvp => currentTick - kvp.Value.CachedTick > lifetimeThreshold)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var key in pawnKeysToRemove)
                {
                    pawnScoreCache.Remove(key);
                    removed++;
                }

                // Cleanup old weapon modification ticks
                var weaponKeysToRemove = weaponModifiedTick
                    .Where(kvp => currentTick - kvp.Value > lifetimeThreshold)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var id in weaponKeysToRemove)
                {
                    weaponModifiedTick.Remove(id);
                    removed++;
                }

                // Cleanup old pawn skill ticks
                var pawnSkillKeysToRemove = pawnSkillChangedTick
                    .Where(kvp => currentTick - kvp.Value > lifetimeThreshold)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var id in pawnSkillKeysToRemove)
                {
                    pawnSkillChangedTick.Remove(id);
                    removed++;
                }
                
                if (Prefs.DevMode && (pawnKeysToRemove.Count > 0 || weaponKeysToRemove.Count > 0 || pawnSkillKeysToRemove.Count > 0))
                {
                    AutoArmLogger.Debug($"WeaponScoreCache cleanup: removed {pawnKeysToRemove.Count} scores, {weaponKeysToRemove.Count} weapon ticks, {pawnSkillKeysToRemove.Count} skill ticks");
                }
            }
            
            return removed;
        }

        public static void ClearAllCaches()
        {
            lock (cacheLock)
            {
                int totalEntries = pawnScoreCache.Count + weaponModifiedTick.Count + pawnSkillChangedTick.Count;
                if (totalEntries > 0)
                    AutoArmLogger.Debug($"WeaponScoreCache cleared: {totalEntries} total entries removed");
                    
                pawnScoreCache.Clear();
                weaponModifiedTick.Clear();
                pawnSkillChangedTick.Clear();
            }
        }
        // Cached dummy pawn for test purposes
        private static Pawn _testDummyPawn;
        private static readonly object _dummyPawnLock = new object();
        private static Pawn TestDummyPawn
        {
            get
            {
                if (_testDummyPawn == null || _testDummyPawn.Destroyed)
                {
                    lock (_dummyPawnLock)
                    {
                        // Double-check after acquiring lock
                        if (_testDummyPawn == null || _testDummyPawn.Destroyed)
                        {
                            _testDummyPawn = new Pawn();
                            _testDummyPawn.skills = new Pawn_SkillTracker(_testDummyPawn);
                        }
                    }
                }
                return _testDummyPawn;
            }
        }
        
        // Helper method for tests to inspect weapon scores
        public static BaseWeaponScores GetBaseWeaponScore(ThingWithComps weapon)
        {
            if (weapon == null) return null;
            
            var scores = new BaseWeaponScores();
            
            // Use cached dummy pawn for weapon property score calculation
            // This gives us the full weapon score without pawn-specific modifiers
            scores.WeaponPropertyScore = WeaponScoringHelper.GetWeaponPropertyScore(TestDummyPawn, weapon);
            
            // Get quality for informational purposes
            if (weapon.TryGetQuality(out QualityCategory quality))
            {
                scores.QualityCategory = quality;
            }
            
            // Get mod-specific bonuses
            scores.ModScore = GetModSpecificBonuses(weapon);
            
            // For ranged weapons, extract basic info
            if (weapon.def.IsRangedWeapon)
            {
                var verb = weapon.def.Verbs?.FirstOrDefault();
                if (verb != null)
                {
                    scores.Range = verb.range;
                    scores.BurstCount = verb.burstShotCount;
                }
            }
            
            return scores;
        }
    }
    
    // Helper class for weapon score inspection
    public class BaseWeaponScores
    {
        public float WeaponPropertyScore { get; set; } // Combined score from WeaponScoringHelper
        public float ModScore { get; set; }
        public QualityCategory QualityCategory { get; set; }
        public float Range { get; set; } // For informational purposes
        public int BurstCount { get; set; } // For informational purposes
    }
}