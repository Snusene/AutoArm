using AutoArm.Compatibility;
using AutoArm.Helpers;
using AutoArm.Jobs;
using HarmonyLib;
using RimWorld;
using Verse;

namespace AutoArm
{
    internal static class SimpleSidearmsJobDefs
    {
        public static readonly JobDef EquipSecondary = DefDatabase<JobDef>.GetNamedSilentFail("EquipSecondary");
    }

    [HarmonyPatch(typeof(Pawn_InventoryTracker), "Notify_ItemRemoved")]
    [HarmonyPatchCategory(Patches.PatchCategories.Compatibility)]
    internal static class Pawn_InventoryTracker_Notify_ItemRemoved_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(Thing item, Pawn ___pawn)
        {
            if (AutoArmMod.settings?.modEnabled != true)
                return;
            if (Current.Game == null || Current.ProgramState != ProgramState.Playing)
                return;

            try
            {
                var playerFaction = Faction.OfPlayerSilentFail;
                if (playerFaction == null || item == null || ___pawn?.Faction != playerFaction)
                    return;

                var weapon = item as ThingWithComps;
                if (weapon == null || !Validation.IsWeapon(weapon))
                    return;

                bool isBeingEquipped = ___pawn.CurJob?.def == JobDefOf.Equip &&
                                       ___pawn.CurJob?.targetA.Thing == weapon;

                if (!isBeingEquipped)
                {
                    if (ForcedWeapons.IsForced(___pawn, weapon))
                    {
                        ForcedWeaponState.MarkForcedWeaponDropped(___pawn, weapon);

                        AutoArmLogger.Debug(() => $"{___pawn.LabelShort}: Weapon {weapon.Label} removed from inventory - starting forced status grace period");
                    }
                }
            }
            catch (System.Exception e)
            {
                AutoArmLogger.ErrorPatch(e, "Pawn_InventoryTracker_Notify_ItemRemoved_Patch");
            }
        }
    }

    [HarmonyPatch(typeof(Pawn_InventoryTracker), "TryAddItemNotForSale")]
    [HarmonyPriority(Priority.High)]
    [HarmonyPatchCategory(Patches.PatchCategories.Compatibility)]
    internal static class Pawn_InventoryTracker_TryAddItemNotForSale_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Thing item, Pawn ___pawn)
        {
            if (AutoArmMod.settings?.modEnabled != true)
                return;
            if (Current.Game == null || Current.ProgramState != ProgramState.Playing)
                return;

            try
            {
                var playerFaction = Faction.OfPlayerSilentFail;
                if (playerFaction == null || item == null || ___pawn?.Faction != playerFaction)
                    return;

                var weapon = item as ThingWithComps;
                if (weapon == null || !Validation.IsWeapon(weapon))
                    return;

                if (!___pawn.inventory.innerContainer.Contains(item))
                    return;

                ForcedWeaponState.WeaponPickedUp(weapon);

                if (ForcedWeapons.IsForced(___pawn, weapon))
                {
                    ForcedWeapons.AddSidearm(___pawn, weapon);
                    AutoArmLogger.Debug(() => $"{___pawn.LabelShort}: Maintaining forced status for {weapon.Label} (ID: {weapon.thingIDNumber}) moved to inventory");
                }
                else if (AutoArmMod.settings?.modEnabled == true &&
                    AutoEquipState.ShouldForceWeapon(___pawn, weapon))
                {
                    ForcedWeapons.AddSidearm(___pawn, weapon);
                    AutoEquipState.ClearWeaponToForce(___pawn);
                    AutoArmLogger.Debug(() => $"{___pawn.LabelShort}: Transferred forced status to upgraded sidearm {weapon.Label}");
                }
                else if (AutoArmMod.settings?.modEnabled == true &&
                    AutoArmMod.settings?.respectWeaponBonds == true &&
                    ModsConfig.RoyaltyActive &&
                    ValidationHelper.IsWeaponBondedToPawn(weapon, ___pawn))
                {
                    ForcedWeapons.AddSidearm(___pawn, weapon);
                    AutoArmLogger.Debug(() => $"{___pawn.LabelShort}: Bonded weapon {weapon.Label} in inventory - auto-forced");
                }
                else if (___pawn.jobs?.curDriver?.job?.playerForced == true &&
                    ___pawn.jobs.curDriver.job.def != null &&
                    SimpleSidearmsJobDefs.EquipSecondary != null &&
                    ___pawn.jobs.curDriver.job.def == SimpleSidearmsJobDefs.EquipSecondary)
                {
                    ForcedWeapons.AddSidearm(___pawn, weapon);
                    AutoArmLogger.Debug(() => $"{___pawn.LabelShort}: Forced sidearm pickup - {weapon.Label}");
                }

            }
            catch (System.Exception e)
            {
                AutoArmLogger.ErrorPatch(e, "Pawn_InventoryTracker_TryAddItemNotForSale_Patch");
            }
        }
    }
}