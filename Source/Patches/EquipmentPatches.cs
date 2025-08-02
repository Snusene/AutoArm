// AutoArm RimWorld 1.5+ mod - automatic weapon management
// This file: Harmony patches for equipment management
// Handles weapon equipping, dropping, and forced weapon tracking

using HarmonyLib;
using RimWorld;
using System;
using System.Linq;
using Verse;
using Verse.AI;

namespace AutoArm
{
    // IMPORTANT: Outfit filter rules only apply to "proper weapons" that appear in the filter UI.
    // Items like wood logs, beer bottles, etc. that have IsWeapon=true but are excluded from the
    // weapon filter system are NOT subject to outfit filter dropping. This prevents colonists from
    // dropping items that can't even be configured in the filter UI.
    [HarmonyPatch(typeof(Pawn_JobTracker), "StartJob")]
    [HarmonyPriority(Priority.High)]
    public static class Pawn_JobTracker_StartJob_Combined_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(Job newJob, Pawn ___pawn)
        {
            if (newJob == null || ___pawn == null)
                return;

            // Check if mod is enabled
            if (AutoArmMod.settings?.modEnabled != true)
                return;

            if (!___pawn.IsColonist || ___pawn.Destroyed)
                return;

            if (newJob.def == JobDefOf.Equip && newJob.targetA.Thing is ThingWithComps targetWeapon)
            {
                if (targetWeapon != null && WeaponValidation.IsProperWeapon(targetWeapon))
                {
                    // Check if this is interrupting a sidearm upgrade
                    if (SimpleSidearmsCompat.HasPendingUpgrade(___pawn))
                    {
                        var upgradeInfo = SimpleSidearmsCompat.GetPendingUpgrade(___pawn);
                        if (upgradeInfo != null && targetWeapon != upgradeInfo.newWeapon && newJob.playerForced)
                        {
                            // Player is manually forcing a different weapon during upgrade
                            SimpleSidearmsCompat.CancelPendingUpgrade(___pawn);

                            AutoArmLogger.LogPawn(___pawn, "Cancelling sidearm upgrade - player forced different weapon");
                        }
                    }

                    // Debug logging to track all equip jobs - only if debug logging is enabled
                    if (AutoArmMod.settings?.debugLogging == true)
                    {
                        AutoArmLogger.LogWeapon(___pawn, targetWeapon, $"Starting equip job:\n  - playerForced: {newJob.playerForced}\n  - interaction: {newJob.interaction}");
                    }

                    // Only mark as forced if player explicitly forced it via right-click
                    // But skip if this is part of a sidearm upgrade
                    // FIXED: More precise check - only skip if this is ACTUALLY part of a sidearm upgrade
                    bool isPartOfSidearmUpgrade = false;
                    if (SimpleSidearmsCompat.IsLoaded() && AutoArmMod.settings?.autoEquipSidearms == true)
                    {
                        // Check if pawn has a pending upgrade and this job is targeting the upgrade weapon
                        if (SimpleSidearmsCompat.HasPendingUpgrade(___pawn))
                        {
                            var upgradeInfo = SimpleSidearmsCompat.GetPendingUpgrade(___pawn);
                            if (upgradeInfo != null && upgradeInfo.newWeapon == targetWeapon)
                            {
                                isPartOfSidearmUpgrade = true;
                                AutoArmLogger.LogWeapon(___pawn, targetWeapon, "Detected actual sidearm upgrade job");
                            }
                        }
                    }

                    if (newJob.playerForced && !isPartOfSidearmUpgrade)
                    {
                        ForcedWeaponHelper.SetForced(___pawn, targetWeapon);

                        AutoArmLogger.LogWeapon(___pawn, targetWeapon, "FORCED: Manually equipping");
                    }
                    else if (isPartOfSidearmUpgrade)
                    {
                        AutoArmLogger.LogWeapon(___pawn, targetWeapon, "Not marking as forced - part of sidearm upgrade");
                    }
                }
            }
            else if (SimpleSidearmsCompat.IsLoaded() &&
                     newJob.def?.defName == "EquipSecondary" &&
                     newJob.targetA.Thing is ThingWithComps sidearmWeapon &&
                     WeaponValidation.IsProperWeapon(sidearmWeapon))
            {
                // Only mark as forced if player explicitly forced it
                if (newJob.playerForced)
                {
                    ForcedWeaponHelper.AddForcedDef(___pawn, sidearmWeapon.def);

                    AutoArmLogger.LogWeapon(___pawn, sidearmWeapon, "FORCED SIDEARM: Manually equipped as sidearm");
                }
            }
        }

        [HarmonyPostfix]
        public static void Postfix(Job newJob, Pawn ___pawn)
        {
            if (newJob == null || ___pawn == null)
                return;

            if (newJob.def == JobDefOf.Equip && AutoEquipTracker.IsAutoEquip(newJob))
            {
                AutoArmLogger.LogPawn(___pawn, $"Starting equip job for {newJob.targetA.Thing?.Label}");
            }
        }
    }

    [HarmonyPatch(typeof(Pawn_EquipmentTracker), "TryDropEquipment")]
    public static class Pawn_EquipmentTracker_TryDropEquipment_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(bool __result, Pawn ___pawn, ThingWithComps resultingEq)
        {
            if (!__result || ___pawn == null || !___pawn.IsColonist || resultingEq == null)
                return;

            // Check if mod is enabled
            if (AutoArmMod.settings?.modEnabled != true)
                return;

            try
            {
                // Check if this is a same-type weapon upgrade (should never happen if forced)
                bool isSameTypeUpgrade = DroppedItemTracker.IsPendingSameTypeUpgrade(resultingEq);

                // Always clear forced status when weapon is dropped (downed, manual drop, etc.)
                // The only exception is SimpleSidearms swaps which aren't real drops
                bool isSimpleSidearmsSwap = false;
                if (SimpleSidearmsCompat.IsLoaded())
                {
                    // Check if this is a remembered sidearm being dropped
                    isSimpleSidearmsSwap = SimpleSidearmsCompat.IsRememberedSidearm(___pawn, resultingEq);

                    // ALSO check if pawn is currently doing a SimpleSidearms job
                    if (!isSimpleSidearmsSwap && ___pawn.CurJob != null)
                    {
                        // Check for EquipSecondary job (SimpleSidearms swap)
                        isSimpleSidearmsSwap = ___pawn.CurJob.def?.defName == "EquipSecondary";

                        if (isSimpleSidearmsSwap)
                        {
                            AutoArmLogger.LogWeapon(___pawn, resultingEq, "Detected SimpleSidearms swap via EquipSecondary job");
                        }
                    }

                    // ALSO check if this is part of a pending sidearm upgrade
                    if (!isSimpleSidearmsSwap && SimpleSidearmsCompat.HasPendingUpgrade(___pawn))
                    {
                        isSimpleSidearmsSwap = true;
                        AutoArmLogger.LogWeapon(___pawn, resultingEq, "Detected SimpleSidearms swap via pending upgrade");
                    }

                    // ALSO check if SimpleSidearms swap is in progress (covers UI swaps)
                    if (!isSimpleSidearmsSwap && DroppedItemTracker.IsSimpleSidearmsSwapInProgress(___pawn))
                    {
                        isSimpleSidearmsSwap = true;
                        AutoArmLogger.LogWeapon(___pawn, resultingEq, "Detected SimpleSidearms swap via swap-in-progress flag");
                    }
                }

                if (!isSimpleSidearmsSwap)
                {
                    // Weapon was dropped (leaves pawn's possession) - clear forced status for THIS weapon only
                    if (ForcedWeaponHelper.GetForcedPrimary(___pawn) == resultingEq)
                    {
                        // Only clear the primary forced weapon reference, don't clear all forced defs
                        ForcedWeaponHelper.ClearForcedPrimary(___pawn);
                        AutoArmLogger.LogWeapon(___pawn, resultingEq, "Primary weapon dropped - cleared primary forced status");
                    }

                    // If this weapon type is no longer carried, remove it from forced defs
                    if (ForcedWeaponHelper.IsWeaponDefForced(___pawn, resultingEq.def))
                    {
                        // Check if pawn still has any weapons of this type
                        bool stillHasThisType = false;

                        // Check primary (after drop it would be null or different)
                        if (___pawn.equipment?.Primary?.def == resultingEq.def)
                            stillHasThisType = true;

                        // Check inventory
                        if (!stillHasThisType)
                        {
                            foreach (var item in ___pawn.inventory?.innerContainer ?? Enumerable.Empty<Thing>())
                            {
                                if (item is ThingWithComps invWeapon && invWeapon.def == resultingEq.def)
                                {
                                    stillHasThisType = true;
                                    break;
                                }
                            }
                        }

                        if (!stillHasThisType)
                        {
                            ForcedWeaponHelper.RemoveForcedDef(___pawn, resultingEq.def);
                            AutoArmLogger.LogWeapon(___pawn, resultingEq, "Last weapon of this type dropped - removed from forced defs");
                        }
                    }
                }
                else
                {
                    // SimpleSidearms swap - weapon stays in inventory, maintain forced status
                    AutoArmLogger.LogWeapon(___pawn, resultingEq, "SimpleSidearms swap detected - maintaining forced status");

                    // Extra check: if this was a forced weapon, ensure it stays forced
                    if (ForcedWeaponHelper.GetForcedPrimary(___pawn) == resultingEq)
                    {
                        AutoArmLogger.LogWeapon(___pawn, resultingEq, "Confirmed: Forced weapon being swapped via SimpleSidearms");
                    }
                }

                // Handle same-type upgrades (shouldn't happen with forced weapons)
                if (isSameTypeUpgrade)
                {
                    DroppedItemTracker.ClearPendingUpgrade(resultingEq);
                    AutoArmLogger.LogWeapon(___pawn, resultingEq, "Same-type weapon upgrade drop");
                    DroppedItemTracker.MarkAsDropped(resultingEq, 1200);

                    if (SimpleSidearmsCompat.IsLoaded())
                    {
                        SimpleSidearmsCompat.InformOfDroppedSidearm(___pawn, resultingEq);
                    }
                }
            }
            catch (Exception e)
            {
                Log.Error($"[AutoArm] Error in TryDropEquipment patch: {e}");
            }
        }
    }

    [HarmonyPatch(typeof(Pawn_EquipmentTracker), "DestroyEquipment")]
    public static class Pawn_EquipmentTracker_DestroyEquipment_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn ___pawn)
        {
            if (___pawn == null || !___pawn.IsColonist)
                return;

            // Check if mod is enabled
            if (AutoArmMod.settings?.modEnabled != true)
                return;

            ForcedWeaponHelper.ClearForced(___pawn);
        }
    }

    [HarmonyPatch(typeof(Pawn_EquipmentTracker), "AddEquipment")]
    public static class Pawn_EquipmentTracker_AddEquipment_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(ThingWithComps newEq, Pawn ___pawn)
        {
            if (newEq == null || ___pawn == null)
                return true;

            // Prevent equipping items from inventory if already equipped
            if (___pawn.equipment?.Primary != null &&
                ___pawn.inventory?.innerContainer?.Contains(newEq) == true)
            {
                AutoArmLogger.LogPawn(___pawn, $"WARNING: Prevented equipping {newEq.Label ?? "item"} from inventory while already equipped");
                return false;
            }

            return true;
        }

        [HarmonyPostfix]
        public static void Postfix(ThingWithComps newEq, Pawn ___pawn)
        {
            if (newEq == null || ___pawn == null || !___pawn.IsColonist)
                return;

            // Auto-force bonded weapons when "Respect weapon bonds" is enabled
            if (AutoArmMod.settings?.modEnabled == true &&
                AutoArmMod.settings?.respectWeaponBonds == true && 
                ModsConfig.RoyaltyActive && 
                ValidationHelper.IsWeaponBondedToPawn(newEq, ___pawn))
            {
                // Automatically mark bonded weapons as forced
                ForcedWeaponHelper.SetForced(___pawn, newEq);
                AutoArmLogger.LogWeapon(___pawn, newEq, "Bonded weapon equipped - automatically marked as forced");
            }
            // Check if this weapon type was already forced as a sidearm
            else if (AutoArmMod.settings?.modEnabled == true && 
                     ForcedWeaponHelper.IsWeaponDefForced(___pawn, newEq.def))
            {
                // Maintain forced status when sidearm becomes primary
                ForcedWeaponHelper.SetForced(___pawn, newEq);
                AutoArmLogger.LogWeapon(___pawn, newEq, "Sidearm moved to primary - maintaining forced status");
            }
            // Only mark as forced if this is completing a player-forced equip job
            else if (AutoArmMod.settings?.modEnabled == true &&
                     ___pawn.CurJob?.def == JobDefOf.Equip && ___pawn.CurJob.playerForced)
            {
                // This was a manual equip - mark as forced
                ForcedWeaponHelper.SetForced(___pawn, newEq);

                AutoArmLogger.LogWeapon(___pawn, newEq, "Manually equipped - marking as forced");
            }

            // Then handle auto-equip notifications
            if (AutoArmMod.settings?.modEnabled == true &&
                ___pawn.CurJob?.def == JobDefOf.Equip && AutoEquipTracker.IsAutoEquip(___pawn.CurJob))
            {
                if (PawnUtility.ShouldSendNotificationAbout(___pawn) &&
                    AutoArmMod.settings?.showNotifications == true)
                {
                    var previousWeapon = AutoEquipTracker.GetPreviousWeapon(___pawn);

                    if (previousWeapon != null)
                    {
                        Messages.Message("AutoArm_UpgradedWeapon".Translate(
                            ___pawn.LabelShort.CapitalizeFirst(),
                            previousWeapon.label ?? "previous weapon",
                            newEq.Label ?? newEq.def?.label ?? "new weapon"
                        ), new LookTargets(___pawn), MessageTypeDefOf.SilentInput, false);
                    }
                    else
                    {
                        Messages.Message("AutoArm_EquippedWeapon".Translate(
                            ___pawn.LabelShort.CapitalizeFirst(),
                            newEq.Label ?? newEq.def?.label ?? "weapon"
                        ), new LookTargets(___pawn), MessageTypeDefOf.SilentInput, false);
                    }
                }

                AutoEquipTracker.Clear(___pawn.CurJob);
                AutoEquipTracker.ClearPreviousWeapon(___pawn);

                // Only log success if debug logging is enabled
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.LogWeapon(___pawn, newEq, "Successfully equipped, clearing job tracking");
                }
            }
        }
    }

    [HarmonyPatch(typeof(Pawn_JobTracker), "EndCurrentJob")]
    public static class Pawn_JobTracker_EndCurrentJob_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(Pawn ___pawn, JobCondition condition, Job ___curJob)
        {
            if (___pawn == null || ___curJob == null)
                return;

            // Check if mod is enabled
            if (AutoArmMod.settings?.modEnabled != true)
                return;

            // Check for failed equip jobs due to mod restrictions
            if (___curJob.def == JobDefOf.Equip && ___curJob.targetA.Thing is ThingWithComps weapon)
            {
                // Check if job failed with an error condition
                if (condition == JobCondition.Errored || condition == JobCondition.Incompletable)
                {
                    // Check if this was an auto-equip job that failed
                    if (AutoEquipTracker.IsAutoEquip(___curJob))
                    {
                        // When auto-equip jobs fail, it's usually due to mod restrictions
                        string errorReason = "mod restriction";

                        // Check the weapon name for hints about the restriction
                        if (weapon.def.defName.Contains("Mech") ||
                            weapon.def.defName.Contains("Marine") ||
                            weapon.def.defName.Contains("Power") ||
                            weapon.def.defName.Contains("Heavy") ||
                            weapon.def.defName.Contains("Exo") ||
                            weapon.GetStatValue(StatDefOf.Mass) > 5.0f)
                        {
                            // Don't blacklist body size restrictions - pawn might get power armor later
                            errorReason = "probable body size restriction";
                            AutoArmLogger.LogPawn(___pawn, $"Not blacklisting {weapon.Label} - {errorReason} (pawn size: {___pawn.BodySize:F1})");
                        }
                        else
                        {
                            // Blacklist this weapon for this pawn
                            WeaponBlacklist.AddToBlacklist(weapon.def, ___pawn, errorReason);
                            AutoArmLogger.LogPawn(___pawn, $"Blacklisted {weapon.Label} due to failed equip job - {errorReason}");
                        }
                    }
                }
            }

            // Clean up auto-equip jobs when they end
            if (___curJob.def == JobDefOf.Equip && AutoEquipTracker.IsAutoEquip(___curJob))
            {
                // Only clear if job actually completed or failed
                if (condition != JobCondition.Ongoing &&
                    condition != JobCondition.QueuedNoLongerValid)
                {
                    AutoEquipTracker.Clear(___curJob);

                    AutoArmLogger.LogPawn(___pawn, $"Cleared completed equip job for {___curJob.targetA.Thing?.Label} - Reason: {condition}");
                }
                else
                {
                    AutoArmLogger.LogPawn(___pawn, $"Ending equip job for {___curJob.targetA.Thing?.Label} - Reason: {condition}");
                }
            }

            // Check if this was part of a sidearm upgrade
            if (___curJob.def == JobDefOf.Equip && SimpleSidearmsCompat.HasPendingUpgrade(___pawn))
            {
                if (condition == JobCondition.Succeeded)
                {
                    // Upgrade completed successfully
                    SimpleSidearmsCompat.CancelPendingUpgrade(___pawn);
                }
                else if (condition != JobCondition.Ongoing)
                {
                    // Upgrade failed, clean up
                    SimpleSidearmsCompat.CancelPendingUpgrade(___pawn);

                    AutoArmLogger.LogPawn(___pawn, $"WARNING: Sidearm upgrade cancelled due to job failure: {condition}");
                }
            }
        }
    }
}
