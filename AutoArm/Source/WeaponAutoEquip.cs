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



    public class JobGiver_PickUpBetterWeapon : ThinkNode_JobGiver
    {
        private const int MaxWeaponsToConsider = 200; // Increased since we check less often
                                                      // No search radius - search entire map like apparel
                                                      // Weapon caching - made internal for MapComponent access
        internal static Dictionary<Map, List<ThingWithComps>> weaponCache = new Dictionary<Map, List<ThingWithComps>>();
        internal static Dictionary<Map, int> weaponCacheAge = new Dictionary<Map, int>();
        private const int CacheLifetime = 500; // ~8 seconds

        protected override Job TryGiveJob(Pawn pawn)
        {
            // Skip if mod is disabled
            if (AutoArmMod.settings?.modEnabled == false)
                return null;

            if (!IsValidPawnForAutoEquip(pawn))
                return null;

            // Note: We no longer skip entirely for Simple Sidearms users
            // Instead, we check more carefully when evaluating upgrades

            // DEBUG
            if (AutoArmMod.settings?.debugLogging == true && pawn.equipment?.Primary == null)
            {
                Log.Message($"[AutoArm DEBUG] {pawn.Name}: JobGiver_PickUpBetterWeapon starting for unarmed pawn");
            }

            // Don't interrupt important work
            if (pawn.CurJob != null && (pawn.CurJob.playerForced ||
                pawn.CurJob.def == JobDefOf.TendPatient ||
                pawn.CurJob.def == JobDefOf.Rescue ||
                pawn.CurJob.def == JobDefOf.ExtinguishSelf ||
                pawn.CurJob.def == JobDefOf.BeatFire))
                return null;

            // Respect "Do until X" jobs (like apparel)
            if (pawn.mindState?.lastJobTag == JobTag.SatisfyingNeeds)
                return null;

            // Initialize stat def if needed
            if (WeaponStatDefOf.RangedWeapon_AverageDPS == null)
                WeaponStatDefOf.RangedWeapon_AverageDPS = DefDatabase<StatDef>.GetNamedSilentFail("RangedWeapon_AverageDPS");

            // Check for forced weapon
            var forcedWeaponDef = ForcedWeaponTracker.GetForcedWeaponDef(pawn);
            if (forcedWeaponDef != null)
            {
                var currentWeapon = pawn.equipment?.Primary;
                if (currentWeapon == null || currentWeapon.def != forcedWeaponDef)
                {
                    var forcedWeapon = FindSpecificWeaponByDef(pawn, forcedWeaponDef);
                    if (forcedWeapon != null)
                    {
                        var equipJob = JobMaker.MakeJob(JobDefOf.Equip, forcedWeapon);
                        AutoEquipTracker.MarkAutoEquip(equipJob, pawn);
                        return equipJob;
                    }
                }
                return null;
            }

            var job = FindBetterWeaponJob(pawn);
            return job;
        }

        private Job FindBetterWeaponJob(Pawn pawn)
        {
            var currentWeapon = pawn.equipment?.Primary;
            float currentScore = currentWeapon != null ? GetWeaponScore(pawn, currentWeapon) : float.MinValue;
            bool currentIsMelee = currentWeapon?.def.IsMeleeWeapon ?? false;
            float improvementThreshold = currentWeapon != null ? currentScore * 1.05f : currentScore; // 10% improvement needed

            ThingWithComps bestWeapon = null;
            float bestScore = currentScore;
            int weaponsChecked = 0;
            int rangedWeaponsFound = 0;
            int meleeWeaponsFound = 0;

            // DEBUG: First check what weapons exist on the map
            if (AutoArmMod.settings?.debugLogging == true && currentWeapon == null)
            {
                var allMapWeapons = pawn.Map.listerThings.ThingsInGroup(ThingRequestGroup.Weapon);
                Log.Message($"[AutoArm DEBUG] {pawn.Name}: Total weapons on map = {allMapWeapons.Count()}");

                // Check if WeaponThingFilterUtility is finding weapons
                Log.Message($"[AutoArm DEBUG] WeaponThingFilterUtility reports {WeaponThingFilterUtility.AllWeapons.Count} weapon defs");
            }

            // Get all weapons from map (entire map, like apparel)
            // Get all weapons from map without sorting
            // Get cached weapons or refresh cache if expired
            // Get cached weapons or refresh cache if expired
            List<ThingWithComps> cachedWeapons;
            int cacheAge;
            if (!weaponCache.TryGetValue(pawn.Map, out cachedWeapons) ||
                !weaponCacheAge.TryGetValue(pawn.Map, out cacheAge) ||
                Find.TickManager.TicksGame - cacheAge > CacheLifetime)
            {
                // Manual loop to avoid LINQ overhead
                cachedWeapons = new List<ThingWithComps>();
                var allMapWeapons = pawn.Map.listerThings.ThingsInGroup(ThingRequestGroup.Weapon);

                // Pre-allocate list capacity to avoid resizing
                cachedWeapons.Capacity = Math.Min(allMapWeapons.Count(), 200);

                foreach (var thing in allMapWeapons)
                {
                    var weapon = thing as ThingWithComps;
                    if (weapon?.def == null) continue;

                    // Quick filter - no LINQ
                    if (!weapon.def.IsRangedWeapon && !weapon.def.IsMeleeWeapon) continue;
                    if (weapon.def.defName == "WoodLog") continue;
                    if (weapon.IsForbidden(Faction.OfPlayer)) continue;

                    // Allow elephant tusks and thrumbo horns explicitly
                    if (weapon.def.defName == "ElephantTusk" || weapon.def.defName == "ThrumboHorn")
                    {
                        cachedWeapons.Add(weapon);
                    }
                    // For other weapons, check the list (this is the important validation)
                    else if (WeaponThingFilterUtility.AllWeapons.Contains(weapon.def))
                    {
                        cachedWeapons.Add(weapon);
                    }
                }

                // Sort by distance using squared distance for performance
                cachedWeapons.Sort((a, b) =>
                {
                    float distA = (a.Position - pawn.Position).LengthHorizontalSquared;
                    float distB = (b.Position - pawn.Position).LengthHorizontalSquared;
                    return distA.CompareTo(distB);
                });

                weaponCache[pawn.Map] = cachedWeapons;
                weaponCacheAge[pawn.Map] = Find.TickManager.TicksGame;

                if (AutoArmMod.settings?.debugLogging == true)
                {
                    Log.Message($"[AutoArm DEBUG] {pawn.Name}: Rebuilt weapon cache - {cachedWeapons.Count} valid weapons");
                    if (cachedWeapons.Count == 0)
                    {
                        Log.Message($"[AutoArm DEBUG] {pawn.Name}: WARNING - No valid weapons found in cache!");
                    }
                }
            }

            // DEBUG: Log weapons before filtering
            if (AutoArmMod.settings?.debugLogging == true && currentWeapon == null)
            {
                var weaponList = cachedWeapons.ToList();
                Log.Message($"[AutoArm DEBUG] {pawn.Name}: Found {weaponList.Count} weapons on entire map");
            }

            foreach (var weapon in cachedWeapons)
            {
                if (weaponsChecked >= MaxWeaponsToConsider)
                    break;

                float distance = weapon.Position.DistanceTo(pawn.Position);
                if (distance > 50f) // Skip far weapons
                    continue;

                // DEBUG: Log why weapons are rejected
                if (AutoArmMod.settings?.debugLogging == true && currentWeapon == null && weaponsChecked < 5)
                {
                    if (!IsWeapon(weapon))
                    {
                        Log.Message($"[AutoArm DEBUG] {pawn.Name}: {weapon.def.defName} failed IsWeapon check");
                    }
                    else if (!IsValidWeaponCandidate(weapon, pawn))
                    {
                        Log.Message($"[AutoArm DEBUG] {pawn.Name}: {weapon.def.defName} failed IsValidWeaponCandidate check");
                    }
                }

                if (IsWeapon(weapon) && IsValidWeaponCandidate(weapon, pawn))
                {
                    weaponsChecked++;

                    if (weapon.def.IsRangedWeapon) rangedWeaponsFound++;
                    if (weapon.def.IsMeleeWeapon) meleeWeaponsFound++;

                    // Skip weapon type switching unless significant improvement
                    if (currentWeapon != null && weapon.def.IsMeleeWeapon != currentIsMelee)
                    {
                        float switchThreshold = currentScore * 1.5f; // Need 50% improvement to switch types
                        float weaponScore = GetWeaponScore(pawn, weapon);
                        if (weaponScore <= switchThreshold)
                            continue;
                    }

                    float score = GetWeaponScore(pawn, weapon);

                    // DEBUG: Log weapon scores for unarmed pawns
                    if (AutoArmMod.settings?.debugLogging == true && currentWeapon == null && weapon.def.IsRangedWeapon)
                    {
                        Log.Message($"[AutoArm DEBUG] {pawn.Name}: Ranged weapon {weapon.def.defName} score = {score}");
                    }

                    if (score > bestScore && (currentWeapon == null || score > improvementThreshold))
                    {
                        bestScore = score;
                        bestWeapon = weapon;
                    }
                }
            }

            // DEBUG: Log summary
            if (AutoArmMod.settings?.debugLogging == true && currentWeapon == null)
            {
                Log.Message($"[AutoArm DEBUG] {pawn.Name}: Checked {weaponsChecked} weapons. Found {rangedWeaponsFound} ranged, {meleeWeaponsFound} melee. Best weapon: {bestWeapon?.def.defName ?? "none"} with score {bestScore}");
            }

            if (bestWeapon != null)
            {
                var equipJob = JobMaker.MakeJob(JobDefOf.Equip, bestWeapon);

                // Mark for notification
                AutoEquipTracker.MarkAutoEquip(equipJob, pawn);

                return equipJob;
            }

            return null;
        }

        public Job TestTryGiveJob(Pawn pawn)
        {
            return TryGiveJob(pawn);
        }

        private ThingWithComps FindSpecificWeaponByDef(Pawn pawn, ThingDef weaponDef)
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
            {
                if (AutoArmMod.settings?.debugLogging == true)
                    Log.Message($"[AutoArm DEBUG] IsWeapon: null thing/def");
                return false;
            }

            // Explicitly allow vanilla animal weapons
            if (thing.def.defName == "ElephantTusk" || thing.def.defName == "ThrumboHorn")
            {
                return true;
            }

            // Use the cached weapon lists for better performance
            bool isInList = WeaponThingFilterUtility.AllWeapons.Contains(thing.def);

            if (AutoArmMod.settings?.debugLogging == true && !isInList)
            {
                Log.Message($"[AutoArm DEBUG] IsWeapon: {thing.def.defName} not in AllWeapons list. IsWeapon={thing.def.IsWeapon}, IsRanged={thing.def.IsRangedWeapon}, IsMelee={thing.def.IsMeleeWeapon}");
            }

            return isInList;
        }

        protected bool IsValidWeaponCandidate(ThingWithComps weapon, Pawn pawn)
        {
            if (weapon == null || weapon.def == null || weapon.Destroyed)
            {
                if (AutoArmMod.settings?.debugLogging == true)
                    Log.Message($"[AutoArm DEBUG] {pawn.Name}: {weapon?.def?.defName ?? "null"} - null/destroyed");
                return false;
            }
            if (weapon.Map != pawn.Map)
            {
                if (AutoArmMod.settings?.debugLogging == true)
                    Log.Message($"[AutoArm DEBUG] {pawn.Name}: {weapon.def.defName} - wrong map");
                return false;
            }
            if (!weapon.def.IsWeapon)
            {
                if (AutoArmMod.settings?.debugLogging == true)
                    Log.Message($"[AutoArm DEBUG] {pawn.Name}: {weapon.def.defName} - not a weapon");
                return false;
            }

            // Check for heavy weapon mod extension
            if (weapon.def.modExtensions?.Any(extension => extension.GetType().Name == "HeavyWeapon") == true)
            {
                if (AutoArmMod.settings?.debugLogging == true)
                    Log.Message($"[AutoArm DEBUG] {pawn.Name}: {weapon.def.defName} - heavy weapon");
                return false;
            }

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
                        if (AutoArmMod.settings?.debugLogging == true)
                        {
                            Log.Message($"[AutoArm DEBUG] {pawn.Name}: {weapon.def.defName} bypassing outfit filter (vanilla animal weapon)");
                        }
                    }
                    else if (!policy.filter.Allows(weapon.def))
                    {
                        if (AutoArmMod.settings?.debugLogging == true)
                        {
                            Log.Message($"[AutoArm DEBUG] {pawn.Name}: {weapon.def.defName} blocked by outfit filter");
                        }
                        return false;
                    }
                }
            }

            // NEW: Check Simple Sidearms compatibility

            if (weapon.IsForbidden(pawn))
            {
                if (AutoArmMod.settings?.debugLogging == true)
                    Log.Message($"[AutoArm DEBUG] {pawn.Name}: {weapon.def.defName} - forbidden");
                return false;
            }

            var biocomp = weapon.TryGetComp<CompBiocodable>();
            if (biocomp?.Biocoded == true && biocomp.CodedPawn != pawn)
            {
                if (AutoArmMod.settings?.debugLogging == true)
                    Log.Message($"[AutoArm DEBUG] {pawn.Name}: {weapon.def.defName} - biocoded to another");
                return false;
            }

            if (weapon.questTags != null && weapon.questTags.Count > 0)
            {
                if (AutoArmMod.settings?.debugLogging == true)
                    Log.Message($"[AutoArm DEBUG] {pawn.Name}: {weapon.def.defName} - quest item");
                return false;
            }

            var reservationManager = pawn.Map.reservationManager;
            if (reservationManager.IsReservedByAnyoneOf(weapon, pawn.Faction) &&
                !reservationManager.CanReserve(pawn, weapon))
            {
                if (AutoArmMod.settings?.debugLogging == true)
                    Log.Message($"[AutoArm DEBUG] {pawn.Name}: {weapon.def.defName} - reserved");
                return false;
            }

            if (!pawn.CanReserveAndReach(weapon, PathEndMode.ClosestTouch, Danger.Deadly))
            {
                if (AutoArmMod.settings?.debugLogging == true)
                    Log.Message($"[AutoArm DEBUG] {pawn.Name}: {weapon.def.defName} - can't reach/reserve");
                return false;
            }

            // Check if weapon is currently equipped by another pawn
            if (weapon.ParentHolder is Pawn_EquipmentTracker || weapon.ParentHolder is Pawn_InventoryTracker)
            {
                if (AutoArmMod.settings?.debugLogging == true)
                    Log.Message($"[AutoArm DEBUG] {pawn.Name}: {weapon.def.defName} - equipped by another");
                return false;
            }

            if (weapon.IsBurning())
            {
                if (AutoArmMod.settings?.debugLogging == true)
                    Log.Message($"[AutoArm DEBUG] {pawn.Name}: {weapon.def.defName} - burning");
                return false;
            }

            // Check if weapon requires research that isn't complete
            // Note: This should only apply to special weapons that require research to USE, not to craft
            // Most vanilla weapons don't have use restrictions
            if (weapon.def.researchPrerequisites != null && weapon.def.researchPrerequisites.Count > 0)
            {
                foreach (var research in weapon.def.researchPrerequisites)
                {
                    if (!research.IsFinished)
                    {
                        if (AutoArmMod.settings?.debugLogging == true)
                            Log.Message($"[AutoArm DEBUG] {pawn.Name}: {weapon.def.defName} - use research not complete: {research.defName}");
                        return false;
                    }
                }
            }

            return true;
        }


        public float GetWeaponScore(Pawn pawn, ThingWithComps weapon)
        {
            if (weapon?.def == null)
                return 0f;

            // Base validation - weapon must do damage
            var equippable = weapon.TryGetComp<CompEquippable>();
            if (equippable?.PrimaryVerb == null || !equippable.PrimaryVerb.HarmsHealth())
            {
                if (AutoArmMod.settings?.debugLogging == true)
                    Log.Message($"[AutoArm DEBUG] {pawn.Name}: Weapon {weapon.def.defName} fails base validation");
                return -1000f;
            }

            float score = 0f;

            // Quality bonus (increased importance)
            if (weapon.TryGetQuality(out QualityCategory qc))
                score += (int)qc * 15f;

            // Condition bonus
            if (weapon.MaxHitPoints > 0)
                score += (weapon.HitPoints / (float)weapon.MaxHitPoints) * 20f;

            // Hunter-specific penalties
            bool isHunter = pawn.workSettings?.WorkIsActive(WorkTypeDefOf.Hunting) ?? false;
            if (isHunter)
            {
                if (equippable.PrimaryVerb.UsesExplosiveProjectiles())
                {
                    if (AutoArmMod.settings?.debugLogging == true)
                        Log.Message($"[AutoArm DEBUG] {pawn.Name}: Hunter penalty for explosive weapon {weapon.def.defName}");
                    return -1000f;
                }

                if (weapon.def.IsMeleeWeapon)
                {
                    if (AutoArmMod.settings?.debugLogging == true)
                        Log.Message($"[AutoArm DEBUG] {pawn.Name}: Hunter penalty for melee weapon {weapon.def.defName}");
                    return -1000f;
                }

                var damageDef = equippable.PrimaryVerb.GetDamageDef();
                if (damageDef != null && (damageDef.hediffSkin != null || damageDef.hediffSolid != null))
                {
                    if (AutoArmMod.settings?.debugLogging == true)
                        Log.Message($"[AutoArm DEBUG] {pawn.Name}: Hunter penalty for damage type on {weapon.def.defName}");
                    return -1000f;
                }

                // Prefer long-range weapons for hunters
                var verbs = weapon.def.Verbs;
                if (verbs?.Count > 0 && verbs[0] != null)
                    score += verbs[0].range * 1.0f;
            }

            // Skill-based scoring
            if (weapon.def.IsRangedWeapon)
            {
                float shootingSkill = pawn.skills?.GetSkill(SkillDefOf.Shooting)?.Level ?? 0f;
                score += shootingSkill * 3f;

                if (WeaponStatDefOf.RangedWeapon_AverageDPS != null)
                {
                    try
                    {
                        float dps = weapon.GetStatValue(WeaponStatDefOf.RangedWeapon_AverageDPS, true, -1);
                        if (dps > 0)
                            score += dps * 5f;
                    }
                    catch { }
                }

                var verbs = weapon.def.Verbs;
                if (verbs?.Count > 0 && verbs[0] != null)
                    score += verbs[0].range * 0.5f;

                // Consider accuracy
                var accuracyStat = DefDatabase<StatDef>.GetNamedSilentFail("AccuracyMedium");
                if (accuracyStat != null)
                {
                    try
                    {
                        float accuracy = weapon.GetStatValue(accuracyStat, true, -1);
                        score += accuracy * 20f;
                    }
                    catch { }
                }
            }
            else if (weapon.def.IsMeleeWeapon)
            {
                float meleeSkill = pawn.skills?.GetSkill(SkillDefOf.Melee)?.Level ?? 0f;
                score += meleeSkill * 3f;

                float meleeDPS = 0f;
                if (StatDefOf.MeleeWeapon_AverageDPS != null)
                {
                    try
                    {
                        meleeDPS = weapon.GetStatValue(StatDefOf.MeleeWeapon_AverageDPS, true, -1);
                        if (meleeDPS > 0)
                            score += meleeDPS * 5f;
                    }
                    catch { }
                }

                // Penalize melee weapons worse than fists
                if (meleeDPS < 2.0f)
                    score -= 100f;

                // Consider armor penetration for melee (if available in mods)
                var armorPenStat = DefDatabase<StatDef>.GetNamedSilentFail("MeleeWeapon_AverageArmorPenetration");
                if (armorPenStat != null)
                {
                    try
                    {
                        float armorPen = weapon.GetStatValue(armorPenStat, true, -1);
                        score += armorPen * 30f;
                    }
                    catch { }
                }
            }

            // Trait-based adjustments
            var traits = pawn.story?.traits;
            if (traits != null)
            {
                if (traits.HasTrait(TraitDefOf.Brawler))
                {
                    if (weapon.def.IsMeleeWeapon)
                        score += 200f;
                    else
                    {
                        if (AutoArmMod.settings?.debugLogging == true)
                            Log.Message($"[AutoArm DEBUG] {pawn.Name}: Brawler penalty for ranged weapon {weapon.def.defName}");
                        return -2000f;
                    }
                }

                if (weapon.def.IsRangedWeapon)
                {
                    var triggerHappy = DefDatabase<TraitDef>.GetNamedSilentFail("ShootingAccuracy");
                    if (triggerHappy != null)
                    {
                        var degree = traits.GetTrait(triggerHappy)?.Degree ?? 0;
                        if (degree == -1) // Trigger-happy
                            score += 20f;
                        else if (degree == 1) // Careful shooter
                            score += 30f;
                    }
                }
            }

            // Skill preference
            if (!isHunter && traits?.HasTrait(TraitDefOf.Brawler) != true)
            {
                float meleeLevel = pawn.skills?.GetSkill(SkillDefOf.Melee)?.Level ?? 0f;
                float shootingLevel = pawn.skills?.GetSkill(SkillDefOf.Shooting)?.Level ?? 0f;

                if (meleeLevel > shootingLevel && weapon.def.IsMeleeWeapon)
                    score += (meleeLevel - shootingLevel) * 5f;
                else if (shootingLevel > meleeLevel && weapon.def.IsRangedWeapon)
                    score += (shootingLevel - meleeLevel) * 5f;
            }

            // Tech level preference (prefer higher tech)
            score += (int)weapon.def.techLevel * 5f;

            // Market value as minor factor
            score += weapon.MarketValue * 0.001f;

            // Infusion 2 compatibility - add bonus for infused weapons
            if (InfusionCompat.IsLoaded())
            {
                score += InfusionCompat.GetInfusionScoreBonus(weapon);
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
                return false;

            if (pawn.WorkTagIsDisabled(WorkTags.Violent))
                return false;

            if (pawn.Drafted)
                return false;

            if (pawn.outfits?.CurrentApparelPolicy?.filter == null)
            {
                Log.Message($"[AutoArm] {pawn.Name}: WeaponsInOutfit - no filter, returning true");
                return true;
            }

            var filter = pawn.outfits.CurrentApparelPolicy.filter;
            bool result = WeaponThingFilterUtility.RangedWeapons.Any(td => filter.Allows(td)) ||
                         WeaponThingFilterUtility.MeleeWeapons.Any(td => filter.Allows(td));

            Log.Message($"[AutoArm] {pawn.Name}: WeaponsInOutfit - {result}");
            return result;
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

            var currentWeapon = pawn.equipment?.Primary;

            // Unarmed pawns - always satisfied
            if (currentWeapon == null)
            {
                if (AutoArmMod.settings?.debugLogging == true && pawn.IsHashIntervalTick(500))
                    Log.Message($"[AutoArm DEBUG] {pawn.Name}: ConditionalUnarmedOrPoorlyArmed - unarmed pawn, returning true");
                return true;
            }

            // Armed pawns - check for poor weapons periodically
            if (!pawn.IsHashIntervalTick(2500 + pawn.thingIDNumber % 500))
                return false;

            // Check if current weapon is extremely poor quality
            if (currentWeapon.def.IsRangedWeapon)
            {
                if (WeaponStatDefOf.RangedWeapon_AverageDPS != null)
                {
                    float dps = currentWeapon.GetStatValue(WeaponStatDefOf.RangedWeapon_AverageDPS, true, -1);
                    if (dps < 3f)
                    {
                        if (AutoArmMod.settings?.debugLogging == true)
                            Log.Message($"[AutoArm DEBUG] {pawn.Name}: ConditionalUnarmedOrPoorlyArmed - poor ranged weapon (DPS: {dps})");
                        return true;
                    }
                }
            }
            else if (currentWeapon.def.IsMeleeWeapon)
            {
                // Consider "poorly armed" if using very basic melee weapons
                float meleeDPS = currentWeapon.GetStatValue(StatDefOf.MeleeWeapon_AverageDPS, true, -1);
                if (currentWeapon.def.techLevel == TechLevel.Neolithic && meleeDPS < 2.0f)
                {
                    if (AutoArmMod.settings?.debugLogging == true)
                        Log.Message($"[AutoArm DEBUG] {pawn.Name}: ConditionalUnarmedOrPoorlyArmed - poor melee weapon (DPS: {meleeDPS})");
                    return true;
                }
            }

            return false;
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
            if (IsCriticalWork(pawn))
                return null;

            // Don't interrupt "Do until X" jobs
            if (pawn.mindState?.lastJobTag == JobTag.SatisfyingNeeds)
                return null;

            // Find ANY weapon quickly
            return FindAnyWeaponJob(pawn);
        }

        private bool IsCriticalWork(Pawn pawn)
        {
            if (pawn.CurJob == null)
                return false;

            var job = pawn.CurJob.def;
            return job == JobDefOf.TendPatient ||
                   job == JobDefOf.Rescue ||
                   job == JobDefOf.ExtinguishSelf ||
                   job == JobDefOf.BeatFire ||
                   pawn.CurJob.playerForced;
        }

        private Job FindAnyWeaponJob(Pawn pawn)
        {
            // Search for closest allowed weapon
            var weapons = pawn.Map.listerThings.ThingsInGroup(ThingRequestGroup.Weapon)
                .OfType<ThingWithComps>()
                .Where(w => IsWeapon(w) && IsValidWeaponCandidate(w, pawn))
                .OrderBy(w => w.Position.DistanceTo(pawn.Position))
                .Take(20); // Only check closest 20 weapons for performance

            foreach (var weapon in weapons)
            {
                // For emergency, take any weapon that's allowed by outfit (or if no outfit)
                var filter = pawn.outfits?.CurrentApparelPolicy?.filter;
                if (filter == null || filter.Allows(weapon.def))
                {
                    var equipJob = JobMaker.MakeJob(JobDefOf.Equip, weapon);
                    AutoEquipTracker.MarkAutoEquip(equipJob, pawn);

                    if (AutoArmMod.settings?.debugLogging == true)
                    {
                        Log.Message($"[AutoArm] Emergency: {pawn.Name} will equip {weapon.Label}");
                    }

                    return equipJob;
                }
            }

            return null;
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
        protected override Job TryGiveJob(Pawn pawn)  // Keep as 'protected override'
        {
            if (AutoArmMod.settings?.debugLogging == true)
            {
                Log.Message($"[AutoArm] Emergency weapon check for {pawn.Name}");
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
            if (IsCriticalWork(pawn))
                return null;

            // Don't interrupt "Do until X" jobs
            if (pawn.mindState?.lastJobTag == JobTag.SatisfyingNeeds)
                return null;

            if (AutoArmMod.settings?.debugLogging == true)
            {
                Log.Message($"[AutoArm DEBUG] {pawn.Name}: JobGiver_GetSidearmEmergency checking for sidearms");
            }

            // Use the existing sidearm finding logic
            var job = SimpleSidearmsCompat.TryGetSidearmUpgradeJob(pawn);

            if (job != null && AutoArmMod.settings?.debugLogging == true)
            {
                Log.Message($"[AutoArm] Emergency: {pawn.Name} will pick up {job.targetA.Thing?.Label} as first sidearm");
            }

            return job;
        }

        private bool IsCriticalWork(Pawn pawn)
        {
            if (pawn.CurJob == null)
                return false;

            var job = pawn.CurJob.def;
            return job == JobDefOf.TendPatient ||
                   job == JobDefOf.Rescue ||
                   job == JobDefOf.ExtinguishSelf ||
                   job == JobDefOf.BeatFire ||
                   pawn.CurJob.playerForced;
        }

        // Add this public method for testing
        public Job TestTryGiveJob(Pawn pawn)
        {
            return TryGiveJob(pawn);
        }
    }

}