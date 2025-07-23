using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;

namespace AutoArm
{
    public class WeaponScoreCache
    {
        // Cache for base weapon scores (don't depend on pawn)
        private static Dictionary<ThingWithComps, BaseWeaponScore> baseScoreCache = new Dictionary<ThingWithComps, BaseWeaponScore>();
        
        // Cache for pawn-specific scores
        private static Dictionary<(int weaponId, int pawnId), CachedScore> pawnScoreCache = new Dictionary<(int, int), CachedScore>();
        
        // Track when weapons were last modified
        private static Dictionary<int, int> weaponModifiedTick = new Dictionary<int, int>();
        
        // Track when pawn skills last changed
        private static Dictionary<int, int> pawnSkillChangedTick = new Dictionary<int, int>();
        
        private const int BaseCacheLifetime = 2500;  // ~40 seconds
        private const int PawnCacheLifetime = 1200;  // ~20 seconds
        private const int MaxCacheEntries = 5000;
        
        public class BaseWeaponScore
        {
            public float QualityScore { get; set; }
            public float DamageScore { get; set; }
            public float RangeScore { get; set; }
            public float ModScore { get; set; }
            public int CachedTick { get; set; }
        }
        
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
            
            // Calculate fresh score
            // Get base weapon scores (quality, damage, range, mods)
            var baseScores = GetBaseWeaponScore(weapon);
            float score = 0f;
            
            if (baseScores != null)
            {
                score += baseScores.QualityScore;
                score += baseScores.DamageScore;
                score += baseScores.RangeScore;
                score += baseScores.ModScore;
            }
            
            // Add pawn-specific scores (traits, skills, policy)
            score += WeaponScoringHelper.GetTotalScore(pawn, weapon);
            
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
        
        public static BaseWeaponScore GetBaseWeaponScore(ThingWithComps weapon)
        {
            if (weapon == null)
                return null;
                
            int currentTick = Find.TickManager.TicksGame;
            
            if (baseScoreCache.TryGetValue(weapon, out var cached))
            {
                if (currentTick - cached.CachedTick < BaseCacheLifetime)
                {
                    int modTick = GetWeaponModifiedTick(weapon);
                    if (modTick <= cached.CachedTick)
                    {
                        return cached;
                    }
                }
            }
            
            // Calculate base scores (these don't depend on pawn)
            var baseScore = new BaseWeaponScore
            {
                QualityScore = CalculateQualityScore(weapon),
                DamageScore = CalculateDamageScore(weapon),
                RangeScore = CalculateRangeScore(weapon),
                ModScore = CalculateModScore(weapon),
                CachedTick = currentTick
            };
            
            baseScoreCache[weapon] = baseScore;
            return baseScore;
        }
        
        private static float CalculateQualityScore(ThingWithComps weapon)
        {
            if (weapon.TryGetQuality(out QualityCategory qc))
            {
                return (int)qc * 50f;
            }
            return 0f;
        }
        
        private static float CalculateDamageScore(ThingWithComps weapon)
        {
            if (weapon?.def == null)
                return 0f;
                
            if (weapon.def.IsRangedWeapon)
            {
                if (weapon.def.Verbs?.Count > 0 && weapon.def.Verbs[0] != null)
                {
                    var verb = weapon.def.Verbs[0];
                    if (verb.defaultProjectile?.projectile == null)
                        return 0f;
                        
                    // Check if this weapon actually does damage (exclude EMP, smoke, etc.)
                    if (verb.defaultProjectile?.projectile?.damageDef?.harmsHealth == false)
                    {
                        return -500f; // Penalty for non-damaging weapons
                    }
                        
                    float damage = verb.defaultProjectile.projectile.GetDamageAmount(weapon);
                    float warmup = verb.warmupTime;
                    float cooldown = weapon.def.GetStatValueAbstract(StatDefOf.RangedWeapon_Cooldown);
                    float burstShots = verb.burstShotCount;

                    float cycleTime = warmup + cooldown + (burstShots - 1) * verb.ticksBetweenBurstShots / 60f;
                    float dps = (damage * burstShots) / cycleTime;

                    float accuracy = 0.5f; // Base accuracy estimate
                    float score = dps * accuracy * 15f;

                    if (burstShots > 1)
                    {
                        score *= 1.5f;
                        score += burstShots * 30f;
                    }

                    return score;
                }
            }
            else if (weapon.def.IsMeleeWeapon)
            {
                float meleeDPS = weapon.def.GetStatValueAbstract(StatDefOf.MeleeWeapon_CooldownMultiplier);
                float meleeDamage = weapon.def.GetStatValueAbstract(StatDefOf.MeleeWeapon_DamageMultiplier);
                return (meleeDPS + meleeDamage) * 20f;
            }
            
            return 0f;
        }
        
        private static float CalculateRangeScore(ThingWithComps weapon)
        {
            if (weapon?.def == null || !weapon.def.IsRangedWeapon)
                return 0f;

            if (weapon.def.Verbs?.Count > 0 && weapon.def.Verbs[0] != null)
            {
                float range = weapon.def.Verbs[0].range;

                if (range >= 30f && range <= 32f)
                    return 150f;
                else if (range >= 33f && range <= 40f)
                    return 100f;
                else if (range >= 20f && range < 30f)
                    return 70f;
                else if (range > 40f)
                    return 30f;
            }
            return 0f;
        }
        
        private static float CalculateModScore(ThingWithComps weapon)
        {
            float score = 0f;

            if (InfusionCompat.IsLoaded())
            {
                score += InfusionCompat.GetInfusionScoreBonus(weapon);
            }

            return score;
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
            
            // Remove destroyed weapon entries
            var destroyedWeapons = baseScoreCache.Keys
                .Where(w => w.Destroyed)
                .ToList();
                
            foreach (var weapon in destroyedWeapons)
            {
                baseScoreCache.Remove(weapon);
            }
            
            // Also cleanup old weapon modification ticks
            var oldWeaponMods = weaponModifiedTick
                .Where(kvp => currentTick - kvp.Value > BaseCacheLifetime * 2)
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
            baseScoreCache.Clear();
            pawnScoreCache.Clear();
            weaponModifiedTick.Clear();
            pawnSkillChangedTick.Clear();
        }
    }
}
