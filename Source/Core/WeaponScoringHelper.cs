
using AutoArm.Definitions;
using AutoArm.Helpers;
using AutoArm.Logging;
using AutoArm.Testing;
using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace AutoArm.Weapons
{
    internal static class WeaponScoringReflectionHelper
    {
        internal static System.Reflection.FieldInfo FindFieldWithFallback(Type type, params string[] fieldNames)
        {
            foreach (var name in fieldNames)
            {
                var field = AccessTools.Field(type, name);
                if (field != null) return field;
            }
            return null;
        }
    }

    /// <summary>
    /// Weapon scoring
    /// </summary>
    public static class WeaponScoringHelper
    {
        private static System.Reflection.FieldInfo FindFieldWithFallback(Type type, params string[] fieldNames)
            => WeaponScoringReflectionHelper.FindFieldWithFallback(type, fieldNames);

        private static float GetRangedMultiplier()
        {
            return AutoArmMod.GetRangedMultiplier();
        }

        private static float GetMeleeMultiplier()
        {
            return AutoArmMod.GetMeleeMultiplier();
        }

        private struct WeaponCacheKey : IEquatable<WeaponCacheKey>
        {
            public readonly int defHash;
            public readonly int quality;
            public readonly int stuffHash;

            public WeaponCacheKey(int defHash, int quality, int stuffHash)
            {
                this.defHash = defHash;
                this.quality = quality;
                this.stuffHash = stuffHash;
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = defHash;
                    hash = (hash * 397) ^ quality;
                    hash = (hash * 397) ^ stuffHash;
                    return hash;
                }
            }

            public override bool Equals(object obj)
            {
                return obj is WeaponCacheKey key && Equals(key);
            }

            public bool Equals(WeaponCacheKey other)
            {
                return defHash == other.defHash && quality == other.quality && stuffHash == other.stuffHash;
            }
        }

        private class CachedWeaponProperties
        {
            public WeaponBaseProperties properties;
            public int lastAccessTick;

            public CachedWeaponProperties(WeaponBaseProperties props, int tick)
            {
                properties = props;
                lastAccessTick = tick;
            }
        }

        private static Dictionary<WeaponCacheKey, CachedWeaponProperties> propertiesCache = new Dictionary<WeaponCacheKey, CachedWeaponProperties>();

        private const int MaxWeaponCacheSize = Constants.MaxWeaponCacheSize;
        private const int CacheEvictionBatchSize = MaxWeaponCacheSize / 4;

        private class WeaponBaseProperties
        {
            public float BaseMarketValue;
            public float ArmorPenetration;
            public float Range;
            public int BurstShotCount;
            public float WarmupTime;
            public bool IsMelee;
            public bool IsRanged;
            public bool IsSituational;
            public QualityCategory Quality;

            public float RangeModifier;

            public float WarmupModifier;
            public float BurstModifier;
            public float APModifier;

            public float LowDamageModifier = 1.0f;
            public float PistolModifier = 1.0f;

            public float MeleeSpeedModifier = 1.0f;
            public float MeleeAPBonusModifier = 1.0f;
        }

        private static readonly Dictionary<ThingDef, bool> situationalWeapons = new Dictionary<ThingDef, bool>(512);

        private static bool situationalCacheInitialized = false;

        private static readonly Dictionary<int, (float shooting, float melee, int lastUpdateTick)> skillCache =
            new Dictionary<int, (float, float, int)>();

        private static readonly Dictionary<int, (int shootingIndex, int meleeIndex, int lastUpdateTick)> skillIndexCache =
            new Dictionary<int, (int, int, int)>();

        private const int SkillCacheLifetimeTicks = Constants.StandardCacheDuration;

        private static readonly Dictionary<int, List<int>> skillCacheExpirySchedule = new Dictionary<int, List<int>>();
        private static readonly Dictionary<int, List<int>> skillIndexCacheExpirySchedule = new Dictionary<int, List<int>>();

        /// <summary>
        /// Weapon score for pawn/weapon combo
        /// </summary>
        public static float GetTotalScore(Pawn pawn, ThingWithComps weapon)
        {
            if (pawn == null || weapon == null)
                return 0f;

            bool debugLogging = AutoArmMod.settings?.debugLogging == true;

            float totalScore = 0f;

            if (pawn.equipment?.Primary == weapon &&
                Helpers.ForcedWeapons.IsForced(pawn, weapon) &&
                AutoArmMod.settings?.allowForcedWeaponUpgrades == false)
            {
                return 10000f;
            }

            float policyScore = GetOutfitPolicyScore(pawn, weapon);

            if (policyScore <= Constants.OutfitFilterDisallowedPenalty && pawn.equipment?.Primary != weapon)
                return policyScore;
            totalScore += policyScore;

            float personaScore = GetPersonaWeaponScore(pawn, weapon);
            if (personaScore <= Constants.OutfitFilterDisallowedPenalty)
                return personaScore;
            totalScore += personaScore;


            totalScore += GetHunterScore(pawn, weapon);

            float mismatchMultiplier = 1f;
            float skillScore = GetSkillScore(pawn, weapon, out mismatchMultiplier);
            totalScore *= mismatchMultiplier;
            totalScore += skillScore;

            float weaponPropertyScore = GetWeaponPropertyScore(pawn, weapon);
            weaponPropertyScore *= mismatchMultiplier;
            totalScore += weaponPropertyScore;

            if (CECompat.IsLoaded() && CECompat.ShouldCheckAmmo())
            {
                float ammoModifier = CECompat.GetAmmoScoreModifier(weapon, pawn);
                if (ammoModifier != 1.0f)
                {
                    AutoArmLogger.Debug(() => $"[{AutoArmLogger.GetPawnName(pawn)}] CE ammo modifier for {weapon.LabelCap}: x{ammoModifier}");
                    totalScore *= ammoModifier;
                }
            }

            return totalScore;
        }

        /// <summary>
        /// Weapon property score (damage, range, quality, etc)
        /// </summary>
        public static float GetWeaponPropertyScore(Pawn pawn, ThingWithComps weapon)
        {
            if (weapon?.def == null)
                return 0f;

            QualityCategory quality = QualityCategory.Normal;
            Caching.Components.TryGetWeaponQuality(weapon, out quality);

            int stuffHash = weapon.Stuff != null ? weapon.Stuff.shortHash : -1;
            var cacheKey = new WeaponCacheKey(weapon.def.shortHash, (int)quality, stuffHash);

            WeaponBaseProperties baseProps = null;
            CachedWeaponProperties cached;
            if (propertiesCache.TryGetValue(cacheKey, out cached))
            {
                AutoArmPerfOverlayWindow.ReportPropertyCacheHit();

                cached.lastAccessTick = Find.TickManager.TicksGame;
                baseProps = cached.properties;
            }
            else
            {
                AutoArmPerfOverlayWindow.ReportPropertyCacheMiss();

                baseProps = CalculateBaseWeaponProperties(weapon, quality);
                propertiesCache[cacheKey] = new CachedWeaponProperties(baseProps, Find.TickManager.TicksGame);

                if (propertiesCache.Count > MaxWeaponCacheSize)
                {
                    EvictOldestEntries();
                }
            }

            float baseScore = 0f;

            baseScore = (float)Math.Pow(baseProps.BaseMarketValue, 1.0 / 4.0) * Constants.MarketValueCubeRootMultiplier;

            baseScore *= baseProps.RangeModifier;
            baseScore *= baseProps.WarmupModifier;
            baseScore *= baseProps.BurstModifier;
            baseScore *= baseProps.APModifier;

            baseScore *= baseProps.LowDamageModifier;
            baseScore *= baseProps.PistolModifier;
            baseScore *= baseProps.MeleeSpeedModifier;
            baseScore *= baseProps.MeleeAPBonusModifier;

            if (baseProps.IsSituational)
            {
                baseScore *= Constants.SituationalWeaponModifier;
            }

            if (baseProps.IsRanged)
            {
                baseScore *= GetRangedMultiplier();
            }
            else if (baseProps.IsMelee)
            {
                baseScore *= GetMeleeMultiplier();
            }

            return baseScore;
        }

        private static void EvictOldestEntries()
        {
            if (propertiesCache.Count <= MaxWeaponCacheSize)
                return;

            var entries = new List<KeyValuePair<WeaponCacheKey, int>>(propertiesCache.Count);
            foreach (var kvp in propertiesCache)
            {
                entries.Add(new KeyValuePair<WeaponCacheKey, int>(kvp.Key, kvp.Value.lastAccessTick));
            }

            entries.Sort((a, b) => a.Value.CompareTo(b.Value));

            int removed = 0;
            for (int i = 0; i < CacheEvictionBatchSize && i < entries.Count; i++)
            {
                if (propertiesCache.Remove(entries[i].Key))
                {
                    removed++;
                }
            }

            AutoArmLogger.Debug(() => $"[WeaponCache] Evicted {removed} oldest entries, cache now at {propertiesCache.Count}");
        }

        private static WeaponBaseProperties CalculateBaseWeaponProperties(ThingWithComps weapon, QualityCategory quality)
        {
            var props = new WeaponBaseProperties();

            props.Quality = quality;

            float marketValue = weapon.MarketValue;
            if (marketValue <= 0f)
            {
                float fallback = weapon.def?.BaseMarketValue ?? 0f;
                if (fallback <= 0f)
                {
                    fallback = Constants.WeaponFallbackMarketValue;
                }

                if (AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.Debug(() => $"Missing market value for {(weapon != null ? weapon.LabelCap : "unknown")}; using fallback {fallback}");
                }

                marketValue = fallback;
            }


            if (weapon.Stuff != null && weapon.def.IsMeleeWeapon && IsLuxuryMeleeMaterial(weapon.Stuff))
            {
                float materialPriceMult = GetMaterialPriceMultiplier(weapon.Stuff);

                if (materialPriceMult > 0f)
                {
                    float originalValue = marketValue;
                    marketValue = marketValue / materialPriceMult;

                    if (AutoArmMod.settings?.debugLogging == true)
                    {
                        AutoArmLogger.Debug(() => $"[Material] {weapon.LabelShort}: {weapon.Stuff.defName} " +
                                          $"is luxury melee material, removed {materialPriceMult:F1}Ã— price inflation " +
                                          $"(MV {originalValue:F0} â†’ {marketValue:F0})");
                    }
                }
                else
                {
                    AutoArmLogger.Warn($"[Material] {weapon.LabelShort}: {weapon.Stuff.defName} has invalid MarketValue multiplier ({materialPriceMult}), skipping luxury correction");
                }
            }

            props.BaseMarketValue = marketValue;
            if (weapon.HitPoints < weapon.MaxHitPoints && weapon.MaxHitPoints > 0)
            {
                float hpPercent = (float)weapon.HitPoints / weapon.MaxHitPoints;
                if (hpPercent > 0f)
                {
                    props.BaseMarketValue = marketValue / hpPercent;
                }
            }

            props.IsMelee = weapon.def.IsMeleeWeapon;
            props.IsRanged = weapon.def.IsRangedWeapon;
            props.IsSituational = IsSituationalWeapon(weapon);

            if (!props.IsMelee && weapon.def.Verbs != null && weapon.def.Verbs.Count > 0)
            {
                var verb = SelectPrimaryVerb(weapon.def, weapon);

                if (verb != null)
                {
                    props.Range = verb.range;
                    if (props.Range < Constants.RangeVeryShortThreshold)
                        props.RangeModifier = Constants.RangeVeryShortModifier;
                    else if (props.Range < Constants.RangeShortThreshold)
                        props.RangeModifier = Constants.RangeShortModifier;
                    else if (props.Range > Constants.RangeLongThreshold)
                        props.RangeModifier = Constants.RangeLongModifier;
                    else
                        props.RangeModifier = 1.0f;

                    props.WarmupTime = verb.warmupTime;
                    if (props.WarmupTime > Constants.WarmupVerySlowThreshold)
                        props.WarmupModifier = Constants.WarmupVerySlowModifier;
                    else if (props.WarmupTime > Constants.WarmupSlowThreshold)
                        props.WarmupModifier = Constants.WarmupSlowModifier;
                    else
                        props.WarmupModifier = 1.0f;

                    props.BurstShotCount = verb.burstShotCount;
                    if (props.BurstShotCount > 1)
                    {
                        float burstBonus = Math.Min(props.BurstShotCount * Constants.BurstShotBonus,
                                                    Constants.BurstShotBonusMax);
                        props.BurstModifier = 1.0f + burstBonus;
                    }
                    else
                    {
                        props.BurstModifier = 1.0f;
                    }

                    if (verb.defaultProjectile?.projectile != null)
                    {
                        props.ArmorPenetration = verb.defaultProjectile.projectile.GetArmorPenetration(weapon);
                        if (props.ArmorPenetration > Constants.RangedHighAPThreshold)
                            props.APModifier = Constants.RangedHighAPModifier;
                        else if (props.ArmorPenetration > Constants.RangedMediumAPThreshold)
                            props.APModifier = Constants.RangedMediumAPModifier;
                        else
                            props.APModifier = 1.0f;

                        int damage = verb.defaultProjectile.projectile.GetDamageAmount(weapon);
                        if (damage > 0 && damage < Constants.LowDamagePerShotThreshold)
                        {
                            props.LowDamageModifier = Constants.LowDamagePerShotModifier;
                        }
                    }
                    else
                    {
                        props.APModifier = 1.0f;
                    }

                    if (props.BurstShotCount == 1 &&
                        props.Range < Constants.PistolRangeThreshold &&
                        props.WarmupTime < Constants.PistolWarmupThreshold)
                    {
                        props.PistolModifier = Constants.PistolCategoryModifier;
                    }
                }
                else
                {
                    props.RangeModifier = 1.0f;
                    props.WarmupModifier = 1.0f;
                    props.BurstModifier = 1.0f;
                    props.APModifier = 1.0f;
                }
            }
            else if (props.IsMelee)
            {
                props.RangeModifier = 1.0f;
                props.WarmupModifier = 1.0f;
                props.BurstModifier = 1.0f;
                props.APModifier = Constants.MeleeBaseModifier;

                Tool meleeTool = null;
                if (weapon.def.tools != null)
                {
                    float minCooldown = float.MaxValue;
                    foreach (Tool tool in weapon.def.tools)
                    {
                        if (tool.cooldownTime < minCooldown)
                        {
                            minCooldown = tool.cooldownTime;
                            meleeTool = tool;
                        }
                    }
                }

                if (meleeTool != null && meleeTool.cooldownTime > 0 &&
                    meleeTool.cooldownTime < Constants.MeleeFastCooldownThreshold)
                {
                    props.MeleeSpeedModifier = Constants.MeleeFastAttackModifier;
                }

                var apStat = DefDatabase<StatDef>.GetNamedSilentFail("MeleeArmorPenetration")
                              ?? DefDatabase<StatDef>.GetNamedSilentFail("ArmorPenetration");
                if (apStat != null)
                {
                    float meleeAP = weapon.GetStatValue(apStat, true);
                    if (meleeAP > Constants.MeleeHighAPThreshold)
                    {
                        props.MeleeAPBonusModifier = Constants.MeleeHighAPModifier;
                    }
                }
            }
            else
            {
                props.RangeModifier = 1.0f;
                props.WarmupModifier = 1.0f;
                props.BurstModifier = 1.0f;
                props.APModifier = 1.0f;
            }

            return props;
        }

        private static bool IsSituationalWeapon(ThingWithComps weapon)
        {
            if (weapon?.def == null) return false;

            if (!situationalCacheInitialized)
            {
                PreCalcWeapons();
            }

            return situationalWeapons.TryGetValue(weapon.def, out bool isSituational) && isSituational;
        }

        public static void PreCalcWeapons()
        {
            if (situationalCacheInitialized)
                return;

            int count = 0;
            int situationalCount = 0;

            foreach (var def in DefDatabase<ThingDef>.AllDefs)
            {
                if (!def.IsWeapon)
                    continue;

                bool isSituational = IsSituational(def);

                situationalWeapons[def] = isSituational;
                count++;
                if (isSituational)
                    situationalCount++;
            }

            situationalCacheInitialized = true;

            AutoArmLogger.Debug(() => $"Pre-calculated situational weapons: {situationalCount} of {count}");
        }

        private static bool IsSituational(ThingDef weaponDef)
        {
            if (weaponDef?.Verbs == null || weaponDef.Verbs.Count == 0)
                return false;

            var verb = weaponDef.Verbs[0];
            var projectile = verb.defaultProjectile?.projectile;

            if (projectile != null)
            {
                var damageDef = projectile.damageDef;

                if (damageDef != null)
                {
                    if (damageDef.isExplosive) return true;

                    if (!damageDef.harmsHealth) return true;

                    if (damageDef == DamageDefOf.EMP ||
                        damageDef == DamageDefOf.Stun ||
                        damageDef == DamageDefOf.Extinguish ||
                        damageDef == DamageDefOf.Smoke)
                        return true;

                    if (damageDef.defName?.Contains("Gas") == true)
                        return true;
                }

                if (projectile.explosionRadius > 0)
                    return true;
            }

            if (verb.ForcedMissRadius > 0)
                return true;

            if (weaponDef.tools != null && weaponDef.tools.Count > 0)
            {
                bool allLowPower = true;
                foreach (var tool in weaponDef.tools)
                {
                    if (tool.power >= 7f)
                    {
                        allLowPower = false;
                        break;
                    }
                }
                if (allLowPower)
                    return true;
            }

            string defName = weaponDef.defName?.ToLower();
            if (defName != null && (
                defName.Contains("grenade") ||
                defName.Contains("launcher") ||
                defName.Contains("molotov") ||
                defName.Contains("emp")))
                return true;

            return false;
        }

        public static void ClearWeaponScoreCache()
        {
            propertiesCache.Clear();

            situationalCacheInitialized = false;
            situationalWeapons.Clear();
            PreCalcWeapons();

            AutoArmLogger.Log("Cleared weapon base score cache and rebuilt situational weapon cache");
        }

        private static float GetOutfitPolicyScore(Pawn pawn, ThingWithComps weapon)
        {
            var filter = pawn.outfits?.CurrentApparelPolicy?.filter;
            if (filter != null && !filter.Allows(weapon.def))
            {
                return Constants.OutfitFilterDisallowedPenalty;
            }
            return 0f;
        }

        private static float GetPersonaWeaponScore(Pawn pawn, ThingWithComps weapon)
        {
            if (TestRunner.IsRunningTests)
                return 0f;

            if (!Caching.Components.IsPersonaWeapon(weapon))
                return 0f;

            if (CompBiocodable.IsBiocodedFor(weapon, pawn))
            {
                return Constants.PersonaWeaponBonus;
            }

            return 0f;
        }

        public static float GetHunterScore(Pawn pawn, ThingWithComps weapon)
        {
            if (weapon?.def == null || pawn?.workSettings == null)
                return 0f;

            bool isHunter = pawn.workSettings.WorkIsActive(WorkTypeDefOf.Hunting) &&
                           !pawn.WorkTypeIsDisabled(WorkTypeDefOf.Hunting);

            if (isHunter && weapon.def.IsRangedWeapon)
            {
                return Constants.HunterRangedBonus;
            }
            return 0f;
        }

        private static readonly float[] SkillBonusLookup = InitializeSkillBonusLookup();

        private static float[] InitializeSkillBonusLookup()
        {
            float[] lookup = new float[21];
            float baseBonus = Constants.SkillBonusBase;
            float growthRate = Constants.SkillBonusGrowthRate;

            for (int i = 0; i <= 20; i++)
            {
                if (i == 0)
                {
                    lookup[i] = 0f;
                }
                else
                {
                    float bonus = baseBonus * (float)Math.Pow(growthRate, i - 1);
                    lookup[i] = Math.Min(bonus, Constants.SkillBonusMax);
                }
            }
            return lookup;
        }

        public static float GetSkillScore(Pawn pawn, ThingWithComps weapon, out float mismatchMultiplier)
        {
            mismatchMultiplier = 1f;

            bool isRanged, isMelee;
            QualityCategory quality = QualityCategory.Normal;
            Caching.Components.TryGetWeaponQuality(weapon, out quality);

            int stuffHash = weapon.Stuff != null ? weapon.Stuff.shortHash : -1;
            var cacheKey = new WeaponCacheKey(weapon.def.shortHash, (int)quality, stuffHash);

            if (propertiesCache.TryGetValue(cacheKey, out var weaponProps))
            {
                isRanged = weaponProps.properties.IsRanged;
                isMelee = weaponProps.properties.IsMelee;
            }
            else
            {
                isRanged = weapon.def.IsRangedWeapon;
                isMelee = weapon.def.IsMeleeWeapon;
            }

            float shootingSkill = 0f;
            float meleeSkill = 0f;
            int currentTick = Find.TickManager.TicksGame;

            int pawnId = pawn.thingIDNumber;

            if (skillCache.TryGetValue(pawnId, out var cached) &&
                (currentTick - cached.lastUpdateTick) < SkillCacheLifetimeTicks)
            {
                AutoArmPerfOverlayWindow.ReportSkillCacheHit();

                shootingSkill = cached.shooting;
                meleeSkill = cached.melee;
            }
            else
            {
                AutoArmPerfOverlayWindow.ReportSkillCacheMiss();

                if (pawn.skills?.skills != null)
                {
                    if (skillIndexCache.TryGetValue(pawnId, out var indices) &&
                        (currentTick - indices.lastUpdateTick) < SkillCacheLifetimeTicks * 4)
                    {
                        var skills = pawn.skills.skills;
                        if (indices.shootingIndex >= 0 && indices.shootingIndex < skills.Count &&
                            skills[indices.shootingIndex].def == SkillDefOf.Shooting)
                        {
                            shootingSkill = skills[indices.shootingIndex].Level;
                        }
                        else
                        {
                            shootingSkill = GetSkillIndex(pawn, SkillDefOf.Shooting, true);
                        }

                        if (indices.meleeIndex >= 0 && indices.meleeIndex < skills.Count &&
                            skills[indices.meleeIndex].def == SkillDefOf.Melee)
                        {
                            meleeSkill = skills[indices.meleeIndex].Level;
                        }
                        else
                        {
                            meleeSkill = GetSkillIndex(pawn, SkillDefOf.Melee, false);
                        }
                    }
                    else
                    {
                        int shootingIndex = -1;
                        int meleeIndex = -1;

                        for (int i = 0; i < pawn.skills.skills.Count; i++)
                        {
                            var skill = pawn.skills.skills[i];
                            if (skill.def == SkillDefOf.Shooting)
                            {
                                shootingIndex = i;
                                shootingSkill = skill.Level;
                                if (meleeIndex >= 0) break;
                            }
                            else if (skill.def == SkillDefOf.Melee)
                            {
                                meleeIndex = i;
                                meleeSkill = skill.Level;
                                if (shootingIndex >= 0) break;
                            }
                        }

                        if (shootingIndex < 0) shootingSkill = 0f;
                        if (meleeIndex < 0) meleeSkill = 0f;

                        if (skillIndexCache.TryGetValue(pawnId, out var oldIndexEntry))
                        {
                            int oldIndexExpireTick = oldIndexEntry.lastUpdateTick + SkillCacheLifetimeTicks * 8;
                            RemoveFromIndexSchedule(pawnId, oldIndexExpireTick);
                        }

                        skillIndexCache[pawnId] = (shootingIndex, meleeIndex, currentTick);

                        int indexExpireTick = currentTick + SkillCacheLifetimeTicks * 8;
                        if (!skillIndexCacheExpirySchedule.TryGetValue(indexExpireTick, out var indexList))
                        {
                            indexList = new List<int>();
                            skillIndexCacheExpirySchedule[indexExpireTick] = indexList;
                        }
                        indexList.Add(pawnId);
                    }
                }
                else
                {
                    shootingSkill = 0f;
                    meleeSkill = 0f;
                }

                if (skillCache.TryGetValue(pawnId, out var oldEntry))
                {
                    int oldExpireTick = oldEntry.lastUpdateTick + SkillCacheLifetimeTicks * 2;
                    RemoveFromSchedule(pawnId, oldExpireTick);
                }

                skillCache[pawnId] = (shootingSkill, meleeSkill, currentTick);

                int expireTick = currentTick + SkillCacheLifetimeTicks * 2;
                if (!skillCacheExpirySchedule.TryGetValue(expireTick, out var list))
                {
                    list = new List<int>();
                    skillCacheExpirySchedule[expireTick] = list;
                }
                list.Add(pawnId);
            }

            float score = 0f;

            float skillDifference = Math.Abs(shootingSkill - meleeSkill);

            if (skillDifference == 0)
                return 0f;

            int skillDiffInt = (int)Math.Min(skillDifference, 20);
            float bonus = SkillBonusLookup[skillDiffInt];

            if (isRanged)
            {
                if (shootingSkill > meleeSkill)
                {
                    score = bonus;
                }
                else
                {
                    mismatchMultiplier = Constants.SkillMismatchMultiplier;
                    score = 0f;
                }
            }
            else if (isMelee)
            {
                if (meleeSkill > shootingSkill)
                {
                    score = bonus;
                }
                else
                {
                    mismatchMultiplier = Constants.SkillMismatchMultiplier;
                    score = 0f;
                }
            }

            return score;
        }

        private static float GetSkillIndex(Pawn pawn, SkillDef skillDef, bool isShooting)
        {
            if (pawn.skills?.skills == null) return 0f;

            for (int i = 0; i < pawn.skills.skills.Count; i++)
            {
                if (pawn.skills.skills[i].def == skillDef)
                {
                    int pawnId = pawn.thingIDNumber;
                    int currentTick = Find.TickManager.TicksGame;

                    if (skillIndexCache.TryGetValue(pawnId, out var indices))
                    {
                        if (isShooting)
                            skillIndexCache[pawnId] = (i, indices.meleeIndex, currentTick);
                        else
                            skillIndexCache[pawnId] = (indices.shootingIndex, i, currentTick);
                    }
                    else
                    {
                        if (isShooting)
                            skillIndexCache[pawnId] = (i, -1, currentTick);
                        else
                            skillIndexCache[pawnId] = (-1, i, currentTick);
                    }

                    return pawn.skills.skills[i].Level;
                }
            }

            return 0f;
        }

        private static VerbProperties SelectPrimaryVerb(ThingDef weaponDef, ThingWithComps weapon)
        {
            if (weaponDef?.Verbs == null || weaponDef.Verbs.Count == 0)
                return null;

            for (int i = 0; i < weaponDef.Verbs.Count; i++)
            {
                var verb = weaponDef.Verbs[i];
                if (verb != null && verb.isPrimary)
                    return verb;
            }

            VerbProperties bestVerb = null;
            float bestScore = float.MinValue;

            for (int i = 0; i < weaponDef.Verbs.Count; i++)
            {
                var verb = weaponDef.Verbs[i];
                if (verb == null)
                    continue;

                float score = EstimateVerbScore(verb, weapon);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestVerb = verb;
                }
            }

            return bestVerb ?? weaponDef.Verbs[0];
        }

        private static float EstimateVerbScore(VerbProperties verb, ThingWithComps weapon)
        {
            if (verb == null)
                return float.MinValue;

            float warmup = verb.warmupTime;
            if (warmup <= 0f)
                warmup = 0.1f;

            int burstCount = verb.burstShotCount;
            if (burstCount <= 0)
                burstCount = 1;

            float damage = 0f;

            if (verb.defaultProjectile?.projectile != null)
            {
                damage = verb.defaultProjectile.projectile.GetDamageAmount(weapon);
            }
            else if (verb.IsMeleeAttack)
            {
                Tool tool = null;
                float maxPower = float.MinValue;
                if (weapon.def?.tools != null)
                {
                    foreach (var t in weapon.def.tools)
                    {
                        if (t.power > maxPower)
                        {
                            maxPower = t.power;
                            tool = t;
                        }
                    }
                }
                if (tool != null)
                    damage = tool.power;
            }

            if (damage <= 0f)
                damage = 1f;

            float score = (damage * burstCount) / warmup;

            if (verb.range > 0f)
            {
                score *= 1f + (verb.range / 100f);
            }

            return score;
        }

        public static void CleanupSkillCache()
        {

            int currentTick = Find.TickManager.TicksGame;

            if (skillCache.Count > 150)
            {
                var expiredIds = ListPool<int>.Get();
                foreach (var kvp in skillCache)
                {
                    if ((currentTick - kvp.Value.lastUpdateTick) > SkillCacheLifetimeTicks * 2)
                    {
                        expiredIds.Add(kvp.Key);
                    }
                }
                foreach (var id in expiredIds)
                {
                    skillCache.Remove(id);
                }
                ListPool<int>.Return(expiredIds);
            }

            if (skillIndexCache.Count > 150)
            {
                var expiredIds = ListPool<int>.Get();
                foreach (var kvp in skillIndexCache)
                {
                    if ((currentTick - kvp.Value.lastUpdateTick) > SkillCacheLifetimeTicks * 8)
                    {
                        expiredIds.Add(kvp.Key);
                    }
                }
                foreach (var id in expiredIds)
                {
                    skillIndexCache.Remove(id);
                }
                ListPool<int>.Return(expiredIds);
            }

            if (skillCache.Count > 200)
            {
                var pairs = ListPool<KeyValuePair<int, (float shootingSkill, float meleeSkill, int lastUpdateTick)>>.Get(skillCache.Count);
                foreach (var kvp in skillCache)
                {
                    pairs.Add(kvp);
                }

                pairs.SortBy(kvp => -kvp.Value.lastUpdateTick);

                skillCache.Clear();
                int count = Math.Min(100, pairs.Count);
                for (int i = 0; i < count; i++)
                {
                    skillCache[pairs[i].Key] = pairs[i].Value;
                }
                ListPool<KeyValuePair<int, (float shootingSkill, float meleeSkill, int lastUpdateTick)>>.Return(pairs);
            }

            if (skillIndexCache.Count > 200)
            {
                var pairs = ListPool<KeyValuePair<int, (int shootingIndex, int meleeIndex, int lastUpdateTick)>>.Get(skillIndexCache.Count);
                foreach (var kvp in skillIndexCache)
                {
                    pairs.Add(kvp);
                }

                pairs.SortBy(kvp => -kvp.Value.lastUpdateTick);

                skillIndexCache.Clear();
                int count = Math.Min(100, pairs.Count);
                for (int i = 0; i < count; i++)
                {
                    skillIndexCache[pairs[i].Key] = pairs[i].Value;
                }
                ListPool<KeyValuePair<int, (int shootingIndex, int meleeIndex, int lastUpdateTick)>>.Return(pairs);
            }
        }

        /// <summary>
        /// Remove expired skill cache entries at this tick (event-based)
        /// </summary>
        public static void ProcessExpiredSkillCache(int tick)
        {
            if (skillCacheExpirySchedule.TryGetValue(tick, out var expiredIds))
            {
                foreach (var pawnId in expiredIds)
                {
                    skillCache.Remove(pawnId);
                }

                skillCacheExpirySchedule.Remove(tick);

                if (AutoArmMod.settings?.debugLogging == true && expiredIds.Count > 0)
                {
                    AutoArmLogger.Debug(() =>
                        $"[SkillCacheEvent] {expiredIds.Count} skill cache entries expired at tick {tick}");
                }
            }

            if (skillIndexCacheExpirySchedule.TryGetValue(tick, out var expiredIndexIds))
            {
                foreach (var pawnId in expiredIndexIds)
                {
                    skillIndexCache.Remove(pawnId);
                }

                skillIndexCacheExpirySchedule.Remove(tick);

                if (AutoArmMod.settings?.debugLogging == true && expiredIndexIds.Count > 0)
                {
                    AutoArmLogger.Debug(() =>
                        $"[SkillCacheEvent] {expiredIndexIds.Count} skill index cache entries expired at tick {tick}");
                }
            }
        }

        /// <summary>
        /// Clear skill cache for new games
        /// </summary>
        public static void ResetSkillCache()
        {
            skillCache.Clear();
            skillIndexCache.Clear();
            skillCacheExpirySchedule.Clear();
            skillIndexCacheExpirySchedule.Clear();
            AutoArmLogger.Debug(() => "WeaponScoringHelper skill cache reset");
        }

        /// <summary>
        /// Rebuild skill cache schedule on load from cached timestamps
        /// </summary>
        public static void RebuildSkillCacheSchedule()
        {
            skillCacheExpirySchedule.Clear();
            skillIndexCacheExpirySchedule.Clear();

            int currentTick = Find.TickManager.TicksGame;

            foreach (var kvp in skillCache)
            {
                int pawnId = kvp.Key;
                int lastUpdateTick = kvp.Value.lastUpdateTick;
                int expireTick = lastUpdateTick + SkillCacheLifetimeTicks * 2;

                if (expireTick > currentTick)
                {
                    if (!skillCacheExpirySchedule.TryGetValue(expireTick, out var list))
                    {
                        list = new List<int>();
                        skillCacheExpirySchedule[expireTick] = list;
                    }
                    list.Add(pawnId);
                }
            }

            foreach (var kvp in skillIndexCache)
            {
                int pawnId = kvp.Key;
                int lastUpdateTick = kvp.Value.lastUpdateTick;
                int expireTick = lastUpdateTick + SkillCacheLifetimeTicks * 8;

                if (expireTick > currentTick)
                {
                    if (!skillIndexCacheExpirySchedule.TryGetValue(expireTick, out var list))
                    {
                        list = new List<int>();
                        skillIndexCacheExpirySchedule[expireTick] = list;
                    }
                    list.Add(pawnId);
                }
            }

            AutoArmLogger.Debug(() => $"SkillCache schedule rebuilt: {skillCache.Count} skill entries, " +
                              $"{skillIndexCache.Count} index entries, " +
                              $"{skillCacheExpirySchedule.Count} skill expiry ticks, " +
                              $"{skillIndexCacheExpirySchedule.Count} index expiry ticks scheduled");
        }

        private static void RemoveFromSchedule(int pawnId, int expireTick)
        {
            if (skillCacheExpirySchedule.TryGetValue(expireTick, out var list))
            {
                list.Remove(pawnId);
                if (list.Count == 0)
                {
                    skillCacheExpirySchedule.Remove(expireTick);
                }
            }
        }

        private static void RemoveFromIndexSchedule(int pawnId, int expireTick)
        {
            if (skillIndexCacheExpirySchedule.TryGetValue(expireTick, out var list))
            {
                list.Remove(pawnId);
                if (list.Count == 0)
                {
                    skillIndexCacheExpirySchedule.Remove(expireTick);
                }
            }
        }

        private static bool IsLuxuryMeleeMaterial(ThingDef stuff)
        {
            if (stuff?.stuffProps?.statFactors == null)
                return false;

            float combatMult = 1.0f;

            foreach (var statFactor in stuff.stuffProps.statFactors)
            {
                if (statFactor.stat == StatDefOf.SharpDamageMultiplier)
                {
                    combatMult *= statFactor.value;
                }
                else if (statFactor.stat == StatDefOf.MeleeWeapon_DamageMultiplier)
                {
                    combatMult *= statFactor.value;
                }
                else if (statFactor.stat == StatDefOf.MeleeWeapon_CooldownMultiplier)
                {
                    if (statFactor.value > 0f)
                    {
                        combatMult *= (1.0f / statFactor.value);
                    }
                }
            }

            float priceMult = GetMaterialPriceMultiplier(stuff);

            return (priceMult > 5.0f && combatMult < 0.95f);
        }

        private static float GetMaterialPriceMultiplier(ThingDef stuff)
        {
            if (stuff?.stuffProps?.statFactors == null)
                return 1.0f;

            foreach (var statFactor in stuff.stuffProps.statFactors)
            {
                if (statFactor.stat == StatDefOf.MarketValue)
                {
                    return statFactor.value;
                }
            }

            return 1.0f;
        }

        /// <summary>
        /// Score breakdown details (for tooltips, debugging)
        /// </summary>
        public struct ScoreBreakdown
        {
            public float baseWeaponScore;
            public float outfitPolicyScore;
            public float personaScore;
            public float hunterScore;
            public float skillScore;
            public float skillMismatchMultiplier;
            public float ceAmmoModifier;
            public float totalScore;

            public bool isForced;
            public bool isForbidden;
        }

        /// <summary>
        /// Detailed score breakdown with all components
        /// </summary>
        public static ScoreBreakdown GetScoreBreakdown(Pawn pawn, ThingWithComps weapon)
        {
            var breakdown = new ScoreBreakdown();

            if (pawn == null || weapon == null)
            {
                return breakdown;
            }

            if (pawn.equipment?.Primary == weapon &&
                Helpers.ForcedWeapons.IsForced(pawn, weapon) &&
                AutoArmMod.settings?.allowForcedWeaponUpgrades == false)
            {
                breakdown.isForced = true;
                breakdown.totalScore = 10000f;
                return breakdown;
            }

            breakdown.outfitPolicyScore = GetOutfitPolicyScore(pawn, weapon);
            breakdown.isForbidden = breakdown.outfitPolicyScore <= Constants.OutfitFilterDisallowedPenalty;

            if (breakdown.isForbidden && pawn.equipment?.Primary != weapon)
            {
                breakdown.totalScore = breakdown.outfitPolicyScore;
                return breakdown;
            }

            breakdown.personaScore = GetPersonaWeaponScore(pawn, weapon);
            if (breakdown.personaScore <= Constants.OutfitFilterDisallowedPenalty)
            {
                breakdown.totalScore = breakdown.personaScore;
                return breakdown;
            }

            breakdown.hunterScore = GetHunterScore(pawn, weapon);

            float mismatchMultiplier = 1f;
            breakdown.skillScore = GetSkillScore(pawn, weapon, out mismatchMultiplier);
            breakdown.skillMismatchMultiplier = mismatchMultiplier;

            breakdown.baseWeaponScore = GetWeaponPropertyScore(pawn, weapon);

            float total = 0f;
            total += breakdown.outfitPolicyScore;
            total += breakdown.personaScore;
            total += breakdown.hunterScore;
            total *= mismatchMultiplier;
            total += breakdown.skillScore;

            float adjustedWeaponScore = breakdown.baseWeaponScore * mismatchMultiplier;
            total += adjustedWeaponScore;

            breakdown.ceAmmoModifier = 1.0f;
            if (CECompat.IsLoaded() && CECompat.ShouldCheckAmmo())
            {
                breakdown.ceAmmoModifier = CECompat.GetAmmoScoreModifier(weapon, pawn);
                total *= breakdown.ceAmmoModifier;
            }

            breakdown.totalScore = total;
            return breakdown;
        }
    }
}
