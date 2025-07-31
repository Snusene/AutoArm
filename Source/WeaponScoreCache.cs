using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace AutoArm
{
    public static class WeaponScoreCache
    {
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
                            return cached.Score;
                        }
                    }
                }
            }

            // Calculate fresh score using WeaponScoringHelper
            float score = WeaponScoringHelper.GetTotalScore(pawn, weapon);

            // Add any mod-specific bonuses that aren't part of base scoring
            score += GetModSpecificBonuses(weapon);

            // Cache the result
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
                CleanupCache();
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

        private static float GetOdysseyUniqueBonus(ThingWithComps weapon)
        {
            if (!IsOdysseyUniqueWeapon(weapon))
                return 0f;

            // Base bonus for being unique (they're rare and special)
            float bonus = 50f;

            // Additional bonus for traits (assume average of 2 beneficial traits)
            bonus += 100f; // ~50 points per beneficial trait

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
            if (weaponModifiedTick.TryGetValue(weapon.thingIDNumber, out int tick))
                return tick;
            return 0;
        }

        private static int GetPawnSkillChangedTick(Pawn pawn)
        {
            if (pawnSkillChangedTick.TryGetValue(pawn.thingIDNumber, out int tick))
                return tick;
            return 0;
        }

        public static void MarkWeaponModified(ThingWithComps weapon)
        {
            if (weapon != null)
                weaponModifiedTick[weapon.thingIDNumber] = Find.TickManager.TicksGame;
        }

        public static void MarkPawnSkillsChanged(Pawn pawn)
        {
            if (pawn != null)
                pawnSkillChangedTick[pawn.thingIDNumber] = Find.TickManager.TicksGame;
        }

        public static void CleanupCache()
        {
            int currentTick = Find.TickManager.TicksGame;

            // Remove old pawn cache entries
            var oldPawnEntries = pawnScoreCache
                .Where(kvp => currentTick - kvp.Value.CachedTick > PawnCacheLifetime * 2)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in oldPawnEntries)
            {
                pawnScoreCache.Remove(key);
            }

            // Also cleanup old weapon modification ticks
            var oldWeaponMods = weaponModifiedTick
                .Where(kvp => currentTick - kvp.Value > PawnCacheLifetime * 2)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var id in oldWeaponMods)
            {
                weaponModifiedTick.Remove(id);
            }

            // Cleanup old pawn skill ticks
            var oldPawnSkills = pawnSkillChangedTick
                .Where(kvp => currentTick - kvp.Value > PawnCacheLifetime * 2)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var id in oldPawnSkills)
            {
                pawnSkillChangedTick.Remove(id);
            }
        }

        public static void ClearAllCaches()
        {
            pawnScoreCache.Clear();
            weaponModifiedTick.Clear();
            pawnSkillChangedTick.Clear();
        }
        
        // Helper method for tests to inspect base weapon scores
        public static BaseWeaponScores GetBaseWeaponScore(ThingWithComps weapon)
        {
            if (weapon == null) return null;
            
            var scores = new BaseWeaponScores();
            
            // Get quality score
            QualityCategory quality;
            if (weapon.TryGetQuality(out quality))
            {
                scores.QualityScore = (float)quality * 20f; // Example scoring
            }
            
            // For ranged weapons
            if (weapon.def.IsRangedWeapon)
            {
                var verb = weapon.def.Verbs?.FirstOrDefault();
                if (verb != null)
                {
                    // Damage score (simplified)
                    var projectile = verb.defaultProjectile?.projectile;
                    if (projectile != null)
                    {
                        float damage = projectile.GetDamageAmount(weapon);
                        scores.DamageScore = damage * 10f;
                    }
                    
                    // Range score
                    scores.RangeScore = verb.range * 2f;
                }
            }
            // For melee weapons
            else if (weapon.def.IsMeleeWeapon)
            {
                float dps = weapon.GetStatValue(StatDefOf.MeleeWeapon_DamageMultiplier, true) *
                           weapon.def.GetStatValueAbstract(StatDefOf.MeleeWeapon_AverageDPS);
                scores.DamageScore = dps * 8f;
                scores.RangeScore = 0f; // Melee has no range
            }
            
            // Mod score
            scores.ModScore = GetModSpecificBonuses(weapon);
            
            return scores;
        }
    }
    
    // Helper class for base weapon scores
    public class BaseWeaponScores
    {
        public float QualityScore { get; set; }
        public float DamageScore { get; set; }
        public float RangeScore { get; set; }
        public float ModScore { get; set; }
    }
}