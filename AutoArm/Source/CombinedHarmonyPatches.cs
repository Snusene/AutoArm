using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Linq;
using Verse;
using Verse.AI;

namespace AutoArm
{
    // COMBINED: Single patch for Pawn_JobTracker.StartJob instead of multiple
    [HarmonyPatch(typeof(Pawn_JobTracker), "StartJob")]
    [HarmonyPriority(Priority.High)]
    public static class Pawn_JobTracker_StartJob_Combined_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(Job newJob, Pawn ___pawn)
        {
            // NULL CHECK: Check all parameters
            if (newJob == null || ___pawn == null)
                return;

            // NULL CHECK: Check pawn properties
            if (!___pawn.IsColonist || ___pawn.Destroyed)
                return;

            // Handle regular weapon equipping
            if (newJob.def == JobDefOf.Equip && newJob.targetA.Thing is ThingWithComps targetWeapon)
            {
                if (targetWeapon?.def?.IsWeapon == true)
                {
                    // Player-forced equip
                    if (newJob.playerForced)
                    {
                        ForcedWeaponTracker.SetForced(___pawn, targetWeapon);
                    }
                    // Simple Sidearms switch
                    else if (SimpleSidearmsCompat.IsSimpleSidearmsSwitch(___pawn, targetWeapon))
                    {
                        ForcedWeaponTracker.SetForced(___pawn, targetWeapon);
                    }
                }
            }
            // Handle sidearm equipping
            else if (SimpleSidearmsCompat.IsLoaded() &&
                     newJob.def?.defName == "EquipSecondary" &&
                     newJob.playerForced)
            {
                var sidearmWeapon = newJob.targetA.Thing as ThingWithComps;
                if (sidearmWeapon?.def?.IsWeapon == true)
                {
                    ForcedWeaponTracker.SetForcedSidearm(___pawn, sidearmWeapon.def);

                    if (AutoArmMod.settings?.debugLogging == true)
                    {
                        Log.Message($"[AutoArm] Player manually equipped {sidearmWeapon.Label} as sidearm for {___pawn.Name}");
                    }
                }
            }
        }

        // Debug postfix
        [HarmonyPostfix]
        public static void Postfix(Job newJob, Pawn ___pawn)
        {
            // NULL CHECK
            if (newJob == null || ___pawn == null || AutoArmMod.settings?.debugLogging != true)
                return;

            if (newJob.def == JobDefOf.Equip && AutoEquipTracker.IsAutoEquip(newJob))
            {
                Log.Message($"[AutoArm DEBUG] {___pawn.Name}: Starting equip job for {newJob.targetA.Thing?.Label}");
            }
        }
    }

    // Updated patch with null checks
    [HarmonyPatch(typeof(Pawn_EquipmentTracker), "TryDropEquipment")]
    public static class Pawn_EquipmentTracker_TryDropEquipment_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(bool __result, Pawn ___pawn)
        {
            // NULL CHECK
            if (!__result || ___pawn == null || !___pawn.IsColonist)
                return;

            ForcedWeaponTracker.ClearForced(___pawn);
        }
    }

    // Updated patch with null checks
    [HarmonyPatch(typeof(Pawn_EquipmentTracker), "DestroyEquipment")]
    public static class Pawn_EquipmentTracker_DestroyEquipment_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn ___pawn)
        {
            // NULL CHECK
            if (___pawn == null || !___pawn.IsColonist)
                return;

            ForcedWeaponTracker.ClearForced(___pawn);
        }
    }

    // Updated patch with null checks
    [HarmonyPatch(typeof(Pawn_InventoryTracker), "Notify_ItemRemoved")]
    public static class Pawn_InventoryTracker_Notify_ItemRemoved_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(Thing item, Pawn ___pawn)
        {
            if (item == null || ___pawn == null || !___pawn.IsColonist)
                return;

            var weapon = item as ThingWithComps;
            if (weapon == null || !weapon.def.IsWeapon)
                return;

            // Check if dropping due to outfit policy
            var filter = ___pawn.outfits?.CurrentApparelPolicy?.filter;
            if (filter != null && !filter.Allows(weapon.def) &&
                AutoArmMod.settings?.showNotifications == true &&
                PawnUtility.ShouldSendNotificationAbout(___pawn))
            {
                Messages.Message("AutoArm_DroppingSidearmDisallowed".Translate(
                    ___pawn.LabelShort.CapitalizeFirst(),
                    weapon.Label
                ), new LookTargets(___pawn), MessageTypeDefOf.SilentInput, false);
            }
        }
    }

    // Updated patch with null checks
    [HarmonyPatch(typeof(Pawn_EquipmentTracker), "AddEquipment")]
    public static class Pawn_EquipmentTracker_AddEquipment_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ThingWithComps newEq, Pawn ___pawn)
        {
            // NULL CHECK
            if (newEq == null || ___pawn == null || !___pawn.IsColonist)
                return;

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
                            previousWeapon.label,
                            newEq.Label
                        ), new LookTargets(___pawn), MessageTypeDefOf.SilentInput, false);
                    }
                    else
                    {
                        Messages.Message("AutoArm_EquippedWeapon".Translate(
                            ___pawn.LabelShort.CapitalizeFirst(),
                            newEq.Label
                        ), new LookTargets(___pawn), MessageTypeDefOf.SilentInput, false);
                    }
                }

                AutoEquipTracker.Clear(___pawn.CurJob);
                AutoEquipTracker.ClearPreviousWeapon(___pawn);
            }
        }
    }

    // Updated patch with null checks
    [HarmonyPatch(typeof(Pawn_OutfitTracker), "CurrentApparelPolicy", MethodType.Setter)]
    public static class Pawn_OutfitTracker_CurrentApparelPolicy_Setter_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn ___pawn)
        {
            // NULL CHECK
            if (___pawn == null || AutoArmMod.settings?.modEnabled != true)
                return;

            if (!___pawn.IsColonist || !___pawn.Spawned || ___pawn.Dead || ___pawn.Drafted)
                return;

            if (___pawn.equipment?.Primary == null || ___pawn.jobs == null)
                return;

            var filter = ___pawn.outfits?.CurrentApparelPolicy?.filter;
            if (filter != null && !filter.Allows(___pawn.equipment.Primary.def))
            {
                ForcedWeaponTracker.ClearForced(___pawn);

                var dropJob = new Job(JobDefOf.DropEquipment, ___pawn.equipment.Primary);
                ___pawn.jobs.TryTakeOrderedJob(dropJob, JobTag.Misc);

                if (AutoArmMod.settings?.debugLogging == true)
                {
                    Log.Message($"[AutoArm] {___pawn.Name}: Outfit changed, dropping {___pawn.equipment.Primary.Label}");
                }

                if (PawnUtility.ShouldSendNotificationAbout(___pawn))
                {
                    Messages.Message("AutoArm_DroppingDisallowed".Translate(
                        ___pawn.LabelShort.CapitalizeFirst(),
                        ___pawn.equipment.Primary.Label
                    ), new LookTargets(___pawn), MessageTypeDefOf.SilentInput, false);
                }
            }

            // Also clear cache for immediate re-evaluation
            if (Pawn_TickRare_Unified_Patch.lastWeaponSearchTick.ContainsKey(___pawn))
            {
                Pawn_TickRare_Unified_Patch.lastWeaponSearchTick.Remove(___pawn);
            }
        }
    }

    // Updated patch with null checks
    [HarmonyPatch(typeof(ThingFilter), "SetDisallowAll")]
    public static class ThingFilter_SetDisallowAll_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ThingFilter __instance)
        {
            // NULL CHECK
            if (__instance == null || AutoArmMod.settings?.modEnabled != true)
                return;

            if (Current.Game == null || Find.Maps == null)
                return;

            var pawnsToDropWeapons = new System.Collections.Generic.List<(Pawn pawn, ThingWithComps weapon)>();

            foreach (var map in Find.Maps)
            {
                // NULL CHECK
                if (map?.mapPawns?.FreeColonists == null)
                    continue;

                foreach (var pawn in map.mapPawns.FreeColonists)
                {
                    // NULL CHECK
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
                        // NULL CHECK again in case things changed
                        if (pawn?.equipment?.Primary == weapon && !pawn.Drafted && pawn.jobs != null)
                        {
                            ForcedWeaponTracker.ClearForced(pawn);

                            var dropJob = new Job(JobDefOf.DropEquipment, weapon);
                            pawn.jobs.TryTakeOrderedJob(dropJob, JobTag.Misc);

                            if (AutoArmMod.settings?.debugLogging == true)
                            {
                                Log.Message($"[AutoArm] {pawn.Name}: All items disallowed, dropping weapon");
                            }

                            if (PawnUtility.ShouldSendNotificationAbout(pawn))
                            {
                                Messages.Message("AutoArm_DroppingDisallowed".Translate(
                                    pawn.LabelShort.CapitalizeFirst(),
                                    weapon.Label
                                ), new LookTargets(pawn), MessageTypeDefOf.SilentInput, false);
                            }
                        }
                    }
                });
            }
        }
    }

    // Updated patch with null checks
    [HarmonyPatch(typeof(Pawn_JobTracker), "EndCurrentJob")]
    public static class Pawn_JobTracker_EndCurrentJob_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(Pawn ___pawn, JobCondition condition, Job ___curJob)
        {
            // NULL CHECK
            if (___pawn == null || ___curJob == null || AutoArmMod.settings?.debugLogging != true)
                return;

            if (___curJob.def == JobDefOf.Equip && AutoEquipTracker.IsAutoEquip(___curJob))
            {
                Log.Message($"[AutoArm DEBUG] {___pawn.Name}: Ending equip job for {___curJob.targetA.Thing?.Label} - Reason: {condition}");
            }
        }
    }

    // Updated patch with null checks
    [HarmonyPatch(typeof(Thing), "SpawnSetup")]
    public static class Thing_SpawnSetup_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Thing __instance)
        {
            // NULL CHECK and early exit
            if (__instance == null || __instance.def == null)
                return;

            if (__instance.def.category != ThingCategory.Item || !__instance.def.IsWeapon)
                return;

            if (__instance is ThingWithComps weapon && weapon.Map != null)
            {
                ImprovedWeaponCacheManager.AddWeaponToCache(weapon);
            }
        }
    }

    // Updated patch with null checks
    [HarmonyPatch(typeof(Thing), "DeSpawn")]
    public static class Thing_DeSpawn_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(Thing __instance)
        {
            // NULL CHECK and early exit
            if (__instance == null || __instance.def == null)
                return;

            if (__instance.def.category != ThingCategory.Item || !__instance.def.IsWeapon)
                return;

            if (__instance is ThingWithComps weapon)
            {
                ImprovedWeaponCacheManager.RemoveWeaponFromCache(weapon);
            }
        }
    }

    // Updated patch with null checks
    [HarmonyPatch(typeof(Thing), "set_Position")]
    public static class Thing_SetPosition_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(Thing __instance, IntVec3 value)
        {
            // NULL CHECK and early exit
            if (__instance == null || __instance.def == null || !__instance.Spawned)
                return;

            if (__instance.def.category != ThingCategory.Item || !__instance.def.IsWeapon)
                return;

            if (__instance is ThingWithComps weapon && weapon.Map != null)
            {
                ImprovedWeaponCacheManager.UpdateWeaponPosition(weapon, __instance.Position, value);
            }
        }
    }

    // Updated patch with null checks
    [HarmonyPatch(typeof(ThinkNode_JobGiver), "TryIssueJobPackage")]
    public static class ThinkNode_JobGiver_TryIssueJobPackage_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ThinkNode_JobGiver __instance, Pawn pawn, JobIssueParams jobParams, ThinkResult __result)
        {
            // NULL CHECK
            if (__instance == null || pawn == null || AutoArmMod.settings?.debugLogging != true)
                return;

            if (__instance is JobGiver_PickUpBetterWeapon && __result.Job != null)
            {
                Log.Message($"[AutoArm DEBUG] {pawn.Name}: {__instance.GetType().Name} issued job: {__result.Job.def.defName} targeting {__result.Job.targetA.Thing?.Label}");
            }
        }
    }
    [HarmonyPatch(typeof(Pawn_InventoryTracker), "TryAddItemNotForSale")]
    public static class Pawn_InventoryTracker_TryAddItemNotForSale_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(bool __result, Thing item, Pawn ___pawn)
        {
            // Check if successfully added a weapon to inventory
            if (!__result || item == null || ___pawn == null || !___pawn.IsColonist)
                return;

            var weapon = item as ThingWithComps;
            if (weapon == null || !weapon.def.IsWeapon)
                return;

            // Check if this was from a sidearm job
            if (___pawn.CurJob?.def?.defName == "EquipSecondary" &&
                AutoArmMod.settings?.showNotifications == true &&
                PawnUtility.ShouldSendNotificationAbout(___pawn))
            {
                // Check if it's an upgrade
                var existingSidearms = ___pawn.inventory?.innerContainer
                    .OfType<ThingWithComps>()
                    .Where(t => t.def.IsWeapon && t != weapon && t.def == weapon.def)
                    .ToList();

                if (existingSidearms?.Any() == true)
                {
                    // It's an upgrade of an existing sidearm type
                    var oldWeapon = existingSidearms.First();

                    Messages.Message("AutoArm_UpgradedSidearm".Translate(
                        ___pawn.LabelShort.CapitalizeFirst(),
                        oldWeapon.Label,
                        weapon.Label
                    ), new LookTargets(___pawn), MessageTypeDefOf.SilentInput, false);
                }
                else
                {
                    // It's a new sidearm
                    Messages.Message("AutoArm_EquippedSidearm".Translate(
                        ___pawn.LabelShort.CapitalizeFirst(),
                        weapon.Label
                    ), new LookTargets(___pawn), MessageTypeDefOf.SilentInput, false);
                }
            }
        }
    }
}