using RimWorld;
using System.Linq;
using Verse;

namespace AutoArm
{
    /// <summary>
    /// Consolidated weapon scoring to fix redundancy #5
    /// All scoring logic in one place
    /// </summary>
    public static class WeaponScoringHelper
    {
        /// <summary>
        /// Get the total score for a weapon/pawn combination
        /// </summary>
        public static float GetTotalScore(Pawn pawn, ThingWithComps weapon)
        {
            if (pawn == null || weapon == null)
                return 0f;

            float totalScore = 0f;

            // Policy and restrictions (can return -1000 to reject)
            float policyScore = GetOutfitPolicyScore(pawn, weapon);
            if (policyScore <= -1000f)
                return policyScore;
            totalScore += policyScore;

            float personaScore = GetPersonaWeaponScore(pawn, weapon);
            if (personaScore <= -1000f)
                return personaScore;
            totalScore += personaScore;

            // Pawn preferences and skills
            totalScore += GetTraitScore(pawn, weapon);
            totalScore += GetHunterScore(pawn, weapon);
            totalScore += GetSkillScore(pawn, weapon);

            // Weapon properties (cacheable) - these are already calculated in WeaponScoreCache
            // so we don't duplicate them here. WeaponScoreCache will add these scores.

            return totalScore;
        }

        private static float GetOutfitPolicyScore(Pawn pawn, ThingWithComps weapon)
        {
            var filter = pawn.outfits?.CurrentApparelPolicy?.filter;
            if (filter != null && !filter.Allows(weapon.def))
                return -1000f;
            return 0f;
        }

        private static float GetPersonaWeaponScore(Pawn pawn, ThingWithComps weapon)
        {
            var bladelinkComp = weapon.TryGetComp<CompBladelinkWeapon>();
            if (bladelinkComp != null)
            {
                // Check SimpleSidearms AllowBlockedWeaponUse setting
                if (SimpleSidearmsCompat.IsLoaded() && !SimpleSidearmsCompat.AllowBlockedWeaponUse())
                {
                    // SS doesn't allow blocked weapons - reject all persona weapons
                    return -1000f;
                }
                
                // Normal persona weapon logic
                if (bladelinkComp.CodedPawn != null)
                {
                    if (bladelinkComp.CodedPawn != pawn)
                        return -1000f;
                    else
                        return 25f;
                }
            }
            return 0f;
        }

        private static float GetTraitScore(Pawn pawn, ThingWithComps weapon)
        {
            if (pawn.story?.traits?.HasTrait(TraitDefOf.Brawler) == true)
            {
                if (weapon.def.IsRangedWeapon)
                    return -500f; // Reduced from -2000f - still avoid but better than nothing
                else if (weapon.def.IsMeleeWeapon)
                    return 200f;
            }
            return 0f;
        }

        private static float GetHunterScore(Pawn pawn, ThingWithComps weapon)
        {
            if (weapon?.def == null || pawn?.workSettings == null)
                return 0f;
                
            // Check if pawn is assigned to hunting
            if (pawn.workSettings.WorkIsActive(WorkTypeDefOf.Hunting))
            {
                // Strong preference for ranged weapons for hunters
                if (weapon.def.IsRangedWeapon)
                {
                    // Extra bonus for longer range weapons (better for hunting)
                    if (weapon.def.Verbs?.Count > 0 && weapon.def.Verbs[0] != null)
                    {
                        float range = weapon.def.Verbs[0].range;
                        if (range >= 30f) // Good hunting range
                            return 300f;
                        else if (range >= 20f) // Acceptable
                            return 200f;
                        else // Too short for safe hunting
                            return 100f;
                    }
                    return 200f; // Default ranged bonus
                }
                else if (weapon.def.IsMeleeWeapon)
                {
                    return -1000f; // Much stronger penalty - hunters must not use melee
                }
            }
            return 0f;
        }

        private static float GetSkillScore(Pawn pawn, ThingWithComps weapon)
        {
            const float SkillDifferenceMultiplier = 30f;
            const float AbsoluteSkillMultiplier = 15f;
            const float WrongTypeMultiplier = 0.5f;
            const float RightTypeMultiplier = 1.5f;

            float shootingSkill = pawn.skills?.GetSkill(SkillDefOf.Shooting)?.Level ?? 0f;
            float meleeSkill = pawn.skills?.GetSkill(SkillDefOf.Melee)?.Level ?? 0f;
            float score = 0f;

            if (weapon.def.IsRangedWeapon)
            {
                score = CalculateRangedSkillScore(weapon, shootingSkill, meleeSkill, 
                    SkillDifferenceMultiplier, AbsoluteSkillMultiplier);
            }
            else if (weapon.def.IsMeleeWeapon)
            {
                score = CalculateMeleeSkillScore(weapon, shootingSkill, meleeSkill,
                    SkillDifferenceMultiplier, AbsoluteSkillMultiplier);
            }

            // Apply type preference multiplier
            if (weapon.def.IsRangedWeapon && shootingSkill >= meleeSkill + 2)
                score *= RightTypeMultiplier;
            else if (weapon.def.IsMeleeWeapon && meleeSkill >= shootingSkill + 2)
                score *= RightTypeMultiplier;
            else if ((weapon.def.IsRangedWeapon && meleeSkill > shootingSkill + 3) ||
                     (weapon.def.IsMeleeWeapon && shootingSkill > meleeSkill + 3))
                score *= WrongTypeMultiplier;

            return score;
        }

        private static float CalculateRangedSkillScore(ThingWithComps weapon, float shootingSkill, float meleeSkill,
            float skillDiffMultiplier, float absoluteMultiplier)
        {
            float score = 0f;

            if (shootingSkill > meleeSkill)
            {
                float skillDiff = shootingSkill - meleeSkill;
                score += skillDiff * skillDiffMultiplier;
                score += shootingSkill * absoluteMultiplier;
            }
            else if (meleeSkill > shootingSkill + 3)
            {
                score -= (meleeSkill - shootingSkill) * 30f;
            }

            return score;
        }

        private static float CalculateMeleeSkillScore(ThingWithComps weapon, float shootingSkill, float meleeSkill,
            float skillDiffMultiplier, float absoluteMultiplier)
        {
            float score = 0f;

            if (meleeSkill > shootingSkill)
            {
                float skillDiff = meleeSkill - shootingSkill;
                score += skillDiff * skillDiffMultiplier;
                score += meleeSkill * absoluteMultiplier;
            }
            else if (shootingSkill > meleeSkill + 3)
            {
                score -= (shootingSkill - meleeSkill) * 30f;
            }

            return score;
        }
    }
}
