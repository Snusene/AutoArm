using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace AutoArm
{
    public static class WeaponStatDefOf
    {
        public static StatDef RangedWeapon_AverageDPS;
    }

    // Simple static tracker for forced weapons (save/load handled via MapComponent)
    public static class ForcedWeaponTracker
    {
        private static Dictionary<Pawn, ThingDef> forcedWeaponsByDef = new Dictionary<Pawn, ThingDef>();
        private static Dictionary<Pawn, HashSet<ThingDef>> forcedSidearmsByDef = new Dictionary<Pawn, HashSet<ThingDef>>();

        public static void SetForced(Pawn pawn, ThingWithComps weapon)
        {
            if (pawn == null || weapon == null) return;
            forcedWeaponsByDef[pawn] = weapon.def;
        }

        public static void ClearForced(Pawn pawn)
        {
            if (pawn == null) return;
            forcedWeaponsByDef.Remove(pawn);
        }

        public static ThingDef GetForcedWeaponDef(Pawn pawn)
        {
            if (pawn == null) return null;
            forcedWeaponsByDef.TryGetValue(pawn, out var def);
            return def;
        }

        public static bool IsForced(Pawn pawn, ThingWithComps weapon)
        {
            if (pawn == null || weapon == null) return false;
            return forcedWeaponsByDef.TryGetValue(pawn, out var def) && def == weapon.def;
        }

        // Sidearm methods
        public static void SetForcedSidearm(Pawn pawn, ThingDef weaponDef)
        {
            if (pawn == null || weaponDef == null) return;

            if (!forcedSidearmsByDef.ContainsKey(pawn))
                forcedSidearmsByDef[pawn] = new HashSet<ThingDef>();

            forcedSidearmsByDef[pawn].Add(weaponDef);
        }

        public static void ClearForcedSidearm(Pawn pawn, ThingDef weaponDef)
        {
            if (pawn == null || weaponDef == null) return;

            if (forcedSidearmsByDef.ContainsKey(pawn))
            {
                forcedSidearmsByDef[pawn].Remove(weaponDef);
                if (forcedSidearmsByDef[pawn].Count == 0)
                    forcedSidearmsByDef.Remove(pawn);
            }
        }

        public static bool IsForcedSidearm(Pawn pawn, ThingDef weaponDef)
        {
            if (pawn == null || weaponDef == null) return false;

            return forcedSidearmsByDef.ContainsKey(pawn) &&
                   forcedSidearmsByDef[pawn].Contains(weaponDef);
        }

        public static HashSet<ThingDef> GetForcedSidearms(Pawn pawn)
        {
            if (pawn == null) return new HashSet<ThingDef>();

            forcedSidearmsByDef.TryGetValue(pawn, out var sidearms);
            return sidearms ?? new HashSet<ThingDef>();
        }

        public static void Cleanup()
        {
            var toRemove = forcedWeaponsByDef.Keys
                .Where(p => p.DestroyedOrNull() || p.Dead)
                .ToList();

            foreach (var pawn in toRemove)
            {
                forcedWeaponsByDef.Remove(pawn);
                forcedSidearmsByDef.Remove(pawn);
            }
        }

        // For save/load
        public static Dictionary<Pawn, ThingDef> GetSaveData()
        {
            return new Dictionary<Pawn, ThingDef>(forcedWeaponsByDef);
        }

        public static void LoadSaveData(Dictionary<Pawn, ThingDef> data)
        {
            forcedWeaponsByDef.Clear();
            if (data != null)
            {
                foreach (var kvp in data)
                {
                    if (kvp.Key != null && kvp.Value != null)
                    {
                        forcedWeaponsByDef[kvp.Key] = kvp.Value;
                    }
                }
            }
        }

        // Sidearm save/load
        public static Dictionary<Pawn, HashSet<ThingDef>> GetSidearmSaveData()
        {
            return new Dictionary<Pawn, HashSet<ThingDef>>(forcedSidearmsByDef);
        }

        public static void LoadSidearmSaveData(Dictionary<Pawn, HashSet<ThingDef>> data)
        {
            forcedSidearmsByDef.Clear();
            if (data != null)
            {
                foreach (var kvp in data)
                {
                    if (kvp.Key != null && kvp.Value != null)
                    {
                        forcedSidearmsByDef[kvp.Key] = new HashSet<ThingDef>(kvp.Value);
                    }
                }
            }
        }
    }

    // MapComponent to handle save/load for forced weapons
    public class AutoArmMapComponent : MapComponent
    {
        private List<Pawn> savedPawns = new List<Pawn>();
        private List<ThingDef> savedDefs = new List<ThingDef>();

        // For sidearms
        private List<Pawn> savedSidearmPawns = new List<Pawn>();
        private List<List<ThingDef>> savedSidearmDefs = new List<List<ThingDef>>();

        public AutoArmMapComponent(Map map) : base(map) { }

        public override void MapComponentTick()
        {
            // Cleanup every 10000 ticks
            if (Find.TickManager.TicksGame % 10000 == 0)
            {
                ForcedWeaponTracker.Cleanup();
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();

            if (Scribe.mode == LoadSaveMode.Saving)
            {
                // Save primary weapons
                var data = ForcedWeaponTracker.GetSaveData();
                savedPawns = data.Keys.ToList();
                savedDefs = data.Values.ToList();

                // Save sidearms
                var sidearmData = ForcedWeaponTracker.GetSidearmSaveData();
                savedSidearmPawns = sidearmData.Keys.ToList();
                savedSidearmDefs = sidearmData.Values.Select(set => set.ToList()).ToList();
            }

            // Primary weapons
            Scribe_Collections.Look(ref savedPawns, "forcedWeaponPawns", LookMode.Reference);
            Scribe_Collections.Look(ref savedDefs, "forcedWeaponDefs", LookMode.Def);

            // Sidearms
            Scribe_Collections.Look(ref savedSidearmPawns, "forcedSidearmPawns", LookMode.Reference);
            Scribe_Collections.Look(ref savedSidearmDefs, "forcedSidearmDefs", LookMode.Deep);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                // Load primary weapons
                var data = new Dictionary<Pawn, ThingDef>();
                if (savedPawns != null && savedDefs != null)
                {
                    for (int i = 0; i < Math.Min(savedPawns.Count, savedDefs.Count); i++)
                    {
                        if (savedPawns[i] != null && savedDefs[i] != null)
                        {
                            data[savedPawns[i]] = savedDefs[i];
                        }
                    }
                }
                ForcedWeaponTracker.LoadSaveData(data);

                // Load sidearms
                var sidearmData = new Dictionary<Pawn, HashSet<ThingDef>>();
                if (savedSidearmPawns != null && savedSidearmDefs != null)
                {
                    for (int i = 0; i < Math.Min(savedSidearmPawns.Count, savedSidearmDefs.Count); i++)
                    {
                        if (savedSidearmPawns[i] != null && savedSidearmDefs[i] != null)
                        {
                            sidearmData[savedSidearmPawns[i]] = new HashSet<ThingDef>(savedSidearmDefs[i]);
                        }
                    }
                }
                ForcedWeaponTracker.LoadSidearmSaveData(sidearmData);
            }
        }
    }

    // Track auto-equipped weapons for notifications
    public static class AutoEquipTracker
    {
        private static HashSet<int> autoEquipJobIds = new HashSet<int>();
        private static Dictionary<Pawn, ThingDef> previousWeapons = new Dictionary<Pawn, ThingDef>();

        public static void MarkAutoEquip(Job job, Pawn pawn = null)
        {
            if (job != null)
            {
                autoEquipJobIds.Add(job.loadID);

                if (pawn != null && job.def == JobDefOf.Equip && job.targetA.Thing is ThingWithComps weapon)
                {
                    if (pawn.equipment?.Primary != null)
                    {
                        previousWeapons[pawn] = pawn.equipment.Primary.def;
                    }
                    else
                    {
                        previousWeapons.Remove(pawn);
                    }
                }
            }
        }

        public static bool IsAutoEquip(Job job)
        {
            return job != null && autoEquipJobIds.Contains(job.loadID);
        }

        public static void Clear(Job job)
        {
            if (job != null)
                autoEquipJobIds.Remove(job.loadID);
        }

        public static ThingDef GetPreviousWeapon(Pawn pawn)
        {
            previousWeapons.TryGetValue(pawn, out var weapon);
            return weapon;
        }

        public static void ClearPreviousWeapon(Pawn pawn)
        {
            previousWeapons.Remove(pawn);
        }
    }

    public class JobGiver_PickUpBetterWeapon : ThinkNode_JobGiver
    {
        private const float MaxSearchDistance = 50f;
        private const int MaxWeaponsToConsider = 30;
        private const int CacheLifetime = 250;

        // Weapon caching
        internal static Dictionary<Map, List<ThingWithComps>> weaponCache = new Dictionary<Map, List<ThingWithComps>>();
        internal static Dictionary<Map, int> weaponCacheAge = new Dictionary<Map, int>();

        protected override Job TryGiveJob(Pawn pawn)
        {
            // Skip if mod is disabled
            if (AutoArmMod.settings?.modEnabled == false)
                return null;

            // Quick validation
            if (!IsValidPawnForAutoEquip(pawn))
                return null;

            bool isUnarmed = pawn.equipment?.Primary == null;

            // For unarmed pawns, be less picky about interrupting jobs
            if (!isUnarmed && JobGiverHelpers.IsCriticalJob(pawn))
                return null;

            // Don't interrupt "Do until X" jobs unless unarmed
            if (!isUnarmed && pawn.mindState?.lastJobTag == JobTag.SatisfyingNeeds)
                return null;

            var job = FindBetterWeaponJob(pawn);

            if (AutoArmMod.settings?.debugLogging == true)
            {
                if (job != null)
                {
                    Log.Message($"[AutoArm DEBUG] {pawn.Name}: TryGiveJob returning equip job for {job.targetA.Thing?.Label}");
                }
                else
                {
                    Log.Message($"[AutoArm DEBUG] {pawn.Name}: TryGiveJob returning null");
                }
            }

            return job;
        }

        public Job FindBetterWeaponJob(Pawn pawn)
        {
            var currentWeapon = pawn.equipment?.Primary;
            bool isUnarmed = currentWeapon == null;

            // Lower threshold for unarmed pawns - any weapon is good
            float currentScore = currentWeapon != null ? GetWeaponScore(pawn, currentWeapon) : float.MinValue;
            float improvementThreshold = isUnarmed ? currentScore : currentScore * 1.1f;

            ThingWithComps bestWeapon = null;
            float bestScore = currentScore;
            int weaponsChecked = 0;

            // Get cached weapons or refresh cache if expired
            List<ThingWithComps> cachedWeapons;
            int cacheAge;
            if (!weaponCache.TryGetValue(pawn.Map, out cachedWeapons) ||
                !weaponCacheAge.TryGetValue(pawn.Map, out cacheAge) ||
                Find.TickManager.TicksGame - cacheAge > CacheLifetime)
            {
                cachedWeapons = new List<ThingWithComps>();
                var allMapWeapons = pawn.Map.listerThings.ThingsInGroup(ThingRequestGroup.Weapon);
                cachedWeapons.Capacity = Math.Min(allMapWeapons.Count(), 200);

                foreach (var thing in allMapWeapons)
                {
                    var weapon = thing as ThingWithComps;
                    if (weapon?.def == null) continue;
                    if (weapon.def.IsWeapon && !weapon.def.IsApparel)
                    {
                        cachedWeapons.Add(weapon);
                    }
                }

                weaponCache[pawn.Map] = cachedWeapons;
                weaponCacheAge[pawn.Map] = Find.TickManager.TicksGame;

                if (AutoArmMod.settings?.debugLogging == true)
                {
                    Log.Message($"[AutoArm DEBUG] Rebuilt weapon cache - {cachedWeapons.Count} valid weapons");
                }
            }

            // Filter nearby weapons and sort only those
            var nearbyWeapons = cachedWeapons
                .Where(w => w.Position.DistanceTo(pawn.Position) <= MaxSearchDistance)
                .OrderBy(w => w.Position.DistanceTo(pawn.Position))
                .Take(50)
                .ToList();

            foreach (var weapon in nearbyWeapons)
            {
                if (weaponsChecked >= MaxWeaponsToConsider)
                    break;

                if (IsWeapon(weapon) && IsValidWeaponCandidate(weapon, pawn))
                {
                    weaponsChecked++;
                    float score = GetWeaponScore(pawn, weapon);

                    if (score > bestScore && (currentWeapon == null || score > improvementThreshold))
                    {
                        bestScore = score;
                        bestWeapon = weapon;
                    }
                }
            }

            if (bestWeapon != null)
            {
                var equipJob = JobMaker.MakeJob(JobDefOf.Equip, bestWeapon);
                AutoEquipTracker.MarkAutoEquip(equipJob, pawn);

                if (AutoArmMod.settings?.debugLogging == true)
                {
                    Log.Message($"[AutoArm DEBUG] {pawn.Name}: Created equip job for {bestWeapon.Label}");
                }

                return equipJob;
            }
            else if (AutoArmMod.settings?.debugLogging == true)
            {
                Log.Message($"[AutoArm DEBUG] {pawn.Name}: No best weapon found");
            }

            return null;
        }

        public Job TestTryGiveJob(Pawn pawn)
        {
            return TryGiveJob(pawn);
        }

        public ThingWithComps FindSpecificWeaponByDef(Pawn pawn, ThingDef weaponDef)
        {
            if (weaponDef == null || pawn == null) return null;

            var weapons = pawn.Map.listerThings.ThingsInGroup(ThingRequestGroup.Weapon)
                .OfType<ThingWithComps>()
                .Where(w => w.def == weaponDef &&
                           IsValidWeaponCandidate(w, pawn))
                .OrderBy(w => w.Position.DistanceTo(pawn.Position))
                .ThenByDescending(w => w.HitPoints)
                .FirstOrDefault();

            return weapons;
        }

        protected bool IsValidPawnForAutoEquip(Pawn pawn)
        {
            if (pawn == null || !pawn.Spawned || pawn.Dead)
                return false;
            if (!pawn.RaceProps.Humanlike || pawn.RaceProps.IsMechanoid)
                return false;
            if (pawn.Map == null || !pawn.Position.IsValid)
                return false;
            if (pawn.Downed || pawn.InMentalState || pawn.IsPrisoner)
                return false;
            if (pawn.WorkTagIsDisabled(WorkTags.Violent))
                return false;
            if (pawn.health?.capacities == null || !pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation))
                return false;
            if (pawn.InBed() || pawn.GetLord() != null)
                return false;
            if (pawn.IsCaravanMember())
                return false;
            if (ModsConfig.BiotechActive && pawn.ageTracker != null && pawn.ageTracker.AgeBiologicalYears < 13)
                return false;
            if (ModsConfig.RoyaltyActive && pawn.royalty?.AllTitlesInEffectForReading?.Any(t => t.conceited) == true)
            {
                if (pawn.equipment?.Primary != null)
                    return false;
            }
            return true;
        }

        protected bool IsWeapon(ThingWithComps thing)
        {
            if (thing?.def == null)
                return false;

            // Explicitly allow vanilla animal weapons
            if (thing.def.defName == "ElephantTusk" || thing.def.defName == "ThrumboHorn")
                return true;

            // Use the cached weapon lists
            bool isInList = WeaponThingFilterUtility.AllWeapons.Contains(thing.def);
            return isInList;
        }

        protected bool IsValidWeaponCandidate(ThingWithComps weapon, Pawn pawn)
        {
            if (weapon == null || weapon.def == null || weapon.Destroyed)
                return false;
            if (weapon.Map != pawn.Map)
                return false;
            if (!weapon.def.IsWeapon)
                return false;

            // Check for heavy weapon mod extension
            if (weapon.def.modExtensions?.Any(extension => extension.GetType().Name == "HeavyWeapon") == true)
                return false;

            // Check outfit filter
            if (pawn.outfits != null)
            {
                var policy = pawn.outfits.CurrentApparelPolicy;
                if (policy?.filter != null)
                {
                    // Skip outfit filter check for vanilla animal weapons
                    if (weapon.def.defName == "ElephantTusk" || weapon.def.defName == "ThrumboHorn")
                    {
                        // Allow these regardless of outfit
                    }
                    else if (!policy.filter.Allows(weapon.def))
                    {
                        return false;
                    }
                }
            }

            if (weapon.IsForbidden(pawn))
                return false;

            var biocomp = weapon.TryGetComp<CompBiocodable>();
            if (biocomp?.Biocoded == true && biocomp.CodedPawn != pawn)
                return false;

            if (weapon.questTags != null && weapon.questTags.Count > 0)
                return false;

            var reservationManager = pawn.Map.reservationManager;
            if (reservationManager.IsReservedByAnyoneOf(weapon, pawn.Faction) &&
                !reservationManager.CanReserve(pawn, weapon))
                return false;

            if (!pawn.CanReserveAndReach(weapon, PathEndMode.ClosestTouch, Danger.Deadly))
                return false;

            // Check if weapon is currently equipped by another pawn
            if (weapon.ParentHolder is Pawn_EquipmentTracker || weapon.ParentHolder is Pawn_InventoryTracker)
                return false;

            if (weapon.IsBurning())
                return false;

            // Check if weapon requires research that isn't complete
            if (weapon.def.researchPrerequisites != null && weapon.def.researchPrerequisites.Count > 0)
            {
                foreach (var research in weapon.def.researchPrerequisites)
                {
                    if (!research.IsFinished)
                        return false;
                }
            }

            return true;
        }

        public float GetWeaponScore(Pawn pawn, ThingWithComps weapon)
        {
            if (weapon?.def == null)
                return 0f;

            // First check: Is it allowed by outfit?
            var filter = pawn.outfits?.CurrentApparelPolicy?.filter;
            if (filter != null && !filter.Allows(weapon.def))
                return -1000f; // Not allowed by outfit

            // Base validation - weapon must do damage
            var equippable = weapon.TryGetComp<CompEquippable>();
            if (equippable?.PrimaryVerb == null || !equippable.PrimaryVerb.HarmsHealth())
                return -1000f;

            // Hard overrides for traits
            if (pawn.story?.traits?.HasTrait(TraitDefOf.Brawler) == true && weapon.def.IsRangedWeapon)
                return -2000f; // Brawlers NEVER use ranged

            bool isHunter = pawn.workSettings?.WorkIsActive(WorkTypeDefOf.Hunting) ?? false;
            if (isHunter && weapon.def.IsRangedWeapon && equippable.PrimaryVerb.UsesExplosiveProjectiles())
                return -1000f; // Hunters shouldn't use explosives

            // Persona weapon check
            var bladelinkComp = weapon.TryGetComp<CompBladelinkWeapon>();
            if (bladelinkComp != null && bladelinkComp.CodedPawn != null)
            {
                if (bladelinkComp.CodedPawn != pawn)
                    return -1000f; // Someone else's persona weapon
            }

            // Get pawn skills
            float shootingSkill = pawn.skills?.GetSkill(SkillDefOf.Shooting)?.Level ?? 0f;
            float meleeSkill = pawn.skills?.GetSkill(SkillDefOf.Melee)?.Level ?? 0f;

            // Start with base score
            float score = 100f;

            // MAJOR skill preference - this should be the primary factor!
            if (weapon.def.IsRangedWeapon)
            {
                if (shootingSkill > meleeSkill)
                {
                    // Strong preference for ranged when better at shooting
                    float skillDiff = shootingSkill - meleeSkill;
                    score += skillDiff * 50f; // 50 points per skill level difference

                    // Additional bonus based on absolute shooting skill
                    score += shootingSkill * 10f;
                }
                else if (meleeSkill > shootingSkill + 3)
                {
                    // Only consider ranged if MUCH worse at melee
                    score -= (meleeSkill - shootingSkill) * 30f;
                }

                // Weapon stats consideration
                if (weapon.def.Verbs?.Count > 0 && weapon.def.Verbs[0] != null)
                {
                    var verb = weapon.def.Verbs[0];

                    // DPS estimate (rough)
                    float damage = verb.defaultProjectile?.projectile?.GetDamageAmount(weapon) ?? 0f;
                    float warmup = verb.warmupTime;
                    float cooldown = weapon.def.GetStatValueAbstract(StatDefOf.RangedWeapon_Cooldown);
                    float burstShots = verb.burstShotCount;

                    float dps = damage * burstShots / (warmup + cooldown + 0.1f);
                    score += dps * 5f; // 5 points per DPS

                    // Range is CRITICAL for shooters
                    float range = verb.range;
                    if (shootingSkill >= 5) // Decent shooters
                    {
                        // Strong preference for good range weapons
                        if (range >= 30f) // Assault rifle range or better
                            score += 100f + (range - 30f) * 3f;
                        else if (range >= 25f) // Decent range
                            score += 50f;
                        else if (range < 20f) // Short range penalty for good shooters
                            score -= (20f - range) * 5f;
                    }
                    else
                    {
                        // Less skilled shooters get smaller range bonus
                        if (range >= 25f)
                            score += (range - 20f) * 2f;
                    }

                    // Burst fire is highly valued
                    if (burstShots > 1)
                    {
                        score += burstShots * 15f; // Much higher than before
                    }

                    // Accuracy bonus for good shooters
                    if (shootingSkill >= 8)
                    {
                        float accuracy = weapon.def.GetStatValueAbstract(StatDefOf.AccuracyLong);
                        score += accuracy * 50f;
                    }
                }
            }
            else if (weapon.def.IsMeleeWeapon)
            {
                if (meleeSkill > shootingSkill)
                {
                    // Strong preference for melee when better at melee
                    float skillDiff = meleeSkill - shootingSkill;
                    score += skillDiff * 50f; // 50 points per skill level difference

                    // Additional bonus based on absolute melee skill
                    score += meleeSkill * 10f;
                }
                else if (shootingSkill > meleeSkill + 3)
                {
                    // Only consider melee if MUCH worse at shooting
                    score -= (shootingSkill - meleeSkill) * 30f;
                }

                // Melee DPS consideration
                float meleeDPS = weapon.def.GetStatValueAbstract(StatDefOf.MeleeWeapon_CooldownMultiplier);
                float meleeDamage = weapon.def.GetStatValueAbstract(StatDefOf.MeleeWeapon_DamageMultiplier);
                score += (meleeDPS + meleeDamage) * 20f;
            }

            // Quality bonus - reduced to not overwhelm skill preference
            if (weapon.TryGetQuality(out QualityCategory qc))
            {
                // Reduced from exponential to linear, capped impact
                score += (int)qc * 15f; // 15 points per quality level
            }

            // Persona weapon bonus (reduced)
            if (bladelinkComp?.CodedPawn == pawn)
                score += 25f;

            // Infusion 2 compatibility
            if (InfusionCompat.IsLoaded())
            {
                score += InfusionCompat.GetInfusionScoreBonus(weapon);
            }

            // Combat Extended ammo check
            if (CECompat.ShouldCheckAmmo())
            {
                score *= CECompat.GetAmmoScoreModifier(weapon, pawn);
            }

            // Final skill-based modifier to ensure strong preference
            if (weapon.def.IsRangedWeapon && shootingSkill >= meleeSkill + 2)
            {
                score *= 1.5f; // 50% bonus for appropriate weapon type
            }
            else if (weapon.def.IsMeleeWeapon && meleeSkill >= shootingSkill + 2)
            {
                score *= 1.5f; // 50% bonus for appropriate weapon type
            }
            else if ((weapon.def.IsRangedWeapon && meleeSkill > shootingSkill + 3) ||
                     (weapon.def.IsMeleeWeapon && shootingSkill > meleeSkill + 3))
            {
                score *= 0.3f; // 70% penalty for wrong weapon type
            }

            return score;
        }
    }

    // Think tree conditional for checking if weapons are allowed in outfit
    public class ThinkNode_ConditionalWeaponsInOutfit : ThinkNode_Conditional
    {
        protected override bool Satisfied(Pawn pawn)
        {
            if (!pawn.IsColonist)
            {
                if (AutoArmMod.settings?.debugLogging == true)
                    Log.Message($"[AutoArm] {pawn.Name}: Not colonist");
                return false;
            }

            if (pawn.WorkTagIsDisabled(WorkTags.Violent))
            {
                if (AutoArmMod.settings?.debugLogging == true)
                    Log.Message($"[AutoArm] {pawn.Name}: Incapable of violence");
                return false;
            }

            if (pawn.Drafted)
            {
                if (AutoArmMod.settings?.debugLogging == true)
                    Log.Message($"[AutoArm] {pawn.Name}: Drafted");
                return false;
            }

            if (pawn.outfits?.CurrentApparelPolicy?.filter == null)
            {
                if (AutoArmMod.settings?.debugLogging == true)
                    Log.Message($"[AutoArm] {pawn.Name}: No outfit filter");
                return true;
            }

            var filter = pawn.outfits.CurrentApparelPolicy.filter;
            bool anyWeaponAllowed = WeaponThingFilterUtility.AllWeapons.Any(td => filter.Allows(td));

            if (AutoArmMod.settings?.debugLogging == true)
                Log.Message($"[AutoArm] {pawn.Name}: Weapons allowed = {anyWeaponAllowed}");

            return anyWeaponAllowed;
        }

        // Public method for testing
        public bool TestSatisfied(Pawn pawn)
        {
            return Satisfied(pawn);
        }
    }

    // Harmony patches for forced weapon tracking
    [HarmonyPatch(typeof(Pawn_JobTracker), "StartJob")]
    [HarmonyPriority(Priority.High)]
    public static class Pawn_JobTracker_TrackForcedEquip_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(Job newJob, Pawn ___pawn)
        {
            if (newJob?.def == JobDefOf.Equip && newJob.playerForced && ___pawn.IsColonist)
            {
                var targetWeapon = newJob.targetA.Thing as ThingWithComps;
                if (targetWeapon?.def.IsWeapon == true)
                {
                    ForcedWeaponTracker.SetForced(___pawn, targetWeapon);
                }
            }
            else if (newJob?.def == JobDefOf.Equip && ___pawn.IsColonist)
            {
                var targetWeapon = newJob.targetA.Thing as ThingWithComps;
                if (targetWeapon?.def.IsWeapon == true && SimpleSidearmsCompat.IsSimpleSidearmsSwitch(___pawn, targetWeapon))
                {
                    ForcedWeaponTracker.SetForced(___pawn, targetWeapon);
                }
            }
        }
    }

    [HarmonyPatch(typeof(Pawn_JobTracker), "StartJob")]
    [HarmonyPriority(Priority.High)]
    public static class Pawn_JobTracker_TrackForcedSidearmEquip_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(Job newJob, Pawn ___pawn)
        {
            if (!___pawn.IsColonist || !SimpleSidearmsCompat.IsLoaded())
                return;

            if (newJob?.def?.defName == "EquipSecondary" && newJob.playerForced)
            {
                var targetWeapon = newJob.targetA.Thing as ThingWithComps;
                if (targetWeapon?.def.IsWeapon == true)
                {
                    ForcedWeaponTracker.SetForcedSidearm(___pawn, targetWeapon.def);

                    if (AutoArmMod.settings?.debugLogging == true)
                    {
                        Log.Message($"[AutoArm] Player manually equipped {targetWeapon.Label} as sidearm for {___pawn.Name}");
                    }
                }
            }
        }
    }

    [HarmonyPatch(typeof(Pawn_EquipmentTracker), "TryDropEquipment")]
    public static class Pawn_EquipmentTracker_ClearForcedOnDrop_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(bool __result, Pawn ___pawn)
        {
            if (__result && ___pawn.IsColonist)
            {
                ForcedWeaponTracker.ClearForced(___pawn);

                if (___pawn.equipment?.Primary == null)
                {
                    Pawn_TickRare_Unified_Patch.MarkRecentlyUnarmed(___pawn);
                }
            }
        }
    }

    [HarmonyPatch(typeof(Pawn_EquipmentTracker), "DestroyEquipment")]
    public static class Pawn_EquipmentTracker_ClearForcedOnDestroy_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn ___pawn)
        {
            if (___pawn.IsColonist)
            {
                ForcedWeaponTracker.ClearForced(___pawn);

                if (___pawn.equipment?.Primary == null)
                {
                    Pawn_TickRare_Unified_Patch.MarkRecentlyUnarmed(___pawn);
                }
            }
        }
    }

    [HarmonyPatch(typeof(Pawn_InventoryTracker), "Notify_ItemRemoved")]
    public static class Pawn_InventoryTracker_ClearForcedSidearm_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Thing item, Pawn ___pawn)
        {
            if (item is ThingWithComps weapon && weapon.def.IsWeapon && ___pawn.IsColonist)
            {
                ForcedWeaponTracker.ClearForcedSidearm(___pawn, weapon.def);
            }
        }
    }

    [HarmonyPatch(typeof(Pawn_EquipmentTracker), "AddEquipment")]
    public static class Pawn_EquipmentTracker_NotifyAutoEquip_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ThingWithComps newEq, Pawn ___pawn)
        {
            if (___pawn.IsColonist && ___pawn.CurJob?.def == JobDefOf.Equip &&
                AutoEquipTracker.IsAutoEquip(___pawn.CurJob))
            {
                if (newEq != null && PawnUtility.ShouldSendNotificationAbout(___pawn) &&
                    AutoArmMod.settings?.showNotifications == true)
                {
                    var previousWeapon = AutoEquipTracker.GetPreviousWeapon(___pawn);

                    if (previousWeapon != null)
                    {
                        Messages.Message("AutoArm_UpgradedWeapon".Translate(
                            ___pawn.LabelShort.CapitalizeFirst(),
                            previousWeapon.label,
                            newEq.Label
                        ), new LookTargets(___pawn), MessageTypeDefOf.SilentInput, false);
                    }
                    else
                    {
                        Messages.Message("AutoArm_EquippedWeapon".Translate(
                            ___pawn.LabelShort.CapitalizeFirst(),
                            newEq.Label
                        ), new LookTargets(___pawn), MessageTypeDefOf.SilentInput, false);
                    }
                }

                AutoEquipTracker.Clear(___pawn.CurJob);
                AutoEquipTracker.ClearPreviousWeapon(___pawn);
            }
        }
    }

    [HarmonyPatch(typeof(Pawn_OutfitTracker), "CurrentApparelPolicy", MethodType.Setter)]
    public static class Pawn_OutfitTracker_PolicyChanged_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn ___pawn)
        {
            if (AutoArmMod.settings?.modEnabled != true)
                return;

            if (___pawn.IsColonist && ___pawn.Spawned && !___pawn.Dead &&
                ___pawn.equipment?.Primary != null && !___pawn.Drafted)
            {
                var filter = ___pawn.outfits?.CurrentApparelPolicy?.filter;
                if (filter != null && !filter.Allows(___pawn.equipment.Primary.def))
                {
                    ForcedWeaponTracker.ClearForced(___pawn);

                    var dropJob = new Job(JobDefOf.DropEquipment, ___pawn.equipment.Primary);
                    ___pawn.jobs.TryTakeOrderedJob(dropJob, JobTag.Misc);

                    if (AutoArmMod.settings.debugLogging)
                    {
                        Log.Message($"[AutoArm] {___pawn.Name}: Outfit changed, dropping {___pawn.equipment.Primary.Label}");
                    }

                    if (PawnUtility.ShouldSendNotificationAbout(___pawn))
                    {
                        Messages.Message("AutoArm_DroppingDisallowed".Translate(
                            ___pawn.LabelShort.CapitalizeFirst(),
                            ___pawn.equipment.Primary.Label
                        ), new LookTargets(___pawn), MessageTypeDefOf.SilentInput, false);
                    }
                }
            }
        }
    }

    [HarmonyPatch(typeof(ThingFilter), "SetAllow", new Type[] { typeof(ThingDef), typeof(bool) })]
    public static class ThingFilter_SetAllow_CheckWeapons_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ThingFilter __instance, ThingDef thingDef, bool allow)
        {
            if (AutoArmMod.settings?.modEnabled != true || allow || !thingDef.IsWeapon)
                return;

            if (Current.Game == null || Find.Maps == null)
                return;

            var pawnsToDropWeapons = new List<(Pawn pawn, ThingWithComps weapon)>();

            foreach (var map in Find.Maps)
            {
                if (map?.mapPawns?.FreeColonists == null)
                    continue;

                foreach (var pawn in map.mapPawns.FreeColonists)
                {
                    if (pawn.Drafted || pawn.jobs == null)
                        continue;

                    if (pawn.outfits?.CurrentApparelPolicy?.filter == __instance &&
                        pawn.equipment?.Primary?.def == thingDef)
                    {
                        pawnsToDropWeapons.Add((pawn, pawn.equipment.Primary));
                    }
                }
            }

            if (pawnsToDropWeapons.Count > 0)
            {
                LongEventHandler.ExecuteWhenFinished(() =>
                {
                    foreach (var (pawn, weapon) in pawnsToDropWeapons)
                    {
                        if (pawn.equipment?.Primary == weapon && !pawn.Drafted)
                        {
                            ForcedWeaponTracker.ClearForced(pawn);

                            var dropJob = new Job(JobDefOf.DropEquipment, weapon);
                            pawn.jobs.TryTakeOrderedJob(dropJob, JobTag.Misc);

                            if (AutoArmMod.settings.debugLogging)
                            {
                                Log.Message($"[AutoArm] {pawn.Name}: {thingDef.label} now disallowed, dropping weapon");
                            }

                            if (PawnUtility.ShouldSendNotificationAbout(pawn))
                            {
                                Messages.Message("AutoArm_DroppingDisallowed".Translate(
                                    pawn.LabelShort.CapitalizeFirst(),
                                    weapon.Label
                                ), new LookTargets(pawn), MessageTypeDefOf.SilentInput, false);
                            }
                        }
                    }
                });
            }
        }
    }

    [HarmonyPatch(typeof(ThingFilter), "SetDisallowAll")]
    public static class ThingFilter_SetDisallowAll_CheckWeapons_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ThingFilter __instance)
        {
            if (AutoArmMod.settings?.modEnabled != true)
                return;

            if (Current.Game == null || Find.Maps == null)
                return;

            var pawnsToDropWeapons = new List<(Pawn pawn, ThingWithComps weapon)>();

            foreach (var map in Find.Maps)
            {
                if (map?.mapPawns?.FreeColonists == null)
                    continue;

                foreach (var pawn in map.mapPawns.FreeColonists)
                {
                    if (pawn.Drafted || pawn.jobs == null)
                        continue;

                    if (pawn.outfits?.CurrentApparelPolicy?.filter == __instance &&
                        pawn.equipment?.Primary != null)
                    {
                        pawnsToDropWeapons.Add((pawn, pawn.equipment.Primary));
                    }
                }
            }

            if (pawnsToDropWeapons.Count > 0)
            {
                LongEventHandler.ExecuteWhenFinished(() =>
                {
                    foreach (var (pawn, weapon) in pawnsToDropWeapons)
                    {
                        if (pawn.equipment?.Primary == weapon && !pawn.Drafted)
                        {
                            ForcedWeaponTracker.ClearForced(pawn);

                            var dropJob = new Job(JobDefOf.DropEquipment, weapon);
                            pawn.jobs.TryTakeOrderedJob(dropJob, JobTag.Misc);

                            if (AutoArmMod.settings.debugLogging)
                            {
                                Log.Message($"[AutoArm] {pawn.Name}: All items disallowed, dropping weapon");
                            }

                            if (PawnUtility.ShouldSendNotificationAbout(pawn))
                            {
                                Messages.Message("AutoArm_DroppingDisallowed".Translate(
                                    pawn.LabelShort.CapitalizeFirst(),
                                    weapon.Label
                                ), new LookTargets(pawn), MessageTypeDefOf.SilentInput, false);
                            }
                        }
                    }
                });
            }
        }
    }

    [HarmonyPatch(typeof(Pawn_OutfitTracker), "CurrentApparelPolicy", MethodType.Setter)]
    public static class Pawn_OutfitTracker_CurrentPolicy_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn_OutfitTracker __instance)
        {
            var pawn = __instance.pawn;
            if (pawn != null && Pawn_TickRare_Unified_Patch.lastWeaponSearchTick.ContainsKey(pawn))
            {
                Pawn_TickRare_Unified_Patch.lastWeaponSearchTick.Remove(pawn);
                Pawn_TickRare_Unified_Patch.cachedWeaponJobs.Remove(pawn);
            }
        }
    }

    [HarmonyPatch(typeof(Pawn_JobTracker), "EndCurrentJob")]
    public static class Debug_JobTracker_EndCurrentJob_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(Pawn ___pawn, JobCondition condition, Job ___curJob)
        {
            if (AutoArmMod.settings?.debugLogging == true &&
                ___curJob?.def == JobDefOf.Equip &&
                AutoEquipTracker.IsAutoEquip(___curJob))
            {
                Log.Message($"[AutoArm DEBUG] {___pawn.Name}: Ending equip job for {___curJob.targetA.Thing?.Label} - Reason: {condition}");
            }
        }
    }

    [HarmonyPatch(typeof(Pawn_JobTracker), "StartJob")]
    public static class Debug_JobTracker_StartJob_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(Pawn ___pawn, Job newJob)
        {
            if (AutoArmMod.settings?.debugLogging == true &&
                newJob?.def == JobDefOf.Equip &&
                AutoEquipTracker.IsAutoEquip(newJob))
            {
                Log.Message($"[AutoArm DEBUG] {___pawn.Name}: Starting equip job for {newJob.targetA.Thing?.Label}");
            }
        }
    }

    [HarmonyPatch(typeof(ThinkNode_JobGiver), "TryIssueJobPackage")]
    public static class Debug_ThinkNode_JobGiver_TryIssueJobPackage_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ThinkNode_JobGiver __instance, Pawn pawn, JobIssueParams jobParams, ThinkResult __result)
        {
            if (AutoArmMod.settings?.debugLogging == true &&
                __instance is JobGiver_PickUpBetterWeapon)
            {
                if (__result.Job != null)
                {
                    Log.Message($"[AutoArm DEBUG] {pawn.Name}: {__instance.GetType().Name} issued job: {__result.Job.def.defName} targeting {__result.Job.targetA.Thing?.Label}");
                }
                else
                {
                    Log.Message($"[AutoArm DEBUG] {pawn.Name}: {__instance.GetType().Name} issued NO job");
                }
            }
        }
    }
}