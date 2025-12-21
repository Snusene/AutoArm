
using AutoArm.Caching;
using AutoArm.Compatibility;
using AutoArm.Definitions;
using AutoArm.Helpers;
using AutoArm.Jobs;
using AutoArm.Logging;
using AutoArm.UI;
using AutoArm.Weapons;
using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace AutoArm
{

    internal static class EquipCooldownTracker
    {
        private const int DefaultCooldownTicks = 300;
        private const int CleanupIntervalTicks = Constants.StandardCacheDuration;
        private static readonly Dictionary<Pawn, int> _expiryTicks = new Dictionary<Pawn, int>();
        private static int _lastCleanupTick = 0;

        public static void Record(Pawn pawn, int cooldownTicks = DefaultCooldownTicks)
        {
            if (pawn == null) return;
            int now = Find.TickManager?.TicksGame ?? 0;
            _expiryTicks[pawn] = now + cooldownTicks;

            TryCleanup(now);
        }

        public static bool IsOnCooldown(Pawn pawn)
        {
            if (pawn == null) return false;
            int now = Find.TickManager?.TicksGame ?? 0;
            int expiry;
            if (_expiryTicks.TryGetValue(pawn, out expiry))
            {
                if (now < expiry) return true;
                _expiryTicks.Remove(pawn);
            }
            return false;
        }

        public static void Clear(Pawn pawn)
        {
            if (pawn != null) _expiryTicks.Remove(pawn);
        }


        private static void TryCleanup(int currentTick)
        {
            if (currentTick - _lastCleanupTick < CleanupIntervalTicks)
                return;

            _lastCleanupTick = currentTick;

            int removedCount = 0;
            List<Pawn> toRemove = null;

            foreach (var kvp in _expiryTicks)
            {
                var pawn = kvp.Key;
                if (pawn == null || pawn.Destroyed || pawn.Dead || !pawn.Spawned)
                {
                    if (toRemove == null)
                        toRemove = ListPool<Pawn>.Get();
                    toRemove.Add(pawn);
                }
            }

            if (toRemove != null)
            {
                removedCount = toRemove.Count;
                foreach (var pawn in toRemove)
                {
                    _expiryTicks.Remove(pawn);
                }

                AutoArmLogger.Debug(() => $"[EquipCooldownTracker] Cleaned up {removedCount} destroyed/dead pawns");

                ListPool<Pawn>.Return(toRemove);
            }
        }

        /// <summary>
        /// Force cleanup of all entries (called on map unload/game exit)
        /// </summary>
        public static void ClearAll()
        {
            _expiryTicks.Clear();
            _lastCleanupTick = 0;
        }
    }
}

namespace AutoArm.Patches
{
    [HarmonyPatch(typeof(Game), nameof(Game.LoadGame))]
    [HarmonyPatchCategory(PatchCategories.Core)]
    [HarmonyAfter("PeteTimesSix.SimpleSidearms")]
    [HarmonyPriority(Priority.Last)]
    public static class Game_LoadGame_Main
    {
        [HarmonyPostfix]
        public static void Postfix(Game __instance)
        {
            try
            {
                Game_LoadGame_InjectThinkTree_Patch.Postfix();


                ThingFilter_Allows_Thing_Patch.DisableForDialog();
                ThingFilter_Allows_Thing_Patch.InvalidateCache();

                if (AutoArmMod.settings?.modEnabled == true &&
                    AutoArmMod.settings?.respectWeaponBonds == true &&
                    ModsConfig.RoyaltyActive)
                {
                    AutoArmMod.MarkAllBondedWeaponsAsForcedOnLoad();
                }

                PawnValidationCache.ClearCache();

                AutoArmLogger.Debug(() => "PawnValidationCache cleared on game load");

                EquipEligibilityCache.Clear();

                EquipCooldownTracker.ClearAll();

                AutoArmLogger.Debug(() => "EquipEligibilityCache and EquipCooldownTracker cleared on game load");

                OutfitFilterCache.RebuildCache();
            }
            catch (Exception e)
            {
                AutoArmLogger.ErrorPatch(e, "Game_LoadGame_Main");
            }
        }
    }

    [HarmonyPatch(typeof(Game), nameof(Game.InitNewGame))]
    [HarmonyPatchCategory(PatchCategories.Core)]
    [HarmonyAfter("PeteTimesSix.SimpleSidearms")]
    [HarmonyPriority(Priority.Last)]
    public static class Game_InitNewGame_Main
    {
        [HarmonyPostfix]
        public static void Postfix(Game __instance)
        {
            try
            {
                ThingFilter_Allows_Thing_Patch.DisableForDialog();
                ThingFilter_Allows_Thing_Patch.InvalidateCache();


                OutfitFilterCache.RebuildCache();

                EquipEligibilityCache.Clear();

                EquipCooldownTracker.ClearAll();

                AutoArmLogger.Debug(() => "EquipEligibilityCache and EquipCooldownTracker cleared on new game init");
            }
            catch (Exception e)
            {
                AutoArmLogger.ErrorPatch(e, "Game_InitNewGame_Main");
            }
        }
    }

    /// <summary>
    /// Equipment tracking
    /// Inventory management
    /// </summary>
    [HarmonyPatch(typeof(Pawn_EquipmentTracker), "AddEquipment")]
    [HarmonyPatchCategory(PatchCategories.Core)]
    public static class Pawn_EquipmentTracker_AddEquipment_Main
    {
        [HarmonyPrefix]
        public static bool Prefix(ThingWithComps newEq, Pawn ___pawn)
        {
            if (newEq == null || ___pawn == null)
                return true;

            if (AutoArmMod.settings?.modEnabled != true)
                return true;

            if (___pawn.equipment?.Primary != null &&
                ___pawn.inventory?.innerContainer?.Contains(newEq) == true)
            {
                return false;
            }

            return true;
        }

        [HarmonyPostfix]
        public static void Postfix(ThingWithComps newEq, Pawn ___pawn)
        {
            try
            {
                var settings = AutoArmMod.settings;

                if (settings?.modEnabled != true)
                    return;

                if (newEq == null)
                    return;

                bool isProperWeapon = WeaponValidation.IsWeapon(newEq);

                if (___pawn != null && ___pawn.IsColonist && isProperWeapon)
            {
                ForcedWeaponState.WeaponPickedUp(newEq);

                var weaponCouldntMove = AutoEquipState.GetWeaponCannotMoveToInventory(___pawn);
                if (weaponCouldntMove != null)
                {
                    if (newEq.Position != IntVec3.Invalid && ___pawn.Map != null)
                    {
                        foreach (var thing in newEq.Position.GetThingList(___pawn.Map))
                        {
                            if (thing == weaponCouldntMove)
                            {
                                DroppedItemTracker.MarkAsDropped(weaponCouldntMove, Constants.LongDropCooldownTicks, ___pawn);

                                if (settings?.debugLogging == true)
                                {
                                    AutoArmLogger.Debug(() => $"[{___pawn.LabelShort}] Swapped {AutoArmLogger.GetWeaponLabelLower(weaponCouldntMove)} at target location, marked as dropped");
                                }
                                break;
                            }
                        }
                    }

                    AutoEquipState.ClearWeaponCannotMoveToInventory(___pawn);
                }

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
                    EquipCooldownTracker.Record(___pawn);

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

                    if (SimpleSidearmsCompat.CanAutoEquipSidearms())
                    {
                        var lastDropped = DroppedItemTracker.GetLastDropped(___pawn);
                        if (lastDropped != null && lastDropped.def == newEq.def &&
                            lastDropped.Position.InHorDistOf(___pawn.Position, 10f))
                        {
                            try
                            {
                                if (SimpleSidearmsCompat.IsManagingPawn(___pawn))
                                {
                                    SimpleSidearmsCompat.InformOfDroppedWeapon(___pawn, lastDropped);
                                    SimpleSidearmsCompat.InformOfAddedSidearm(___pawn, newEq);

                                    if (settings.debugLogging)
                                    {
                                        AutoArmLogger.Debug(() => $"[{___pawn.LabelShort}] Updated SimpleSidearms memory: forgot {AutoArmLogger.GetWeaponLabelLower(lastDropped)}, added {AutoArmLogger.GetWeaponLabelLower(newEq)}");
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
                else if (settings.debugLogging)
                {
                    if (___pawn?.CurJob == null || ___pawn.CurJob.def != JobDefOf.Equip)
                    {
                        AutoArmLogger.Debug(() => $"[{AutoArmLogger.GetPawnName(___pawn)}] Equipped {AutoArmLogger.GetWeaponLabelLower(newEq)} (manual/other)");
                    }
                }
            }

            if (isProperWeapon)
            {
                WeaponCacheManager.RemoveWeaponFromCache(newEq);
                WeaponCacheManager.ClearTemporaryReservation(newEq);
            }
            }
            catch (Exception e)
            {
                AutoArmLogger.ErrorPatch(e, "Pawn_EquipmentTracker_AddEquipment_Main");
            }
        }
    }

    [HarmonyPatch(typeof(Pawn_EquipmentTracker), "TryDropEquipment")]
    [HarmonyPatchCategory(PatchCategories.Core)]
    public static class Pawn_EquipmentTracker_TryDropEquipment_Main
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

                bool isSameTypeUpgrade = DroppedItemTracker.IsPendingSameTypeUpgrade(resultingEq);

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
                    DroppedItemTracker.ClearPendingUpgrade(resultingEq);
                    DroppedItemTracker.MarkAsDropped(resultingEq, 1200);
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
                        DroppedItemTracker.MarkAsDropped(resultingEq, DroppedItemTracker.DefaultIgnoreTicks);

                        if (settings.debugLogging)
                        {
                            AutoArmLogger.Debug(() => $"[{___pawn.LabelShort}] Player dropped {AutoArmLogger.GetWeaponLabelLower(resultingEq)}, applying {DroppedItemTracker.DefaultIgnoreTicks} tick cooldown");
                        }
                    }
                }

                if (WeaponValidation.IsWeapon(resultingEq) && resultingEq.Map != null)
                {
                    WeaponCacheManager.AddWeaponToCache(resultingEq);
                }
            }
            catch (Exception e)
            {
                AutoArmLogger.ErrorPatch(e, "Pawn_EquipmentTracker_TryDropEquipment_Main");
            }
        }
    }

    [HarmonyPatch(typeof(Pawn_JobTracker), "StartJob")]
    [HarmonyAfter("PeteTimesSix.SimpleSidearms", "CETeam.CombatExtended")]
    [HarmonyPatchCategory(PatchCategories.Core)]
    public static class Pawn_JobTracker_StartJob_Combined_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(Job newJob, Pawn ___pawn)
        {
            var settings = AutoArmMod.settings;

            if (settings?.modEnabled != true)
                return;

            if (newJob == null || ___pawn == null)
                return;

            if (!___pawn.IsColonist || ___pawn.Destroyed)
                return;

            if ((newJob.def == JobDefOf.Equip || newJob.def?.defName == "EquipSecondary") &&
                newJob.targetA.Thing is ThingWithComps targetWeapon)
            {
                if (targetWeapon != null && WeaponValidation.IsWeapon(targetWeapon))
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

                        if (newJob.def?.defName == "EquipSecondary")
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
                    else if (isPartOfSidearmUpgrade)
                    {
                    }
                }
            }
            else if (SimpleSidearmsCompat.IsLoaded)
            {
            }
        }
    }

    [HarmonyPatch(typeof(Pawn_EquipmentTracker), "DestroyEquipment")]
    [HarmonyPatchCategory(PatchCategories.Core)]
    public static class Pawn_EquipmentTracker_DestroyEquipment_Patch
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

                ForcedWeapons.ClearForced(___pawn);
            }
            catch (Exception e)
            {
                AutoArmLogger.ErrorPatch(e, "Pawn_EquipmentTracker_DestroyEquipment");
            }
        }
    }

    [HarmonyPatch(typeof(Pawn_JobTracker), "EndCurrentJob")]
    [HarmonyPatchCategory(PatchCategories.Core)]
    public static class Pawn_JobTracker_EndCurrentJob_Patch
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
                if (isEquipJob && condition == JobCondition.Succeeded)
            {
                JobGiver_PickUpBetterWeapon.RecordWeaponEquip(___pawn);
                EquipCooldownTracker.Record(___pawn);

                if (settings.debugLogging && ___curJob.targetA.Thing is ThingWithComps equippedWeapon)
                {
                    AutoArmLogger.Debug(() => $"[{___pawn.LabelShort}] Equipped {AutoArmLogger.GetWeaponLabelLower(equippedWeapon)}");
                }
            }

            if (isEquipJob && ___curJob.targetA.Thing is ThingWithComps weapon)
            {
                if (condition == JobCondition.Errored)
                {
                    if (AutoEquipState.IsAutoEquip(___curJob))
                    {
                        string cantReason;
                        if (!EquipmentUtility.CanEquip(weapon, ___pawn, out cantReason, checkBonded: false))
                        {
                            WeaponBlacklist.AddToBlacklist(weapon.def, ___pawn, cantReason);
                        }
                        else if (settings.debugLogging)
                        {
                            AutoArmLogger.Debug(() => $"[{___pawn.LabelShort}] Equip job errored for {AutoArmLogger.GetWeaponLabelLower(weapon)}, but CanEquip passed (unknown issue, not blacklisting)");
                        }
                    }
                }
                else if (condition == JobCondition.Incompletable && AutoEquipState.IsAutoEquip(___curJob))
                {
                    if (settings.debugLogging)
                    {
                        string issue = weapon.IsForbidden(___pawn) ? "forbidden" :
                                      !___pawn.CanReserve(weapon) ? "reservation conflict" :
                                      !___pawn.CanReach(weapon, PathEndMode.Touch, Danger.Deadly) ? "unreachable" :
                                      "unknown";
                        AutoArmLogger.Debug(() => $"[{___pawn.LabelShort}] Equip job incompletable for {AutoArmLogger.GetWeaponLabelLower(weapon)} ({issue}, not blacklisting)");
                    }
                }
            }

            if (isEquipJob && AutoEquipState.IsAutoEquip(___curJob))
            {
                if (condition != JobCondition.Ongoing &&
                    condition != JobCondition.QueuedNoLongerValid)
                {
                    AutoEquipState.Clear(___curJob);
                }
            }
            }
            catch (Exception e)
            {
                AutoArmLogger.ErrorPatch(e, "Pawn_JobTracker_EndCurrentJob");
            }
        }
    }

    [HarmonyPatch(typeof(OutfitDatabase), "MakeNewOutfit")]
    [HarmonyPatchCategory(PatchCategories.UI)]
    public static class OutfitDatabase_MakeNewOutfit_Main
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            try
            {
                ThingFilter_Allows_Thing_Patch.InvalidateCache();
                OutfitFilterCache.RebuildCache();
            }
            catch (Exception e)
            {
                AutoArmLogger.ErrorPatch(e, "OutfitDatabase_MakeNewOutfit");
            }
        }
    }

    [HarmonyPatch(typeof(OutfitDatabase), "TryDelete")]
    [HarmonyPatchCategory(PatchCategories.UI)]
    public static class OutfitDatabase_TryDelete_Main
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            try
            {
                ThingFilter_Allows_Thing_Patch.InvalidateCache();
                OutfitFilterCache.RebuildCache();
            }
            catch (Exception e)
            {
                AutoArmLogger.ErrorPatch(e, "OutfitDatabase_TryDelete");
            }
        }
    }

    [HarmonyPatch(typeof(Dialog_ModSettings), "PreClose")]
    [HarmonyPatchCategory(PatchCategories.UI)]
    public static class Dialog_ModSettings_PreClose_Patch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            try
            {

                SimpleSidearmsCompat.InvalidateAllCaches();

                WeaponCacheManager.ClearScoreCache();

                PawnValidationCache.ClearCache();

                GenericCache.ClearAll();

                if (AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.Debug(() => "[Settings] Invalidated all caches after mod settings closed");
                }
            }
            catch (Exception e)
            {
                AutoArmLogger.ErrorPatch(e, "Dialog_ModSettings_PostClose");
            }
        }
    }
}
