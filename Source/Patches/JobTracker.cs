
using AutoArm.Caching;
using AutoArm.Compatibility;
using AutoArm.Definitions;
using AutoArm.Helpers;
using AutoArm.Jobs;
using HarmonyLib;
using RimWorld;
using System;
using Verse;
using Verse.AI;

namespace AutoArm.Patches
{
    [HarmonyPatch(typeof(Pawn_JobTracker), "StartJob")]
    [HarmonyAfter("PeteTimesSix.SimpleSidearms", "CETeam.CombatExtended")]
    [HarmonyPriority(Priority.Low)]
    [HarmonyPatchCategory(PatchCategories.Core)]
    internal static class Pawn_JobTracker_StartJob_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(Job newJob, Pawn ___pawn)
        {
            try
            {
                var settings = AutoArmMod.settings;

                if (settings?.modEnabled != true)
                    return;

                if (newJob == null || ___pawn == null)
                    return;

                if (!___pawn.IsColonist || ___pawn.Destroyed)
                    return;

                if ((newJob.def == JobDefOf.Equip || newJob.def == AutoArmDefOf.EquipSecondary) &&
                    newJob.targetA.Thing is ThingWithComps targetWeapon)
                {
                    if (targetWeapon != null && JobGiver_PickUpBetterWeapon.IsWeaponCached(targetWeapon, ___pawn?.Map))
                    {

                        if (settings.debugLogging)
                        {
                            AutoArmLogger.Debug(() => $"[{AutoArmLogger.GetPawnName(___pawn)}] Starting {newJob.def.defName} job for {AutoArmLogger.GetWeaponLabelLower(targetWeapon)} [playerForced: {AutoArmLogger.FormatBool(newJob.playerForced)}]");
                        }

                        bool isPartOfSidearmUpgrade = false;
                        if (SimpleSidearmsCompat.IsLoaded && newJob.def == JobDefOf.Equip)
                        {
                            if (SimpleSidearmsCompat.CanAutoEquipSidearms() && !newJob.playerForced && AutoEquipState.IsAutoEquip(newJob))
                            {
                                isPartOfSidearmUpgrade = true;
                            }
                        }

                        if (newJob.playerForced && !isPartOfSidearmUpgrade)
                        {
                            JobGiver_PickUpBetterWeapon.ClearWeaponCooldown(___pawn);

                            if (newJob.def == AutoArmDefOf.EquipSecondary)
                            {
                                ForcedWeapons.AddSidearm(___pawn, targetWeapon);
                                if (settings.debugLogging)
                                {
                                    AutoArmLogger.Debug(() => $"[{___pawn.LabelShort}] Player forced sidearm: {AutoArmLogger.GetWeaponLabelLower(targetWeapon)} (ID: {targetWeapon.thingIDNumber})");
                                }
                            }
                            else
                            {
                                ForcedWeapons.SetForced(___pawn, targetWeapon, "player-forced");
                            }
                        }
                    }
                }
            }
            catch (Exception e) { AutoArmLogger.ErrorPatch(e, "Pawn_JobTracker_StartJob_Prefix"); }
        }
    }

    [HarmonyPatch(typeof(Pawn_JobTracker), "EndCurrentJob")]
    [HarmonyAfter("PeteTimesSix.SimpleSidearms", "CETeam.CombatExtended")]
    [HarmonyPriority(Priority.Low)]
    [HarmonyPatchCategory(PatchCategories.Core)]
    internal static class Pawn_JobTracker_EndCurrentJob_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(Pawn ___pawn, JobCondition condition, Job ___curJob)
        {
            try
            {
                if (___pawn == null || !___pawn.IsColonist || ___curJob == null)
                    return;

                if (AutoArmMod.settings?.modEnabled != true)
                    return;

                var settings = AutoArmMod.settings;

                bool isEquipJob = ___curJob.def == JobDefOf.Equip ||
                                 ___curJob.def == AutoArmDefOf.EquipSecondary;
                bool isSwapJob = ___curJob.def == AutoArmDefOf.AutoArmSwapPrimary ||
                                 ___curJob.def == AutoArmDefOf.AutoArmSwapSidearm;
                bool isWeaponJob = isEquipJob || isSwapJob;

                if (isWeaponJob && condition == JobCondition.Succeeded)
                {
                    JobGiver_PickUpBetterWeapon.RecordWeaponEquip(___pawn);

                    if (settings.debugLogging && ___curJob.targetA.Thing is ThingWithComps equippedWeapon)
                    {
                        AutoArmLogger.Debug(() => $"[{___pawn.LabelShort}] Equipped {AutoArmLogger.GetWeaponLabelLower(equippedWeapon)}");
                    }
                }

                if (isWeaponJob && ___curJob.targetA.Thing is ThingWithComps weapon)
                {
                    if (condition == JobCondition.Errored)
                    {
                        JobGiver_PickUpBetterWeapon.RecordFailedJob(___pawn, weapon);

                        if (AutoEquipState.IsAutoEquip(___curJob))
                        {
                            string cantReason;
                            if (!EquipmentUtility.CanEquip(weapon, ___pawn, out cantReason, checkBonded: false))
                            {
                                Blacklist.AddToBlacklist(weapon.def, ___pawn, cantReason);
                            }
                            else if (settings.debugLogging)
                            {
                                AutoArmLogger.Debug(() => $"[{___pawn.LabelShort}] Equip job errored for {AutoArmLogger.GetWeaponLabelLower(weapon)}, but CanEquip passed (unknown issue, not blacklisting)");
                            }
                        }
                    }
                    else if (condition == JobCondition.Incompletable)
                    {
                        JobGiver_PickUpBetterWeapon.RecordFailedJob(___pawn, weapon);

                        if (AutoEquipState.IsAutoEquip(___curJob) && settings.debugLogging)
                        {
                            string issue = weapon.IsForbidden(___pawn) ? "forbidden" :
                                          !___pawn.CanReserve(weapon) ? "reservation conflict" :
                                          "unknown";
                            AutoArmLogger.Debug(() => $"[{___pawn.LabelShort}] Equip job incompletable for {AutoArmLogger.GetWeaponLabelLower(weapon)} ({issue})");
                        }
                    }
                }

                if (isWeaponJob && AutoEquipState.IsAutoEquip(___curJob))
                {
                    if (condition != JobCondition.Ongoing &&
                        condition != JobCondition.QueuedNoLongerValid)
                    {
                        AutoEquipState.Clear(___curJob);
                        if (condition != JobCondition.Succeeded && ___curJob.targetA.Thing is ThingWithComps twc)
                            WeaponCache.ClearTemporaryReservation(twc);
                    }
                }
            }
            catch (Exception e)
            {
                AutoArmLogger.ErrorPatch(e, "Pawn_JobTracker_EndCurrentJob");
            }
        }
    }
}
