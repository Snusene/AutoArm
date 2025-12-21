using AutoArm.Helpers;
using AutoArm.Jobs;
using AutoArm.Logging;
using AutoArm.Weapons;
using HarmonyLib;
using RimWorld;
using System.Linq;
using Verse;

namespace AutoArm
{
    internal static class SimpleSidearmsJobDefs
    {
        public static readonly JobDef EquipPrimary = DefDatabase<JobDef>.GetNamedSilentFail("EquipPrimary");
        public static readonly JobDef EquipSecondary = DefDatabase<JobDef>.GetNamedSilentFail("EquipSecondary");
        public static readonly JobDef ReequipSecondary = DefDatabase<JobDef>.GetNamedSilentFail("ReequipSecondary");
        public static readonly JobDef ReequipSecondaryCombat = DefDatabase<JobDef>.GetNamedSilentFail("ReequipSecondaryCombat");
    }

    [HarmonyPatch(typeof(Pawn_InventoryTracker), "Notify_ItemRemoved")]
    [HarmonyPatchCategory(Patches.PatchCategories.Compatibility)]
    public static class Pawn_InventoryTracker_Notify_ItemRemoved_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(Thing item, Pawn ___pawn)
        {
            // Skip during world gen
            if (Current.Game == null || Current.ProgramState != ProgramState.Playing)
                return;

            try
            {
                var playerFaction = Faction.OfPlayerSilentFail;
                if (playerFaction == null || item == null || ___pawn?.Faction != playerFaction)
                    return;
            }
            catch
            {
                return;
            }

            if (AutoArmMod.settings?.modEnabled != true)
                return;

            var weapon = item as ThingWithComps;
            if (weapon == null || !WeaponValidation.IsWeapon(weapon))
                return;

            bool isSimpleSidearmsSwap = false;

            if (!isSimpleSidearmsSwap)
            {
                bool isBeingEquipped = ___pawn.CurJob?.def == JobDefOf.Equip &&
                                       ___pawn.CurJob?.targetA.Thing == weapon;

                bool isSSSwapping = ___pawn.CurJob?.def != null &&
                                    SimpleSidearmsJobDefs.EquipPrimary != null &&
                                    ___pawn.CurJob.def == SimpleSidearmsJobDefs.EquipPrimary &&
                                    ___pawn.CurJob?.targetA.Thing == weapon;

                if (!isBeingEquipped && !isSSSwapping)
                {
                    if (ForcedWeapons.IsForced(___pawn, weapon))
                    {
                        ForcedWeaponState.MarkForcedWeaponDropped(___pawn, weapon);

                        AutoArmLogger.Debug(() => $"{___pawn.LabelShort}: Weapon {weapon.Label} removed from inventory - starting forced status grace period");
                    }
                }
            }
        }
    }

    [HarmonyPatch(typeof(Pawn_InventoryTracker), "TryAddItemNotForSale")]
    [HarmonyPriority(Priority.High)]
    [HarmonyPatchCategory(Patches.PatchCategories.Compatibility)]
    public static class Pawn_InventoryTracker_TryAddItemNotForSale_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Thing item, Pawn ___pawn)
        {
            // Skip during world gen
            if (Current.Game == null || Current.ProgramState != ProgramState.Playing)
                return;

            try
            {
                var playerFaction = Faction.OfPlayerSilentFail;
                if (playerFaction == null || item == null || ___pawn?.Faction != playerFaction)
                    return;
            }
            catch
            {
                return;
            }

            var weapon = item as ThingWithComps;
            if (weapon == null || !WeaponValidation.IsWeapon(weapon))
                return;

            if (!___pawn.inventory.innerContainer.Contains(item))
                return;

            ForcedWeaponState.WeaponPickedUp(weapon);

            var curJobDef = ___pawn.CurJob?.def;
            if (curJobDef != null &&
                !(___pawn.CurJob?.playerForced ?? false) &&
                (curJobDef == SimpleSidearmsJobDefs.EquipSecondary ||
                 curJobDef == SimpleSidearmsJobDefs.ReequipSecondary ||
                 curJobDef == SimpleSidearmsJobDefs.ReequipSecondaryCombat))
            {
                EquipCooldownTracker.Record(___pawn);
            }

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

            if (___pawn.CurJob?.def != null &&
                SimpleSidearmsJobDefs.EquipSecondary != null &&
                ___pawn.CurJob.def == SimpleSidearmsJobDefs.EquipSecondary &&
                AutoArmMod.settings?.showNotifications == true &&
                PawnUtility.ShouldSendNotificationAbout(___pawn))
            {
                ThingWithComps oldWeapon = null;
                var innerContainer = ___pawn.inventory?.innerContainer;
                if (innerContainer != null)
                {
                    for (int i = 0; i < innerContainer.Count; i++)
                    {
                        var invItem = innerContainer[i];
                        if (invItem is ThingWithComps twc &&
                            twc != weapon &&
                            twc.def == weapon.def &&
                            WeaponValidation.IsWeapon(twc))
                        {
                            oldWeapon = twc;
                            break;
                        }
                    }
                }

                if (oldWeapon != null)
                {
                    Messages.Message("AutoArm_UpgradedSidearm".Translate(
                        ___pawn.LabelShort.CapitalizeFirst(),
                        oldWeapon.Label ?? oldWeapon.def?.label ?? "old sidearm",
                        weapon.Label ?? weapon.def?.label ?? "new sidearm"
                    ), new LookTargets(___pawn), MessageTypeDefOf.SilentInput, false);
                }
                else
                {
                    Messages.Message("AutoArm_EquippedSidearm".Translate(
                        ___pawn.LabelShort.CapitalizeFirst(),
                        weapon.Label ?? weapon.def?.label ?? "sidearm"
                    ), new LookTargets(___pawn), MessageTypeDefOf.SilentInput, false);
                }
            }
        }
    }
}