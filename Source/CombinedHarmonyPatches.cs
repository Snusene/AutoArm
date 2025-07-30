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

                            AutoArmDebug.LogPawn(___pawn, "Cancelling sidearm upgrade - player forced different weapon");
                        }
                    }

                    // Debug logging to track all equip jobs - only if debug logging is enabled
                    if (AutoArmMod.settings?.debugLogging == true)
                    {
                        AutoArmDebug.LogWeapon(___pawn, targetWeapon, $"Starting equip job:\n  - playerForced: {newJob.playerForced}\n  - interaction: {newJob.interaction}");
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
                                AutoArmDebug.LogWeapon(___pawn, targetWeapon, "Detected actual sidearm upgrade job");
                            }
                        }
                    }

                    if (newJob.playerForced && !isPartOfSidearmUpgrade)
                    {
                        ForcedWeaponHelper.SetForced(___pawn, targetWeapon);

                        AutoArmDebug.LogWeapon(___pawn, targetWeapon, "FORCED: Manually equipping");
                    }
                    else if (isPartOfSidearmUpgrade)
                    {
                        AutoArmDebug.LogWeapon(___pawn, targetWeapon, "Not marking as forced - part of sidearm upgrade");
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

                    AutoArmDebug.LogWeapon(___pawn, sidearmWeapon, "FORCED SIDEARM: Manually equipped as sidearm");
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
                AutoArmDebug.LogPawn(___pawn, $"Starting equip job for {newJob.targetA.Thing?.Label}");
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
                            AutoArmDebug.LogWeapon(___pawn, resultingEq, "Detected SimpleSidearms swap via EquipSecondary job");
                        }
                    }

                    // ALSO check if this is part of a pending sidearm upgrade
                    if (!isSimpleSidearmsSwap && SimpleSidearmsCompat.HasPendingUpgrade(___pawn))
                    {
                        isSimpleSidearmsSwap = true;
                        AutoArmDebug.LogWeapon(___pawn, resultingEq, "Detected SimpleSidearms swap via pending upgrade");
                    }

                    // ALSO check if SimpleSidearms swap is in progress (covers UI swaps)
                    if (!isSimpleSidearmsSwap && DroppedItemTracker.IsSimpleSidearmsSwapInProgress(___pawn))
                    {
                        isSimpleSidearmsSwap = true;
                        AutoArmDebug.LogWeapon(___pawn, resultingEq, "Detected SimpleSidearms swap via swap-in-progress flag");
                    }
                }

                if (!isSimpleSidearmsSwap)
                {
                    // Weapon was dropped (leaves pawn's possession) - clear forced status for THIS weapon only
                    if (ForcedWeaponHelper.GetForcedPrimary(___pawn) == resultingEq)
                    {
                        // Only clear the primary forced weapon reference, don't clear all forced defs
                        ForcedWeaponHelper.ClearForcedPrimary(___pawn);
                        AutoArmDebug.LogWeapon(___pawn, resultingEq, "Primary weapon dropped - cleared primary forced status");
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
                            AutoArmDebug.LogWeapon(___pawn, resultingEq, "Last weapon of this type dropped - removed from forced defs");
                        }
                    }
                }
                else
                {
                    // SimpleSidearms swap - weapon stays in inventory, maintain forced status
                    AutoArmDebug.LogWeapon(___pawn, resultingEq, "SimpleSidearms swap detected - maintaining forced status");

                    // Extra check: if this was a forced weapon, ensure it stays forced
                    if (ForcedWeaponHelper.GetForcedPrimary(___pawn) == resultingEq)
                    {
                        AutoArmDebug.LogWeapon(___pawn, resultingEq, "Confirmed: Forced weapon being swapped via SimpleSidearms");
                    }
                }

                // Handle same-type upgrades (shouldn't happen with forced weapons)
                if (isSameTypeUpgrade)
                {
                    DroppedItemTracker.ClearPendingUpgrade(resultingEq);
                    AutoArmDebug.LogWeapon(___pawn, resultingEq, "Same-type weapon upgrade drop");
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

            ForcedWeaponHelper.ClearForced(___pawn);
        }
    }

    [HarmonyPatch(typeof(Pawn_InventoryTracker), "Notify_ItemRemoved")]
    public static class Pawn_InventoryTracker_Notify_ItemRemoved_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(Thing item, Pawn ___pawn)
        {
            if (item == null || ___pawn == null || !___pawn.IsColonist)
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
                        AutoArmDebug.LogWeapon(___pawn, weapon, "Last forced weapon removed from inventory - cleared forced status");
                    }
                }
            }
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
                AutoArmDebug.LogPawn(___pawn, $"WARNING: Prevented equipping {newEq.Label ?? "item"} from inventory while already equipped");
                return false;
            }

            return true;
        }

        [HarmonyPostfix]
        public static void Postfix(ThingWithComps newEq, Pawn ___pawn)
        {
            if (newEq == null || ___pawn == null || !___pawn.IsColonist)
                return;

            // Check if this weapon type was already forced as a sidearm
            if (ForcedWeaponHelper.IsWeaponDefForced(___pawn, newEq.def))
            {
                // Maintain forced status when sidearm becomes primary
                ForcedWeaponHelper.SetForced(___pawn, newEq);
                AutoArmDebug.LogWeapon(___pawn, newEq, "Sidearm moved to primary - maintaining forced status");
            }
            // Only mark as forced if this is completing a player-forced equip job
            else if (___pawn.CurJob?.def == JobDefOf.Equip && ___pawn.CurJob.playerForced)
            {
                // This was a manual equip - mark as forced
                ForcedWeaponHelper.SetForced(___pawn, newEq);

                AutoArmDebug.LogWeapon(___pawn, newEq, "Manually equipped - marking as forced");
            }

            // Then handle auto-equip notifications
            if (___pawn.CurJob?.def == JobDefOf.Equip && AutoEquipTracker.IsAutoEquip(___pawn.CurJob))
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
                    AutoArmDebug.LogWeapon(___pawn, newEq, "Successfully equipped, clearing job tracking");
                }
            }
        }
    }

    [HarmonyPatch(typeof(Pawn_OutfitTracker), "CurrentApparelPolicy", MethodType.Setter)]
    public static class Pawn_OutfitTracker_CurrentApparelPolicy_Setter_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn ___pawn)
        {
            if (___pawn == null || AutoArmMod.settings?.modEnabled != true)
                return;

            if (!___pawn.IsColonist || !___pawn.Spawned || ___pawn.Dead || ___pawn.Drafted)
                return;

            // Don't interfere if pawn is hauling something
            if (___pawn.CurJob?.def == JobDefOf.HaulToCell ||
                ___pawn.CurJob?.def == JobDefOf.HaulToContainer ||
                ___pawn.CurJob?.def?.defName?.Contains("Haul") == true ||
                // Pick Up And Haul specific jobs
                ___pawn.CurJob?.def?.defName == "HaulToInventory" ||
                ___pawn.CurJob?.def?.defName == "UnloadYourHauledInventory" ||
                // Other inventory-related jobs
                ___pawn.CurJob?.def?.defName?.Contains("Inventory") == true ||
                ___pawn.CurJob?.def?.defName == "UnloadYourInventory" ||
                ___pawn.CurJob?.def?.defName == "TakeToInventory")
            {
                AutoArmDebug.LogPawn(___pawn, "Not checking weapons - pawn is hauling");
                return;
            }

            // Check primary weapon
            if (___pawn.equipment?.Primary != null && ___pawn.jobs != null)
            {
                // Don't interfere with sidearm upgrades in progress
                if (SimpleSidearmsCompat.IsLoaded() && AutoArmMod.settings?.autoEquipSidearms == true &&
                    SimpleSidearmsCompat.PawnHasTemporarySidearmEquipped(___pawn))
                {
                    AutoArmDebug.LogPawn(___pawn, "Not dropping weapon due to outfit change - sidearm upgrade in progress");
                    return;
                }

                // Don't force temporary colonists (quest pawns) to drop their weapons
                if (JobGiverHelpers.IsTemporaryColonist(___pawn))
                {
                    AutoArmDebug.LogPawn(___pawn, "Not dropping weapon due to outfit change - temporary colonist");
                    return;
                }

                var filter = ___pawn.outfits?.CurrentApparelPolicy?.filter;
                if (filter != null)
                {
                    // Only check filter for proper weapons that appear in the UI
                    if (!WeaponValidation.IsProperWeapon(___pawn.equipment.Primary))
                    {
                        // Not a proper weapon (wood log, beer, etc.) - don't apply filter rules
                        AutoArmDebug.LogWeapon(___pawn, ___pawn.equipment.Primary,
                            "Not a proper weapon - ignoring outfit filter");
                        return;
                    }

                    bool weaponAllowed = filter.Allows(___pawn.equipment.Primary);
                    AutoArmDebug.LogWeapon(___pawn, ___pawn.equipment.Primary,
                        $"Outfit filter check - Weapon allowed: {weaponAllowed}, HP: {___pawn.equipment.Primary.HitPoints}/{___pawn.equipment.Primary.MaxHitPoints} ({(float)___pawn.equipment.Primary.HitPoints / ___pawn.equipment.Primary.MaxHitPoints * 100f:F0}%)");

                    if (!weaponAllowed)
                    {
                        // Check if this weapon is forced - if so, keep it equipped
                        if (ForcedWeaponHelper.IsForced(___pawn, ___pawn.equipment.Primary))
                        {
                            // ALWAYS keep forced weapons regardless of outfit filter
                            AutoArmDebug.LogWeapon(___pawn, ___pawn.equipment.Primary, "Outfit changed but keeping forced weapon (forced weapons bypass outfit filters)");
                            return; // Don't drop forced weapons
                        }

                        // Check if pawn is in a ritual - don't drop ritual items during ceremonies
                        if (ValidationHelper.IsInRitual(___pawn))
                        {
                            AutoArmDebug.LogWeapon(___pawn, ___pawn.equipment.Primary, "Outfit changed but keeping item - pawn is in ritual");
                            return; // Don't drop items during rituals
                        }

                        ForcedWeaponHelper.ClearForced(___pawn);

                        // Mark weapon as dropped to prevent immediate re-pickup
                        DroppedItemTracker.MarkAsDropped(___pawn.equipment.Primary, 600); // 10 second cooldown

                        var dropJob = new Job(JobDefOf.DropEquipment, ___pawn.equipment.Primary);
                        ___pawn.jobs.TryTakeOrderedJob(dropJob, JobTag.Misc);

                        AutoArmDebug.LogWeapon(___pawn, ___pawn.equipment.Primary, "Outfit changed, dropping weapon");

                        if (PawnUtility.ShouldSendNotificationAbout(___pawn) && AutoArmMod.settings?.showNotifications == true)
                        {
                            Messages.Message("AutoArm_DroppingDisallowed".Translate(
                                ___pawn.LabelShort.CapitalizeFirst(),
                                ___pawn.equipment.Primary.Label ?? ___pawn.equipment.Primary.def?.label ?? "weapon"
                            ), new LookTargets(___pawn), MessageTypeDefOf.SilentInput, false);
                        }
                    }
                }
            }

            // Also check sidearms when outfit changes
            CheckAndDropDisallowedSidearms(___pawn);

            // Schedule an additional check next tick to ensure all weapons are dropped
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                if (___pawn != null && ___pawn.Spawned && !___pawn.Dead)
                {
                    CheckAndDropDisallowedSidearms(___pawn);
                }
            });

            if (Pawn_TickRare_Unified_Patch.lastWeaponSearchTick.ContainsKey(___pawn))
            {
                Pawn_TickRare_Unified_Patch.lastWeaponSearchTick.Remove(___pawn);
            }
        }

        public static void CheckAndDropDisallowedSidearms(Pawn pawn)
        {
            if (!SimpleSidearmsCompat.IsLoaded() || pawn?.inventory?.innerContainer == null)
                return;

            // Don't interfere if pawn is hauling something
            if (pawn.CurJob?.def == JobDefOf.HaulToCell ||
                pawn.CurJob?.def == JobDefOf.HaulToContainer ||
                pawn.CurJob?.def?.defName?.Contains("Haul") == true ||
                // Pick Up And Haul specific jobs
                pawn.CurJob?.def?.defName == "HaulToInventory" ||
                pawn.CurJob?.def?.defName == "UnloadYourHauledInventory" ||
                // Other inventory-related jobs
                pawn.CurJob?.def?.defName?.Contains("Inventory") == true ||
                pawn.CurJob?.def?.defName == "UnloadYourInventory" ||
                pawn.CurJob?.def?.defName == "TakeToInventory")
            {
                return;
            }

            var filter = pawn.outfits?.CurrentApparelPolicy?.filter;
            if (filter == null)
                return;

            var weaponsToCheck = pawn.inventory.innerContainer
                .OfType<ThingWithComps>()
                .Where(t => t.def.IsWeapon)
                .ToList();

            foreach (var weapon in weaponsToCheck)
            {
                // Only check filter for proper weapons that appear in the UI
                if (!WeaponValidation.IsProperWeapon(weapon))
                {
                    continue; // Skip non-proper weapons like wood logs, beer bottles, etc.
                }

                if (!filter.Allows(weapon))
                {
                    // Check if this is a forced weapon - don't drop forced weapons
                    if (ForcedWeaponHelper.IsWeaponDefForced(pawn, weapon.def))
                    {
                        continue;
                    }

                    // Drop the weapon
                    Thing droppedWeapon;
                    if (pawn.inventory.innerContainer.TryDrop(weapon, pawn.Position, pawn.Map,
                        ThingPlaceMode.Near, out droppedWeapon))
                    {
                        if (droppedWeapon != null)
                        {
                            // Mark it as dropped so it won't be immediately picked up again
                            SimpleSidearmsCompat.MarkWeaponAsRecentlyDropped(droppedWeapon);
                            // Additional marking with longer cooldown to prevent pickup loops
                            DroppedItemTracker.MarkAsDropped(droppedWeapon, 1200); // 20 seconds

                            AutoArmDebug.LogPawn(pawn, $"Dropped disallowed sidearm: {weapon.Label}");
                        }
                    }
                }
            }
        }
    }

    [HarmonyPatch(typeof(ThingFilter), "SetDisallowAll")]
    public static class ThingFilter_SetDisallowAll_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ThingFilter __instance)
        {
            if (__instance == null || AutoArmMod.settings?.modEnabled != true)
                return;

            if (Current.Game == null || Find.Maps == null)
                return;

            var pawnsToDropWeapons = new System.Collections.Generic.List<(Pawn pawn, ThingWithComps weapon)>();

            foreach (var map in Find.Maps)
            {
                if (map?.mapPawns?.FreeColonists == null)
                    continue;

                foreach (var pawn in map.mapPawns.FreeColonists)
                {
                    if (pawn == null || pawn.Drafted || pawn.jobs == null)
                        continue;

                    // Don't interfere if pawn is hauling something
                    if (pawn.CurJob?.def == JobDefOf.HaulToCell ||
                        pawn.CurJob?.def == JobDefOf.HaulToContainer ||
                        pawn.CurJob?.def?.defName?.Contains("Haul") == true ||
                        // Pick Up And Haul specific jobs
                        pawn.CurJob?.def?.defName == "HaulToInventory" ||
                        pawn.CurJob?.def?.defName == "UnloadYourHauledInventory" ||
                        // Other inventory-related jobs
                        pawn.CurJob?.def?.defName?.Contains("Inventory") == true ||
                        pawn.CurJob?.def?.defName == "UnloadYourInventory" ||
                        pawn.CurJob?.def?.defName == "TakeToInventory")
                    {
                        continue;
                    }

                    if (pawn.outfits?.CurrentApparelPolicy?.filter == __instance &&
                        pawn.equipment?.Primary != null &&
                        WeaponValidation.IsProperWeapon(pawn.equipment.Primary)) // Only drop proper weapons that appear in filter UI
                    {
                        pawnsToDropWeapons.Add((pawn, pawn.equipment.Primary));
                    }
                }
            }

            if (pawnsToDropWeapons.Count > 0)
            {
                LongEventHandler.ExecuteWhenFinished(() =>
                {
                    foreach (var (pawn, weapon) in pawnsToDropWeapons)
                    {
                        if (pawn?.equipment?.Primary == weapon && !pawn.Drafted && pawn.jobs != null)
                        {
                            // Don't interfere with sidearm upgrades in progress
                            if (SimpleSidearmsCompat.IsLoaded() && AutoArmMod.settings?.autoEquipSidearms == true &&
                                SimpleSidearmsCompat.PawnHasTemporarySidearmEquipped(pawn))
                            {
                                AutoArmDebug.LogPawn(pawn, "Not dropping weapon - sidearm upgrade in progress");
                                continue;
                            }

                            // Don't force temporary colonists (quest pawns) to drop their weapons
                            if (JobGiverHelpers.IsTemporaryColonist(pawn))
                            {
                                AutoArmDebug.LogPawn(pawn, "Not dropping weapon - temporary colonist");
                                continue;
                            }

                            // Check if this weapon is forced - if so, keep it equipped
                            if (ForcedWeaponHelper.IsForced(pawn, weapon))
                            {
                                AutoArmDebug.LogWeapon(pawn, weapon, "All items disallowed, but keeping forced weapon");
                                continue; // Skip this pawn, don't drop forced weapons
                            }

                            // Check if pawn is in a ritual - don't drop ritual items during ceremonies
                            if (ValidationHelper.IsInRitual(pawn))
                            {
                                AutoArmDebug.LogWeapon(pawn, weapon, "All items disallowed, but keeping item - pawn is in ritual");
                                continue; // Don't drop items during rituals
                            }

                            ForcedWeaponHelper.ClearForced(pawn);

                            var dropJob = new Job(JobDefOf.DropEquipment, weapon);
                            pawn.jobs.TryTakeOrderedJob(dropJob, JobTag.Misc);

                            AutoArmDebug.LogPawn(pawn, "All items disallowed, dropping weapon");
                        }
                    }
                });
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
                            AutoArmDebug.LogPawn(___pawn, $"Not blacklisting {weapon.Label} - {errorReason} (pawn size: {___pawn.BodySize:F1})");
                        }
                        else
                        {
                            // Blacklist this weapon for this pawn
                            WeaponBlacklist.AddToBlacklist(weapon.def, ___pawn, errorReason);
                            AutoArmDebug.LogPawn(___pawn, $"Blacklisted {weapon.Label} due to failed equip job - {errorReason}");
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

                    AutoArmDebug.LogPawn(___pawn, $"Cleared completed equip job for {___curJob.targetA.Thing?.Label} - Reason: {condition}");
                }
                else
                {
                    AutoArmDebug.LogPawn(___pawn, $"Ending equip job for {___curJob.targetA.Thing?.Label} - Reason: {condition}");
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

                    AutoArmDebug.LogPawn(___pawn, $"WARNING: Sidearm upgrade cancelled due to job failure: {condition}");
                }
            }
        }
    }

    [HarmonyPatch(typeof(Thing), "SpawnSetup")]
    public static class Thing_SpawnSetup_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Thing __instance)
        {
            if (__instance == null || __instance.def == null)
                return;

            // Use proper weapon validation to match main logic
            if (!WeaponValidation.IsProperWeapon(__instance))
                return;

            if (__instance is ThingWithComps weapon && weapon.Map != null)
            {
                ImprovedWeaponCacheManager.AddWeaponToCache(weapon);
            }
        }
    }

    [HarmonyPatch(typeof(Thing), "DeSpawn")]
    public static class Thing_DeSpawn_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(Thing __instance)
        {
            if (__instance == null || __instance.def == null)
                return;

            if (!WeaponValidation.IsProperWeapon(__instance))
                return;

            if (__instance is ThingWithComps weapon)
            {
                ImprovedWeaponCacheManager.RemoveWeaponFromCache(weapon);
            }
        }
    }

    [HarmonyPatch(typeof(Thing), "set_Position")]
    public static class Thing_SetPosition_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(Thing __instance, IntVec3 value)
        {
            if (__instance == null || __instance.def == null || !__instance.Spawned)
                return;

            if (!WeaponValidation.IsProperWeapon(__instance))
                return;

            if (__instance is ThingWithComps weapon && weapon.Map != null)
            {
                ImprovedWeaponCacheManager.UpdateWeaponPosition(weapon, __instance.Position, value);
            }
        }
    }

    [HarmonyPatch(typeof(ThinkNode_JobGiver), "TryIssueJobPackage")]
    public static class ThinkNode_JobGiver_TryIssueJobPackage_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ThinkNode_JobGiver __instance, Pawn pawn, JobIssueParams jobParams, ThinkResult __result)
        {
            if (__instance == null || pawn == null)
                return;

            if (__instance is JobGiver_PickUpBetterWeapon && __result.Job != null)
            {
                AutoArmDebug.LogPawn(pawn, $"{__instance.GetType().Name} issued job: {__result.Job.def.defName} targeting {__result.Job.targetA.Thing?.Label}");
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
            // Check if this weapon is marked for same-type upgrade drop
            if (item is ThingWithComps weapon && DroppedItemTracker.IsPendingSameTypeUpgrade(weapon))
            {
                // Prevent adding to inventory - let it drop to ground
                AutoArmDebug.LogWeapon(___pawn, weapon, "Preventing same-type upgrade weapon from going to inventory");
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

            // Check if this was a player-forced action (for sidearms added directly to inventory)
            if (___pawn.jobs?.curDriver?.job?.playerForced == true &&
                ___pawn.jobs.curDriver.job.def?.defName == "EquipSecondary")
            {
                ForcedWeaponHelper.AddForcedDef(___pawn, weapon.def);
                AutoArmDebug.LogWeapon(___pawn, weapon, "Player forced sidearm pickup - marked as forced");
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

    // ============================================================================
    // FORCED WEAPON LABEL PATCHES
    // ============================================================================

    /// <summary>
    /// Patches to add ", forced" to weapon labels in the gear tab, similar to forced apparel
    /// </summary>
    [HarmonyPatch(typeof(Thing), "Label", MethodType.Getter)]
    public static class Thing_Label_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Thing __instance, ref string __result)
        {
            ForcedWeaponLabelHelper.AddForcedText(__instance, ref __result);
        }
    }

    [HarmonyPatch(typeof(Thing), "LabelNoCount", MethodType.Getter)]
    public static class Thing_LabelNoCount_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Thing __instance, ref string __result)
        {
            ForcedWeaponLabelHelper.AddForcedText(__instance, ref __result);
        }
    }

    [HarmonyPatch(typeof(Thing), "LabelCap", MethodType.Getter)]
    public static class Thing_LabelCap_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Thing __instance, ref string __result)
        {
            ForcedWeaponLabelHelper.AddForcedText(__instance, ref __result);
        }
    }

    internal static class ForcedWeaponLabelHelper
    {
        /// <summary>
        /// Adds ", forced" text to weapon labels when appropriate (optimized for gear tab only)
        /// </summary>
        internal static void AddForcedText(Thing thing, ref string label)
        {
            if (thing == null || !thing.def.IsWeapon || !(thing is ThingWithComps weapon) || label.EndsWith(", forced"))
                return;

            // Performance optimization: Only run when viewing a colonist
            var selectedPawn = Find.Selector.SingleSelectedThing as Pawn;
            if (selectedPawn == null || !selectedPawn.IsColonist)
                return;

            // Check if the inspect pane is open and showing gear tab
            var inspectPane = (MainTabWindow_Inspect)MainButtonDefOf.Inspect.TabWindow;
            if (inspectPane == null || Find.MainTabsRoot.OpenTab != MainButtonDefOf.Inspect)
                return;

            // Check if the gear tab is the active tab
            var curTab = inspectPane.CurTabs?.FirstOrDefault(t => t is ITab_Pawn_Gear);
            if (curTab == null || inspectPane.OpenTabType != typeof(ITab_Pawn_Gear))
                return;

            // Only check if this specific pawn has this weapon forced
            if (CheckPawnHasForcedWeapon(selectedPawn, weapon))
            {
                label += ", forced";
            }
        }

        /// <summary>
        /// Checks if a pawn has a specific weapon marked as forced
        /// </summary>
        private static bool CheckPawnHasForcedWeapon(Pawn pawn, ThingWithComps weapon)
        {
            // Check if equipped as primary
            if (pawn.equipment?.Primary == weapon && ForcedWeaponHelper.IsForced(pawn, weapon))
                return true;

            // Check if in inventory
            if (pawn.inventory?.innerContainer?.Contains(weapon) == true)
            {
                if (ForcedWeaponHelper.IsForced(pawn, weapon) ||
                    ForcedWeaponHelper.IsWeaponDefForced(pawn, weapon.def))
                    return true;
            }

            // Check if weapon def is forced (for equipped weapons)
            if (pawn.equipment?.Primary == weapon && ForcedWeaponHelper.IsWeaponDefForced(pawn, weapon.def))
                return true;

            return false;
        }
    }
}