
using RimWorld;
using Verse;

namespace AutoArm.Caching
{
    internal static class Components
    {
        public static CompBladelinkWeapon GetBladelink(ThingWithComps weapon)
        {
            return weapon?.TryGetComp<CompBladelinkWeapon>();
        }

        public static bool IsBiocodedTo(ThingWithComps weapon, Pawn pawn)
        {
            return CompBiocodable.IsBiocodedFor(weapon, pawn);
        }

        public static bool IsBiocodedToOther(ThingWithComps weapon, Pawn pawn)
        {
            if (!CompBiocodable.IsBiocoded(weapon))
                return false;

            return !CompBiocodable.IsBiocodedFor(weapon, pawn);
        }

        public static bool IsPersonaWeapon(ThingWithComps weapon)
        {
            if (!ModsConfig.RoyaltyActive) return false;
            return weapon?.TryGetComp<CompBladelinkWeapon>() != null;
        }

        public static bool IsPersonaBondedTo(ThingWithComps weapon, Pawn pawn)
        {
            if (!ModsConfig.RoyaltyActive) return false;
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

        public static bool TryGetWeaponQuality(ThingWithComps weapon, out QualityCategory quality)
        {
            quality = QualityCategory.Normal;
            if (weapon == null)
                return false;

            return weapon.TryGetQuality(out quality);
        }
    }
}
