// AutoArm RimWorld 1.5+ mod - automatic weapon management
// This file: Harmony patches for equipment management
// Handles weapon equipping, dropping, and forced weapon tracking
// OPTIMIZED: Settings reference cached locally to reduce property access overhead

using AutoArm.Definitions;
using AutoArm.Helpers;
using AutoArm.Jobs;
using AutoArm.Logging;
using AutoArm.Weapons;
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
            // OPTIMIZATION: Cache settings reference to avoid multiple property accesses
            var settings = AutoArmMod.settings;
            
            // Check if mod is enabled - FIRST CHECK
            if (settings?.modEnabled != true)
                return;

            if (newJob == null || ___pawn == null)
                return;

            if (!___pawn.IsColonist || ___pawn.Destroyed)
                return;

            if (newJob.def == JobDefOf.Equip && newJob.targetA.Thing is ThingWithComps targetWeapon)
            {
                if (targetWeapon != null && WeaponValidation.IsProperWeapon(targetWeapon))
                {
                    // SimpleSidearmsCompat simplified - pending upgrade tracking removed

                    // Debug logging to track all equip jobs - only if debug logging is enabled
                    if (settings.debugLogging)
                    {
                        AutoArmLogger.LogPawnWeapon(___pawn, targetWeapon, $"Starting equip job [playerForced: {newJob.playerForced}, interaction: {newJob.interaction}]");
                    }

                    // Only mark as forced if player explicitly forced it via right-click
                    // But skip if this is part of a sidearm upgrade
                    // SimpleSidearmsCompat simplified - use job context to detect sidearm upgrades
                    bool isPartOfSidearmUpgrade = false;
                    if (SimpleSidearmsCompat.IsLoaded() && settings.autoEquipSidearms)
                    {
                        // Check if this is an auto-equip job (not player forced)
                        isPartOfSidearmUpgrade = !newJob.playerForced && AutoEquipTracker.IsAutoEquip(newJob);
                    }

                    if (newJob.playerForced && !isPartOfSidearmUpgrade)
                    {
                        // Clear weapon cooldown when player manually equips
                        JobGiver_PickUpBetterWeapon.ClearWeaponCooldown(___pawn);
                        
                        // Only track as forced in AutoArm's system
                        // Don't automatically sync with SimpleSidearms - let SS manage its own forcing
                        if (!SimpleSidearmsCompat.IsLoaded())
                        {
                            ForcedWeaponHelper.SetForced(___pawn, targetWeapon);

                            if (settings.debugLogging)
                            {
                                AutoArmLogger.Debug($"{___pawn.LabelShort}: Forced weapon - {targetWeapon.Label}");
                            }
                        }
                    }
                    else if (isPartOfSidearmUpgrade)
                    {
                        // Not forced - sidearm upgrade
                    }
                }
            }
            else if (SimpleSidearmsCompat.IsLoaded() &&
                     (newJob.def?.defName == "EquipSecondary" ||
                      newJob.def?.defName == "ReequipSecondary" ||
                      newJob.def?.defName == "ReequipSecondaryCombat") &&
                     newJob.targetA.Thing is ThingWithComps sidearmWeapon &&
                     WeaponValidation.IsProperWeapon(sidearmWeapon))
            {
                // Debug logging for SimpleSidearms jobs
                if (settings.debugLogging)
                {
                    AutoArmLogger.LogPawnWeapon(___pawn, sidearmWeapon,
                        $"Starting SimpleSidearms job ({newJob.def.defName}):\n" +
                        $"  - playerForced: {newJob.playerForced}\n" +
                        $"  - interaction: {newJob.interaction}\n" +
                        $"  - Source: {(newJob.playerForced ? "Player" : "SimpleSidearms AI")}");
                }

                // Only mark as forced if player explicitly forced it
                if (newJob.playerForced)
                {
                    // Don't auto-sync with SimpleSidearms
                    // Players should use SS's UI to force sidearms
                    if (!SimpleSidearmsCompat.IsLoaded())
                    {
                        ForcedWeaponHelper.AddForcedSidearm(___pawn, sidearmWeapon);

                        if (settings.debugLogging)
                        {
                            AutoArmLogger.Debug($"{___pawn.LabelShort}: Forced sidearm - {sidearmWeapon.Label}");
                        }
                    }
                }
            }
        }

        [HarmonyPostfix]
        public static void Postfix(Job newJob, Pawn ___pawn)
        {
            // OPTIMIZATION: Cache settings reference
            var settings = AutoArmMod.settings;
            
            // Check if mod is enabled - FIRST CHECK
            if (settings?.modEnabled != true)
                return;

            if (newJob == null || ___pawn == null)
                return;

            if (newJob.def == JobDefOf.Equip && AutoEquipTracker.IsAutoEquip(newJob))
            {
                // Auto-equip job started
            }
        }
    }

    [HarmonyPatch(typeof(Pawn_EquipmentTracker), "TryDropEquipment")]
    public static class Pawn_EquipmentTracker_TryDropEquipment_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(bool __result, Pawn ___pawn, ThingWithComps resultingEq)
        {
            // OPTIMIZATION: Cache settings reference
            var settings = AutoArmMod.settings;
            
            // Check if mod is enabled - FIRST CHECK
            if (settings?.modEnabled != true)
                return;

            if (!__result || ___pawn == null || !___pawn.IsColonist || resultingEq == null)
                return;

            try
            {
                // Check if this is a same-type weapon upgrade
                bool isSameTypeUpgrade = DroppedItemTracker.IsPendingSameTypeUpgrade(resultingEq);

                // Track dropped forced weapons with a timer instead of complex SimpleSidearms detection
                // This handles SimpleSidearms swaps and any other mod interactions gracefully
                if (ForcedWeaponHelper.IsForced(___pawn, resultingEq))
                {
                    // Mark this forced weapon as dropped with the pawn who dropped it
                    // It will be cleared after 60 ticks (1 second) if not picked back up
                    ForcedWeaponTracker.MarkForcedWeaponDropped(___pawn, resultingEq);

                    if (settings.debugLogging)
                    {
                        AutoArmLogger.Debug($"[{___pawn.LabelShort}] Dropped forced weapon {resultingEq.Label} - will clear forced status in 1 second if not re-equipped");
                    }
                }

                // Handle same-type upgrades
                if (isSameTypeUpgrade)
                {
                    DroppedItemTracker.ClearPendingUpgrade(resultingEq);
                    DroppedItemTracker.MarkAsDropped(resultingEq, 1200);
                }
                else
                {
                    // Check if this is a player-initiated drop (not part of an auto-equip)
                    bool isPlayerDrop = false;

                    // No job at all = dropped via gear tab
                    if (___pawn.CurJob == null)
                    {
                        isPlayerDrop = true;
                    }
                    // Has a job but it's not an auto-equip job
                    else if (___pawn.CurJob?.def != JobDefOf.Equip || !AutoEquipTracker.IsAutoEquip(___pawn.CurJob))
                    {
                        // Not part of an auto-equip sequence
                        isPlayerDrop = true;
                    }

                    // Apply cooldown for player drops
                    if (isPlayerDrop)
                    {
                        // Use default cooldown (600 ticks / 10 seconds) for player drops
                        DroppedItemTracker.MarkAsDropped(resultingEq, DroppedItemTracker.DefaultIgnoreTicks);
                        
                        if (settings.debugLogging)
                        {
                            AutoArmLogger.Debug($"[{___pawn.LabelShort}] Player dropped {resultingEq.Label} - applying {DroppedItemTracker.DefaultIgnoreTicks} tick cooldown");
                        }
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
            // Check if mod is enabled - FIRST CHECK (no need to cache for this simple check)
            if (AutoArmMod.settings?.modEnabled != true)
                return;

            if (___pawn == null || !___pawn.IsColonist)
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
            // Always allow normal game operations if mod disabled
            if (AutoArmMod.settings?.modEnabled != true)
                return true;

            if (newEq == null || ___pawn == null)
                return true;

            // Prevent equipping items from inventory if already equipped
            if (___pawn.equipment?.Primary != null &&
                ___pawn.inventory?.innerContainer?.Contains(newEq) == true)
            {
                // Inventory equip prevention
                return false;
            }

            return true;
        }

        [HarmonyPostfix]
        public static void Postfix(ThingWithComps newEq, Pawn ___pawn)
        {
            // OPTIMIZATION: Cache settings reference
            var settings = AutoArmMod.settings;
            
            // Check if mod is enabled - FIRST CHECK
            if (settings?.modEnabled != true)
                return;

            if (newEq == null || ___pawn == null || !___pawn.IsColonist)
                return;

            // Notify tracker that weapon was picked up (cancels forced status timer)
            ForcedWeaponTracker.WeaponPickedUp(newEq);

            // Check if we had a weapon that couldn't move to inventory
            // The vanilla Equip job has now swapped it at the target location
            var weaponCouldntMove = AutoEquipTracker.GetWeaponCannotMoveToInventory(___pawn);
            if (weaponCouldntMove != null)
            {
                // The weapon has been swapped - it's now on the ground where the new weapon was
                // Find it and mark it as dropped so we don't immediately pick it up again
                var droppedWeapon = newEq.Position.GetThingList(___pawn.Map)
                    .OfType<ThingWithComps>()
                    .FirstOrDefault(t => t == weaponCouldntMove);

                if (droppedWeapon != null)
                {
                    DroppedItemTracker.MarkAsDropped(droppedWeapon, Constants.LongDropCooldownTicks);
                    if (settings.debugLogging)
                    {
                        AutoArmLogger.Debug($"[{___pawn.LabelShort}] Weapon {droppedWeapon.Label} was swapped at target location and marked as dropped");
                    }
                }

                // Clear the tracking
                AutoEquipTracker.ClearWeaponCannotMoveToInventory(___pawn);
            }

            // Check if this weapon should be forced (from forced weapon upgrade)
            if (AutoEquipTracker.ShouldForceWeapon(___pawn, newEq))
            {
                // Transfer forced status to the upgraded weapon
                ForcedWeaponHelper.SetForced(___pawn, newEq);
                AutoEquipTracker.ClearWeaponToForce(___pawn);
                if (settings.debugLogging)
                {
                    AutoArmLogger.Debug($"{___pawn.LabelShort}: Transferred forced status to upgraded weapon {newEq.Label}");
                }
            }
            // Auto-force bonded weapons when "Respect weapon bonds" is enabled
            else if (settings.respectWeaponBonds &&
                ModsConfig.RoyaltyActive &&
                ValidationHelper.IsWeaponBondedToPawn(newEq, ___pawn))
            {
                // For bonded weapons, we DO want to sync with SimpleSidearms
                // because bonded weapons should always be forced
                if (SimpleSidearmsCompat.IsLoaded())
                {
                    // Use the special method for bonded weapons
                    ForcedWeaponHelper.ForceBondedWeaponInSimpleSidearms(___pawn, newEq);
                }
                else
                {
                    // When SS is not loaded, use normal forcing
                    ForcedWeaponHelper.SetForced(___pawn, newEq);
                }

                if (settings.debugLogging)
                {
                    AutoArmLogger.Debug($"{___pawn.LabelShort}: Bonded weapon {newEq.Label} auto-forced");
                }
            }
            // Check if this weapon type was already forced as a sidearm
            else if (!SimpleSidearmsCompat.IsLoaded())
            {
                // Only do this when SimpleSidearms is NOT loaded
                // When SS is loaded, it manages its own forcing
                bool hasForcedOfSameType = false;
                // Check primary
                if (___pawn.equipment?.Primary != null && ___pawn.equipment.Primary.def == newEq.def &&
                    ForcedWeaponHelper.IsForced(___pawn, ___pawn.equipment.Primary))
                {
                    hasForcedOfSameType = true;
                }
                // Check inventory
                if (!hasForcedOfSameType && ___pawn.inventory?.innerContainer != null)
                {
                    foreach (var item in ___pawn.inventory.innerContainer)
                    {
                        if (item is ThingWithComps weapon && weapon.def == newEq.def &&
                            ForcedWeaponHelper.IsForced(___pawn, weapon))
                        {
                            hasForcedOfSameType = true;
                            break;
                        }
                    }
                }

                if (hasForcedOfSameType)
                {
                    // Maintain forced status when sidearm becomes primary
                    ForcedWeaponHelper.SetForced(___pawn, newEq);
                    // Sidearm to primary - maintain forced
                }
            }
            // Only mark as forced if this is completing a player-forced equip job
            else if (___pawn.CurJob?.def == JobDefOf.Equip && ___pawn.CurJob.playerForced &&
                     !AutoEquipTracker.IsAutoEquip(___pawn.CurJob))
            {
                // This was a manual equip
                // Only mark as forced in AutoArm when SimpleSidearms is NOT loaded
                // Let SimpleSidearms manage its own forcing through its UI
                if (!SimpleSidearmsCompat.IsLoaded())
                {
                    ForcedWeaponHelper.SetForced(___pawn, newEq);

                    if (settings.debugLogging)
                    {
                        AutoArmLogger.Debug($"{___pawn.LabelShort}: Manually equipped {newEq.Label} - forced");
                    }
                }
            }

            // Then handle auto-equip notifications
            if (___pawn.CurJob?.def == JobDefOf.Equip && AutoEquipTracker.IsAutoEquip(___pawn.CurJob))
            {
                // Don't inform SimpleSidearms about primary weapon equips
                // This was causing SimpleSidearms to "remember" primary weapons as sidearms

                if (PawnUtility.ShouldSendNotificationAbout(___pawn) &&
                    settings.showNotifications)
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

                // Cooldown functionality removed - no longer needed

                // Only log success if debug logging is enabled
                if (settings.debugLogging)
                {
                    AutoArmLogger.LogPawnWeapon(___pawn, newEq, "Successfully equipped, clearing job tracking");
                }

                // STREAMLINED: After equipping, let SimpleSidearms know about sidearm changes
                // But DON'T trigger reordering for primary weapons to avoid duplication issues
                if (SimpleSidearmsCompat.IsLoaded() && settings.autoEquipSidearms)
                {
                    try
                    {
                        // Check if this was a sidearm upgrade (same type weapon was dropped)
                        var droppedWeapons = DroppedItemTracker.GetRecentlyDroppedWeapons();
                        ThingWithComps droppedSameType = null;
                        bool wasSidearmUpgrade = false;

                        foreach (var dropped in droppedWeapons)
                        {
                            if (dropped != null && dropped.def == newEq.def &&
                                dropped.Position.InHorDistOf(___pawn.Position, 10f))
                            {
                                droppedSameType = dropped;
                                // Check if the dropped weapon was in inventory (sidearm)
                                wasSidearmUpgrade = true; // Assume it was a sidearm if same type was dropped
                                break;
                            }
                        }

                        if (droppedSameType != null && wasSidearmUpgrade)
                        {
                            // This was a sidearm upgrade - inform SimpleSidearms
                            SimpleSidearmsCompat.InformOfDroppedSidearm(___pawn, droppedSameType);
                            SimpleSidearmsCompat.InformOfAddedSidearm(___pawn, newEq);

                            if (settings.debugLogging)
                            {
                                AutoArmLogger.Debug($"[{___pawn.LabelShort}] SS memory updated for sidearm upgrade: forgot {droppedSameType.Label}, added {newEq.Label}");
                            }
                            
                            // Only reorder for sidearm upgrades
                            SimpleSidearmsCompat.ReorderWeaponsAfterEquip(___pawn);
                            
                            if (settings.debugLogging)
                            {
                                AutoArmLogger.Debug($"[{___pawn.LabelShort}] SimpleSidearms reordered weapons after sidearm upgrade");
                            }
                        }
                        else
                        {
                            // Primary weapon equip - DON'T inform SS and DON'T reorder
                            // This prevents SS from trying to pick up same-type weapons as sidearms
                            if (settings.debugLogging)
                            {
                                AutoArmLogger.Debug($"[{___pawn.LabelShort}] Primary weapon {newEq.Label} equipped - skipping SimpleSidearms integration to prevent duplication");
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        AutoArmLogger.Warn($"Failed to handle SimpleSidearms integration: {e.Message}");
                    }
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
            // OPTIMIZATION: Cache settings reference
            var settings = AutoArmMod.settings;
            
            // Check if mod is enabled - FIRST CHECK
            if (settings?.modEnabled != true)
                return;

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
                        // When auto-equip jobs fail, blacklist the weapon to prevent loops
                        // Body size restrictions ARE blacklisted - the blacklist clears periodically
                        // and when pawn's body size changes (e.g., equipping power armor)
                        string errorReason = "failed to equip - mod restriction";
                        
                        // Always blacklist failed weapons to prevent equip loops
                        WeaponBlacklist.AddToBlacklist(weapon.def, ___pawn, errorReason);
                        
                        if (settings.debugLogging)
                        {
                            AutoArmLogger.Debug($"{___pawn.LabelShort}: Blacklisted {weapon.Label} - {errorReason} (job condition: {condition})");
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

                    // Equip job cleared
                }
                else
                {
                    // Equip job ending
                }
            }

            // SimpleSidearmsCompat simplified - pending upgrade tracking removed
        }
    }
}
