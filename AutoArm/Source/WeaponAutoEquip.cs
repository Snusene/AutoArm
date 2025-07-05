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

    public static class ForcedWeaponTracker
    {
        private static readonly Dictionary<Pawn, ThingDef> forcedWeaponsByDef = new Dictionary<Pawn, ThingDef>();

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

        public static void Cleanup()
        {
            var toRemove = new List<Pawn>();
            foreach (var kvp in forcedWeaponsByDef)
            {
                if (kvp.Key.DestroyedOrNull())
                    toRemove.Add(kvp.Key);
            }
            foreach (var pawn in toRemove)
                forcedWeaponsByDef.Remove(pawn);
        }
    }

    public class WeaponEquipGameComponent : GameComponent
    {
        private int lastCheckTick = 0;
        private const int CheckInterval = 60;
        private int pawnIndex = 0;
        private HashSet<int> failedEquipAttempts = new HashSet<int>();
        private Dictionary<int, int> lastFailedAttemptTick = new Dictionary<int, int>();
        private const int MaxWeaponsToConsider = 50;
        private const float SearchRadiusCombat = 8f;
        private const float SearchRadiusNormal = 30f;
        private const int FailedAttemptCooldown = 2500;
        private const int MaxColonistsPerTick = 1;

        public WeaponEquipGameComponent(Game game) : base()
        {
            if (WeaponStatDefOf.RangedWeapon_AverageDPS == null)
                WeaponStatDefOf.RangedWeapon_AverageDPS = DefDatabase<StatDef>.GetNamedSilentFail("RangedWeapon_AverageDPS");
        }

        public override void GameComponentTick()
        {
            try
            {
                base.GameComponentTick();

                if (Current.Game == null || Find.Maps == null || Find.TickManager == null)
                    return;

                if (Find.TickManager.Paused || Find.TickManager.CurTimeSpeed == TimeSpeed.Ultrafast)
                    return;

                if (Find.TickManager.TicksGame < 180)
                    return;

                bool inCombat = Find.Maps.Any(m => m.attackTargetsCache.TargetsHostileToColony.Any());
                int currentInterval = inCombat ? CheckInterval * 2 : CheckInterval;
                if (Find.TickManager.TicksGame - lastCheckTick < currentInterval)
                    return;
                lastCheckTick = Find.TickManager.TicksGame;

                if (Find.TickManager.TicksGame % 1000 == 0)
                {
                    CleanupCaches();
                    ForcedWeaponTracker.Cleanup();
                }

                var colonists = PawnsFinder.AllMaps_FreeColonistsSpawned;
                if (colonists.Count == 0)
                    return;

                int colonistsProcessed = 0;
                for (int i = 0; i < colonists.Count && colonistsProcessed < MaxColonistsPerTick; i++)
                {
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
            if (IsOnCooldown(pawn))
                return false;
            if (!IsValidPawnForAutoEquip(pawn))
                return false;
            if (IsPawnBusy(pawn))
                return false;

            var forcedWeaponDef = ForcedWeaponTracker.GetForcedWeaponDef(pawn);
            if (forcedWeaponDef != null)
            {
                var currentWeapon = pawn.equipment?.Primary;
                if (currentWeapon == null || currentWeapon.def != forcedWeaponDef)
                {
                    TryEquipSpecificWeaponByDef(pawn, forcedWeaponDef);
                }
                return false;
            }

            var current = pawn.equipment?.Primary;
            var filter = pawn.outfits?.CurrentApparelPolicy?.filter;
            if (current != null && filter != null && !filter.Allows(current.def))
            {
                pawn.equipment.TryDropEquipment(current, out _, pawn.Position, forbid: false);
                return true;
            }

            TryEquipBetterWeapon(pawn);
            return true;
        }


        private void TryEquipSpecificWeaponByDef(Pawn pawn, ThingDef weaponDef)
        {
            if (weaponDef == null || pawn == null) return;

            var weapons = GenRadial.RadialDistinctThingsAround(pawn.Position, pawn.Map, 30f, true)
                .OfType<ThingWithComps>()
                .Where(w => w.def == weaponDef && IsValidWeaponCandidate(w, pawn))
                .ToList();

            if (weapons.Count == 0)
                return;

            var current = pawn.equipment?.Primary;
            if (current != null && current.def == weaponDef && weapons.Contains(current))
                return;

            var weaponToEquip = weapons.OrderByDescending(w => w.HitPoints).First();

            if (!IsAlreadyEquippingWeapon(pawn, weaponToEquip))
            {
                var job = JobMaker.MakeJob(JobDefOf.Equip, weaponToEquip);
                job.playerForced = false;
                pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
            }
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
            if (pawn.Drafted)
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

        private bool IsPawnBusy(Pawn pawn)
        {
            if (pawn.CurJob == null)
                return false;

            if (pawn.Drafted)
                return true;

            var job = pawn.CurJob.def;
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
                   pawn.CurJob.playerForced ||
                   job.driverClass == typeof(JobDriver_Flee);
        }

        private void TryEquipBetterWeapon(Pawn pawn)
        {
            var currentWeapon = pawn.equipment?.Primary;
            var forcedWeaponDef = ForcedWeaponTracker.GetForcedWeaponDef(pawn);

            float currentScore = currentWeapon != null ? GetWeaponScore(pawn, currentWeapon) : float.MinValue;
            bool currentIsMelee = currentWeapon?.def.IsMeleeWeapon ?? false;
            float improvementThreshold = currentWeapon != null ? currentScore * 1.05f : currentScore;

            bool inCombat = pawn.Map.attackTargetsCache.TargetsHostileToColony
                .Any(t => t?.Thing != null && !t.Thing.Destroyed && t.Thing.Position.DistanceTo(pawn.Position) < 40f);
            float searchRadius = inCombat ? SearchRadiusCombat : SearchRadiusNormal;

            var nearbyThings = GenRadial.RadialDistinctThingsAround(
                pawn.Position, pawn.Map, searchRadius, true);

            ThingWithComps bestWeapon = null;
            float bestScore = currentScore;
            int weaponsChecked = 0;

            foreach (var t in nearbyThings)
            {
                if (weaponsChecked >= MaxWeaponsToConsider)
                    break;

                if (t is ThingWithComps weapon && IsWeapon(weapon) && IsValidWeaponCandidate(weapon, pawn))
                {
                    if (forcedWeaponDef != null && weapon.def == forcedWeaponDef)
                        continue;

                    var biocomp = weapon.TryGetComp<CompBiocodable>();
                    if (biocomp?.Biocoded == true && biocomp.CodedPawn != pawn)
                        continue;

                    weaponsChecked++;

                    if (currentWeapon != null && weapon.def.IsMeleeWeapon != currentIsMelee)
                        continue;

                    float score = GetWeaponScore(pawn, weapon);
                    if (score > bestScore && (currentWeapon == null || score > improvementThreshold))
                    {
                        bestScore = score;
                        bestWeapon = weapon;
                    }
                }
            }

            if (bestWeapon != null && !IsAlreadyEquippingWeapon(pawn, bestWeapon))
            {
                if (bestWeapon.Destroyed || bestWeapon.Map != pawn.Map)
                    return;

                var job = JobMaker.MakeJob(JobDefOf.Equip, bestWeapon);
                job.playerForced = false;

                if (!pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc))
                {
                    MarkFailedAttempt(pawn);
                }
            }
        }

        private bool IsWeapon(ThingWithComps thing)
        {
            if (thing?.def == null)
                return false;

            if (thing.def.defName == "WoodLog" || thing.def.defName == "Steel")
                return false;

            if (!thing.def.IsWeapon)
                return false;

            if (thing.def.IsApparel)
                return false;

            if (!thing.def.IsRangedWeapon && !thing.def.IsMeleeWeapon)
                return false;

            return true;
        }

        private bool IsValidWeaponCandidate(ThingWithComps weapon, Pawn pawn)
        {
            if (weapon == null || weapon.def == null || weapon.Destroyed)
                return false;
            if (weapon.Map != pawn.Map)
                return false;
            if (!weapon.def.IsWeapon)
                return false;

            if (pawn.outfits != null)
            {
                var policy = pawn.outfits.CurrentApparelPolicy;
                if (policy?.filter != null)
                {
                    if (!policy.filter.Allows(weapon.def))
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

            if (weapon.ParentHolder is Pawn_EquipmentTracker || weapon.ParentHolder is Pawn_InventoryTracker)
                return false;

            if (weapon.IsBurning() || (weapon.HitPoints < weapon.MaxHitPoints * 0.2f))
                return false;

            return true;
        }

        private float GetWeaponScore(Pawn pawn, ThingWithComps weapon)
        {
            if (weapon?.def == null)
                return 0f;

            float score = 0f;

            if (weapon.TryGetQuality(out QualityCategory qc))
                score += (int)qc * 10f;

            if (weapon.MaxHitPoints > 0)
                score += (weapon.HitPoints / (float)weapon.MaxHitPoints) * 20f;

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

                if (weapon.def.IsRangedWeapon)
                {
                    var triggerHappy = DefDatabase<TraitDef>.GetNamedSilentFail("TriggerHappy");
                    if (triggerHappy != null && traits.HasTrait(triggerHappy))
                        score += 20f;
                }
            }

            score += weapon.MarketValue * 0.001f;

            return score;
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
            var toRemove = new List<int>();
            foreach (var kvp in lastFailedAttemptTick)
            {
                if (Find.TickManager.TicksGame - kvp.Value > FailedAttemptCooldown)
                    toRemove.Add(kvp.Key);
            }
            foreach (var id in toRemove)
            {
                failedEquipAttempts.Remove(id);
                lastFailedAttemptTick.Remove(id);
            }

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
                if (failedEquipAttempts == null)
                    failedEquipAttempts = new HashSet<int>();
                if (lastFailedAttemptTick == null)
                    lastFailedAttemptTick = new Dictionary<int, int>();
            }
        }
    }
}
