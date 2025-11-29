
using RimWorld;
using Verse;

namespace AutoArm.Definitions
{
    [DefOf]
    public static class AutoArmDefOf
    {
        static AutoArmDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(AutoArmDefOf));
        }

        public static ThingCategoryDef Weapons;

        public static ThingCategoryDef Apparel;

        public static ThingDef ElephantTusk;

        public static ThingDef ThrumboHorn;

        public static ThingDef Gun_Revolver;

        public static ThingDef Gun_Autopistol;
        public static ThingDef Gun_MachinePistol;
        public static ThingDef Gun_PumpShotgun;
        public static ThingDef Gun_ChainShotgun;
        public static ThingDef Gun_BoltActionRifle;
        public static ThingDef Gun_AssaultRifle;
        public static ThingDef Gun_SniperRifle;
        public static ThingDef Gun_ChargeRifle;
        public static ThingDef Gun_HeavySMG;
        public static ThingDef Gun_LMG;
        public static ThingDef Gun_Minigun;

        public static ThingDef MeleeWeapon_Knife;

        public static ThingDef MeleeWeapon_Club;
        public static ThingDef MeleeWeapon_Mace;
        public static ThingDef MeleeWeapon_Gladius;
        public static ThingDef MeleeWeapon_LongSword;

        public static JobDef AutoArmSwapPrimary;
        public static JobDef AutoArmSwapSidearm;

        [MayRequire("PeteTimesSix.SimpleSidearms")]
        public static JobDef EquipSecondary;

        [MayRequire("PeteTimesSix.SimpleSidearms")]
        public static JobDef ReequipSecondary;

        [MayRequire("PeteTimesSix.SimpleSidearms")]
        public static JobDef ReequipSecondaryCombat;


        [MayRequire("Mehni.PickUpAndHaul")]
        public static JobDef HaulToInventory;

        [MayRequire("Mehni.PickUpAndHaul")]
        public static JobDef UnloadYourHauledInventory;

        public static JobDef UnloadYourInventory;

    }

}
