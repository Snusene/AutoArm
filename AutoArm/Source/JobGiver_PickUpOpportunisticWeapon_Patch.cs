using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace AutoArm
{
    // High priority patch to track player-forced weapon equips
    [HarmonyPatch(typeof(Pawn_JobTracker), "StartJob")]
    [HarmonyPriority(Priority.High)]
    public static class Pawn_JobTracker_TrackForcedEquip_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(Job newJob, Pawn ___pawn)
        {
            // Track when player forces a weapon equip
            if (newJob?.def == JobDefOf.Equip && newJob.playerForced && ___pawn.IsColonist)
            {
                var targetWeapon = newJob.targetA.Thing as ThingWithComps;
                if (targetWeapon?.def.IsWeapon == true)
                {
                    ForcedWeaponTracker.SetForced(___pawn, targetWeapon);
                }
            }
            // Also check for Simple Sidearms weapon switches
            else if (newJob?.def == JobDefOf.Equip && ___pawn.IsColonist)
            {
                var targetWeapon = newJob.targetA.Thing as ThingWithComps;
                if (targetWeapon?.def.IsWeapon == true && SimpleSidearmsCompat.IsSimpleSidearmsSwitch(___pawn, targetWeapon))
                {
                    ForcedWeaponTracker.SetForced(___pawn, targetWeapon);
                }
            }
        }
    }

    // Patch to detect when Simple Sidearms EquipSecondary job is player-forced
    [HarmonyPatch(typeof(Pawn_JobTracker), "StartJob")]
    [HarmonyPriority(Priority.High)]
    public static class Pawn_JobTracker_TrackForcedSidearmEquip_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(Job newJob, Pawn ___pawn)
        {
            // Skip if not a colonist or Simple Sidearms isn't loaded
            if (!___pawn.IsColonist || !SimpleSidearmsCompat.IsLoaded())
                return;

            // Check if this is a Simple Sidearms EquipSecondary job
            if (newJob?.def?.defName == "EquipSecondary" && newJob.playerForced)
            {
                var targetWeapon = newJob.targetA.Thing as ThingWithComps;
                if (targetWeapon?.def.IsWeapon == true)
                {
                    ForcedWeaponTracker.SetForcedSidearm(___pawn, targetWeapon.def);

                    if (AutoArmMod.settings?.debugLogging == true)
                    {
                        Log.Message($"[AutoArm] Player manually equipped {targetWeapon.Label} as sidearm for {___pawn.Name}");
                    }
                }
            }
        }
    }

    // Patch to clear forced weapon when dropped
    [HarmonyPatch(typeof(Pawn_EquipmentTracker), "TryDropEquipment")]
    public static class Pawn_EquipmentTracker_ClearForcedOnDrop_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(bool __result, Pawn ___pawn)
        {
            if (__result && ___pawn.IsColonist)
            {
                ForcedWeaponTracker.ClearForced(___pawn);

                // Mark as recently unarmed for urgent checks
                if (___pawn.equipment?.Primary == null)
                {
                    Pawn_TickRare_Unified_Patch.MarkRecentlyUnarmed(___pawn);
                }
            }
        }
    }

    // Also clear when equipment is destroyed
    [HarmonyPatch(typeof(Pawn_EquipmentTracker), "DestroyEquipment")]
    public static class Pawn_EquipmentTracker_ClearForcedOnDestroy_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn ___pawn)
        {
            if (___pawn.IsColonist)
            {
                ForcedWeaponTracker.ClearForced(___pawn);

                // Mark as recently unarmed for urgent checks
                if (___pawn.equipment?.Primary == null)
                {
                    Pawn_TickRare_Unified_Patch.MarkRecentlyUnarmed(___pawn);
                }
            }
        }
    }

    // Also patch when sidearms are dropped/removed
    [HarmonyPatch(typeof(Pawn_InventoryTracker), "Notify_ItemRemoved")]
    public static class Pawn_InventoryTracker_ClearForcedSidearm_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Thing item, Pawn ___pawn)
        {
            if (item is ThingWithComps weapon && weapon.def.IsWeapon && ___pawn.IsColonist)
            {
                ForcedWeaponTracker.ClearForcedSidearm(___pawn, weapon.def);
            }
        }
    }

    // Track auto-equipped weapons for notifications
    public static class AutoEquipTracker
    {
        private static HashSet<int> autoEquipJobIds = new HashSet<int>();

        // Track the previous weapon for upgrade notifications
        private static Dictionary<Pawn, ThingDef> previousWeapons = new Dictionary<Pawn, ThingDef>();

        public static void MarkAutoEquip(Job job, Pawn pawn = null)
        {
            if (job != null)
            {
                autoEquipJobIds.Add(job.loadID);

                // Track the current weapon before equipping new one
                if (pawn != null && job.def == JobDefOf.Equip && job.targetA.Thing is ThingWithComps weapon)
                {
                    if (pawn.equipment?.Primary != null)
                    {
                        previousWeapons[pawn] = pawn.equipment.Primary.def;
                    }
                    else
                    {
                        previousWeapons.Remove(pawn); // Was unarmed
                    }
                }
            }
        }

        public static bool IsAutoEquip(Job job)
        {
            return job != null && autoEquipJobIds.Contains(job.loadID);
        }

        public static void Clear(Job job)
        {
            if (job != null)
                autoEquipJobIds.Remove(job.loadID);
        }

        public static ThingDef GetPreviousWeapon(Pawn pawn)
        {
            previousWeapons.TryGetValue(pawn, out var weapon);
            return weapon;
        }

        public static void ClearPreviousWeapon(Pawn pawn)
        {
            previousWeapons.Remove(pawn);
        }
    }

    // Notify when auto-equipped weapon is equipped
    [HarmonyPatch(typeof(Pawn_EquipmentTracker), "AddEquipment")]
    public static class Pawn_EquipmentTracker_NotifyAutoEquip_Patch
    {

        [HarmonyPostfix]
        public static void Postfix(ThingWithComps newEq, Pawn ___pawn)
        {
            // Check if this was an auto-equip job
            if (___pawn.IsColonist && ___pawn.CurJob?.def == JobDefOf.Equip &&
                AutoEquipTracker.IsAutoEquip(___pawn.CurJob))
            {
                // Show notification like apparel
                if (newEq != null && PawnUtility.ShouldSendNotificationAbout(___pawn) &&
                    AutoArmMod.settings?.showNotifications == true)
                {
                    // Get previous weapon for upgrade notifications
                    var previousWeapon = AutoEquipTracker.GetPreviousWeapon(___pawn);

                    if (previousWeapon != null)
                    {
                        // Upgraded weapon - use translation key
                        Messages.Message("AutoArm_UpgradedWeapon".Translate(
                            ___pawn.LabelShort.CapitalizeFirst(),
                            previousWeapon.label,
                            newEq.Label
                        ), new LookTargets(___pawn), MessageTypeDefOf.SilentInput, false);
                    }
                    else
                    {
                        // Equipped first weapon - use translation key
                        Messages.Message("AutoArm_EquippedWeapon".Translate(
                            ___pawn.LabelShort.CapitalizeFirst(),
                            newEq.Label
                        ), new LookTargets(___pawn), MessageTypeDefOf.SilentInput, false);
                    }
                }

                // Clear the tracking
                AutoEquipTracker.Clear(___pawn.CurJob);
                AutoEquipTracker.ClearPreviousWeapon(___pawn);
            }
        }
    }

    // Force think tree re-evaluation when outfit policy changes
    [HarmonyPatch(typeof(Pawn_OutfitTracker), "CurrentApparelPolicy", MethodType.Setter)]
    public static class Pawn_OutfitTracker_PolicyChanged_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn ___pawn)
        {
            // Skip if mod settings not loaded yet
            if (AutoArmMod.settings?.modEnabled != true)
                return;

            // Only for colonists with weapons
            if (___pawn.IsColonist && ___pawn.Spawned && !___pawn.Dead &&
                ___pawn.equipment?.Primary != null && !___pawn.Drafted)
            {
                // Check if the new policy disallows the current weapon
                var filter = ___pawn.outfits?.CurrentApparelPolicy?.filter;
                if (filter != null && !filter.Allows(___pawn.equipment.Primary.def))
                {
                    // Clear forced weapon tracker
                    ForcedWeaponTracker.ClearForced(___pawn);

                    // Create drop job immediately
                    var dropJob = new Job(JobDefOf.DropEquipment, ___pawn.equipment.Primary);
                    ___pawn.jobs.TryTakeOrderedJob(dropJob, JobTag.Misc);

                    if (AutoArmMod.settings.debugLogging)
                    {
                        Log.Message($"[AutoArm] {___pawn.Name}: Outfit changed, dropping {___pawn.equipment.Primary.Label}");
                    }

                    // Show notification
                    if (PawnUtility.ShouldSendNotificationAbout(___pawn))
                    {
                        Messages.Message("AutoArm_DroppingDisallowed".Translate(
                            ___pawn.LabelShort.CapitalizeFirst(),
                            ___pawn.equipment.Primary.Label
                        ), new LookTargets(___pawn), MessageTypeDefOf.SilentInput, false);
                    }
                }
            }
        }
    }

    // Also check when filter is modified
    [HarmonyPatch(typeof(ThingFilter), "SetAllow", new Type[] { typeof(ThingDef), typeof(bool) })]
    public static class ThingFilter_SetAllow_CheckWeapons_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ThingFilter __instance, ThingDef thingDef, bool allow)
        {
            // Skip if mod is disabled or if allowing (we only care about disallow)
            if (AutoArmMod.settings?.modEnabled != true || allow || !thingDef.IsWeapon)
                return;

            // Skip if game is not fully loaded yet
            if (Current.Game == null || Find.Maps == null)
                return;

            // Collect pawns that need to drop weapons
            var pawnsToDropWeapons = new List<(Pawn pawn, ThingWithComps weapon)>();

            foreach (var map in Find.Maps)
            {
                if (map?.mapPawns?.FreeColonists == null)
                    continue;

                foreach (var pawn in map.mapPawns.FreeColonists)
                {
                    if (pawn.Drafted || pawn.jobs == null)
                        continue;

                    if (pawn.outfits?.CurrentApparelPolicy?.filter == __instance &&
                        pawn.equipment?.Primary?.def == thingDef)
                    {
                        pawnsToDropWeapons.Add((pawn, pawn.equipment.Primary));
                    }
                }
            }

            // Defer weapon dropping to avoid collection modification errors
            if (pawnsToDropWeapons.Count > 0)
            {
                LongEventHandler.ExecuteWhenFinished(() =>
                {
                    foreach (var (pawn, weapon) in pawnsToDropWeapons)
                    {
                        if (pawn.equipment?.Primary == weapon && !pawn.Drafted)
                        {
                            // Clear forced weapon tracker
                            ForcedWeaponTracker.ClearForced(pawn);

                            // Create drop job
                            var dropJob = new Job(JobDefOf.DropEquipment, weapon);
                            pawn.jobs.TryTakeOrderedJob(dropJob, JobTag.Misc);

                            if (AutoArmMod.settings.debugLogging)
                            {
                                Log.Message($"[AutoArm] {pawn.Name}: {thingDef.label} now disallowed, dropping weapon");
                            }

                            // Show notification
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

    // Check when entire filter is cleared
    [HarmonyPatch(typeof(ThingFilter), "SetDisallowAll")]
    public static class ThingFilter_SetDisallowAll_CheckWeapons_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ThingFilter __instance)
        {
            // Skip if mod is disabled
            if (AutoArmMod.settings?.modEnabled != true)
                return;

            // Skip if game is not fully loaded yet
            if (Current.Game == null || Find.Maps == null)
                return;

            // Collect pawns that need to drop weapons
            var pawnsToDropWeapons = new List<(Pawn pawn, ThingWithComps weapon)>();

            foreach (var map in Find.Maps)
            {
                if (map?.mapPawns?.FreeColonists == null)
                    continue;

                foreach (var pawn in map.mapPawns.FreeColonists)
                {
                    if (pawn.Drafted || pawn.jobs == null)
                        continue;

                    if (pawn.outfits?.CurrentApparelPolicy?.filter == __instance &&
                        pawn.equipment?.Primary != null)
                    {
                        pawnsToDropWeapons.Add((pawn, pawn.equipment.Primary));
                    }
                }
            }

            // Defer weapon dropping
            if (pawnsToDropWeapons.Count > 0)
            {
                LongEventHandler.ExecuteWhenFinished(() =>
                {
                    foreach (var (pawn, weapon) in pawnsToDropWeapons)
                    {
                        if (pawn.equipment?.Primary == weapon && !pawn.Drafted)
                        {
                            // Clear forced weapon tracker
                            ForcedWeaponTracker.ClearForced(pawn);

                            // Create drop job
                            var dropJob = new Job(JobDefOf.DropEquipment, weapon);
                            pawn.jobs.TryTakeOrderedJob(dropJob, JobTag.Misc);

                            if (AutoArmMod.settings.debugLogging)
                            {
                                Log.Message($"[AutoArm] {pawn.Name}: All items disallowed, dropping weapon");
                            }

                            // Show notification
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

    // Pawn_TickRare_UnarmedCheck_Patch has been moved to UnifiedTickRarePatch.cs
}