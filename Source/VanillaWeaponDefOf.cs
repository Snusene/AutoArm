using Verse;

namespace AutoArm
{
    public static class VanillaWeaponDefOf
    {
        public static ThingDef Gun_Revolver => DefDatabase<ThingDef>.GetNamedSilentFail("Gun_Revolver");
        public static ThingDef Gun_Autopistol => DefDatabase<ThingDef>.GetNamedSilentFail("Gun_Autopistol");
        public static ThingDef Gun_MachinePistol => DefDatabase<ThingDef>.GetNamedSilentFail("Gun_MachinePistol");
        public static ThingDef Gun_PumpShotgun => DefDatabase<ThingDef>.GetNamedSilentFail("Gun_PumpShotgun");
        public static ThingDef Gun_ChainShotgun => DefDatabase<ThingDef>.GetNamedSilentFail("Gun_ChainShotgun");
        public static ThingDef Gun_BoltActionRifle => DefDatabase<ThingDef>.GetNamedSilentFail("Gun_BoltActionRifle");
        public static ThingDef Gun_AssaultRifle => DefDatabase<ThingDef>.GetNamedSilentFail("Gun_AssaultRifle");
        public static ThingDef Gun_SniperRifle => DefDatabase<ThingDef>.GetNamedSilentFail("Gun_SniperRifle");
        public static ThingDef Gun_ChargeRifle => DefDatabase<ThingDef>.GetNamedSilentFail("Gun_ChargeRifle");

        public static ThingDef MeleeWeapon_Knife => DefDatabase<ThingDef>.GetNamedSilentFail("MeleeWeapon_Knife");
        public static ThingDef MeleeWeapon_Club => DefDatabase<ThingDef>.GetNamedSilentFail("MeleeWeapon_Club");
        public static ThingDef MeleeWeapon_Mace => DefDatabase<ThingDef>.GetNamedSilentFail("MeleeWeapon_Mace");
        public static ThingDef MeleeWeapon_Gladius => DefDatabase<ThingDef>.GetNamedSilentFail("MeleeWeapon_Gladius");
        public static ThingDef MeleeWeapon_LongSword => DefDatabase<ThingDef>.GetNamedSilentFail("MeleeWeapon_LongSword");
    }
}