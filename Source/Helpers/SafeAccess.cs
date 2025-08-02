// AutoArm RimWorld 1.5+ mod - automatic weapon management
// This file: Null-safe property access helpers
// Reduces defensive coding boilerplate while maintaining safety
// Uses: Throughout mod for safe pawn property access
// Note: Simplifies code readability without sacrificing null safety

using RimWorld;
using System.Linq;
using Verse;
using Verse.AI.Group;

namespace AutoArm
{
    /// <summary>
    /// Simplified null checking patterns (fixes #10)
    /// Reduces excessive defensive null checking while maintaining safety
    /// </summary>
    public static class SafeAccess
    {
        /// <summary>
        /// Safely get primary weapon
        /// </summary>
        public static ThingWithComps GetPrimaryWeapon(Pawn pawn)
        {
            return pawn?.equipment?.Primary;
        }

        /// <summary>
        /// Check if pawn has a valid primary weapon
        /// </summary>
        public static bool HasPrimaryWeapon(Pawn pawn)
        {
            var weapon = GetPrimaryWeapon(pawn);
            return weapon != null && WeaponValidation.IsProperWeapon(weapon);
        }

        /// <summary>
        /// Safely get outfit filter
        /// </summary>
        public static ThingFilter GetOutfitFilter(Pawn pawn)
        {
            return pawn?.outfits?.CurrentApparelPolicy?.filter;
        }

        /// <summary>
        /// Check if outfit allows a thing def
        /// </summary>
        public static bool OutfitAllows(Pawn pawn, ThingDef def)
        {
            var filter = GetOutfitFilter(pawn);
            return filter == null || filter.Allows(def);
        }

        /// <summary>
        /// Safely check if pawn can manipulate
        /// </summary>
        public static bool CanManipulate(Pawn pawn)
        {
            return pawn?.health?.capacities?.CapableOf(PawnCapacityDefOf.Manipulation) ?? false;
        }

        /// <summary>
        /// Safely get pawn skill level
        /// </summary>
        public static float GetSkillLevel(Pawn pawn, SkillDef skill)
        {
            return pawn?.skills?.GetSkill(skill)?.Level ?? 0f;
        }

        /// <summary>
        /// Safely check if pawn has trait
        /// </summary>
        public static bool HasTrait(Pawn pawn, TraitDef trait)
        {
            return pawn?.story?.traits?.HasTrait(trait) ?? false;
        }

        /// <summary>
        /// Safely check work settings
        /// </summary>
        public static bool WorkIsActive(Pawn pawn, WorkTypeDef work)
        {
            return pawn?.workSettings?.WorkIsActive(work) ?? false;
        }

        /// <summary>
        /// Safely get lord
        /// </summary>
        public static Lord GetLord(Pawn pawn)
        {
            return pawn?.GetLord();
        }

        /// <summary>
        /// Safely check royal title
        /// </summary>
        public static bool HasConceitedTitle(Pawn pawn)
        {
            if (!ModsConfig.RoyaltyActive)
                return false;
            return pawn?.royalty?.AllTitlesInEffectForReading?.Any(t => t.conceited) ?? false;
        }
    }
}