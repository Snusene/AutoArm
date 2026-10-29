
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
            public readonly int itemId;

            public WeaponCacheKey(int defHash, int quality, int stuffHash, int itemId = 0)
            {
                this.defHash = defHash;
                this.quality = quality;
                this.stuffHash = stuffHash;
                this.itemId = itemId;
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = defHash;
                    hash = (hash * 397) ^ quality;
                    hash = (hash * 397) ^ stuffHash;
                    hash = (hash * 397) ^ itemId;
                    return hash;
                }
            }

            public override bool Equals(object obj)
            {
                return obj is WeaponCacheKey key && Equals(key);
            }

            public bool Equals(WeaponCacheKey other)
            {
                return defHash == other.defHash && quality == other.quality && stuffHash == other.stuffHash && itemId == other.itemId;
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
            public float[] RangeWeights;
        }

        private static readonly float[] RangeSamples = { 3f, 8f, 12f, 18f, 25f, 32f, 40f };

        private static readonly Dictionary<ThingDef, bool> situationalWeapons = new Dictionary<ThingDef, bool>(512);

        private static bool situationalCacheInitialized = false;

        public static float GetTotalScore(Pawn pawn, ThingWithComps weapon)
        {
            return GetScoreBreakdown(pawn, weapon).totalScore;
        }

        public static float GetWeaponPropertyScore(Pawn pawn, ThingWithComps weapon)
        {
            if (weapon?.def == null)
                return 0f;

            QualityCategory quality = QualityCategory.Normal;
            weapon.TryGetQuality(out quality);

            int stuffHash = weapon.Stuff != null ? weapon.Stuff.shortHash : -1;
            int itemId = Caching.Components.IsUnique(weapon) ? weapon.thingIDNumber : 0;
            var cacheKey = new WeaponCacheKey(weapon.def.shortHash, (int)quality, stuffHash, itemId);

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

            if (pawn != null && baseProps.RangeWeights != null && !CECompat.IsLoaded)
                baseScore *= RangeFit(pawn, baseProps.RangeWeights);

            if (weapon.MaxHitPoints > 0 && weapon.HitPoints * 2 < weapon.MaxHitPoints)
                baseScore *= Constants.DamagedWeaponPenalty;

            return baseScore;
        }

        public static string GetBaseScoreBreakdownText(ThingWithComps weapon)
        {
            if (weapon?.def == null)
                return "";

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
                s += $"  Avg DPS: {dps:F2}\n";
                s += $"  Avg armor pen: {ap:P0}\n";
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

                var p = ComputeRangedParts(weapon, statReq, verb);
                dps = p.dps * p.sustain;
                accuracy = p.accuracy;
                apBonus = p.apBonus;
                hasAccuracy = true;
                canCompute = true;

                s += p.ce ? "Type: Ranged (Combat Extended)\n\n" : "Type: Ranged\n\n";
                s += "Cycle:\n";
                if (p.pellets > 1)
                    s += $"  Damage/shot: {p.damage:F1} ({p.pellets} pellets, extras at {Constants.CEExtraPelletValue:P0})\n";
                else
                    s += $"  Damage/shot: {p.damage:F1}\n";
                s += $"  Burst count: {p.burst}\n";
                s += $"  Warmup: {p.warmup:F2}s\n";
                s += $"  Burst gap: {p.burstGap:F2}s\n";
                s += $"  Cooldown: {p.cooldown:F2}s\n";
                s += $"  Total cycle: {p.cycle:F2}s\n";
                s += $"  DPS (dmg×burst÷cycle): {p.dps:F2}\n";
                if (p.mag > 0)
                    s += $"  Magazine {p.mag}, reload {p.reload:F2}s: ×{p.sustain:F2} sustained\n";
                s += "\nHit & pen:\n";
                if (p.ce)
                {
                    s += $"  Accuracy (√sights efficiency): ×{p.accuracy:F2}\n";
                    s += $"  Sharp pen: {p.ap:F2} mm\n";
                    s += $"  AP bonus (1+pen÷{Constants.CESharpPenScale:F0}): ×{p.apBonus:F2}\n";
                }
                else
                {
                    s += $"  Blended accuracy: {p.accuracy:P0} (0.2·S + 0.3·M + 0.5·L)\n";
                    s += $"  Armor pen: {p.ap:P0}\n";
                    s += $"  AP bonus (1+AP): ×{p.apBonus:F2}\n";
                }
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
            s += $"  {dps:F2}  DPS\n";
            if (hasAccuracy)
            {
                running *= accuracy;
                s += $"  × {accuracy:F2}  accuracy\n";
            }
            running *= apBonus;
            s += $"  × {apBonus:F2}  AP bonus\n";
            running *= Constants.CombatScoreMultiplier;
            s += $"  × {Constants.CombatScoreMultiplier:F2}  combat scale\n";

            if (isSituational)
            {
                running *= Constants.SituationalWeaponModifier;
                s += $"  × {Constants.SituationalWeaponModifier:F2}  situational\n";
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
                s += $"  × {abilityMult:F2}  ability ({charges} {(charges == 1 ? "charge" : "charges")})\n";
            }

            if (isRanged)
            {
                float rm = AutoArmMod.GetRangedMultiplier();
                running *= rm;
                s += $"  × {rm:F2}  ranged type\n";

                var rangeVerb = SelectPrimaryVerb(weapon.def, weapon);
                if (rangeVerb != null && rangeVerb.range > 0f)
                {
                    float rp = rangeVerb.range / 28f;
                    if (rp < 0.7f) rp = 0.7f;
                    else if (rp > 1.2f) rp = 1.2f;
                    if (rp != 1f)
                    {
                        running *= rp;
                        s += $"  × {rp:F2}  range ({rangeVerb.range:F0} tiles)\n";
                    }
                }
            }
            else if (isMelee)
            {
                float mm = AutoArmMod.GetMeleeMultiplier();
                running *= mm;
                s += $"  × {mm:F2}  melee type\n";
            }

            if (isDamaged)
            {
                running *= Constants.DamagedWeaponPenalty;
                s += $"  × {Constants.DamagedWeaponPenalty:F2}  damaged (<50% HP)\n";
            }

            s += $"  = {running:F0}  base score\n";
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

        private struct RangedParts
        {
            public float damage;
            public int pellets;
            public int burst;
            public float warmup;
            public float burstGap;
            public float cooldown;
            public float cycle;
            public float dps;
            public int mag;
            public float reload;
            public float sustain;
            public float accuracy;
            public float ap;
            public float apBonus;
            public bool ce;
        }

        private static RangedParts ComputeRangedParts(ThingWithComps weapon, StatRequest req, VerbProperties verb)
        {
            var proj = verb.defaultProjectile.projectile;
            var p = new RangedParts { ce = CECompat.IsLoaded, pellets = 1, sustain = 1f };

            p.damage = proj.GetDamageAmount(weapon);
            if (p.ce)
            {
                p.pellets = CECompat.Pellets(proj);
                p.damage *= 1f + (p.pellets - 1) * Constants.CEExtraPelletValue;
            }

            float burst = verb.burstShotCount;
            float gapTicks = verb.ticksBetweenBurstShots;
            if (weapon.TryGetComp<CompUniqueWeapon>(out var unique))
            {
                var traits = unique.TraitsListForReading;
                for (int i = 0; i < traits.Count; i++)
                {
                    burst *= traits[i].burstShotCountMultiplier;
                    gapTicks /= traits[i].burstShotSpeedMultiplier;
                }
            }

            p.burst = Math.Max(1, p.ce ? CECompat.Burst(weapon, verb) : UnityEngine.Mathf.CeilToInt(burst));
            p.warmup = verb.warmupTime * StatDefOf.RangedWeapon_WarmupMultiplier.Worker.GetValue(req);
            p.cooldown = StatDefOf.RangedWeapon_Cooldown.Worker.GetValue(req);
            p.burstGap = gapTicks / 60f;
            p.cycle = p.warmup + (p.burst - 1) * p.burstGap + p.cooldown;
            if (p.cycle < 0.1f)
                p.cycle = 0.1f;
            p.dps = p.damage * p.burst / p.cycle;

            if (p.ce)
            {
                p.mag = CECompat.Magazine(weapon, out p.reload);
                if (p.mag > 0 && p.reload > 0f)
                {
                    float firing = Math.Max(1f, (float)p.mag / p.burst) * p.cycle;
                    p.sustain = firing / (firing + p.reload);
                }
                p.accuracy = (float)Math.Sqrt(CECompat.Sights(weapon));
            }
            else
            {
                p.accuracy = ComputeBlendedAccuracyAbstract(req);
            }

            float apMult = StatDefOf.RangedWeapon_ArmorPenetrationMultiplier.Worker.GetValue(req);
            if (p.ce && CECompat.TrySharpPen(proj, out float sharp))
            {
                p.ap = sharp * apMult;
                p.apBonus = 1f + p.ap / Constants.CESharpPenScale;
            }
            else
            {
                p.ap = proj.GetArmorPenetration(null) * apMult;
                p.apBonus = 1f + p.ap;
            }
            return p;
        }

        private static float ComputeRangedBaseScore(ThingWithComps weapon, StatRequest req, VerbProperties verb)
        {
            if (verb == null || verb.defaultProjectile?.projectile == null)
                return 0f;

            var p = ComputeRangedParts(weapon, req, verb);
            float score = p.dps * p.sustain * p.accuracy * p.apBonus;
            if (float.IsNaN(score) || float.IsInfinity(score))
                return 0f;
            return score;
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
            var statReq = StatRequest.For(weapon);

            bool isMelee = weapon.def.IsMeleeWeapon;
            bool isRanged = weapon.def.IsRangedWeapon;

            float rangePref = 1f;
            float computedBaseScore = 0f;
            float[] rangeWeights = null;

            if (isRanged)
            {
                var verb = SelectPrimaryVerb(weapon.def, weapon);
                if (verb != null && verb.range > 0f)
                {
                    rangePref = verb.range / 28f;
                    if (rangePref < 0.7f) rangePref = 0.7f;
                    else if (rangePref > 1.2f) rangePref = 1.2f;
                    rangeWeights = ComputeRangeWeights(verb, weapon);
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
                RangeWeights = rangeWeights,
            };
        }

        private static float[] ComputeRangeWeights(VerbProperties verb, ThingWithComps weapon)
        {
            var weights = new float[RangeSamples.Length];
            bool any = false;
            for (int i = 0; i < RangeSamples.Length; i++)
            {
                float d = RangeSamples[i];
                if (d > verb.range)
                    break;
                weights[i] = d * verb.GetHitChanceFactor(weapon, d);
                any = true;
            }
            return any ? weights : null;
        }

        internal static float RangeFit(Pawn pawn, float[] weights)
        {
            float touch = pawn.GetStatValue(StatDefOf.ShootingAccuracyFactor_Touch, true, 2500);
            float shrt = pawn.GetStatValue(StatDefOf.ShootingAccuracyFactor_Short, true, 2500);
            float medium = pawn.GetStatValue(StatDefOf.ShootingAccuracyFactor_Medium, true, 2500);
            float lng = pawn.GetStatValue(StatDefOf.ShootingAccuracyFactor_Long, true, 2500);
            if (touch == 1f && shrt == 1f && medium == 1f && lng == 1f)
                return 1f;

            float sum = 0f;
            float fit = 0f;
            for (int i = 0; i < weights.Length; i++)
            {
                float w = weights[i];
                if (w <= 0f)
                    continue;
                float d = RangeSamples[i];
                float f;
                if (d <= 3f) f = touch;
                else if (d <= 12f) f = UnityEngine.Mathf.Lerp(touch, shrt, (d - 3f) / 9f);
                else if (d <= 25f) f = UnityEngine.Mathf.Lerp(shrt, medium, (d - 12f) / 13f);
                else if (d <= 40f) f = UnityEngine.Mathf.Lerp(medium, lng, (d - 25f) / 15f);
                else f = lng;
                sum += w;
                fit += w * f;
            }
            return sum > 0f ? fit / sum : 1f;
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

        public static bool OutfitDisallows(Pawn pawn, ThingWithComps weapon)
        {
            var filter = pawn.outfits?.CurrentApparelPolicy?.filter;
            if (filter == null)
                return false;

            if (!filter.Allows(weapon.def) && !filter.OnlySpecialFilters)
                return true;

            if (filter.AllowedQualityLevels != QualityRange.All &&
                weapon.def.FollowQualityThingFilter() &&
                weapon.TryGetQuality(out var quality) &&
                !filter.AllowedQualityLevels.Includes(quality))
            {
                return true;
            }

            return false;
        }

        private static float GetOutfitPolicyScore(Pawn pawn, ThingWithComps weapon)
        {
            return OutfitDisallows(pawn, weapon) ? Constants.OutfitFilterDisallowedPenalty : 0f;
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
            weapon.TryGetQuality(out quality);

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

            var skills = pawn.skills;
            float shootingSkill = skills != null ? skills.GetSkill(SkillDefOf.Shooting).Level : 0f;
            float meleeSkill = skills != null ? skills.GetSkill(SkillDefOf.Melee).Level : 0f;

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
