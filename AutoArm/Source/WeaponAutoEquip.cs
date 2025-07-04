using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;
using HarmonyLib;

namespace AutoArm
{
    public class WeaponEquipGameComponent : GameComponent
    {
        private int lastCheckTick = 0;
        private const int CheckInterval = 60;
        private static List<Thing> tmpThings = new List<Thing>(128);
        private int pawnIndex = 0;

        public WeaponEquipGameComponent(Game game) : base() { }

        public override void GameComponentTick()
        {
            base.GameComponentTick();

            // Check one pawn per second
            if (Find.TickManager.TicksGame - lastCheckTick < CheckInterval)
                return;

            lastCheckTick = Find.TickManager.TicksGame;

            var colonists = PawnsFinder.AllMaps_FreeColonistsSpawned;
            if (colonists.Count == 0)
                return;

            // Cycle through colonists
            pawnIndex = pawnIndex % colonists.Count;
            Pawn pawn = colonists[pawnIndex];
            pawnIndex++;

            // Skip invalid pawns
            if (pawn?.Map == null || pawn.Dead || pawn.Downed || !pawn.Spawned)
                return;

            // Skip non-violent pawns
            if (pawn.WorkTagIsDisabled(WorkTags.Violent))
                return;

            // Skip if pawn is busy with important work
            if (pawn.CurJob != null && pawn.CurJob.def.alwaysShowWeapon)
                return;

            // Get outfit policy
            ApparelPolicy policy = pawn.outfits?.CurrentApparelPolicy;
            if (policy?.filter == null)
                return;

            TryEquipBetterWeapon(pawn, policy.filter);
        }

        private void TryEquipBetterWeapon(Pawn pawn, ThingFilter filter)
        {
            var currentWeapon = pawn.equipment?.Primary;

            // Determine weapon type preference
            bool preferMelee = ShouldPreferMelee(pawn);
            bool currentIsMelee = currentWeapon?.def.IsMeleeWeapon ?? false;

            // Calculate current weapon score
            float currentScore = currentWeapon != null ? GetWeaponScoreForPawn(pawn, currentWeapon) : -1000f;

            // Find better weapon
            Thing bestWeapon = null;
            float bestScore = currentScore;

            tmpThings.Clear();
            tmpThings.AddRange(GenRadial.RadialDistinctThingsAround(pawn.Position, pawn.Map, 25f, true));

            foreach (Thing t in tmpThings)
            {
                var weapon = t as ThingWithComps;
                if (!IsValidWeaponCandidate(weapon, pawn, filter))
                    continue;

                // Skip weapons of wrong type unless current weapon is really bad
                bool weaponIsMelee = weapon.def.IsMeleeWeapon;
                if (currentWeapon != null && currentScore > 0 && weaponIsMelee != currentIsMelee)
                    continue;

                float score = GetWeaponScoreForPawn(pawn, weapon);

                // Require significant improvement to switch
                if (score > bestScore + 5f)
                {
                    bestScore = score;
                    bestWeapon = weapon;
                }
            }

            // Equip better weapon if found
            if (bestWeapon != null && CanEquipWeapon(pawn, bestWeapon))
            {
                var job = JobMaker.MakeJob(JobDefOf.Equip, bestWeapon);
                pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
            }
        }

        private bool IsValidWeaponCandidate(ThingWithComps weapon, Pawn pawn, ThingFilter filter)
        {
            if (weapon?.def == null || !weapon.def.IsWeapon)
                return false;

            if (!filter.Allows(weapon.def))
                return false;

            if (weapon.IsForbidden(pawn))
                return false;

            if (!pawn.CanReserveAndReach(weapon, PathEndMode.ClosestTouch, Danger.Deadly))
                return false;

            return true;
        }

        private bool CanEquipWeapon(Pawn pawn, Thing weapon)
        {
            // Check if pawn is already trying to equip this weapon
            if (pawn.CurJob?.def == JobDefOf.Equip && pawn.CurJob.targetA.Thing == weapon)
                return false;

            // Check if weapon is reserved by another pawn
            if (pawn.Map.reservationManager.IsReservedByAnyoneOf(weapon, pawn.Faction))
                return false;

            return true;
        }

        private bool ShouldPreferMelee(Pawn pawn)
        {
            // Brawlers always prefer melee
            if (pawn.story?.traits?.HasTrait(TraitDefOf.Brawler) == true)
                return true;

            // Check shooting skill vs melee skill
            int shootingSkill = pawn.skills?.GetSkill(SkillDefOf.Shooting)?.Level ?? 0;
            int meleeSkill = pawn.skills?.GetSkill(SkillDefOf.Melee)?.Level ?? 0;

            return meleeSkill > shootingSkill + 3;
        }

        private float GetWeaponScoreForPawn(Pawn pawn, ThingWithComps weapon)
        {
            if (weapon?.def == null)
                return -1000f;

            float score = 0f;

            // Base damage output calculation
            if (weapon.def.IsRangedWeapon)
            {
                // Calculate ranged weapon effectiveness
                score += CalculateRangedWeaponScore(weapon);
            }
            else if (weapon.def.IsMeleeWeapon)
            {
                // Use built-in melee weapon DPS stat
                score += weapon.GetStatValue(StatDefOf.MeleeWeapon_AverageDPS) * 10f;
            }

            // Quality bonus
            if (weapon.TryGetQuality(out QualityCategory qc))
                score += (int)qc * 10f;

            // Condition penalty
            if (weapon.def.useHitPoints && weapon.MaxHitPoints > 0)
            {
                float hpPercent = (float)weapon.HitPoints / weapon.MaxHitPoints;
                score *= hpPercent;
            }

            // Skill-based scoring
            if (weapon.def.IsRangedWeapon)
            {
                int shootingLevel = pawn.skills?.GetSkill(SkillDefOf.Shooting)?.Level ?? 0;
                score += shootingLevel * 2f;

                // Brawler penalty for ranged
                if (pawn.story?.traits?.HasTrait(TraitDefOf.Brawler) == true)
                    score -= 50f;
            }
            else if (weapon.def.IsMeleeWeapon)
            {
                int meleeLevel = pawn.skills?.GetSkill(SkillDefOf.Melee)?.Level ?? 0;
                score += meleeLevel * 2f;

                // Brawler bonus for melee
                if (pawn.story?.traits?.HasTrait(TraitDefOf.Brawler) == true)
                    score += 30f;
            }

            // Distance penalty
            float distance = (weapon.Position - pawn.Position).LengthHorizontal;
            score -= distance * 0.1f;

            return score;
        }

        private float CalculateRangedWeaponScore(ThingWithComps weapon)
        {
            float score = 0f;

            // Use market value as base score
            score += weapon.MarketValue * 0.1f;

            // Get weapon verb properties for additional scoring
            var verb = weapon.def.Verbs?.FirstOrDefault();
            if (verb != null)
            {
                // Add range bonus
                score += verb.range * 0.5f;

                // Add accuracy bonus
                float accuracy = verb.accuracyMedium;
                score += accuracy * 20f;

                // Add burst shot bonus
                if (verb.burstShotCount > 1)
                {
                    score += verb.burstShotCount * 2f;
                }
            }

            return score;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref lastCheckTick, "lastCheckTick", 0);
            Scribe_Values.Look(ref pawnIndex, "pawnIndex", 0);
        }
    }
}