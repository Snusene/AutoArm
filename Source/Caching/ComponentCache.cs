
using RimWorld;
using Verse;
using AutoArm.Weapons;

namespace AutoArm.Caching
{
    /// <summary>
    /// Fast weapon component checks and quality lookups
    /// </summary>
    public static class Components
    {
        // Cache DLC status - biocode/bladelink are Royalty-only
        private static bool? _royaltyActive;
        private static bool RoyaltyActive => _royaltyActive ?? (_royaltyActive = ModsConfig.RoyaltyActive).Value;

        /// <summary>
        /// Warmup component lookups
        /// Pre-calls all common component lookups to populate RimWorld's internal comp cache.
        /// Pre-calc base score
        /// </summary>
        public static void WarmupWeapon(ThingWithComps weapon)
        {
            if (weapon == null)
                return;

            // Only warmup Royalty components if DLC is active
            if (RoyaltyActive)
            {
                weapon.TryGetComp<CompBladelinkWeapon>();
                weapon.TryGetComp<CompBiocodable>();
            }

            weapon.TryGetComp<CompQuality>();

            QualityCategory quality;
            weapon.TryGetQuality(out quality);

            WeaponScoringHelper.GetWeaponPropertyScore(null, weapon);
        }

        /// <summary>
        /// Persona weapon component
        /// </summary>
        public static CompBladelinkWeapon GetBladelink(ThingWithComps weapon)
        {
            return weapon?.TryGetComp<CompBladelinkWeapon>();
        }

        /// <summary>
        /// Checks biocode match
        /// Uses RimWorld's static helper for cleaner code
        /// </summary>
        public static bool IsBiocodedTo(ThingWithComps weapon, Pawn pawn)
        {
            if (!RoyaltyActive) return false;
            return CompBiocodable.IsBiocodedFor(weapon, pawn);
        }

        /// <summary>
        /// Checks wrong biocode
        /// </summary>
        public static bool IsBiocodedToOther(ThingWithComps weapon, Pawn pawn)
        {
            if (!RoyaltyActive) return false;
            if (!CompBiocodable.IsBiocoded(weapon))
                return false;

            return !CompBiocodable.IsBiocodedFor(weapon, pawn);
        }

        /// <summary>
        /// Checks persona weapon
        /// </summary>
        public static bool IsPersonaWeapon(ThingWithComps weapon)
        {
            if (!RoyaltyActive) return false;
            return weapon?.TryGetComp<CompBladelinkWeapon>() != null;
        }

        /// <summary>
        /// Checks persona bond
        /// Uses multiple checks for comprehensive bond detection
        /// </summary>
        public static bool IsPersonaBondedTo(ThingWithComps weapon, Pawn pawn)
        {
            if (!RoyaltyActive) return false;
            if (weapon == null || pawn == null)
                return false;

            if (pawn.equipment?.bondedWeapon == weapon)
                return true;

            if (CompBiocodable.IsBiocodedFor(weapon, pawn))
            {
                var bladelink = weapon.TryGetComp<CompBladelinkWeapon>();
                return bladelink != null;
            }

            return false;
        }

        /// <summary>
        /// Weapon quality
        /// Uses RimWorld's optimized TryGetQuality extension method
        /// </summary>
        public static bool TryGetWeaponQuality(ThingWithComps weapon, out QualityCategory quality)
        {
            quality = QualityCategory.Normal;
            if (weapon == null)
                return false;

            return weapon.TryGetQuality(out quality);
        }
    }
}
