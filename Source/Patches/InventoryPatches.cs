// AutoArm RimWorld 1.5+ mod - automatic weapon management
// This file: Harmony patches for inventory management
// Handles weapon movement between equipment and inventory

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

            // SimpleSidearmsCompat simplified - can't check if remembered sidearm
            bool isSimpleSidearmsSwap = false;

            if (!isSimpleSidearmsSwap)
            {
                // Check if this specific weapon instance is forced
                if (ForcedWeaponHelper.IsForced(___pawn, weapon))
                {
                    // Remove this specific weapon from forced list
                    ForcedWeaponHelper.RemoveForcedWeapon(___pawn, weapon);
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
                // Preventing upgrade weapon in inventory
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

            // Notify tracker that weapon was picked up (cancels forced status timer)
            ForcedWeaponTracker.WeaponPickedUp(weapon);

            // SimpleSidearmsCompat simplified - pending upgrade tracking removed

            // Check if this weapon should be forced (from forced weapon upgrade)
            if (AutoArmMod.settings?.modEnabled == true &&
                AutoEquipTracker.ShouldForceWeapon(___pawn, weapon))
            {
                // Transfer forced status to the upgraded sidearm
                ForcedWeaponHelper.AddForcedSidearm(___pawn, weapon);
                AutoEquipTracker.ClearWeaponToForce(___pawn);
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.Debug($"{___pawn.LabelShort}: Transferred forced status to upgraded sidearm {weapon.Label}");
                }
            }
            // Auto-force bonded weapons when "Respect weapon bonds" is enabled
            else if (AutoArmMod.settings?.modEnabled == true &&
                AutoArmMod.settings?.respectWeaponBonds == true &&
                ModsConfig.RoyaltyActive &&
                ValidationHelper.IsWeaponBondedToPawn(weapon, ___pawn))
            {
                // Automatically mark bonded weapons as forced
                ForcedWeaponHelper.AddForcedSidearm(___pawn, weapon);
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.Debug($"{___pawn.LabelShort}: Bonded weapon {weapon.Label} in inventory - auto-forced");
                }
            }
            // Check if this was a player-forced action (for sidearms added directly to inventory)
            else if (___pawn.jobs?.curDriver?.job?.playerForced == true &&
                ___pawn.jobs.curDriver.job.def?.defName == "EquipSecondary")
            {
                ForcedWeaponHelper.AddForcedSidearm(___pawn, weapon);
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.Debug($"{___pawn.LabelShort}: Forced sidearm pickup - {weapon.Label}");
                }
            }

            // SimpleSidearmsCompat simplified - auto-equipped sidearm tracking removed

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