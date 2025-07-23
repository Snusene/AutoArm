using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;
using Verse.AI;

namespace AutoArm
{
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
                    if (newJob.playerForced && !SimpleSidearmsCompat.PawnHasTemporarySidearmEquipped(___pawn))
                    {
                        ForcedWeaponHelper.SetForced(___pawn, targetWeapon);
                        
                        AutoArmDebug.LogWeapon(___pawn, targetWeapon, "FORCED: Manually equipping");
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
        // Track pawns actively swapping weapons
        private static HashSet<Pawn> pawnsCurrentlySwapping = new HashSet<Pawn>();
        
        [HarmonyPrefix]
        public static void Prefix(Pawn ___pawn)
        {
            // Before dropping, check if this is part of an equip job
            if (___pawn?.CurJob?.def == JobDefOf.Equip)
            {
                pawnsCurrentlySwapping.Add(___pawn);
            }
        }
        
        [HarmonyPostfix]
        public static void Postfix(bool __result, Pawn ___pawn, ThingWithComps resultingEq)
        {
            if (!__result || ___pawn == null || !___pawn.IsColonist || resultingEq == null)
                return;

            try
            {
                // Check if this drop was part of an equip job (weapon swap)
                bool wasSwapping = pawnsCurrentlySwapping.Contains(___pawn);
                
                // Check if this is a same-type weapon upgrade
                bool isSameTypeUpgrade = DroppedItemTracker.IsPendingSameTypeUpgrade(resultingEq);
                
                // For SimpleSidearms swaps, check if weapon will end up in inventory
                // We check both job-based swaps AND UI-based instant swaps
                bool isSimpleSidearmsOperation = SimpleSidearmsCompat.IsLoaded() && 
                    (___pawn.CurJob?.def?.defName == "EquipSecondary" || // Job-based sidearm pickup
                     SimpleSidearmsCompat.IsRememberedSidearm(___pawn, resultingEq)); // UI-based instant swap
                     
                // Debug logging for SimpleSidearms detection
                if (SimpleSidearmsCompat.IsLoaded() && SimpleSidearmsCompat.IsRememberedSidearm(___pawn, resultingEq) && 
                    ___pawn.CurJob?.def?.defName != "EquipSecondary")
                {
                    AutoArmDebug.LogWeapon(___pawn, resultingEq, "Detected SimpleSidearms UI swap (no job, but weapon is remembered sidearm)");
                }
                
                if (!wasSwapping && !isSimpleSidearmsOperation)
                {
                    // This is a manual drop - clear all forced status
                    ForcedWeaponHelper.ClearForced(___pawn);
                    // Also clear any forced sidearm defs
                    if (ForcedWeaponHelper.IsWeaponDefForced(___pawn, resultingEq.def))
                    {
                        ForcedWeaponHelper.RemoveForcedDef(___pawn, resultingEq.def);
                    }
                    
                    AutoArmDebug.LogWeapon(___pawn, resultingEq, "Manual weapon drop - cleared all forced status");
                }
                else if (isSameTypeUpgrade)
                {
                    // This is a same-type upgrade - clear pending status and let it drop
                    DroppedItemTracker.ClearPendingUpgrade(resultingEq);
                    AutoArmDebug.LogWeapon(___pawn, resultingEq, "Same-type weapon upgrade - weapon will be dropped");
                    
                    // Mark as recently dropped so it won't be picked up immediately
                    DroppedItemTracker.MarkAsDropped(resultingEq);
                }
                else
                {
                    // This is a swap operation - maintain forced status appropriately
                    string swapReason = wasSwapping ? "equip job" : "SimpleSidearms UI";
                    AutoArmDebug.LogWeapon(___pawn, resultingEq, 
                        $"Weapon swap detected ({swapReason}) - maintaining forced status");
                    
                    // If swapping to sidearm and weapon was forced, track it
                    if (isSimpleSidearmsOperation && ForcedWeaponHelper.IsWeaponDefForced(___pawn, resultingEq.def))
                    {
                        ForcedWeaponHelper.AddForcedDef(___pawn, resultingEq.def);
                        AutoArmDebug.LogWeapon(___pawn, resultingEq, "Weapon being swapped to sidearm - tracking as forced sidearm");
                    }
                }
            }
            finally
            {
                // Clean up tracking
                pawnsCurrentlySwapping.Remove(___pawn);
            }
            
            // Mark as recently dropped for SimpleSidearms
            if (resultingEq != null && SimpleSidearmsCompat.IsLoaded())
            {
                SimpleSidearmsCompat.MarkWeaponAsRecentlyDropped(resultingEq);
                AutoArmDebug.Log($"Marked dropped weapon {resultingEq.Label} as recently dropped");
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
            
            // Check if this weapon is being equipped (SimpleSidearms swap or manual equip)
            bool isBeingEquipped = ___pawn.CurJob?.def == JobDefOf.Equip && 
                                  ___pawn.CurJob.targetA.Thing == weapon;
            
            // Check if this is a SimpleSidearms operation
            bool isSimpleSidearmsJob = ___pawn.CurJob?.def?.defName == "EquipSecondary";
            
            if (!isBeingEquipped && !isSimpleSidearmsJob)
            {
                // This is a manual drop from inventory - clear forced status
                if (ForcedWeaponHelper.IsWeaponDefForced(___pawn, weapon.def))
                {
                    ForcedWeaponHelper.RemoveForcedDef(___pawn, weapon.def);
                    AutoArmDebug.LogWeapon(___pawn, weapon, "Manually dropped forced sidearm - cleared forced status");
                }
            }
            else
            {
                // This is a swap or equip operation - maintain forced status
                AutoArmDebug.LogWeapon(___pawn, weapon, "Weapon being equipped from inventory - maintaining forced status");
            }

            // Show notification if dropping disallowed sidearm
            var filter = ___pawn.outfits?.CurrentApparelPolicy?.filter;
            if (filter != null && !filter.Allows(weapon.def) &&
                AutoArmMod.settings?.showNotifications == true &&
                PawnUtility.ShouldSendNotificationAbout(___pawn))
            {
                string weaponLabel = weapon.Label ?? weapon.def?.label ?? "Unknown weapon";
                Messages.Message("AutoArm_DroppingSidearmDisallowed".Translate(
                    ___pawn.LabelShort.CapitalizeFirst(),
                    weaponLabel
                ), new LookTargets(___pawn), MessageTypeDefOf.SilentInput, false);
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

            if (___pawn.equipment?.Primary == null || ___pawn.jobs == null)
                return;

            // Don't interfere with sidearm upgrades in progress
            if (SimpleSidearmsCompat.PawnHasTemporarySidearmEquipped(___pawn))
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
            if (filter != null && !filter.Allows(___pawn.equipment.Primary.def))
            {
                // Check if this weapon is forced - if so, keep it equipped
                if (ForcedWeaponHelper.IsForced(___pawn, ___pawn.equipment.Primary))
                {
                    AutoArmDebug.LogWeapon(___pawn, ___pawn.equipment.Primary, "Outfit changed but keeping forced weapon");
                    return; // Don't drop forced weapons
                }

                ForcedWeaponHelper.ClearForced(___pawn);

                var dropJob = new Job(JobDefOf.DropEquipment, ___pawn.equipment.Primary);
                ___pawn.jobs.TryTakeOrderedJob(dropJob, JobTag.Misc);

                AutoArmDebug.LogWeapon(___pawn, ___pawn.equipment.Primary, "Outfit changed, dropping weapon");

                if (PawnUtility.ShouldSendNotificationAbout(___pawn))
                {
                    Messages.Message("AutoArm_DroppingDisallowed".Translate(
                        ___pawn.LabelShort.CapitalizeFirst(),
                        ___pawn.equipment.Primary.Label ?? ___pawn.equipment.Primary.def?.label ?? "weapon"
                    ), new LookTargets(___pawn), MessageTypeDefOf.SilentInput, false);
                }
            }

            if (Pawn_TickRare_Unified_Patch.lastWeaponSearchTick.ContainsKey(___pawn))
            {
                Pawn_TickRare_Unified_Patch.lastWeaponSearchTick.Remove(___pawn);
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

                    if (pawn.outfits?.CurrentApparelPolicy?.filter == __instance &&
                        pawn.equipment?.Primary != null)
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
                            if (SimpleSidearmsCompat.PawnHasTemporarySidearmEquipped(pawn))
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

                            ForcedWeaponHelper.ClearForced(pawn);

                            var dropJob = new Job(JobDefOf.DropEquipment, weapon);
                            pawn.jobs.TryTakeOrderedJob(dropJob, JobTag.Misc);

                            AutoArmDebug.LogPawn(pawn, "All items disallowed, dropping weapon");

                            if (PawnUtility.ShouldSendNotificationAbout(pawn))
                            {
                                Messages.Message("AutoArm_DroppingDisallowed".Translate(
                                    pawn.LabelShort.CapitalizeFirst(),
                                    weapon.Label ?? weapon.def?.label ?? "weapon"
                                ), new LookTargets(pawn), MessageTypeDefOf.SilentInput, false);
                            }
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
}
