using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace AutoArm
{
    public static class WeaponStatDefOf
    {
        public static readonly StatDef RangedWeapon_AverageDPS = DefDatabase<StatDef>.GetNamedSilentFail("RangedWeapon_AverageDPS");
    }

    public class WeaponEquipGameComponent : GameComponent
    {
        private int lastCheckTick = 0;
        private const int CheckInterval = 60; // One pawn per tick
        private static List<Thing> tmpThings = new List<Thing>(128);
        private int pawnIndex = 0;

        public WeaponEquipGameComponent(Game game) : base() { }

        public override void GameComponentTick()
        {
            base.GameComponentTick();

            if (Find.TickManager.TicksGame - lastCheckTick < CheckInterval)
                return;

            lastCheckTick = Find.TickManager.TicksGame;

            var colonists = PawnsFinder.AllMaps_FreeColonistsSpawned;
            if (colonists.Count == 0)
                return;

            pawnIndex %= colonists.Count;
            Pawn pawn = colonists[pawnIndex];
            pawnIndex++;

            // Skip if not valid or if not on a map
            if (pawn == null || pawn.Dead || !pawn.RaceProps.Humanlike)
                return;
            if (pawn.Map == null || pawn.Position == IntVec3.Invalid)
                return;
            // Skip all logic for non-combat colonists
            if (pawn.WorkTagIsDisabled(WorkTags.Violent))
                return;

            ApparelPolicy policy = pawn.outfits?.CurrentApparelPolicy;
            if (policy == null) return;

            var filter = policy.filter;
            if (filter == null) return;

            var eq = pawn.equipment?.Primary;

            // Only allow like-for-like upgrades if pawn already has a weapon
            if (eq != null && filter.Allows(eq.def))
            {
                bool lookingForMelee = eq.def.IsMeleeWeapon;
                Thing bestWeapon = null;
                float bestScore = GetWeaponScoreForPawn(pawn, eq);

                tmpThings.Clear();
                tmpThings.AddRange(GenRadial.RadialDistinctThingsAround(pawn.Position, pawn.Map, 25f, true));

                foreach (Thing t in tmpThings)
                {
                    var weapon = t as ThingWithComps;
                    if (weapon == null) continue;
                    if (weapon.def == null) continue; // Prevents null reference
                    if (!weapon.def.IsWeapon) continue;
                    if (!filter.Allows(weapon.def)) continue;
                    if (!pawn.CanReserveAndReach(weapon, PathEndMode.ClosestTouch, Danger.Deadly)) continue;
                    if (lookingForMelee != weapon.def.IsMeleeWeapon) continue;

                    float score = GetWeaponScoreForPawn(pawn, weapon);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestWeapon = weapon;
                    }
                }

                if (bestWeapon != null && (pawn.CurJob == null || pawn.CurJob.def != JobDefOf.Equip || pawn.CurJob.targetA.Thing != bestWeapon))
                {
                    var job = JobMaker.MakeJob(JobDefOf.Equip, bestWeapon);
                    pawn.jobs.TryTakeOrderedJob(job);
                }
                return;
            }

            // If pawn is unarmed, allow any weapon
            if (eq == null)
            {
                Thing bestWeapon = null;
                float bestScore = float.MinValue;

                tmpThings.Clear();
                tmpThings.AddRange(GenRadial.RadialDistinctThingsAround(pawn.Position, pawn.Map, 25f, true));

                foreach (Thing t in tmpThings)
                {
                    var weapon = t as ThingWithComps;
                    if (weapon == null) continue;
                    if (weapon.def == null) continue; // Prevents null reference
                    if (!weapon.def.IsWeapon) continue;
                    if (!filter.Allows(weapon.def)) continue;
                    if (!pawn.CanReserveAndReach(weapon, PathEndMode.ClosestTouch, Danger.Deadly)) continue;

                    float score = GetWeaponScoreForPawn(pawn, weapon);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestWeapon = weapon;
                    }
                }

                if (bestWeapon != null && (pawn.CurJob == null || pawn.CurJob.def != JobDefOf.Equip || pawn.CurJob.targetA.Thing != bestWeapon))
                {
                    var job = JobMaker.MakeJob(JobDefOf.Equip, bestWeapon);
                    pawn.jobs.TryTakeOrderedJob(job);
                }
            }
        }

        private float GetWeaponScoreForPawn(Pawn pawn, ThingWithComps weapon)
        {
            float score = 0f;

            if (weapon.TryGetQuality(out QualityCategory qc))
                score += (int)qc * 10f;

            // Prevent division by zero on broken items
            if (weapon.MaxHitPoints > 0)
                score += weapon.HitPoints / (float)weapon.MaxHitPoints * 5f;

            if (weapon.def.IsRangedWeapon && WeaponStatDefOf.RangedWeapon_AverageDPS != null)
                score += weapon.GetStatValue(WeaponStatDefOf.RangedWeapon_AverageDPS, true);
            else if (weapon.def.IsMeleeWeapon && StatDefOf.MeleeWeapon_AverageDPS != null)
                score += weapon.GetStatValue(StatDefOf.MeleeWeapon_AverageDPS, true);

            if (weapon.def.IsRangedWeapon && weapon.def.Verbs != null && weapon.def.Verbs.Count > 0)
                score += weapon.def.Verbs[0].range / 2f;

            if (pawn.WorkTagIsDisabled(WorkTags.Violent))
                score -= 1000f;
            else if (pawn.story?.traits?.HasTrait(TraitDefOf.Brawler) == true && weapon.def.IsMeleeWeapon)
                score += 20f;
            else if (pawn.story?.traits?.HasTrait(TraitDefOf.Brawler) == true && weapon.def.IsRangedWeapon)
                score -= 40f;

            if (weapon.IsForbidden(pawn))
                score -= 500f;

            return score;
        }
    }
}
