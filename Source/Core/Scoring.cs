
using AutoArm.Definitions;
using AutoArm.Helpers;
using RimWorld;
using System;
using System.Collections.Generic;
using Verse;

namespace AutoArm
{
    internal static class Scoring
    {
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

        private static readonly Dictionary<WeaponCacheKey, CachedWeaponProperties> propertiesCache = new Dictionary<WeaponCacheKey, CachedWeaponProperties>();

        private const int MaxWeaponCacheSize = Constants.MaxWeaponCacheSize;
        private const int CacheEvictionBatchSize = MaxWeaponCacheSize / 16;

        private class WeaponBaseProperties
        {
            public bool IsMelee;
            public bool IsRanged;
            public bool IsSituational;
            public float RangePreference;
            public float ComputedBaseScore;
            public float AbilityMultiplier;
        }

        private static readonly Dictionary<ThingDef, bool> situationalWeapons = new Dictionary<ThingDef, bool>(512);

        private static bool situationalCacheInitialized = false;

        private static readonly Dictionary<int, (float shooting, float melee, int lastUpdateTick)> skillCache =
            new Dictionary<int, (float, float, int)>();

        private static readonly Dictionary<int, (int shootingIndex, int meleeIndex, int lastUpdateTick)> skillIndexCache =
            new Dictionary<int, (int, int, int)>();

        private const int SkillCacheLifetimeTicks = Constants.StandardCacheDuration;

        public static float GetTotalScore(Pawn pawn, ThingWithComps weapon)
        {
            return GetScoreBreakdown(pawn, weapon).totalScore;
        }

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
                PerfMetrics.ReportPropertyCacheHit();
                cached.lastAccessTick = Find.TickManager.TicksGame;
                baseProps = cached.properties;
            }
            else
            {
                PerfMetrics.ReportPropertyCacheMiss();
                baseProps = CalculateBaseWeaponProperties(weapon);
                propertiesCache[cacheKey] = new CachedWeaponProperties(baseProps, Find.TickManager.TicksGame);

                if (propertiesCache.Count > MaxWeaponCacheSize)
                    EvictOldestEntries();
            }

            float baseScore = baseProps.ComputedBaseScore;

            baseScore *= Constants.CombatScoreMultiplier;

            if (baseProps.IsSituational)
                baseScore *= Constants.SituationalWeaponModifier;

            if (baseProps.AbilityMultiplier != 1f)
                baseScore *= baseProps.AbilityMultiplier;

            if (baseProps.IsRanged)
                baseScore *= AutoArmMod.GetRangedMultiplier();
            else if (baseProps.IsMelee)
                baseScore *= AutoArmMod.GetMeleeMultiplier();

            if (baseProps.IsRanged && baseProps.RangePreference != 1f)
                baseScore *= baseProps.RangePreference;

            if (weapon.MaxHitPoints > 0 && weapon.HitPoints * 2 < weapon.MaxHitPoints)
                baseScore *= Constants.DamagedWeaponPenalty;

            return baseScore;
        }

        public static string GetBaseScoreBreakdownText(ThingWithComps weapon)
        {
            if (weapon?.def == null)
                return "";

            QualityCategory quality = QualityCategory.Normal;
            Caching.Components.TryGetWeaponQuality(weapon, out quality);
            var statReq = StatRequest.For(weapon);

            bool isMelee = weapon.def.IsMeleeWeapon;
            bool isRanged = weapon.def.IsRangedWeapon;
            bool isSituational = IsSituationalWeapon(weapon);
            bool isDamaged = weapon.MaxHitPoints > 0 && weapon.HitPoints * 2 < weapon.MaxHitPoints;

            string s = "";
            float dps = 0f;
            float accuracy = 1f;
            float apBonus = 1f;
            bool hasAccuracy = false;
            bool canCompute = false;

            if (isMelee)
            {
                dps = StatDefOf.MeleeWeapon_AverageDPS.Worker.GetValue(statReq);
                var apStat = GetMeleeAverageAPStat();
                float ap = apStat != null ? apStat.Worker.GetValue(statReq) : 0f;
                apBonus = 1.0f + ap;
                canCompute = true;

                s += "Type: Melee\n\n";
                s += "Combat stats:\n";
                s += $"  Avg DPS:            {dps:F2}\n";
                s += $"  Avg armor pen:      {ap:P0}\n";
                s += $"  AP bonus (1+AP): ×{apBonus:F2}\n";
            }
            else if (isRanged)
            {
                var verb = SelectPrimaryVerb(weapon.def, weapon);
                if (verb == null || verb.defaultProjectile?.projectile == null)
                {
                    s += "Type: Ranged (no default projectile)\n\nBase score: 0\n";
                    return s;
                }

                float damage = verb.defaultProjectile.projectile.GetDamageAmount(weapon);
                int burstCount = Math.Max(1, verb.burstShotCount);
                float warmupMult = StatDefOf.RangedWeapon_WarmupMultiplier.Worker.GetValue(statReq);
                float warmup = verb.warmupTime * warmupMult;
                float cooldown = StatDefOf.RangedWeapon_Cooldown.Worker.GetValue(statReq);
                float burstGap = verb.ticksBetweenBurstShots / 60f;
                float cycle = warmup + (burstCount - 1) * burstGap + cooldown;
                if (cycle < 0.1f) cycle = 0.1f;
                dps = damage * burstCount / cycle;

                float accShort = StatDefOf.AccuracyShort.Worker.GetValue(statReq);
                float accMed = StatDefOf.AccuracyMedium.Worker.GetValue(statReq);
                float accLong = StatDefOf.AccuracyLong.Worker.GetValue(statReq);
                accuracy = 0.2f * accShort + 0.3f * accMed + 0.5f * accLong;
                if (accuracy <= 0f) accuracy = 0.5f;
                float baseAp = verb.defaultProjectile.projectile.GetArmorPenetration(null);
                float apMult = StatDefOf.RangedWeapon_ArmorPenetrationMultiplier.Worker.GetValue(statReq);
                float ap = baseAp * apMult;
                apBonus = 1.0f + ap;
                hasAccuracy = true;
                canCompute = true;

                s += "Type: Ranged\n\n";
                s += "Cycle:\n";
                s += $"  Damage/shot:        {damage:F1}\n";
                s += $"  Burst count:        {burstCount}\n";
                s += $"  Warmup:             {warmup:F2}s\n";
                s += $"  Burst gap:          {burstGap:F2}s\n";
                s += $"  Cooldown:           {cooldown:F2}s\n";
                s += $"  Total cycle:        {cycle:F2}s\n";
                s += $"  DPS (dmg×burst÷cycle): {dps:F2}\n";
                s += "\nHit & pen:\n";
                s += $"  Acc short/med/long: {accShort:P0} / {accMed:P0} / {accLong:P0}\n";
                s += $"  Blended accuracy:   {accuracy:P0}   (0.2·S + 0.3·M + 0.5·L)\n";
                s += $"  Armor pen:          {ap:P0}\n";
                s += $"  AP bonus (1+AP): ×{apBonus:F2}\n";
            }
            else
            {
                s += "Type: Unknown\n\nBase score: 0\n";
                return s;
            }

            if (!canCompute)
                return s;

            s += "\nScore calculation:\n";
            float running = dps;
            s += $"  {dps,8:F2}     DPS\n";
            if (hasAccuracy)
            {
                running *= accuracy;
                s += $"  × {accuracy,6:F2}    accuracy\n";
            }
            running *= apBonus;
            s += $"  × {apBonus,6:F2}    AP bonus\n";
            running *= Constants.CombatScoreMultiplier;
            s += $"  × {Constants.CombatScoreMultiplier,6:F2}    combat scale\n";

            if (isSituational)
            {
                running *= Constants.SituationalWeaponModifier;
                s += $"  × {Constants.SituationalWeaponModifier,6:F2}    situational\n";
            }

            var abilityProps = ResolveAbilityProps(weapon);
            if (abilityProps != null)
            {
                int charges = 1;
                if (abilityProps is CompProperties_EquippableAbilityReloadable reloadable && reloadable.maxCharges > 0)
                    charges = reloadable.maxCharges;
                float bonus = Math.Min(0.20f, 0.07f + charges * 0.03f);
                float abilityMult = 1f + bonus;
                running *= abilityMult;
                s += $"  × {abilityMult,6:F2}    ability ({charges} {(charges == 1 ? "charge" : "charges")})\n";
            }

            if (isRanged)
            {
                float rm = AutoArmMod.GetRangedMultiplier();
                running *= rm;
                s += $"  × {rm,6:F2}    ranged type\n";

                var rangeVerb = SelectPrimaryVerb(weapon.def, weapon);
                if (rangeVerb != null && rangeVerb.range > 0f)
                {
                    float rp = rangeVerb.range / 28f;
                    if (rp < 0.7f) rp = 0.7f;
                    else if (rp > 1.2f) rp = 1.2f;
                    if (rp != 1f)
                    {
                        running *= rp;
                        s += $"  × {rp,6:F2}    range ({rangeVerb.range:F0} tiles)\n";
                    }
                }
            }
            else if (isMelee)
            {
                float mm = AutoArmMod.GetMeleeMultiplier();
                running *= mm;
                s += $"  × {mm,6:F2}    melee type\n";
            }

            if (isDamaged)
            {
                running *= Constants.DamagedWeaponPenalty;
                s += $"  × {Constants.DamagedWeaponPenalty,6:F2}    damaged (<50% HP)\n";
            }

            s += $"  ─────\n";
            s += $"  = {running,6:F0}    base score\n";
            return s;
        }

        private static StatDef meleeAverageAPStat;
        private static bool meleeAverageAPStatResolved;

        private static StatDef GetMeleeAverageAPStat()
        {
            if (!meleeAverageAPStatResolved)
            {
                meleeAverageAPStat = DefDatabase<StatDef>.GetNamedSilentFail("MeleeWeapon_AverageArmorPenetration");
                meleeAverageAPStatResolved = true;
            }
            return meleeAverageAPStat;
        }

        private static float ComputeMeleeBaseScore(StatRequest req)
        {
            float dps = StatDefOf.MeleeWeapon_AverageDPS.Worker.GetValue(req);
            var apStat = GetMeleeAverageAPStat();
            float ap = apStat != null ? apStat.Worker.GetValue(req) : 0f;
            float apBonus = 1.0f + ap;
            return dps * apBonus;
        }

        private static float ComputeBlendedAccuracyAbstract(StatRequest req)
        {
            float accShort = StatDefOf.AccuracyShort.Worker.GetValue(req);
            float accMed = StatDefOf.AccuracyMedium.Worker.GetValue(req);
            float accLong = StatDefOf.AccuracyLong.Worker.GetValue(req);
            float blended = 0.2f * accShort + 0.3f * accMed + 0.5f * accLong;
            if (blended <= 0f)
                blended = 0.5f;
            return blended;
        }

        private static float ComputeRangedBaseScore(ThingWithComps weapon, StatRequest req, VerbProperties verb)
        {
            if (verb == null || verb.defaultProjectile?.projectile == null)
                return 0f;

            float damage = verb.defaultProjectile.projectile.GetDamageAmount(weapon);
            int burstCount = Math.Max(1, verb.burstShotCount);

            float warmupMult = StatDefOf.RangedWeapon_WarmupMultiplier.Worker.GetValue(req);
            float warmup = verb.warmupTime * warmupMult;
            float cooldown = StatDefOf.RangedWeapon_Cooldown.Worker.GetValue(req);
            float burstGap = verb.ticksBetweenBurstShots / 60f;
            float cycle = warmup + (burstCount - 1) * burstGap + cooldown;
            if (cycle < 0.1f)
                cycle = 0.1f;

            float dps = damage * burstCount / cycle;

            float accuracy = ComputeBlendedAccuracyAbstract(req);

            float baseAp = verb.defaultProjectile.projectile.GetArmorPenetration(null);
            float apMult = StatDefOf.RangedWeapon_ArmorPenetrationMultiplier.Worker.GetValue(req);
            float ap = baseAp * apMult;
            float apBonus = 1.0f + ap;

            float result = dps * accuracy * apBonus;
            if (float.IsNaN(result) || float.IsInfinity(result))
                return 0f;
            return result;
        }

        private static void EvictOldestEntries()
        {
            if (propertiesCache.Count <= MaxWeaponCacheSize)
                return;

            var entries = ListPool<KeyValuePair<WeaponCacheKey, int>>.Get(propertiesCache.Count);
            int removed = 0;
            try
            {
                foreach (var kvp in propertiesCache)
                    entries.Add(new KeyValuePair<WeaponCacheKey, int>(kvp.Key, kvp.Value.lastAccessTick));

                entries.Sort((a, b) => a.Value.CompareTo(b.Value));

                for (int i = 0; i < CacheEvictionBatchSize && i < entries.Count; i++)
                {
                    if (propertiesCache.Remove(entries[i].Key))
                        removed++;
                }
            }
            finally { ListPool<KeyValuePair<WeaponCacheKey, int>>.Return(entries); }

            AutoArmLogger.Debug(() => $"[WeaponCache] Evicted {removed} oldest entries, cache now at {propertiesCache.Count}");
        }

        private static WeaponBaseProperties CalculateBaseWeaponProperties(ThingWithComps weapon)
        {
            QualityCategory quality = QualityCategory.Normal;
            Caching.Components.TryGetWeaponQuality(weapon, out quality);
            var statReq = StatRequest.For(weapon);

            bool isMelee = weapon.def.IsMeleeWeapon;
            bool isRanged = weapon.def.IsRangedWeapon;

            float rangePref = 1f;
            float computedBaseScore = 0f;

            if (isRanged)
            {
                var verb = SelectPrimaryVerb(weapon.def, weapon);
                if (verb != null && verb.range > 0f)
                {
                    rangePref = verb.range / 28f;
                    if (rangePref < 0.7f) rangePref = 0.7f;
                    else if (rangePref > 1.2f) rangePref = 1.2f;
                }
                computedBaseScore = ComputeRangedBaseScore(weapon, statReq, verb);
            }
            else if (isMelee)
            {
                computedBaseScore = ComputeMeleeBaseScore(statReq);
            }

            return new WeaponBaseProperties
            {
                IsMelee = isMelee,
                IsRanged = isRanged,
                IsSituational = IsSituationalWeapon(weapon),
                RangePreference = rangePref,
                ComputedBaseScore = computedBaseScore,
                AbilityMultiplier = ComputeAbilityMultiplier(weapon),
            };
        }

        private static CompProperties_EquippableAbility ResolveAbilityProps(ThingWithComps weapon)
        {
            var defProps = weapon.def.GetCompProperties<CompProperties_EquippableAbility>();
            if (defProps != null && defProps.abilityDef != null)
                return defProps;

            var instanceComp = weapon.TryGetComp<CompEquippableAbilityReloadable>();
            if (instanceComp?.Props?.abilityDef != null)
                return instanceComp.Props;

            return null;
        }

        private static float ComputeAbilityMultiplier(ThingWithComps weapon)
        {
            var abilityProps = ResolveAbilityProps(weapon);
            if (abilityProps == null)
                return 1f;

            int charges = 1;
            if (abilityProps is CompProperties_EquippableAbilityReloadable reloadable && reloadable.maxCharges > 0)
                charges = reloadable.maxCharges;

            float bonus = Math.Min(0.20f, 0.07f + charges * 0.03f);
            return 1f + bonus;
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

            VerbProperties verb = null;
            for (int i = 0; i < weaponDef.Verbs.Count; i++)
            {
                if (weaponDef.Verbs[i] != null && weaponDef.Verbs[i].isPrimary)
                {
                    verb = weaponDef.Verbs[i];
                    break;
                }
            }
            if (verb == null)
                verb = weaponDef.Verbs[0];

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

            if (weaponDef.weaponTags != null)
            {
                for (int i = 0; i < weaponDef.weaponTags.Count; i++)
                {
                    var tag = weaponDef.weaponTags[i];
                    if (tag != null && (tag.IndexOf("SingleUse", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                        tag.IndexOf("OneShot", StringComparison.OrdinalIgnoreCase) >= 0))
                        return true;
                }
            }

            if (weaponDef.thingSetMakerTags != null)
            {
                for (int i = 0; i < weaponDef.thingSetMakerTags.Count; i++)
                {
                    var tag = weaponDef.thingSetMakerTags[i];
                    if (tag != null && (tag.IndexOf("SingleUse", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                        tag.IndexOf("OneShot", StringComparison.OrdinalIgnoreCase) >= 0))
                        return true;
                }
            }

            if (verb.verbClass != null)
            {
                var verbClassName = verb.verbClass.Name;
                if (verbClassName != null && (verbClassName.IndexOf("OneUse", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                              verbClassName.IndexOf("OneShot", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                              verbClassName.IndexOf("SingleUse", StringComparison.OrdinalIgnoreCase) >= 0))
                    return true;
            }

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
            if (filter == null)
                return 0f;

            if (!filter.Allows(weapon.def) && !filter.OnlySpecialFilters)
                return Constants.OutfitFilterDisallowedPenalty;

            if (filter.AllowedQualityLevels != QualityRange.All &&
                weapon.def.FollowQualityThingFilter() &&
                weapon.TryGetQuality(out var quality) &&
                !filter.AllowedQualityLevels.Includes(quality))
            {
                return Constants.OutfitFilterDisallowedPenalty;
            }

            return 0f;
        }

        private static float GetPersonaMultiplier(Pawn pawn, ThingWithComps weapon)
        {
            if (!Caching.Components.IsPersonaWeapon(weapon))
                return 1.0f;

            if (CompBiocodable.IsBiocodedFor(weapon, pawn))
                return Constants.PersonaWeaponMultiplier;

            return 1.0f;
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
                PerfMetrics.ReportSkillCacheHit();

                shootingSkill = cached.shooting;
                meleeSkill = cached.melee;
            }
            else
            {
                PerfMetrics.ReportSkillCacheMiss();

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

                        skillIndexCache[pawnId] = (shootingIndex, meleeIndex, currentTick);
                    }
                }
                else
                {
                    shootingSkill = 0f;
                    meleeSkill = 0f;
                }

                skillCache[pawnId] = (shootingSkill, meleeSkill, currentTick);
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
                try
                {
                    foreach (var kvp in skillCache)
                    {
                        if ((currentTick - kvp.Value.lastUpdateTick) > SkillCacheLifetimeTicks * 2)
                        {
                            expiredIds.Add(kvp.Key);
                        }
                    }
                    foreach (var id in expiredIds)
                        skillCache.Remove(id);
                }
                finally
                {
                    ListPool<int>.Return(expiredIds);
                }
            }

            if (skillIndexCache.Count > 150)
            {
                var expiredIds = ListPool<int>.Get();
                try
                {
                    foreach (var kvp in skillIndexCache)
                    {
                        if ((currentTick - kvp.Value.lastUpdateTick) > SkillCacheLifetimeTicks * 8)
                        {
                            expiredIds.Add(kvp.Key);
                        }
                    }
                    foreach (var id in expiredIds)
                        skillIndexCache.Remove(id);
                }
                finally
                {
                    ListPool<int>.Return(expiredIds);
                }
            }

            if (skillCache.Count > 200)
            {
                var pairs = ListPool<KeyValuePair<int, (float shootingSkill, float meleeSkill, int lastUpdateTick)>>.Get(skillCache.Count);
                try
                {
                    foreach (var kvp in skillCache)
                    {
                        pairs.Add(kvp);
                    }

                    pairs.SortBy(kvp => -kvp.Value.lastUpdateTick);

                    int keepCount = Math.Min(100, pairs.Count);

                    skillCache.Clear();
                    for (int i = 0; i < keepCount; i++)
                    {
                        skillCache[pairs[i].Key] = pairs[i].Value;
                    }
                }
                finally
                {
                    ListPool<KeyValuePair<int, (float shootingSkill, float meleeSkill, int lastUpdateTick)>>.Return(pairs);
                }
            }

            if (skillIndexCache.Count > 200)
            {
                var pairs = ListPool<KeyValuePair<int, (int shootingIndex, int meleeIndex, int lastUpdateTick)>>.Get(skillIndexCache.Count);
                try
                {
                    foreach (var kvp in skillIndexCache)
                    {
                        pairs.Add(kvp);
                    }

                    pairs.SortBy(kvp => -kvp.Value.lastUpdateTick);

                    int keepCount = Math.Min(100, pairs.Count);

                    skillIndexCache.Clear();
                    for (int i = 0; i < keepCount; i++)
                    {
                        skillIndexCache[pairs[i].Key] = pairs[i].Value;
                    }
                }
                finally
                {
                    ListPool<KeyValuePair<int, (int shootingIndex, int meleeIndex, int lastUpdateTick)>>.Return(pairs);
                }
            }
        }

        public static void ResetSkillCache()
        {
            skillCache.Clear();
            skillIndexCache.Clear();
            // TickScheduler clears events
            AutoArmLogger.Debug(() => "Scoring skill cache reset");
        }

        internal struct ScoreBreakdown
        {
            public float baseWeaponScore;
            public float outfitPolicyScore;
            public float personaMultiplier;
            public float hunterScore;
            public float skillScore;
            public float skillMismatchMultiplier;
            public float ceAmmoModifier;
            public float totalScore;

            public bool isForced;
            public bool isForbidden;
        }

        public static ScoreBreakdown GetScoreBreakdown(Pawn pawn, ThingWithComps weapon)
        {
            var breakdown = new ScoreBreakdown();

            if (pawn == null || weapon == null)
            {
                return breakdown;
            }

            if (pawn.equipment?.Primary == weapon &&
                ForcedWeapons.IsForced(pawn, weapon) &&
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

            breakdown.personaMultiplier = GetPersonaMultiplier(pawn, weapon);
            breakdown.hunterScore = GetHunterScore(pawn, weapon);

            float mismatchMultiplier = 1f;
            breakdown.skillScore = GetSkillScore(pawn, weapon, out mismatchMultiplier);
            breakdown.skillMismatchMultiplier = mismatchMultiplier;

            breakdown.baseWeaponScore = GetWeaponPropertyScore(pawn, weapon);

            float total = 0f;
            total += breakdown.outfitPolicyScore;
            total += breakdown.hunterScore;
            total *= mismatchMultiplier;
            total += breakdown.skillScore;

            float adjustedWeaponScore = breakdown.baseWeaponScore * mismatchMultiplier;
            total += adjustedWeaponScore;

            total *= breakdown.personaMultiplier;

            breakdown.ceAmmoModifier = 1.0f;
            if (CECompat.IsLoaded && CECompat.ShouldCheckAmmo())
            {
                breakdown.ceAmmoModifier = CECompat.GetAmmoScoreModifier(weapon, pawn);
                total *= breakdown.ceAmmoModifier;
            }

            breakdown.totalScore = total;
            return breakdown;
        }
    }
}
