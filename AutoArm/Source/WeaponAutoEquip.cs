using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace AutoArm
{
    public static class WeaponStatDefOf
    {
        public static StatDef RangedWeapon_AverageDPS;

        static WeaponStatDefOf()
        {
            LongEventHandler.ExecuteWhenFinished(() => {
                RangedWeapon_AverageDPS = DefDatabase<StatDef>.GetNamedSilentFail("RangedWeapon_AverageDPS");
            });
        }
    }

    public class WeaponEquipGameComponent : GameComponent
    {
        private int lastCheckTick = 0;
        private const int CheckInterval = 60;
        private Dictionary<int, List<Thing>> mapThingsCache = new Dictionary<int, List<Thing>>();
        private int pawnIndex = 0;
        private HashSet<int> failedEquipAttempts = new HashSet<int>();
        private Dictionary<int, int> lastFailedAttemptTick = new Dictionary<int, int>();

        // Performance settings
        private const int MaxWeaponsToConsider = 50;
        private const float SearchRadiusCombat = 8f;
        private const float SearchRadiusNormal = 15f;
        private const int FailedAttemptCooldown = 2500; // ~1 game hour
        private const int MaxColonistsPerTick = 1; // Process at most 1 colonist per tick

        public WeaponEquipGameComponent(Game game) : base() { }

        public override void GameComponentTick()
        {
            try
            {
                base.GameComponentTick();

                // Skip during loading or if game systems not ready
                if (Current.Game == null || Find.Maps == null || Find.TickManager == null)
                    return;

                // Skip if game is paused or in special state
                if (Find.TickManager.Paused || Find.TickManager.CurTimeSpeed == TimeSpeed.Ultrafast)
                    return;

                // During raids, slow down checks
                bool inCombat = Find.Maps.Any(m => m.attackTargetsCache.TargetsHostileToColony.Any());
                int currentInterval = inCombat ? CheckInterval * 2 : CheckInterval;

                if (Find.TickManager.TicksGame - lastCheckTick < currentInterval)
                    return;

                lastCheckTick = Find.TickManager.TicksGame;

                // Clean up caches periodically
                if (Find.TickManager.TicksGame % 1000 == 0)
                {
                    CleanupCaches();
                }

                var colonists = PawnsFinder.AllMaps_FreeColonistsSpawned;
                if (colonists.Count == 0)
                    return;

                // Process limited number of colonists to prevent lag spikes
                int colonistsProcessed = 0;
                for (int i = 0; i < colonists.Count && colonistsProcessed < MaxColonistsPerTick; i++)
                {
                    // Ensure valid index
                    pawnIndex = pawnIndex % colonists.Count;
                    Pawn pawn = colonists[pawnIndex];
                    pawnIndex = (pawnIndex + 1) % colonists.Count;

                    if (ProcessPawn(pawn))
                        colonistsProcessed++;
                }
            }
            catch (Exception e)
            {
                Log.ErrorOnce($"[AutoArm] Error in GameComponentTick: {e}", 8475632);
            }
        }

        private bool ProcessPawn(Pawn pawn)
        {
            // Skip if pawn recently failed to equip
            if (IsOnCooldown(pawn))
                return false;

            // Comprehensive validation
            if (!IsValidPawnForAutoEquip(pawn))
                return false;

            // Skip if pawn is busy
            if (IsPawnBusy(pawn))
                return false;

            // Check apparel policy
            ApparelPolicy policy = pawn.outfits?.CurrentApparelPolicy;
            if (policy?.filter == null)
                return false;

            TryEquipBetterWeapon(pawn, policy.filter);
            return true;
        }

        private bool IsValidPawnForAutoEquip(Pawn pawn)
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

            // Check manipulation capacity
            if (pawn.health?.capacities == null || !pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation))
                return false;

            // Don't equip while in bed, in caravan, or doing ceremonies
            if (pawn.InBed() || pawn.GetCaravan() != null || pawn.GetLord() != null)
                return false;

            // Child check for Biotech
            if (ModsConfig.BiotechActive && pawn.ageTracker != null && pawn.ageTracker.AgeBiologicalYears < 13)
                return false;

            // Royalty title restrictions
            if (ModsConfig.RoyaltyActive && pawn.royalty?.AllTitlesInEffectForReading?.Any(t => t.conceited) == true)
            {
                // Nobles might have weapon restrictions
                if (pawn.equipment?.Primary != null)
                    return false; // Don't auto-switch weapons for conceited nobles
            }

            return true;
        }

        private bool IsPawnBusy(Pawn pawn)
        {
            if (pawn.CurJob == null)
                return false;

            var job = pawn.CurJob.def;

            // Comprehensive job check
            return job == JobDefOf.TendPatient ||
                   job == JobDefOf.Rescue ||
                   job == JobDefOf.ExtinguishSelf ||
                   job == JobDefOf.FleeAndCower ||
                   job == JobDefOf.Flee ||
                   job == JobDefOf.AttackMelee ||
                   job == JobDefOf.AttackStatic ||
                   job == JobDefOf.Wait_Combat ||
                   job == JobDefOf.Equip ||
                   job == JobDefOf.DropEquipment ||
                   job == JobDefOf.GiveToPackAnimal ||
                   job == JobDefOf.EnterTransporter ||
                   job.playerInterruptible == false ||
                   job.alwaysShowWeapon ||
                   pawn.CurJob.playerForced || // Don't interrupt player-forced jobs
                   (pawn.jobs?.curDriver?.layingDown ?? false) || // Sleeping/resting
                   job.driverClass == typeof(JobDriver_Flee);
        }

        private void TryEquipBetterWeapon(Pawn pawn, ThingFilter filter)
        {
            var currentWeapon = pawn.equipment?.Primary;

            // Check if current weapon is biocoded to someone else
            if (currentWeapon?.TryGetComp<CompBiocodable>()?.Biocoded == true &&
                currentWeapon.TryGetComp<CompBiocodable>().CodedPawn != pawn)
            {
                // Pawn is holding someone else's biocoded weapon, should drop it
                return;
            }

            float searchRadius = GetSearchRadius(pawn);

            // Get cached things list for this map
            var mapId = pawn.Map.uniqueID;
            if (!mapThingsCache.ContainsKey(mapId))
                mapThingsCache[mapId] = new List<Thing>(256);

            var tmpThings = mapThingsCache[mapId];
            tmpThings.Clear();

            // Collect nearby weapons
            var nearbyThings = GenRadial.RadialDistinctThingsAround(
                pawn.Position, pawn.Map, searchRadius, true);

            int weaponCount = 0;
            foreach (Thing t in nearbyThings)
            {
                if (weaponCount >= MaxWeaponsToConsider)
                    break;

                if (t is ThingWithComps twc && IsWeapon(twc))
                {
                    tmpThings.Add(t);
                    weaponCount++;
                }
            }

            Thing bestWeapon = null;
            float bestScore = currentWeapon != null ? GetWeaponScore(pawn, currentWeapon) : float.MinValue;
            bool currentIsMelee = currentWeapon?.def.IsMeleeWeapon ?? false;

            foreach (Thing t in tmpThings)
            {
                var weapon = t as ThingWithComps;
                if (!IsValidWeaponCandidate(weapon, pawn, filter))
                    continue;

                // If armed, only consider same weapon type
                if (currentWeapon != null && weapon.def.IsMeleeWeapon != currentIsMelee)
                    continue;

                float score = GetWeaponScore(pawn, weapon);

                // Minimum improvement threshold
                float improvementThreshold = currentWeapon != null ? bestScore * 1.15f : bestScore;

                if (score > improvementThreshold)
                {
                    bestScore = score;
                    bestWeapon = weapon;
                }
            }

            tmpThings.Clear();

            // Try to equip better weapon
            if (bestWeapon != null && !IsAlreadyEquippingWeapon(pawn, bestWeapon))
            {
                // Final validation
                if (bestWeapon.Destroyed || bestWeapon.Map != pawn.Map)
                    return;

                var job = JobMaker.MakeJob(JobDefOf.Equip, bestWeapon);

                if (!pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc))
                {
                    // Job failed - add to cooldown
                    MarkFailedAttempt(pawn);
                }
            }
        }

        private bool IsWeapon(ThingWithComps thing)
        {
            if (thing?.def == null)
                return false;

            // Basic weapon check
            if (!thing.def.IsWeapon)
                return false;

            // Skip apparel that looks like weapons (shields, etc)
            if (thing.def.IsApparel)
                return false;

            return true;
        }

        private bool IsValidWeaponCandidate(ThingWithComps weapon, Pawn pawn, ThingFilter filter)
        {
            if (weapon == null || weapon.def == null || weapon.Destroyed)
                return false;

            if (weapon.Map != pawn.Map)
                return false;

            if (!filter.Allows(weapon.def))
                return false;

            if (weapon.IsForbidden(pawn))
                return false;

            // Biocoding check
            var biocomp = weapon.TryGetComp<CompBiocodable>();
            if (biocomp?.Biocoded == true && biocomp.CodedPawn != pawn)
                return false;

            // Quest item check
            if (weapon.questTags != null && weapon.questTags.Count > 0)
                return false;

            // Check reservation
            var reservationManager = pawn.Map.reservationManager;
            if (reservationManager.IsReservedByAnyoneOf(weapon, pawn.Faction) &&
                !reservationManager.IsReservedBy(weapon, pawn))
                return false;

            if (!pawn.CanReserveAndReach(weapon, PathEndMode.ClosestTouch, Danger.Deadly))
                return false;

            // Container and holder checks
            if (weapon.ParentHolder != null)
                return false;

            // Condition checks
            if (weapon.IsBurning() || (weapon.HitPoints < weapon.MaxHitPoints * 0.2f))
                return false;

            // Ideology restrictions (if Ideology DLC is active)
            if (ModsConfig.IdeologyActive && pawn.Ideo != null)
            {
                // Check if weapon is allowed by ideology
                var primaryRole = pawn.Ideo.GetRole(pawn);
                if (primaryRole?.apparelRequirements?.Any(req => req.requirement is ApparelRequirement_DisallowWeaponKind dwk &&
                    dwk.weaponKind == weapon.def.weaponTags?.FirstOrDefault()) == true)
                {
                    return false;
                }
            }

            return true;
        }

        private float GetWeaponScore(Pawn pawn, ThingWithComps weapon)
        {
            if (weapon?.def == null)
                return 0f;

            float score = 0f;

            // Quality bonus
            if (weapon.TryGetQuality(out QualityCategory qc))
                score += (int)qc * 10f;

            // Condition bonus
            if (weapon.MaxHitPoints > 0)
                score += (weapon.HitPoints / (float)weapon.MaxHitPoints) * 20f;

            // Skill-based scoring
            if (weapon.def.IsRangedWeapon)
            {
                float shootingSkill = pawn.skills?.GetSkill(SkillDefOf.Shooting)?.Level ?? 0f;
                score += shootingSkill * 2f;

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

                // Range bonus
                var verbs = weapon.def.Verbs;
                if (verbs?.Count > 0 && verbs[0] != null)
                    score += verbs[0].range * 0.5f;
            }
            else if (weapon.def.IsMeleeWeapon)
            {
                float meleeSkill = pawn.skills?.GetSkill(SkillDefOf.Melee)?.Level ?? 0f;
                score += meleeSkill * 2f;

                if (StatDefOf.MeleeWeapon_AverageDPS != null)
                {
                    try
                    {
                        float dps = weapon.GetStatValue(StatDefOf.MeleeWeapon_AverageDPS, true, -1);
                        if (dps > 0)
                            score += dps * 5f;
                    }
                    catch { }
                }
            }

            // Trait modifiers
            var traits = pawn.story?.traits;
            if (traits != null)
            {
                if (traits.HasTrait(TraitDefOf.Brawler))
                {
                    if (weapon.def.IsMeleeWeapon)
                        score += 50f;
                    else
                        score -= 100f;
                }

                // Shooting accuracy trait bonus for ranged weapons
                if (weapon.def.IsRangedWeapon && traits.HasTrait(TraitDef.Named("ShootingAccuracy")))
                {
                    score += 20f;
                }
            }

            // Market value as minor tiebreaker
            score += weapon.MarketValue * 0.001f;

            return score;
        }

        private float GetSearchRadius(Pawn pawn)
        {
            // Check for nearby hostiles
            var hostilesNearby = pawn.Map.attackTargetsCache.TargetsHostileToColony
                .Where(t => t?.Thing != null && !t.Thing.Destroyed)
                .Any(t => t.Thing.Position.DistanceTo(pawn.Position) < 40f);

            return hostilesNearby ? SearchRadiusCombat : SearchRadiusNormal;
        }

        private bool IsAlreadyEquippingWeapon(Pawn pawn, Thing weapon)
        {
            return pawn.CurJob != null &&
                   pawn.CurJob.def == JobDefOf.Equip &&
                   pawn.CurJob.targetA.Thing == weapon;
        }

        private bool IsOnCooldown(Pawn pawn)
        {
            if (!lastFailedAttemptTick.ContainsKey(pawn.thingIDNumber))
                return false;

            return Find.TickManager.TicksGame - lastFailedAttemptTick[pawn.thingIDNumber] < FailedAttemptCooldown;
        }

        private void MarkFailedAttempt(Pawn pawn)
        {
            failedEquipAttempts.Add(pawn.thingIDNumber);
            lastFailedAttemptTick[pawn.thingIDNumber] = Find.TickManager.TicksGame;
        }

        private void CleanupCaches()
        {
            // Clean up failed attempts
            var toRemove = new List<int>();
            foreach (var kvp in lastFailedAttemptTick)
            {
                if (Find.TickManager.TicksGame - kvp.Value > FailedAttemptCooldown)
                {
                    toRemove.Add(kvp.Key);
                }
            }

            foreach (var id in toRemove)
            {
                failedEquipAttempts.Remove(id);
                lastFailedAttemptTick.Remove(id);
            }

            // Clean up map caches for destroyed maps
            var mapsToRemove = mapThingsCache.Keys.Where(id => !Find.Maps.Any(m => m.uniqueID == id)).ToList();
            foreach (var mapId in mapsToRemove)
            {
                mapThingsCache.Remove(mapId);
            }

            // Limit cache sizes
            if (failedEquipAttempts.Count > 100)
            {
                failedEquipAttempts.Clear();
                lastFailedAttemptTick.Clear();
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref pawnIndex, "autoArm_pawnIndex", 0);
            Scribe_Values.Look(ref lastCheckTick, "autoArm_lastCheckTick", 0);
            Scribe_Collections.Look(ref lastFailedAttemptTick, "autoArm_failedAttempts", LookMode.Value, LookMode.Value);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (mapThingsCache == null)
                    mapThingsCache = new Dictionary<int, List<Thing>>();
                if (failedEquipAttempts == null)
                    failedEquipAttempts = new HashSet<int>();
                if (lastFailedAttemptTick == null)
                    lastFailedAttemptTick = new Dictionary<int, int>();
            }
        }
    }
}