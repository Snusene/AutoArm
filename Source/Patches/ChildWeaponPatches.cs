using AutoArm.Definitions;
using AutoArm.Logging;
using HarmonyLib;
using RimWorld;
using System;
using Verse;
using Verse.AI;

namespace AutoArm.Source.Patches
{
    // Manual patches to allow children to pick up weapons for AutoArm
    // These are applied manually to avoid conflicts with automatic patching
    public static class ChildWeaponPatches
    {
        private static bool patchesApplied = false;

        public static void ApplyPatches(Harmony harmony)
        {
            if (patchesApplied)
                return;

            try
            {
                // Patch EquipmentUtility.CanEquip
                var canEquipMethod = AccessTools.Method(typeof(EquipmentUtility), "CanEquip",
                    new Type[] { typeof(Thing), typeof(Pawn), typeof(string).MakeByRefType(), typeof(bool) });

                if (canEquipMethod != null)
                {
                    var postfix = AccessTools.Method(typeof(ChildWeaponPatches), nameof(CanEquip_Postfix));
                    harmony.Patch(canEquipMethod, postfix: new HarmonyMethod(postfix));
                    AutoArmLogger.Debug("Patched EquipmentUtility.CanEquip for child weapon restrictions");
                }

                // Patch JobGiver_PickUpOpportunisticWeapon.ShouldEquipWeapon
                var shouldEquipMethod = AccessTools.Method(typeof(JobGiver_PickUpOpportunisticWeapon), "ShouldEquipWeapon",
                    new Type[] { typeof(Thing), typeof(Pawn) });
                if (shouldEquipMethod != null)
                {
                    var postfix = AccessTools.Method(typeof(ChildWeaponPatches), nameof(ShouldEquipWeapon_Postfix));
                    harmony.Patch(shouldEquipMethod, postfix: new HarmonyMethod(postfix));
                    AutoArmLogger.Debug("Patched JobGiver_PickUpOpportunisticWeapon.ShouldEquipWeapon for child weapon restrictions");
                }

                // Patch JobGiver_OptimizeApparel.TryGiveJob
                var tryGiveJobMethod = AccessTools.Method(typeof(JobGiver_OptimizeApparel), "TryGiveJob",
                    new Type[] { typeof(Pawn) });
                if (tryGiveJobMethod != null)
                {
                    var postfix = AccessTools.Method(typeof(ChildWeaponPatches), nameof(OptimizeApparel_TryGiveJob_Postfix));
                    harmony.Patch(tryGiveJobMethod, postfix: new HarmonyMethod(postfix));
                    AutoArmLogger.Debug("Patched JobGiver_OptimizeApparel.TryGiveJob for child weapon restrictions");
                }

                patchesApplied = true;
            }
            catch (Exception ex)
            {
                AutoArmLogger.Error($"Failed to apply child weapon patches: {ex.Message}", ex);
            }
        }

        // Postfix for EquipmentUtility.CanEquip
        private static void CanEquip_Postfix(Thing thing, Pawn pawn, ref bool __result, ref string cantReason)
        {
            // Only modify result if it was false and it's a weapon
            if (!__result && thing != null && thing.def.IsWeapon && pawn?.ageTracker != null)
            {
                // Check if pawn is within our allowed age range
                int biologicalAge = pawn.ageTracker.AgeBiologicalYears;
                var settings = AutoArmMod.settings;

                if (settings != null &&
                    settings.allowChildrenToEquipWeapons &&
                    biologicalAge >= settings.childrenMinAge &&
                    biologicalAge < Constants.ChildMaxAgeLimit)
                {
                    // Check if the reason was due to age/violence restriction
                    if (pawn.WorkTagIsDisabled(WorkTags.Violent))
                    {
                        // Allow weapon equipping for children in our age range
                        __result = true;
                        cantReason = null;
                    }
                }
            }
        }

        // Postfix for JobGiver_PickUpOpportunisticWeapon.ShouldEquipWeapon
        // IMPORTANT: The parameter name must match exactly - it's "newWep" not "weapon"
        private static void ShouldEquipWeapon_Postfix(Thing newWep, Pawn pawn, ref bool __result)
        {
            // Only modify if result was false and pawn is a child
            if (!__result && newWep != null && newWep.def.IsWeapon && pawn?.ageTracker != null)
            {
                int biologicalAge = pawn.ageTracker.AgeBiologicalYears;
                var settings = AutoArmMod.settings;

                if (settings != null &&
                    settings.allowChildrenToEquipWeapons &&
                    biologicalAge >= settings.childrenMinAge &&
                    biologicalAge < Constants.ChildMaxAgeLimit)
                {
                    // Allow opportunistic weapon pickup for children in our age range
                    __result = true;
                }
            }
        }

        // Postfix for JobGiver_OptimizeApparel.TryGiveJob
        private static void OptimizeApparel_TryGiveJob_Postfix(Pawn pawn, ref Job __result)
        {
            // If the pawn is a child within our age range and they're trying to equip a weapon
            if (__result != null &&
                __result.def == JobDefOf.Wear &&
                __result.targetA.Thing?.def.IsWeapon == true &&
                pawn?.ageTracker != null)
            {
                int biologicalAge = pawn.ageTracker.AgeBiologicalYears;
                var settings = AutoArmMod.settings;

                if (settings != null &&
                    settings.allowChildrenToEquipWeapons &&
                    biologicalAge >= settings.childrenMinAge &&
                    biologicalAge < Constants.ChildMaxAgeLimit)
                {
                    // The job is already created, so we don't need to modify it
                    // This patch just ensures children can execute weapon equip jobs
                }
            }
        }
    }
}