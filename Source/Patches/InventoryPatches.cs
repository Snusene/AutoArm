// AutoArm RimWorld 1.5+ mod - automatic weapon management
// This file: Harmony patches for inventory management
// Handles weapon movement between equipment and inventory

using HarmonyLib;
using RimWorld;
using System;
using System.Linq;
using Verse;

namespace AutoArm
{
    [HarmonyPatch(typeof(Pawn_InventoryTracker), "Notify_ItemRemoved")]
    public static class Pawn_InventoryTracker_Notify_ItemRemoved_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(Thing item, Pawn ___pawn)
        {
            if (item == null || ___pawn == null || !___pawn.IsColonist)
                return;

            // Check if mod is enabled
            if (AutoArmMod.settings?.modEnabled != true)
                return;

            var weapon = item as ThingWithComps;
            if (weapon == null || !WeaponValidation.IsProperWeapon(weapon))
                return;

            // SimpleSidearms compatibility - maintain forced status during swaps
            bool isSimpleSidearmsSwap = SimpleSidearmsCompat.IsLoaded() &&
                                        SimpleSidearmsCompat.IsRememberedSidearm(___pawn, weapon);

            if (!isSimpleSidearmsSwap)
            {
                // Weapon removed from inventory - clear forced status if it's the last one
                if (ForcedWeaponHelper.IsWeaponDefForced(___pawn, weapon.def))
                {
                    // Count remaining weapons of this type
                    int sameTypeCount = 0;

                    // Check primary
                    if (___pawn.equipment?.Primary?.def == weapon.def)
                        sameTypeCount++;

                    // Check inventory (excluding the one being removed)
                    foreach (var invItem in ___pawn.inventory?.innerContainer ?? Enumerable.Empty<Thing>())
                    {
                        if (invItem != weapon && invItem is ThingWithComps invWeapon && invWeapon.def == weapon.def)
                            sameTypeCount++;
                    }

                    if (sameTypeCount == 0)
                    {
                        ForcedWeaponHelper.RemoveForcedDef(___pawn, weapon.def);
                        AutoArmLogger.LogWeapon(___pawn, weapon, "Last forced weapon removed from inventory - cleared forced status");
                    }
                }
            }
        }
    }

    // IMPORTANT: Sidearm upgrade functionality has been moved to SimpleSidearmsUpgradePatch.cs
    // The new approach uses a simpler swap method instead of complex toil injection

    [HarmonyPatch(typeof(Pawn_InventoryTracker), "TryAddItemNotForSale")]
    [HarmonyPriority(Priority.High)] // Run before SimpleSidearms
    public static class Pawn_InventoryTracker_TryAddItemNotForSale_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(Thing item, Pawn ___pawn)
        {
            // Check if mod is enabled
            if (AutoArmMod.settings?.modEnabled != true)
                return true;

            // Check if this weapon is marked for same-type upgrade drop
            if (item is ThingWithComps weapon && DroppedItemTracker.IsPendingSameTypeUpgrade(weapon))
            {
                // Prevent adding to inventory - let it drop to ground
                AutoArmLogger.LogWeapon(___pawn, weapon, "Preventing same-type upgrade weapon from going to inventory");
                DroppedItemTracker.ClearPendingUpgrade(weapon);
                return false; // Skip original method
            }
            return true; // Continue to original method
        }

        [HarmonyPostfix]
        public static void Postfix(Thing item, Pawn ___pawn)
        {
            if (item == null || ___pawn == null || !___pawn.IsColonist)
                return;

            var weapon = item as ThingWithComps;
            if (weapon == null || !WeaponValidation.IsProperWeapon(weapon))
                return;

            // Note: Sidearm drop handling is now in SidearmDropFix.cs

            if (!___pawn.inventory.innerContainer.Contains(item))
                return;

            // Clear any pending upgrade flag
            SimpleSidearmsCompat.CancelPendingUpgrade(___pawn);

            // Auto-force bonded weapons when "Respect weapon bonds" is enabled
            if (AutoArmMod.settings?.modEnabled == true &&
                AutoArmMod.settings?.respectWeaponBonds == true && 
                ModsConfig.RoyaltyActive && 
                ValidationHelper.IsWeaponBondedToPawn(weapon, ___pawn))
            {
                // Automatically mark bonded weapons as forced
                ForcedWeaponHelper.AddForcedDef(___pawn, weapon.def);
                AutoArmLogger.LogWeapon(___pawn, weapon, "Bonded weapon added to inventory - automatically marked as forced");
            }
            // Check if this was a player-forced action (for sidearms added directly to inventory)
            else if (___pawn.jobs?.curDriver?.job?.playerForced == true &&
                ___pawn.jobs.curDriver.job.def?.defName == "EquipSecondary")
            {
                ForcedWeaponHelper.AddForcedDef(___pawn, weapon.def);
                AutoArmLogger.LogWeapon(___pawn, weapon, "Player forced sidearm pickup - marked as forced");
            }

            if (___pawn.CurJob?.def?.defName == "EquipSecondary" &&
                AutoArmMod.settings?.showNotifications == true &&
                PawnUtility.ShouldSendNotificationAbout(___pawn))
            {
                var existingSidearms = ___pawn.inventory?.innerContainer
                    .OfType<ThingWithComps>()
                    .Where(t => WeaponValidation.IsProperWeapon(t) && t != weapon && t.def == weapon.def)
                    .ToList();

                if (existingSidearms?.Any() == true)
                {
                    var oldWeapon = existingSidearms.First();

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
