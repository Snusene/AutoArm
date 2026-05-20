
using AutoArm.Caching;
using AutoArm.Helpers;
using HarmonyLib;
using RimWorld;
using System;
using Verse;

namespace AutoArm.Patches
{
    [HarmonyPatch]
    [HarmonyPatchCategory(PatchCategories.Performance)]
    internal static class PawnValidationCachePatches
    {
        private static readonly AccessTools.FieldRef<PawnCapacitiesHandler, Pawn> _capacitiesPawnRef = BindCapacitiesPawnRef();

        private static AccessTools.FieldRef<PawnCapacitiesHandler, Pawn> BindCapacitiesPawnRef()
        {
            try { return AccessTools.FieldRefAccess<PawnCapacitiesHandler, Pawn>("pawn"); }
            catch (Exception e)
            {
                AutoArmLogger.Warn($"PawnCapacitiesHandler.pawn field bind failed: {e.Message}");
                return null;
            }
        }

        [HarmonyPatch(typeof(Pawn), "SetFaction")]
        [HarmonyPostfix]
        public static void SetFaction_Postfix(Pawn __instance)
        {
            try
            {
                if (__instance == null) return;
                PawnValidation.InvalidatePawn(__instance);
                WeaponCache.RemovePawnFromScoreCache(__instance.thingIDNumber);
                EquipEligibility.InvalidatePawn(__instance.thingIDNumber);
            }
            catch (Exception e) { AutoArmLogger.ErrorPatch(e, "SetFaction_Postfix"); }
        }

        [HarmonyPatch(typeof(Pawn_GuestTracker), "SetGuestStatus")]
        [HarmonyPostfix]
        public static void SetGuestStatus_Postfix(Pawn ___pawn)
        {
            try { PawnValidation.InvalidatePawn(___pawn); }
            catch (Exception e) { AutoArmLogger.ErrorPatch(e, "SetGuestStatus_Postfix"); }
        }

        [HarmonyPatch(typeof(PawnCapacitiesHandler), "Notify_CapacityLevelsDirty")]
        [HarmonyPostfix]
        public static void Notify_CapacityLevelsDirty_Postfix(PawnCapacitiesHandler __instance)
        {
            try
            {
                if (AutoArmMod.settings?.modEnabled != true) return;
                if (_capacitiesPawnRef == null) return;
                var pawn = _capacitiesPawnRef(__instance);
                if (pawn == null) return;
                PawnValidation.InvalidateIfManipulationChanged(pawn);
            }
            catch (Exception e) { AutoArmLogger.ErrorPatch(e, "Notify_CapacityLevelsDirty_Postfix"); }
        }

        [HarmonyPatch(typeof(Pawn_WorkSettings), "Notify_DisabledWorkTypesChanged")]
        [HarmonyPostfix]
        public static void Notify_DisabledWorkTypesChanged_Postfix(Pawn ___pawn)
        {
            try
            {
                if (AutoArmMod.settings?.modEnabled != true) return;
                PawnValidation.InvalidatePawn(___pawn);
            }
            catch (Exception e) { AutoArmLogger.ErrorPatch(e, "Notify_DisabledWorkTypesChanged_Postfix"); }
        }

        [HarmonyPatch(typeof(Pawn_AgeTracker), "BirthdayBiological")]
        [HarmonyPostfix]
        public static void BirthdayBiological_Postfix(Pawn ___pawn)
        {
            try
            {
                if (AutoArmMod.settings?.modEnabled != true) return;
                if (___pawn == null || (!___pawn.IsColonist && !___pawn.IsSlaveOfColony)) return;
                PawnValidation.InvalidatePawn(___pawn);
            }
            catch (Exception e) { AutoArmLogger.ErrorPatch(e, "BirthdayBiological_Postfix"); }
        }


        [HarmonyPatch(typeof(Pawn), "Destroy")]
        internal static class Pawn_Destroy_Cache_Patch
        {
            [HarmonyPrefix]
            public static void Prefix(Pawn __instance)
            {
                try
                {
                    if (__instance == null) return;
                    if (AutoArmMod.settings?.modEnabled != true) return;
                    Helpers.Cleanup.OnPawnRemoved(__instance);
                }
                catch (Exception e) { AutoArmLogger.ErrorPatch(e, "Pawn_Destroy_Prefix"); }
            }
        }

    }
}
