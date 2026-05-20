
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

        public static ThingDef Gun_Autopistol;
        public static ThingDef Gun_ChainShotgun;
        public static ThingDef Gun_BoltActionRifle;
        public static ThingDef Gun_AssaultRifle;

        public static ThingDef MeleeWeapon_Knife;
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
