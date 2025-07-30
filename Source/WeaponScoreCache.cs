using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;

namespace AutoArm
{
    public static class WeaponScoreCache
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
            public float QualityScore { get; set; } // Deprecated - quality now included in damage/stat calculations
            public float DamageScore { get; set; } // Includes quality effects
            public float RangeScore { get; set; }
            public float ArmorPenScore { get; set; } // Armor penetration effectiveness
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
            // Get base weapon scores (damage with quality included, range, mods)
            var baseScores = GetBaseWeaponScore(weapon);
            float score = 0f;

            if (baseScores != null)
            {
                score += baseScores.QualityScore; // Always 0 now - quality is in damage/stats
                score += baseScores.DamageScore;  // Includes quality effects
                score += baseScores.RangeScore;
                score += baseScores.ArmorPenScore; // Armor penetration effectiveness
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
                ArmorPenScore = CalculateArmorPenetrationScore(weapon),
                ModScore = CalculateModScore(weapon),
                CachedTick = currentTick
            };

            baseScoreCache[weapon] = baseScore;
            return baseScore;
        }

        private static float CalculateQualityScore(ThingWithComps weapon)
        {
            // Quality is now included in weapon stats when using GetStatValue/GetDamageAmount
            // No need for separate quality scoring
            return 0f;
        }

        private static float CalculateDamageScore(ThingWithComps weapon)
        {
            if (weapon?.def == null)
                return 0f;

            if (weapon.def.IsRangedWeapon)
            {
                // TODO: Implement situational weapon detection here
                // Cap situational weapons (grenades, launchers) at ~80 points total
                // 
                // if (IsSituationalWeapon(weapon))
                //     return -420f; // Results in ~80 total score after other bonuses
                // 
                // Where IsSituationalWeapon would be:
                // // If it explodes OR doesn't do health damage OR has forced miss
                // var verb = weapon.def.Verbs?[0];
                // var projectile = verb?.defaultProjectile?.projectile;
                // return projectile?.explosionRadius > 0 || 
                //        projectile?.damageDef?.harmsHealth == false ||
                //        verb?.ForcedMissRadius > 0;
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

                    // GetDamageAmount already includes quality effects
                    float damage = verb.defaultProjectile.projectile.GetDamageAmount(weapon);
                    float warmup = verb.warmupTime;
                    // Use GetStatValue to include quality effects on cooldown
                    float cooldown = weapon.GetStatValue(StatDefOf.RangedWeapon_Cooldown);
                    float burstShots = verb.burstShotCount;
                    float range = verb.range;

                    float cycleTime = warmup + cooldown + (burstShots - 1) * verb.ticksBetweenBurstShots / 60f;
                    float dps = (damage * burstShots) / cycleTime;

                    // Base score from DPS - more balanced multiplier
                    float score = dps * 8f; // Reduced from 15f

                    // Range-based accuracy multiplier
                    if (range < 15f) // Short range (shotguns, etc)
                    {
                        // Short range weapons have limited tactical options
                        score *= 0.85f;
                        
                        // Strong penalty for short-range high-burst weapons (can't kite effectively)
                        if (burstShots > 3 && range < 18f)
                        {
                            score *= 0.75f; // Heavy SMG gets significant penalty
                        }
                    }
                    else if (range >= 15f && range < 25f) // Medium-short range
                    {
                        score *= 0.95f;
                        
                        // Slight bonus for medium range burst weapons (good kiting ability)
                        if (burstShots > 1 && range >= 20f)
                        {
                            score *= 1.05f; // Assault rifle gets small bonus
                        }
                    }
                    else if (range >= 25f && range <= 35f) // Optimal range
                    {
                        score *= 1.0f;
                    }
                    else if (range > 35f) // Long range
                    {
                        score *= 1.05f;
                    }

                    // Burst bonus - with diminishing returns for larger bursts
                    if (burstShots > 1)
                    {
                        // Diminishing returns: first extra shot worth 30, second worth 18, third+ worth 6 each
                        float burstBonus = 0f;
                        if (burstShots >= 2) burstBonus += 30f; // 2-shot gets +30
                        if (burstShots >= 3) burstBonus += 18f; // 3-shot gets +48 total
                        if (burstShots >= 4) burstBonus += (burstShots - 3) * 6f; // 4+ shots get +6 each
                        
                        score += burstBonus;
                        
                        // Range-based burst penalty - short range burst weapons can't kite effectively
                        if (range < 20f)
                        {
                            // The shorter the range, the worse burst weapons perform (can't kite)
                            float rangePenalty = (20f - range) / 20f; // 0-1 scale, max penalty at 0 range
                            float burstPenalty = (burstShots - 1) * 10f * rangePenalty;
                            score -= burstPenalty;
                            
                            // Extra penalty for very high burst at very short range
                            if (burstShots >= 5 && range < 17f)
                            {
                                score -= 20f; // Heavy SMG specific penalty
                            }
                        }
                    }

                    // Special handling for known shotgun weapons
                    if (weapon.def.defName.Contains("Shotgun") || weapon.def.defName.Contains("shotgun"))
                    {
                        // Shotguns are effective at close range despite lower DPS
                        score *= 1.3f;
                    }

                    return score;
                }
            }
            else if (weapon.def.IsMeleeWeapon)
            {
                // Use GetStatValue to include quality effects
                float meleeDPS = weapon.GetStatValue(StatDefOf.MeleeWeapon_AverageDPS);
                // Multiplier balanced to give melee ~25% advantage over ranged to compensate for close combat risk
                return meleeDPS * 10f;
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

                // More balanced range scoring
                if (range >= 30f && range <= 35f) // Optimal range
                    return 70f;  // Reduced from 100f
                else if (range >= 25f && range < 30f) // Good range
                    return 55f;  // Reduced from 80f
                else if (range >= 20f && range < 25f) // Decent range
                    return 40f;  // Reduced from 60f
                else if (range >= 15f && range < 20f) // Short range
                    return 25f;  // Reduced from 40f
                else if (range >= 10f && range < 15f) // Very short range (shotguns)
                    return 15f;  // Reduced from 25f
                else if (range < 10f) // Extremely short
                    return 5f;   // Reduced from 10f
                else if (range > 35f && range <= 45f) // Long range
                    return 50f;  // Reduced from 70f
                else if (range > 45f) // Very long range
                    return 35f;  // Reduced from 50f
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

            // Add Odyssey unique weapon bonus
            score += CalculateOdysseyUniqueBonus(weapon);

            return score;
        }

        private static float CalculateArmorPenetrationScore(ThingWithComps weapon)
        {
            // Armor penetration scoring based on ability to penetrate common armor types
            // Higher scores for weapons that can penetrate late-game armor
            // Common armor values:
            // - Cloth/Tribal: 0-20% Sharp
            // - Flak Vest: 52% Sharp, 40% Blunt
            // - Recon Armor: 40% Sharp, 30% Blunt  
            // - Marine Armor: 75% Sharp, 37% Blunt
            // - Cataphract: 110% Sharp, 55% Blunt
            // - Centipede: 72% Sharp, 22% Blunt
            
            float apScore = 0f;
            
            if (weapon.def.IsMeleeWeapon)
            {
                // For melee weapons, we need to calculate AP from the weapon's attacks
                // Since MeleeWeapon_AverageArmorPenetration might not exist, we'll calculate it
                float meleeAP = 0f;
                
                // Get melee verb tools (attacks)
                var tools = weapon.def.tools;
                if (tools != null && tools.Count > 0)
                {
                    float totalAP = 0f;
                    float totalChance = 0f;
                    
                    foreach (var tool in tools)
                    {
                        // Each tool has an armor penetration value
                        float toolAP = tool.armorPenetration;
                        float toolChance = tool.chanceFactor;
                        
                        totalAP += toolAP * toolChance;
                        totalChance += toolChance;
                    }
                    
                    if (totalChance > 0)
                    {
                        meleeAP = totalAP / totalChance;
                    }
                }
                
                // If no tools found or AP is 0, estimate based on damage
                if (meleeAP <= 0)
                {
                    float meleeDPS = weapon.GetStatValue(StatDefOf.MeleeWeapon_AverageDPS);
                    // Rough estimate: higher DPS weapons tend to have better penetration
                    meleeAP = meleeDPS * 0.015f;
                }
                
                // Score based on common armor thresholds
                if (meleeAP >= 0.75f) // Can penetrate marine armor (75%)
                    apScore = 150f;
                else if (meleeAP >= 0.52f) // Can penetrate flak vest (52%)
                    apScore = 100f;
                else if (meleeAP >= 0.40f) // Can penetrate recon armor (40%)
                    apScore = 75f;
                else if (meleeAP >= 0.20f) // Can penetrate basic armor
                    apScore = 50f;
                else
                    apScore = meleeAP * 100f; // Linear scale for low AP
            }
            else if (weapon.def.IsRangedWeapon)
            {
                // For ranged, get AP from projectile
                if (weapon.def.Verbs?.Count > 0 && weapon.def.Verbs[0]?.defaultProjectile != null)
                {
                    var projectile = weapon.def.Verbs[0].defaultProjectile.projectile;
                    
                    // Get armor penetration value from the projectile
                    float rangedAP = 0f;
                    
                    // RimWorld uses armorPenetrationBase for projectiles
                    // Use reflection to safely access the field in case it doesn't exist in some versions
                    try
                    {
                        var apField = projectile.GetType().GetField("armorPenetrationBase", BindingFlags.Public | BindingFlags.Instance);
                        if (apField != null)
                        {
                            var value = apField.GetValue(projectile);
                            if (value != null)
                                rangedAP = Convert.ToSingle(value);
                        }
                    }
                    catch (Exception ex)
                    {
                        // Log but don't crash
                        Log.Message($"[AutoArm] Could not get armorPenetrationBase: {ex.Message}");
                    }
                    
                    // If AP not found or is 0, use default calculation (0.015 * damage)
                    if (rangedAP <= 0 && projectile.damageDef != null)
                    {
                        float damage = projectile.GetDamageAmount(weapon);
                        rangedAP = damage * 0.015f;
                    }
                    
                    // Score based on armor thresholds
                    if (rangedAP >= 0.75f) // Can penetrate marine armor
                        apScore = 150f;
                    else if (rangedAP >= 0.52f) // Can penetrate flak vest
                        apScore = 100f;
                    else if (rangedAP >= 0.40f) // Can penetrate recon armor
                        apScore = 75f;
                    else if (rangedAP >= 0.20f) // Can penetrate basic armor
                        apScore = 50f;
                    else
                        apScore = rangedAP * 100f; // Linear scale for low AP
                }
            }
            
            return apScore;
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

        private static float CalculateOdysseyUniqueBonus(ThingWithComps weapon)
        {
            float bonus = 0f;

            // First, we need to check if this is actually a unique weapon
            if (!IsOdysseyUniqueWeapon(weapon))
                return 0f;

            // Base bonus for being unique (they're rare and special)
            bonus += 50f;

            // Per-trait bonus (since we can't easily detect specific traits)
            // Assume average of 2 traits, with most being beneficial
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
            if (weapon.def.defName.Contains("_Unique"))
                return true;

            return false;
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