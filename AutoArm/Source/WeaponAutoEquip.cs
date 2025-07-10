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
using static AutoArm.ForcedWeaponTracker;

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



    public class JobGiver_PickUpBetterWeapon : ThinkNode_JobGiver
    {
        private const float MaxSearchDistance = 50f;  // Same as your old distance check
        private const int MaxWeaponsToConsider = 30;  // If you don't have this already
        private const int CacheLifetime = 250;        // If you don't have this already

        // Weapon caching
        internal static Dictionary<Map, List<ThingWithComps>> weaponCache = new Dictionary<Map, List<ThingWithComps>>();
        internal static Dictionary<Map, int> weaponCacheAge = new Dictionary<Map, int>();

        protected override Job TryGiveJob(Pawn pawn)
        {
            if (AutoArmMod.settings?.debugLogging == true)
            {
                Log.Message($"[AutoArm] Emergency weapon check for {pawn.Name}");
            }

            // Skip if mod is disabled
            if (AutoArmMod.settings?.modEnabled == false)
                return null;

            // Quick validation
            if (!IsValidPawnForAutoEquip(pawn))
                return null;

            // For emergency job giver, only help unarmed pawns
            if (pawn.equipment?.Primary != null)
                return null;

            // Skip if doing absolutely critical work
            if (JobGiverHelpers.IsCriticalJob(pawn))
                return null;

            // Don't interrupt "Do until X" jobs
            if (pawn.mindState?.lastJobTag == JobTag.SatisfyingNeeds)
                return null;

            // Find ANY weapon quickly
            var job = FindBetterWeaponJob(pawn);

            // Add the debug logging HERE
            if (AutoArmMod.settings?.debugLogging == true)
            {
                if (job != null)
                {
                    Log.Message($"[AutoArm DEBUG] {pawn.Name}: Emergency TryGiveJob returning equip job for {job.targetA.Thing?.Label}");
                }
                else
                {
                    Log.Message($"[AutoArm DEBUG] {pawn.Name}: Emergency TryGiveJob returning null");
                }
            }

            return job;
        }

        public Job FindBetterWeaponJob(Pawn pawn)
        {
            var currentWeapon = pawn.equipment?.Primary;
            float currentScore = currentWeapon != null ? GetWeaponScore(pawn, currentWeapon) : float.MinValue;
            float improvementThreshold = currentWeapon != null ? currentScore * 1.1f : currentScore; // 10% improvement needed
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

                // DON'T SORT HERE - just cache all weapons unsorted
                weaponCache[pawn.Map] = cachedWeapons;
                weaponCacheAge[pawn.Map] = Find.TickManager.TicksGame;

                if (AutoArmMod.settings?.debugLogging == true)
                {
                    Log.Message($"[AutoArm DEBUG] Rebuilt weapon cache - {cachedWeapons.Count} valid weapons");
                }
            }

            // AFTER getting cache, filter nearby weapons and sort only those
            var nearbyWeapons = cachedWeapons
                .Where(w => w.Position.DistanceTo(pawn.Position) <= MaxSearchDistance)
                .OrderBy(w => w.Position.DistanceTo(pawn.Position))
                .Take(50)  // Only evaluate closest 50 weapons
                .ToList();

            // Use nearbyWeapons, NOT cachedWeapons
            foreach (var weapon in nearbyWeapons)  // CHANGED THIS LINE
            {
                if (weaponsChecked >= MaxWeaponsToConsider)
                    break;

                // No need to check distance again - nearbyWeapons already filtered
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

            // Simple scoring
            float score = 100f;

            // Quality bonus with exponential scaling
            if (weapon.TryGetQuality(out QualityCategory qc))
            {
                // Normal=40, Good=56, Excellent=73, Masterwork=92, Legendary=113
                score += Mathf.Pow((int)qc, 1.5f) * 20f;
            }
            else
            {
                // For weapons without quality (some modded weapons), use value as a small factor
                score += Math.Min(weapon.MarketValue * 0.01f, 20f); // Cap at 20 points
            }

            // Hard overrides only for things outfit can't handle
            if (pawn.story?.traits?.HasTrait(TraitDefOf.Brawler) == true && weapon.def.IsRangedWeapon)
                return -2000f; // Brawlers NEVER use ranged

            bool isHunter = pawn.workSettings?.WorkIsActive(WorkTypeDefOf.Hunting) ?? false;
            if (isHunter && weapon.def.IsRangedWeapon && equippable.PrimaryVerb.UsesExplosiveProjectiles())
                return -1000f; // Hunters shouldn't use explosives

            var bladelinkComp = weapon.TryGetComp<CompBladelinkWeapon>();
            if (bladelinkComp != null && bladelinkComp.CodedPawn != null)
            {
                if (bladelinkComp.CodedPawn != pawn)
                {
                    return -1000f; // Someone else's persona weapon
                }
                else
                {
                    score += 15f; // Own persona weapon
                }
            }

            // Small skill preference with range consideration
            if (weapon.def.IsRangedWeapon)
            {
                float shootingSkill = pawn.skills?.GetSkill(SkillDefOf.Shooting)?.Level ?? 0f;
                float meleeSkill = pawn.skills?.GetSkill(SkillDefOf.Melee)?.Level ?? 0f;

                if (shootingSkill > meleeSkill)
                {
                    score += 5f; // Small bonus if they're better at shooting

                    // Range bonus only for shooters
                    var verbs = weapon.def.Verbs;
                    if (verbs?.Count > 0 && verbs[0] != null)
                    {
                        float range = verbs[0].range;
                        if (range >= 25f) // Good range weapons
                        {
                            score += (range - 20f) * 0.5f; // 0.5 points per tile over 20
                        }

                        // Bonus for burst fire weapons
                        if (verbs[0].burstShotCount > 1)
                        {
                            score += 5f; // Automatic weapons bonus
                        }
                    }
                }
            }
            else if (weapon.def.IsMeleeWeapon)
            {
                float meleeSkill = pawn.skills?.GetSkill(SkillDefOf.Melee)?.Level ?? 0f;
                float shootingSkill = pawn.skills?.GetSkill(SkillDefOf.Shooting)?.Level ?? 0f;

                if (meleeSkill > shootingSkill)
                    score += 5f; // Small bonus if they're better at melee
            }

            // Infusion 2 compatibility - add bonus for infused weapons
            if (InfusionCompat.IsLoaded())
            {
                score += InfusionCompat.GetInfusionScoreBonus(weapon);
            }

            // If Combat Extended is loaded and checking ammo
            if (CECompat.ShouldCheckAmmo())
            {
                score *= CECompat.GetAmmoScoreModifier(weapon, pawn);
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

    // High priority conditional for unarmed colonists
    public class ThinkNode_ConditionalUnarmedOrPoorlyArmed : ThinkNode_Conditional
    {
        protected override bool Satisfied(Pawn pawn)
        {
            // Only check colonists
            if (!pawn.IsColonist)
                return false;

            // Skip if can't use weapons
            if (pawn.WorkTagIsDisabled(WorkTags.Violent))
                return false;

            // Skip if drafted
            if (pawn.Drafted)
                return false;

            // Skip if on fire
            if (pawn.IsBurning())
                return false;

            // Skip if doing critical work
            if (JobGiverHelpers.IsCriticalJob(pawn))
                return false;

            // Skip if in mental state (berserk, etc)
            if (pawn.InMentalState)
                return false;

            // Only care about truly unarmed pawns
            return pawn.equipment?.Primary == null;
        }

        // Public method for testing
        public bool TestSatisfied(Pawn pawn)
        {
            return Satisfied(pawn);
        }
    }

    // Emergency weapon acquisition job giver for unarmed pawns
    public class JobGiver_GetWeaponEmergency : JobGiver_PickUpBetterWeapon
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            if (AutoArmMod.settings?.debugLogging == true)
            {
                Log.Message($"[AutoArm] Emergency weapon check for {pawn.Name}");
            }

            // Skip if mod is disabled
            if (AutoArmMod.settings?.modEnabled == false)
                return null;

            // Quick validation
            if (!IsValidPawnForAutoEquip(pawn))
                return null;

            // For emergency job giver, only help unarmed pawns
            if (pawn.equipment?.Primary != null)
                return null;

            // Skip if doing absolutely critical work
            if (JobGiverHelpers.IsCriticalJob(pawn))
                return null;

            // Don't interrupt "Do until X" jobs
            if (pawn.mindState?.lastJobTag == JobTag.SatisfyingNeeds)
                return null;

            // Use the base class method - for unarmed pawns, any weapon is better
            var job = FindBetterWeaponJob(pawn);

            // Add the debug logging
            if (AutoArmMod.settings?.debugLogging == true)
            {
                if (job != null)
                {
                    Log.Message($"[AutoArm DEBUG] {pawn.Name}: Emergency TryGiveJob returning equip job for {job.targetA.Thing?.Label}");
                }
                else
                {
                    Log.Message($"[AutoArm DEBUG] {pawn.Name}: Emergency TryGiveJob returning null");
                }
            }

            return job;
        }

    }

    // Think tree conditional for checking if pawn has no sidearms
    public class ThinkNode_ConditionalNoSidearms : ThinkNode_Conditional
    {
        protected override bool Satisfied(Pawn pawn)
        {
            // Only check colonists
            if (!pawn.IsColonist)
                return false;

            // Skip if Simple Sidearms isn't loaded
            if (!SimpleSidearmsCompat.IsLoaded())
                return false;

            // Skip if sidearm auto-equip is disabled
            if (AutoArmMod.settings?.autoEquipSidearms != true)
                return false;

            // Skip if can't use weapons
            if (pawn.WorkTagIsDisabled(WorkTags.Violent))
                return false;

            // Skip if drafted
            if (pawn.Drafted)
                return false;

            // Check if pawn has any weapons in inventory
            if (pawn.inventory?.innerContainer == null || pawn.inventory.innerContainer.Count == 0)
            {
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    Log.Message($"[AutoArm] {pawn.Name}: ConditionalNoSidearms - no inventory items, returning true");
                }
                return true;
            }

            // Check if any weapons in inventory
            bool hasWeapons = pawn.inventory.innerContainer.Any(t => t.def.IsWeapon);

            if (AutoArmMod.settings?.debugLogging == true)
            {
                Log.Message($"[AutoArm] {pawn.Name}: ConditionalNoSidearms - has weapons: {hasWeapons}, returning {!hasWeapons}");
            }

            return !hasWeapons;
        }

        // Public method for testing
        public bool TestSatisfied(Pawn pawn)
        {
            return Satisfied(pawn);
        }
    }

    // Emergency sidearm acquisition job giver
    public class JobGiver_GetSidearmEmergency : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            if (AutoArmMod.settings?.debugLogging == true)
            {
                Log.Message($"[AutoArm] Emergency sidearm check for {pawn.Name}");
            }

            // Skip if mod is disabled
            if (AutoArmMod.settings?.modEnabled == false)
                return null;

            // Skip if Simple Sidearms isn't loaded
            if (!SimpleSidearmsCompat.IsLoaded())
                return null;

            // Skip if sidearm auto-equip is disabled
            if (AutoArmMod.settings?.autoEquipSidearms != true)
                return null;

            // Basic validation
            if (pawn == null || !pawn.IsColonist || pawn.Drafted || pawn.Dead || pawn.Downed)
                return null;

            if (pawn.WorkTagIsDisabled(WorkTags.Violent))
                return null;

            // Skip if doing absolutely critical work
            if (JobGiverHelpers.IsCriticalJob(pawn))
                return null;

            // Don't interrupt "Do until X" jobs
            if (pawn.mindState?.lastJobTag == JobTag.SatisfyingNeeds)
                return null;

            // Use the existing sidearm finding logic
            var job = SimpleSidearmsCompat.TryGetSidearmUpgradeJob(pawn);

            // Add the debug logging HERE
            if (AutoArmMod.settings?.debugLogging == true)
            {
                if (job != null)
                {
                    Log.Message($"[AutoArm DEBUG] {pawn.Name}: Sidearm TryGiveJob returning equip job for {job.targetA.Thing?.Label}");
                }
                else
                {
                    Log.Message($"[AutoArm DEBUG] {pawn.Name}: Sidearm TryGiveJob returning null");
                }
            }

            return job;
        }

        // Add this public method for testing
        public Job TestTryGiveJob(Pawn pawn)
        {
            return TryGiveJob(pawn);
        }
    }

}