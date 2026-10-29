
using RimWorld;
using Verse;

namespace AutoArm.Caching
{
    internal static class Components
    {
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

        public static bool IsUnique(ThingWithComps weapon)
        {
            return weapon.TryGetComp<CompUniqueWeapon>() != null;
        }
    }
}
