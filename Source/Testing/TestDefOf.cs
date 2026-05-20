
using RimWorld;
using Verse;

namespace AutoArm.Testing
{
    [DefOf]
    public static class TestDefOf
    {
        static TestDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(TestDefOf));
        }

        public static ThingDef Gun_Revolver;
        public static ThingDef Gun_MachinePistol;
        public static ThingDef Gun_PumpShotgun;
        public static ThingDef Gun_SniperRifle;
        public static ThingDef Gun_ChargeRifle;
        public static ThingDef Gun_HeavySMG;
        public static ThingDef Gun_LMG;
        public static ThingDef Gun_Minigun;

        public static ThingDef MeleeWeapon_Club;
        public static ThingDef MeleeWeapon_Mace;
        public static ThingDef MeleeWeapon_Gladius;
    }
}
