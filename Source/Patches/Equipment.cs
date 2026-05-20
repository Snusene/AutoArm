
using AutoArm.Caching;
using AutoArm.Compatibility;
using AutoArm.Definitions;
using AutoArm.Helpers;
using AutoArm.Jobs;
using AutoArm.Testing;
using HarmonyLib;
using RimWorld;
using System;
using Verse;

namespace AutoArm.Patches
{
    [HarmonyPatch(typeof(Pawn_EquipmentTracker), "AddEquipment")]
    [HarmonyAfter("PeteTimesSix.SimpleSidearms", "CETeam.CombatExtended")]
    [HarmonyPatchCategory(PatchCategories.Core)]
    internal static class Pawn_EquipmentTracker_AddEquipment_Patch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        public static bool Prefix(ThingWithComps newEq, Pawn ___pawn, out bool __state)
        {
            __state = true;
            try
            {
                if (newEq == null || ___pawn == null)
                    return true;

                if (AutoArmMod.settings?.modEnabled != true)
                    return true;

                if (___pawn.equipment?.Primary != null &&
                    ___pawn.inventory?.innerContainer?.Contains(newEq) == true)
                {
                    AutoArmLogger.Debug(() => $"[{___pawn.LabelShort}] AddEquipment skipped: {newEq.Label} already in inventory while primary {___pawn.equipment.Primary.Label} held");
                    __state = false;
                    return false;
                }

                return true;
            }
            catch (Exception e)
            {
                AutoArmLogger.ErrorPatch(e, "Pawn_EquipmentTracker_AddEquipment_Prefix");
                return true;
            }
        }

        [HarmonyPostfix]
        public static void Postfix(ThingWithComps newEq, Pawn ___pawn, bool __state)
        {
            if (!__state) return;
            try
            {
                var settings = AutoArmMod.settings;

                if (settings?.modEnabled != true)
                    return;

                if (newEq == null)
                    return;

                bool isProperWeapon = JobGiver_PickUpBetterWeapon.IsWeaponCached(newEq, ___pawn?.Map);

                if (___pawn != null && ___pawn.IsColonist && isProperWeapon)
                {
                    ForcedWeaponState.WeaponPickedUp(newEq);

                    if (AutoEquipState.ShouldForceWeapon(___pawn, newEq))
                    {
                        ForcedWeapons.SetForced(___pawn, newEq);
                        AutoEquipState.ClearWeaponToForce(___pawn);

                        if (settings.debugLogging)
                        {
                            AutoArmLogger.Debug(() => $"[{___pawn.LabelShort}] Transferred forced status to upgraded {AutoArmLogger.GetWeaponLabelLower(newEq)}");
                        }
                    }
                    else if (settings.respectWeaponBonds &&
                        ModsConfig.RoyaltyActive &&
                        Components.IsPersonaBondedTo(newEq, ___pawn))
                    {
                        ForcedWeapons.SetForced(___pawn, newEq, "auto-forced (bonded)");
                    }
                    else if (ForcedWeapons.IsForced(___pawn, newEq))
                    {
                        if (settings.debugLogging)
                        {
                            AutoArmLogger.Debug(() => $"[{___pawn.LabelShort}] {AutoArmLogger.GetWeaponLabelLower(newEq)} (ID: {newEq.thingIDNumber}) is already forced, maintaining status");
                        }
                    }

                    if (___pawn.CurJob?.def == JobDefOf.Equip && ___pawn.CurJob.playerForced &&
                        !AutoEquipState.IsAutoEquip(___pawn.CurJob))
                    {
                        ForcedWeapons.SetForced(___pawn, newEq, "manually equipped", log: false);
                    }

                    bool isAutoEquipJob = false;
                    var curJob = ___pawn.CurJob;
                    if (curJob != null && AutoEquipState.IsAutoEquip(curJob))
                    {
                        isAutoEquipJob = true;
                    }

                    if (isAutoEquipJob)
                    {
                        if (settings.showNotifications && PawnUtility.ShouldSendNotificationAbout(___pawn))
                        {
                            var previousWeaponLabel = AutoEquipState.GetPreviousWeapon(___pawn);

                            if (!string.IsNullOrEmpty(previousWeaponLabel))
                            {
                                Messages.Message("AutoArm_UpgradedWeapon".Translate(
                                    ___pawn.LabelShort.CapitalizeFirst(),
                                    previousWeaponLabel,
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

                        AutoEquipState.Clear(curJob);
                        AutoEquipState.ClearPreviousWeapon(___pawn);

                        if (SimpleSidearmsCompat.CanAutoEquipSidearms() &&
                            curJob?.def != AutoArmDefOf.AutoArmSwapPrimary)
                        {
                            var lastDropped = DroppedItems.GetLastDropped(___pawn);
                            if (lastDropped != null && lastDropped.def == newEq.def &&
                                lastDropped.Position.InHorDistOf(___pawn.Position, 10f))
                            {
                                try
                                {
                                    if (SimpleSidearmsCompat.IsManagingPawn(___pawn))
                                    {
                                        SimpleSidearmsCompat.InformOfDroppedWeapon(___pawn, lastDropped);
                                        SimpleSidearmsCompat.InformOfAddedPrimary(___pawn, newEq);

                                        if (settings.debugLogging)
                                        {
                                            AutoArmLogger.Debug(() => $"[{___pawn.LabelShort}] Updated SimpleSidearms memory: forgot {AutoArmLogger.GetWeaponLabelLower(lastDropped)}, added {AutoArmLogger.GetWeaponLabelLower(newEq)}");
                                        }
                                    }
                                }
                                catch (Exception e)
                                {
                                    AutoArmLogger.WarnFileOnly($"Failed to handle SimpleSidearms integration: {e.Message}");
                                }
                            }
                        }
                    }
                    else if (settings.debugLogging && !TestRunner.IsRunningTests)
                    {
                        var curDef = ___pawn?.CurJob?.def;
                        bool isAutoArmEquip = curDef == JobDefOf.Equip ||
                                              curDef == AutoArmDefOf.EquipSecondary ||
                                              curDef == AutoArmDefOf.AutoArmSwapPrimary ||
                                              curDef == AutoArmDefOf.AutoArmSwapSidearm;
                        if (!isAutoArmEquip)
                        {
                            AutoArmLogger.Debug(() => $"[{AutoArmLogger.GetPawnName(___pawn)}] Equipped {AutoArmLogger.GetWeaponLabelLower(newEq)} (manual/other)");
                        }
                    }
                }

                if (isProperWeapon)
                {
                    WeaponCache.RemoveWeaponFromCache(newEq);
                    WeaponCache.ClearTemporaryReservation(newEq);
                }
            }
            catch (Exception e)
            {
                AutoArmLogger.ErrorPatch(e, "Pawn_EquipmentTracker_AddEquipment_Patch");
            }
        }
    }

    [HarmonyPatch(typeof(Pawn_EquipmentTracker), "TryDropEquipment")]
    [HarmonyAfter("PeteTimesSix.SimpleSidearms", "CETeam.CombatExtended")]
    [HarmonyPatchCategory(PatchCategories.Core)]
    internal static class Pawn_EquipmentTracker_TryDropEquipment_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(bool __result, Pawn ___pawn, ThingWithComps resultingEq)
        {
            var settings = AutoArmMod.settings;

            if (settings?.modEnabled != true)
                return;

            if (!__result || ___pawn == null || !___pawn.IsColonist || resultingEq == null)
                return;

            try
            {
                bool isSameTypeUpgrade = DroppedItems.IsPendingSameTypeUpgrade(resultingEq);

                if (ForcedWeapons.IsForced(___pawn, resultingEq))
                {
                    ForcedWeaponState.MarkForcedWeaponDropped(___pawn, resultingEq);

                    if (settings.debugLogging)
                    {
                        AutoArmLogger.Debug(() => $"[{___pawn.LabelShort}] Dropped forced {AutoArmLogger.GetWeaponLabelLower(resultingEq)}, will clear forced status in 1 second if not re-equipped");
                    }
                }

                if (isSameTypeUpgrade)
                {
                    DroppedItems.ClearPendingUpgrade(resultingEq);
                    DroppedItems.MarkAsDropped(resultingEq, Constants.ExtendedDropCooldownTicks);
                }
                else
                {
                    bool isPlayerDrop = false;

                    if (___pawn.CurJob == null)
                    {
                        isPlayerDrop = true;
                    }
                    else if (___pawn.CurJob?.def != JobDefOf.Equip || !AutoEquipState.IsAutoEquip(___pawn.CurJob))
                    {
                        isPlayerDrop = true;
                    }

                    if (isPlayerDrop)
                    {
                        DroppedItems.MarkAsDropped(resultingEq, DroppedItems.DefaultIgnoreTicks);

                        if (settings.debugLogging)
                        {
                            AutoArmLogger.Debug(() => $"[{___pawn.LabelShort}] Dropped {AutoArmLogger.GetWeaponLabelLower(resultingEq)}, applying {DroppedItems.DefaultIgnoreTicks} tick cooldown");
                        }
                    }
                }

            }
            catch (Exception e)
            {
                AutoArmLogger.ErrorPatch(e, "Pawn_EquipmentTracker_TryDropEquipment_Patch");
            }
        }
    }

    [HarmonyPatch(typeof(Pawn_EquipmentTracker), "DestroyEquipment")]
    [HarmonyPatchCategory(PatchCategories.Core)]
    internal static class Pawn_EquipmentTracker_DestroyEquipment_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn ___pawn)
        {
            try
            {
                if (___pawn == null || !___pawn.IsColonist)
                    return;

                if (AutoArmMod.settings?.modEnabled != true)
                    return;

                ForcedWeapons.ClearForcedPrimary(___pawn);
            }
            catch (Exception e)
            {
                AutoArmLogger.ErrorPatch(e, "Pawn_EquipmentTracker_DestroyEquipment");
            }
        }
    }
}
