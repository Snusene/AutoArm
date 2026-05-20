using AutoArm.Definitions;
using HarmonyLib;
using RimWorld;
using System;
using Verse;

namespace AutoArm.Patches
{
    internal static class ChildWeapon
    {
        private static bool patchesApplied = false;
        private static System.Reflection.MethodBase _canEquipTarget;
        private static System.Reflection.MethodBase _shouldEquipTarget;

        public static void UnpatchPatches(Harmony harmony)
        {
            if (!patchesApplied || harmony == null) return;
            try
            {
                if (_canEquipTarget != null)
                    harmony.Unpatch(_canEquipTarget, HarmonyPatchType.Postfix, harmony.Id);
                if (_shouldEquipTarget != null)
                    harmony.Unpatch(_shouldEquipTarget, HarmonyPatchType.Postfix, harmony.Id);
            }
            catch (Exception ex)
            {
                AutoArmLogger.Warn($"ChildWeapon: Unpatch failed: {ex.Message}");
            }
            _canEquipTarget = null;
            _shouldEquipTarget = null;
            patchesApplied = false;
        }

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
                        var postfix = AccessTools.Method(typeof(ChildWeapon), nameof(CanEquip_Postfix));
                        if (postfix != null)
                        {
                            var harmonyMethod = new HarmonyMethod(postfix);
                            harmonyMethod.priority = Priority.Normal;
                            harmony.Patch(canEquipMethod, postfix: harmonyMethod);
                            _canEquipTarget = canEquipMethod;
                            AutoArmLogger.Debug(() => "Patched EquipmentUtility.CanEquip for child weapon restrictions");
                        }
                        else
                        {
                            AutoArmLogger.Warn("ChildWeapon: CanEquip_Postfix method not found");
                        }
                    }
                    catch (Exception ex)
                    {
                        AutoArmLogger.Warn($"ChildWeapon: Failed to patch CanEquip: {ex.Message}");
                    }
                }
                else
                {
                    AutoArmLogger.Debug(() => "ChildWeapon: EquipmentUtility.CanEquip method not found (may be normal for this game version)");
                }

                var shouldEquipMethod = AccessTools.Method(typeof(JobGiver_PickUpOpportunisticWeapon), "ShouldEquipWeapon",
                    new Type[] { typeof(Thing), typeof(Pawn) });
                if (shouldEquipMethod != null)
                {
                    var postfix = AccessTools.Method(typeof(ChildWeapon), nameof(ShouldEquipWeapon_Postfix));
                    var harmonyMethod = new HarmonyMethod(postfix);
                    harmonyMethod.priority = Priority.Normal;
                    harmony.Patch(shouldEquipMethod, postfix: harmonyMethod);
                    _shouldEquipTarget = shouldEquipMethod;
                    AutoArmLogger.Debug(() => "Patched JobGiver_PickUpOpportunisticWeapon.ShouldEquipWeapon for child weapon restrictions");
                }

                patchesApplied = true;
            }
            catch (Exception ex)
            {
                AutoArmLogger.ErrorPatch(ex, "ChildWeapon");
            }
        }

        private static bool CanChildEquip(Pawn pawn)
        {
            var settings = AutoArmMod.settings;
            bool sliderActive = settings?.allowChildrenToEquipWeapons ?? false;
            if (!sliderActive)
                return pawn.DevelopmentalStage >= DevelopmentalStage.Child;

            bool isRaceAdult = pawn.ageTracker?.Adult == true;
            int minAge = settings?.childrenMinAge ?? Constants.ChildDefaultMinAge;
            int age = pawn.ageTracker?.AgeBiologicalYears ?? 0;
            return isRaceAdult || age >= minAge;
        }

        private static void CanEquip_Postfix(Thing thing, Pawn pawn, ref bool __result, ref string cantReason)
        {
            if (AutoArmMod.settings?.modEnabled != true)
                return;

            try
            {
                if (!__result && thing != null && thing.def.IsWeapon && pawn != null
                    && CanChildEquip(pawn) && pawn.WorkTagIsDisabled(WorkTags.Violent))
                {
                    __result = true;
                    cantReason = null;
                }
            }
            catch (Exception e)
            {
                AutoArmLogger.ErrorPatch(e, "ChildWeapon.CanEquip_Postfix");
            }
        }

        private static void ShouldEquipWeapon_Postfix(Thing newWep, Pawn pawn, ref bool __result)
        {
            if (AutoArmMod.settings?.modEnabled != true)
                return;

            try
            {
                if (!__result && newWep != null && newWep.def.IsWeapon && pawn != null && CanChildEquip(pawn))
                    __result = true;
            }
            catch (Exception e)
            {
                AutoArmLogger.ErrorPatch(e, "ChildWeapon.ShouldEquipWeapon_Postfix");
            }
        }
    }
}
