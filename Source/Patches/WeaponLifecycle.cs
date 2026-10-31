
using AutoArm.Caching;
using AutoArm.Helpers;
using HarmonyLib;
using RimWorld;
using System;
using Verse;

namespace AutoArm
{

    [HarmonyPatch(typeof(Thing), nameof(Thing.Destroy))]
    [HarmonyPatchCategory(Patches.PatchCategories.Performance)]
    [HarmonyPriority(Priority.Last)]
    internal static class Thing_Destroy_WeaponCache_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(Thing __instance)
        {
            try
            {
                if (__instance == null || __instance.def == null)
                    return;

                if (__instance.Destroyed)
                    return;

                if (__instance.Spawned)
                    return;

                if (AutoArmMod.settings?.modEnabled != true)
                    return;

                if (!__instance.def.IsWeapon)
                    return;

                Cleanup.OnWeaponRemoved(__instance);
            }
            catch (Exception e)
            {
                AutoArmLogger.ErrorPatch(e, "Thing_Destroy_WeaponCache_Patch");
            }
        }
    }

    [HarmonyPatch(typeof(Thing), nameof(Thing.DeSpawn), new[] { typeof(DestroyMode) })]
    [HarmonyPatchCategory(Patches.PatchCategories.Performance)]
    [HarmonyPriority(Priority.Last)]
    internal static class Thing_DeSpawn_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(Thing __instance)
        {
            try
            {
                if (__instance == null || __instance.def == null)
                    return;

                if (AutoArmMod.settings?.modEnabled != true)
                    return;

                if (!__instance.def.IsWeapon)
                    return;

                Cleanup.OnWeaponRemoved(__instance);
            }
            catch (Exception e)
            {
                AutoArmLogger.ErrorPatch(e, "Thing_DeSpawn_Patch");
            }
        }
    }

    [HarmonyPatch(typeof(CompForbiddable), "Forbidden", MethodType.Setter)]
    [HarmonyPatchCategory(Patches.PatchCategories.Performance)]
    [HarmonyPriority(Priority.Last)]
    internal static class CompForbiddable_Forbidden_Set_Patch
    {
        private static bool _firstCall = true;

        [HarmonyFinalizer]
        public static void Finalizer(CompForbiddable __instance, bool value)
        {
            try
            {
                if (AutoArmMod.settings?.modEnabled != true) return;

                if (_firstCall)
                {
                    _firstCall = false;
                    AutoArmLogger.Debug(() => "CompForbiddable_Forbidden setter patch is active");
                }

                if (__instance?.parent != null && __instance.parent.def != null && __instance.parent.def.IsWeapon)
                {
                    WeaponCache.NotifyForbiddenStatusChanged(__instance.parent);
                }
            }
            catch (Exception e)
            {
                AutoArmLogger.ErrorPatch(e, "CompForbiddable_Forbidden_Set_Patch");
            }
        }
    }


    [HarmonyPatch(typeof(SkillRecord), nameof(SkillRecord.Level), MethodType.Setter)]
    [HarmonyPatchCategory(Patches.PatchCategories.Performance)]
    internal static class SkillRecord_Level_Set_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(SkillRecord __instance)
        {
            try
            {
                if (__instance == null)
                    return;

                if (AutoArmMod.settings?.modEnabled != true)
                    return;

                if (__instance.def != SkillDefOf.Shooting && __instance.def != SkillDefOf.Melee)
                    return;

                if (__instance?.Pawn != null && PawnValidation.CanConsiderWeapons(__instance.Pawn))
                {
                    WeaponCache.MarkPawnSkillsChanged(__instance.Pawn);

                    AutoArmLogger.Debug(() => $"[{__instance.Pawn.LabelShort}] skill {__instance.def.defName} changed to level {__instance.Level}, invalidating weapon score cache");
                }
            }
            catch (Exception e)
            {
                AutoArmLogger.ErrorPatch(e, "SkillRecord_Level_Set_Patch");
            }
        }
    }

    [HarmonyPatch(typeof(CompBiocodable), nameof(CompBiocodable.CodeFor))]
    [HarmonyPatchCategory(Patches.PatchCategories.Core)]
    internal static class CompBiocodable_CodeFor_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(CompBiocodable __instance)
        {
            try
            {
                if (__instance?.parent is ThingWithComps weapon && weapon.def?.IsWeapon == true)
                {
                    WeaponCache.InvalidateWeaponScores(weapon);
                    EquipEligibility.InvalidateWeapon(weapon.thingIDNumber);
                }
            }
            catch (Exception e)
            {
                AutoArmLogger.ErrorPatch(e, "CompBiocodable_CodeFor_Patch");
            }
        }
    }
}
