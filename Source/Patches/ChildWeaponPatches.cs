using AutoArm.Definitions;
using AutoArm.Logging;
using HarmonyLib;
using RimWorld;
using System;
using Verse;

namespace AutoArm.Patches
{
    public static class ChildWeaponPatches
    {
        private static bool patchesApplied = false;

        public static void ApplyPatches(Harmony harmony)
        {
            if (patchesApplied)
                return;

            try
            {
                var canEquipMethod = AccessTools.Method(typeof(EquipmentUtility), "CanEquip",
                    new Type[] { typeof(Thing), typeof(Pawn), typeof(string).MakeByRefType(), typeof(bool) });

                if (canEquipMethod != null)
                {
                    try
                    {
                        var postfix = AccessTools.Method(typeof(ChildWeaponPatches), nameof(CanEquip_Postfix));
                        if (postfix != null)
                        {
                            var harmonyMethod = new HarmonyMethod(postfix);
                            harmonyMethod.priority = Priority.Normal;
                            harmony.Patch(canEquipMethod, postfix: harmonyMethod);
                            AutoArmLogger.Debug(() => "Patched EquipmentUtility.CanEquip for child weapon restrictions");
                        }
                        else
                        {
                            AutoArmLogger.Warn("ChildWeaponPatches: CanEquip_Postfix method not found");
                        }
                    }
                    catch (Exception ex)
                    {
                        AutoArmLogger.Warn($"ChildWeaponPatches: Failed to patch CanEquip: {ex.Message}");
                    }
                }
                else
                {
                    AutoArmLogger.Debug(() => "ChildWeaponPatches: EquipmentUtility.CanEquip method not found (may be normal for this game version)");
                }

                var shouldEquipMethod = AccessTools.Method(typeof(JobGiver_PickUpOpportunisticWeapon), "ShouldEquipWeapon",
                    new Type[] { typeof(Thing), typeof(Pawn) });
                if (shouldEquipMethod != null)
                {
                    var postfix = AccessTools.Method(typeof(ChildWeaponPatches), nameof(ShouldEquipWeapon_Postfix));
                    var harmonyMethod = new HarmonyMethod(postfix);
                    harmonyMethod.priority = Priority.Normal;
                    harmony.Patch(shouldEquipMethod, postfix: harmonyMethod);
                    AutoArmLogger.Debug(() => "Patched JobGiver_PickUpOpportunisticWeapon.ShouldEquipWeapon for child weapon restrictions");
                }

                patchesApplied = true;
            }
            catch (Exception ex)
            {
                AutoArmLogger.ErrorPatch(ex, "ChildWeaponPatches");
            }
        }

        private static void CanEquip_Postfix(Thing thing, Pawn pawn, ref bool __result, ref string cantReason)
        {
            if (!__result && thing != null && thing.def.IsWeapon && pawn != null)
            {
                var settings = AutoArmMod.settings;
                var devStage = pawn.DevelopmentalStage;
                bool sliderActive = settings?.allowChildrenToEquipWeapons ?? false;

                bool canEquip = false;
                if (!sliderActive)
                {
                    // Match vanilla: Child and Adult can equip, only Baby blocked
                    canEquip = devStage >= DevelopmentalStage.Child;
                }
                else
                {
                    // Slider active: apply minAge restriction
                    bool isRaceAdult = pawn.ageTracker?.Adult == true;
                    int minAge = settings?.childrenMinAge ?? Constants.ChildDefaultMinAge;
                    int age = pawn.ageTracker?.AgeBiologicalYears ?? 0;
                    canEquip = isRaceAdult || age >= minAge;
                }

                if (canEquip && pawn.WorkTagIsDisabled(WorkTags.Violent))
                {
                    __result = true;
                    cantReason = null;
                }
            }
        }

        private static void ShouldEquipWeapon_Postfix(Thing newWep, Pawn pawn, ref bool __result)
        {
            if (!__result && newWep != null && newWep.def.IsWeapon && pawn != null)
            {
                var settings = AutoArmMod.settings;
                var devStage = pawn.DevelopmentalStage;
                bool sliderActive = settings?.allowChildrenToEquipWeapons ?? false;

                bool canEquip = false;
                if (!sliderActive)
                {
                    // Match vanilla: Child and Adult can equip, only Baby blocked
                    canEquip = devStage >= DevelopmentalStage.Child;
                }
                else
                {
                    // Slider active: apply minAge restriction
                    bool isRaceAdult = pawn.ageTracker?.Adult == true;
                    int minAge = settings?.childrenMinAge ?? Constants.ChildDefaultMinAge;
                    int age = pawn.ageTracker?.AgeBiologicalYears ?? 0;
                    canEquip = isRaceAdult || age >= minAge;
                }

                if (canEquip)
                {
                    __result = true;
                }
            }
        }
    }
}
