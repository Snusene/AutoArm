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

            // Check if this pawn has a forced weapon def
            if (!forcedWeaponsByDef.TryGetValue(pawn, out var forcedDef))
                return false;

            // The weapon is only "forced" if:
            // 1. It matches the forced def AND
            // 2. It's currently equipped by the pawn
            return weapon.def == forcedDef && pawn.equipment?.Primary == weapon;
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

        // Track when jobs were added for cleanup
        private static Dictionary<int, int> jobAddedTick = new Dictionary<int, int>();
        private const int JobRetentionTicks = 1000; // Keep job IDs for ~16 seconds

        public static void MarkAutoEquip(Job job, Pawn pawn = null)
        {
            if (job != null)
            {
                autoEquipJobIds.Add(job.loadID);

                // Track when this job was marked
                jobAddedTick[job.loadID] = Find.TickManager.TicksGame;

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
            {
                autoEquipJobIds.Remove(job.loadID);
                jobAddedTick.Remove(job.loadID);
            }
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

        // New cleanup method
        public static void CleanupOldJobs()
        {
            if (jobAddedTick.Count == 0)
                return;

            int currentTick = Find.TickManager.TicksGame;
            var toRemove = new List<int>();

            foreach (var kvp in jobAddedTick)
            {
                if (currentTick - kvp.Value > JobRetentionTicks)
                {
                    toRemove.Add(kvp.Key);
                }
            }

            foreach (int jobId in toRemove)
            {
                autoEquipJobIds.Remove(jobId);
                jobAddedTick.Remove(jobId);
            }

            // Also cleanup previous weapons for dead/null pawns
            var deadPawns = previousWeapons.Keys.Where(p => p.DestroyedOrNull() || p.Dead).ToList();
            foreach (var pawn in deadPawns)
            {
                previousWeapons.Remove(pawn);
            }

            if (AutoArmMod.settings?.debugLogging == true && toRemove.Count > 0)
            {
                Log.Message($"[AutoArm] Cleaned up {toRemove.Count} old job IDs and {deadPawns.Count} dead pawn records");
            }
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

        // Performance optimization fields
        private static Dictionary<Pawn, int> lastSuccessfulSearchTick = new Dictionary<Pawn, int>();
        private static Dictionary<Pawn, float> lastWeaponScore = new Dictionary<Pawn, float>();

        private static readonly CompositeWeaponScorer weaponScorer = new CompositeWeaponScorer();

        protected override Job TryGiveJob(Pawn pawn)
        {
            if (AutoArmMod.settings?.debugLogging == true)
            {
                var stackTrace = new System.Diagnostics.StackTrace();
                var callingMethod = stackTrace.GetFrame(1)?.GetMethod()?.Name;
                Log.Message($"[AutoArm DEBUG] TryGiveJob called from: {callingMethod}");
            }
            // Add immediate debug logging
            if (AutoArmMod.settings?.debugLogging == true)
            {
                var current = pawn.equipment?.Primary;
                Log.Message($"[AutoArm DEBUG] TryGiveJob called for {pawn.Name} - Current: {current?.Label ?? "nothing"}, IsWeapon: {current?.def.IsWeapon ?? false}");
            }

            // Skip if mod is disabled
            if (AutoArmMod.settings?.modEnabled == false)
                return null;

            // Quick validation
            string reason;
            if (!JobGiverHelpers.IsValidPawnForAutoEquip(pawn, out reason))
            {
                // Only log if it's NOT the common "not spawned" case
                if (AutoArmMod.settings?.debugLogging == true && reason != "Not spawned")
                {
                    Log.Message($"[AutoArm DEBUG] {pawn.Name} - Invalid pawn: {reason}");
                }
                return null;
            }

            var currentWeapon = pawn.equipment?.Primary;

            // Check if current weapon is forced
            if (currentWeapon != null && ForcedWeaponTracker.IsForced(pawn, currentWeapon))
                return null;

            if (AutoArmMod.settings?.debugLogging == true && currentWeapon != null)
            {
                bool isForced = ForcedWeaponTracker.IsForced(pawn, currentWeapon);
                Log.Message($"[AutoArm DEBUG] {pawn.Name} weapon {currentWeapon.Label} forced status: {isForced}");
            }

            // Check if current weapon is forced
            if (currentWeapon != null && ForcedWeaponTracker.IsForced(pawn, currentWeapon))
            {
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    Log.Message($"[AutoArm DEBUG] {pawn.Name} - Skipping because weapon is forced");
                }
                return null;
            }

            bool isUnarmed = currentWeapon == null;

            // For armed pawns, check if we recently found nothing
            if (!isUnarmed)
            {
                if (lastSuccessfulSearchTick.TryGetValue(pawn, out int lastTick))
                {
                    int waitTime = pawn.Map.mapPawns.FreeColonistsCount > 20 ? 1200 : 600;
                    if (Find.TickManager.TicksGame - lastTick < waitTime)
                    {
                        if (AutoArmMod.settings?.debugLogging == true)
                        {
                            Log.Message($"[AutoArm DEBUG] {pawn.Name} - Skipping due to recent search (wait {waitTime - (Find.TickManager.TicksGame - lastTick)} more ticks)");
                        }
                        return null;
                    }
                }

                // Don't interrupt critical jobs unless unarmed
                if (JobGiverHelpers.IsCriticalJob(pawn))
                {
                    if (AutoArmMod.settings?.debugLogging == true)
                    {
                        Log.Message($"[AutoArm DEBUG] {pawn.Name} - Skipping due to critical job: {pawn.CurJob?.def?.defName}");
                    }
                    return null;
                }
            }

            if (AutoArmMod.settings?.debugLogging == true)
            {
                Log.Message($"[AutoArm DEBUG] {pawn.Name} proceeding to FindBetterWeaponJob...");
            }

            var job = FindBetterWeaponJob(pawn);

            // Track results
            if (job == null)
            {
                lastSuccessfulSearchTick[pawn] = Find.TickManager.TicksGame;
            }
            else
            {
                lastSuccessfulSearchTick.Remove(pawn); // Reset on success

                if (AutoArmMod.settings?.debugLogging == true)
                {
                    Log.Message($"[AutoArm DEBUG] {pawn.Name}: TryGiveJob returning job to equip {job.targetA.Thing?.Label}");
                    Log.Message($"[AutoArm DEBUG] {pawn.Name}: Current job is {pawn.CurJob?.def?.defName ?? "null"} (playerForced: {pawn.CurJob?.playerForced})");

                    // Check think tree position
                    var thinkNode = pawn.thinker?.MainThinkNodeRoot;
                    if (thinkNode != null)
                    {
                        Log.Message($"[AutoArm DEBUG] {pawn.Name}: Think tree root type: {thinkNode.GetType().Name}");
                    }
                }

                // OPTION 1: Selective expiry based on weapon quality
                if (currentWeapon == null || !currentWeapon.def.IsWeapon)
                {
                    // Unarmed - don't set expiry, let them get weapon ASAP
                    if (AutoArmMod.settings?.debugLogging == true)
                    {
                        Log.Message($"[AutoArm DEBUG] {pawn.Name}: Unarmed - no expiry set");
                    }
                }
                else if (job.targetA.Thing is ThingWithComps newWeapon)
                {
                    float currentScore = GetWeaponScore(pawn, currentWeapon);
                    float newScore = GetWeaponScore(pawn, newWeapon);
                    float improvement = newScore / currentScore;

                    if (improvement >= 1.15f) // 15%+ better
                    {
                        // Significant upgrade - set long expiry to eventually interrupt low-priority tasks
                        job.expiryInterval = 2500; // ~40 seconds
                        job.checkOverrideOnExpire = true;

                        if (AutoArmMod.settings?.debugLogging == true)
                        {
                            Log.Message($"[AutoArm DEBUG] {pawn.Name}: Set 40s expiry for {(improvement - 1f) * 100f:F0}% weapon upgrade");
                        }
                    }
                    else
                    {
                        if (AutoArmMod.settings?.debugLogging == true)
                        {
                            Log.Message($"[AutoArm DEBUG] {pawn.Name}: Minor upgrade ({(improvement - 1f) * 100f:F0}%) - no expiry");
                        }
                    }
                }
            }

            return job;
        }

        public float GetWeaponScore(Pawn pawn, ThingWithComps weapon)
        {
            if (weapon == null || pawn == null)
                return 0f;

            // Check if it's a forced weapon
            if (ForcedWeaponTracker.IsForced(pawn, weapon))
                return 10000f; // High score but not float.MaxValue

            // Use the composite scorer
            return weaponScorer.GetScore(pawn, weapon);
        }

        public Job FindBetterWeaponJob(Pawn pawn)
        {
            var currentWeapon = pawn.equipment?.Primary;
            bool isUnarmed = currentWeapon == null || !currentWeapon.def.IsWeapon;  // Fix: Check for actual weapons

            // Check if current weapon is forced - don't replace forced weapons
            if (currentWeapon != null && ForcedWeaponTracker.IsForced(pawn, currentWeapon))
            {
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    Log.Message($"[AutoArm DEBUG] {pawn.Name}: Current weapon {currentWeapon.Label} is forced, skipping upgrade check");
                }
                return null;
            }

            float currentScore = currentWeapon != null ? GetWeaponScore(pawn, currentWeapon) : -1000f;
            float improvementThreshold = isUnarmed ? currentScore : currentScore * 1.05f;

            ThingWithComps bestWeapon = null;
            float bestScore = currentScore;
            int weaponsChecked = 0;

            // Get ALL weapons first to debug
            var allWeaponsInCache = ImprovedWeaponCacheManager.GetWeaponsNear(pawn.Map, pawn.Position, MaxSearchDistance).ToList();

            if (AutoArmMod.settings?.debugLogging == true && isUnarmed)
            {
                Log.Message($"[AutoArm DEBUG] {pawn.Name}: Raw cache has {allWeaponsInCache.Count} weapons near position {pawn.Position}");

                // Check if assault rifles are in cache at all
                var assaultRifles = allWeaponsInCache.Where(w => w.def.defName == "Gun_AssaultRifle").ToList();
                if (assaultRifles.Any())
                {
                    Log.Message($"[AutoArm DEBUG] {pawn.Name}: Found {assaultRifles.Count} assault rifles in cache");
                    var closest = assaultRifles.OrderBy(w => w.Position.DistanceTo(pawn.Position)).First();
                    Log.Message($"[AutoArm DEBUG] Closest assault rifle: {closest.Label} at distance {closest.Position.DistanceTo(pawn.Position):F1}, Forbidden: {closest.IsForbidden(pawn)}");
                }
                else
                {
                    Log.Message($"[AutoArm DEBUG] {pawn.Name}: NO assault rifles in cache within {MaxSearchDistance} cells!");
                }
            }

            // Use the cache manager instead of direct query
            var mapWeapons = ImprovedWeaponCacheManager.GetWeaponsNear(pawn.Map, pawn.Position, MaxSearchDistance)
                .Where(w => w != currentWeapon)
                .Take(MaxWeaponsToConsider)  // or 50
                .ToList();

            if (AutoArmMod.settings?.debugLogging == true)
            {
                Log.Message($"[AutoArm DEBUG] {pawn.Name}: Found {mapWeapons.Count} weapons within {MaxSearchDistance} cells after filtering");

                // For unarmed pawns, log the first few weapons found
                if (isUnarmed && mapWeapons.Count > 0)
                {
                    Log.Message($"[AutoArm DEBUG] {pawn.Name} is UNARMED, checking weapons:");
                    for (int i = 0; i < Math.Min(5, mapWeapons.Count); i++)
                    {
                        var w = mapWeapons[i];
                        Log.Message($"[AutoArm DEBUG] - {w.Label} at distance {w.Position.DistanceTo(pawn.Position):F1}");
                    }
                }
            }

            foreach (var weapon in mapWeapons)
            {
                if (weaponsChecked >= MaxWeaponsToConsider)
                    break;

                // Validate weapon
                string invalidReason;
                if (!JobGiverHelpers.IsValidWeaponCandidate(weapon, pawn, out invalidReason))
                {
                    if (AutoArmMod.settings?.debugLogging == true && weaponsChecked < 5)
                    {
                        Log.Message($"[AutoArm DEBUG] {pawn.Name}: {weapon.Label} invalid: {invalidReason}");
                    }
                    continue;
                }

                weaponsChecked++;
                float score = GetWeaponScore(pawn, weapon);

                if (AutoArmMod.settings?.debugLogging == true && weaponsChecked <= 5)
                {
                    Log.Message($"[AutoArm DEBUG] {pawn.Name}: {weapon.Label} score: {score:F1} (need > {improvementThreshold:F1})");
                }

                if (score > bestScore && score > improvementThreshold)
                {
                    bestScore = score;
                    bestWeapon = weapon;
                }
            }

            if (bestWeapon != null)
            {
                var equipJob = JobMaker.MakeJob(JobDefOf.Equip, bestWeapon);
                AutoEquipTracker.MarkAutoEquip(equipJob, pawn);

                if (AutoArmMod.settings?.debugLogging == true)
                {
                    Log.Message($"[AutoArm DEBUG] {pawn.Name}: Will equip {bestWeapon.Label} (score: {bestScore:F1})");
                }

                return equipJob;
            }

            if (AutoArmMod.settings?.debugLogging == true)
            {
                Log.Message($"[AutoArm DEBUG] {pawn.Name}: No upgrade found (checked {weaponsChecked} weapons)");
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

            // Direct checks without relying on cached lists
            if (!thing.def.IsWeapon)
                return false;

            if (thing.def.IsApparel)
                return false;

            if (thing.def.equipmentType == EquipmentType.None)
                return false;

            // Explicitly allow vanilla animal weapons
            if (thing.def.defName == "ElephantTusk" || thing.def.defName == "ThrumboHorn")
                return true;

            return true;
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

        // Cleanup method
        public static void CleanupCaches()
        {
            var toRemove = lastSuccessfulSearchTick.Keys.Where(p => p.DestroyedOrNull() || p.Dead).ToList();
            foreach (var pawn in toRemove)
            {
                lastSuccessfulSearchTick.Remove(pawn);
                lastWeaponScore.Remove(pawn);
            }
        }

        // Nested interface and scorer classes
        public interface IWeaponScorer
        {
            float GetScore(Pawn pawn, ThingWithComps weapon);
        }

        public class CompositeWeaponScorer : IWeaponScorer
        {
            private static readonly List<IWeaponScorer> scorers = new List<IWeaponScorer>
            {
                new OutfitPolicyScorer(),
                new TraitScorer(),
                new SkillScorer(),
                new QualityScorer(),
                new DamageScorer(),
                new RangeScorer(),
                new ModCompatibilityScorer(),
                new PersonaWeaponScorer()
            };

            public CompositeWeaponScorer()
            {
                // Scorers are now static and initialized once
            }

            public float GetScore(Pawn pawn, ThingWithComps weapon)
            {
                float totalScore = 0f;

                foreach (var scorer in scorers)
                {
                    float score = scorer.GetScore(pawn, weapon);

                    // Early exit for hard disqualifications
                    if (score <= -1000f)
                        return score;

                    totalScore += score;
                }

                return totalScore;
            }
        }

        public class OutfitPolicyScorer : IWeaponScorer
        {
            public float GetScore(Pawn pawn, ThingWithComps weapon)
            {
                var filter = pawn.outfits?.CurrentApparelPolicy?.filter;
                if (filter != null && !filter.Allows(weapon.def))
                    return -1000f; // Hard disqualification
                return 0f;
            }
        }

        public class TraitScorer : IWeaponScorer
        {
            public float GetScore(Pawn pawn, ThingWithComps weapon)
            {
                if (pawn.story?.traits?.HasTrait(TraitDefOf.Brawler) == true)
                {
                    if (weapon.def.IsRangedWeapon)
                        return -2000f; // Brawlers never use ranged
                    else if (weapon.def.IsMeleeWeapon)
                        return 200f; // Brawler melee bonus
                }
                return 0f;
            }
        }

        public class SkillScorer : IWeaponScorer
        {
            private const float SkillDifferenceMultiplier = 50f;
            private const float AbsoluteSkillMultiplier = 10f;
            private const float WrongTypeMultiplier = 0.3f;
            private const float RightTypeMultiplier = 1.5f;

            public float GetScore(Pawn pawn, ThingWithComps weapon)
            {
                float shootingSkill = pawn.skills?.GetSkill(SkillDefOf.Shooting)?.Level ?? 0f;
                float meleeSkill = pawn.skills?.GetSkill(SkillDefOf.Melee)?.Level ?? 0f;
                float score = 0f;

                if (weapon.def.IsRangedWeapon)
                {
                    score = CalculateRangedScore(weapon, shootingSkill, meleeSkill);
                }
                else if (weapon.def.IsMeleeWeapon)
                {
                    score = CalculateMeleeScore(weapon, shootingSkill, meleeSkill);
                }

                // Apply final multipliers based on skill match
                if (weapon.def.IsRangedWeapon && shootingSkill >= meleeSkill + 2)
                    score *= RightTypeMultiplier;
                else if (weapon.def.IsMeleeWeapon && meleeSkill >= shootingSkill + 2)
                    score *= RightTypeMultiplier;
                else if ((weapon.def.IsRangedWeapon && meleeSkill > shootingSkill + 3) ||
                         (weapon.def.IsMeleeWeapon && shootingSkill > meleeSkill + 3))
                    score *= WrongTypeMultiplier;

                return score;
            }

            private float CalculateRangedScore(ThingWithComps weapon, float shootingSkill, float meleeSkill)
            {
                float score = 0f;

                if (shootingSkill > meleeSkill)
                {
                    float skillDiff = shootingSkill - meleeSkill;
                    score += skillDiff * SkillDifferenceMultiplier;
                    score += shootingSkill * AbsoluteSkillMultiplier;
                }
                else if (meleeSkill > shootingSkill + 3)
                {
                    score -= (meleeSkill - shootingSkill) * 30f;
                }

                return score;
            }

            private float CalculateMeleeScore(ThingWithComps weapon, float shootingSkill, float meleeSkill)
            {
                float score = 0f;

                if (meleeSkill > shootingSkill)
                {
                    float skillDiff = meleeSkill - shootingSkill;
                    score += skillDiff * SkillDifferenceMultiplier;
                    score += meleeSkill * AbsoluteSkillMultiplier;
                }
                else if (shootingSkill > meleeSkill + 3)
                {
                    score -= (shootingSkill - meleeSkill) * 30f;
                }

                return score;
            }
        }

        public class DamageScorer : IWeaponScorer
        {
            public float GetScore(Pawn pawn, ThingWithComps weapon)
            {
                if (weapon.def.IsRangedWeapon)
                    return GetRangedDamageScore(weapon);
                else if (weapon.def.IsMeleeWeapon)
                    return GetMeleeDamageScore(weapon);
                return 0f;
            }

            private float GetRangedDamageScore(ThingWithComps weapon)
            {
                if (weapon.def.Verbs?.Count > 0 && weapon.def.Verbs[0] != null)
                {
                    var verb = weapon.def.Verbs[0];
                    float damage = verb.defaultProjectile?.projectile?.GetDamageAmount(weapon) ?? 0f;
                    float warmup = verb.warmupTime;
                    float cooldown = weapon.def.GetStatValueAbstract(StatDefOf.RangedWeapon_Cooldown);
                    float burstShots = verb.burstShotCount;

                    // Calculate true DPS
                    float cycleTime = warmup + cooldown + (burstShots - 1) * verb.ticksBetweenBurstShots / 60f;
                    float dps = (damage * burstShots) / cycleTime;

                    float score = dps * 15f;  // Base DPS score

                    // HUGE burst fire bonus - assault rifles have 3-round burst
                    if (burstShots > 1)
                    {
                        score *= 1.5f;  // 50% multiplier for burst weapons
                        score += burstShots * 40f;  // Additional flat bonus
                    }

                    return score;
                }
                return 0f;
            }

            private float GetMeleeDamageScore(ThingWithComps weapon)
            {
                float meleeDPS = weapon.def.GetStatValueAbstract(StatDefOf.MeleeWeapon_CooldownMultiplier);
                float meleeDamage = weapon.def.GetStatValueAbstract(StatDefOf.MeleeWeapon_DamageMultiplier);
                return (meleeDPS + meleeDamage) * 20f;
            }
        }

        public class QualityScorer : IWeaponScorer
        {
            private const float QualityMultiplier = 50f;  // Changed from 15f

            public float GetScore(Pawn pawn, ThingWithComps weapon)
            {
                if (weapon.TryGetQuality(out QualityCategory qc))
                {
                    return (int)qc * QualityMultiplier;
                }
                return 0f;
            }
        }

        public class RangeScorer : IWeaponScorer
        {
            public float GetScore(Pawn pawn, ThingWithComps weapon)
            {
                if (!weapon.def.IsRangedWeapon)
                    return 0f;

                if (weapon.def.Verbs?.Count > 0 && weapon.def.Verbs[0] != null)
                {
                    float range = weapon.def.Verbs[0].range;

                    // Optimal range is around 30 (assault rifle range)
                    if (range >= 28f && range <= 32f)
                        return 100f;  // Perfect range bonus
                    else if (range < 25f)
                        return -(25f - range) * 10f;  // Penalty for too short
                    else if (range > 35f)
                        return 50f;  // Diminishing returns for excessive range
                    else
                        return 70f;  // Good but not perfect
                }
                return 0f;
            }
        }

        public class ModCompatibilityScorer : IWeaponScorer
        {
            public float GetScore(Pawn pawn, ThingWithComps weapon)
            {
                float score = 0f;

                // Infusion 2
                if (InfusionCompat.IsLoaded())
                {
                    score += InfusionCompat.GetInfusionScoreBonus(weapon);
                }

                // Combat Extended
                if (CECompat.ShouldCheckAmmo())
                {
                    float ammoModifier = CECompat.GetAmmoScoreModifier(weapon, pawn);
                    if (ammoModifier < 1f)
                        score *= ammoModifier;
                    else
                        score += (ammoModifier - 1f) * 100f; // Convert bonus to points
                }

                return score;
            }
        }

        public class PersonaWeaponScorer : IWeaponScorer
        {
            public float GetScore(Pawn pawn, ThingWithComps weapon)
            {
                var bladelinkComp = weapon.TryGetComp<CompBladelinkWeapon>();
                if (bladelinkComp != null && bladelinkComp.CodedPawn != null)
                {
                    if (bladelinkComp.CodedPawn != pawn)
                        return -1000f; // Someone else's persona weapon
                    else
                        return 25f; // Bonus for own persona weapon
                }
                return 0f;
            }
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

    // Note: The following patches have been moved to CombinedHarmonyPatches.cs to avoid duplication:
    // - Pawn_JobTracker_TrackForcedEquip_Patch and Pawn_JobTracker_TrackForcedSidearmEquip_Patch (combined into one)
    // - Pawn_EquipmentTracker_TryDropEquipment_Patch (renamed with null checks)
    // - Pawn_EquipmentTracker_DestroyEquipment_Patch (renamed with null checks)
    // - Pawn_InventoryTracker_Notify_ItemRemoved_Patch (renamed with null checks)
    // - Pawn_EquipmentTracker_AddEquipment_Patch (renamed with null checks)
    // - Pawn_OutfitTracker_CurrentApparelPolicy_Patch (renamed with null checks)
    // - ThingFilter_SetDisallowAll_CheckWeapons_Patch (renamed with null checks)
    // - Pawn_JobTracker_EndCurrentJob_Patch (renamed with null checks)
    // - Thing_SpawnSetup_Patch (moved with null checks)
    // - Thing_DeSpawn_Patch (moved with null checks)
    // - Thing_SetPosition_Patch (moved with null checks)
    // - ThinkNode_JobGiver_TryIssueJobPackage_Patch (renamed with null checks)
}