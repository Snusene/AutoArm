
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
    [HarmonyPatch(typeof(Pawn_JobTracker), "TryTakeOrderedJob")]
    [HarmonyPatchCategory(PatchCategories.Core)]
    internal static class Pawn_JobTracker_TryTakeOrderedJob_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Job job, Pawn ___pawn, bool __result)
        {
            try
            {
                var settings = AutoArmMod.settings;

                if (settings?.modEnabled != true)
                    return;

                if (!__result || job == null || ___pawn == null)
                    return;

                if (!___pawn.IsColonist || ___pawn.Destroyed)
                    return;

                if (job.def != JobDefOf.Equip && job.def != AutoArmDefOf.EquipSecondary && !PocketSandCompat.EquipJob(job.def))
                    return;

                if (!(job.targetA.Thing is ThingWithComps targetWeapon))
                    return;

                if (!JobGiver_PickUpBetterWeapon.IsWeaponCached(targetWeapon, ___pawn.Map))
                    return;

                JobGiver_PickUpBetterWeapon.ClearWeaponCooldown(___pawn);

                if (job.def == AutoArmDefOf.EquipSecondary)
                {
                    ForcedWeapons.AddSidearm(___pawn, targetWeapon);
                    if (settings.debugLogging)
                    {
                        AutoArmLogger.Debug(() => $"[{___pawn.LabelShort}] Player forced sidearm: {AutoArmLogger.GetWeaponLabelLower(targetWeapon)} (ID: {targetWeapon.thingIDNumber})");
                    }
                }
            }
            catch (Exception e) { AutoArmLogger.ErrorPatch(e, "Pawn_JobTracker_TryTakeOrderedJob_Postfix"); }
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
                                 ___curJob.def == AutoArmDefOf.EquipSecondary ||
                                 PocketSandCompat.EquipJob(___curJob.def);
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
                        {
                            WeaponCache.ClearTemporaryReservation(twc);
                            if (AutoEquipState.ShouldForceWeapon(___pawn, twc))
                                AutoEquipState.ClearWeaponToForce(___pawn);
                        }
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
