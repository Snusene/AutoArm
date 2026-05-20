
using AutoArm.Caching;
using AutoArm.Helpers;
using HarmonyLib;
using RimWorld;
using System;
using Verse;

namespace AutoArm
{

    [HarmonyPatch(typeof(Thing), "SpawnSetup")]
    [HarmonyPatch(new Type[] { typeof(Map), typeof(bool) })]
    [HarmonyPatchCategory(Patches.PatchCategories.Performance)]
    [HarmonyPriority(Priority.Last)]
    [HarmonyAfter("PeteTimesSix.SimpleSidearms", "CETeam.CombatExtended", "LWM.DeepStorage")]
    internal static class Thing_SpawnSetup_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Thing __instance)
        {
            try
            {
                if (__instance == null || __instance.def == null)
                    return;

                if (AutoArmMod.settings?.modEnabled == false && Current.ProgramState == ProgramState.Playing)
                    return;

                if (!__instance.def.IsWeapon)
                    return;

                if (!Validation.IsWeapon(__instance))
                {
                    if (AutoArmMod.settings?.debugLogging == true && __instance.def.defName.Contains("Gun_"))
                    {
                        AutoArmLogger.Debug(() => $"[SpawnSetup] Weapon {__instance.Label} failed IsProperWeapon check");
                    }
                    return;
                }

                if (__instance is ThingWithComps weapon)
                {
                    if (WeaponCache.ShouldTrackWeapon(weapon))
                    {
                        WeaponCache.AddWeaponToCache(weapon);
                    }
                    else if (AutoArmMod.settings?.debugLogging == true)
                    {
                        AutoArmLogger.Debug(() => $"[SpawnSetup] Weapon spawned but not tracked: {weapon.Label} (spawned: {weapon.Spawned}, parent: {weapon.ParentHolder?.GetType().Name ?? "null"})");
                    }
                }
            }
            catch (Exception e)
            {
                AutoArmLogger.ErrorPatch(e, "Thing_SpawnSetup_Patch");
            }
        }
    }

    [HarmonyPatch(typeof(GenSpawn), nameof(GenSpawn.Spawn))]
    [HarmonyPatch(new Type[] { typeof(Thing), typeof(IntVec3), typeof(Map), typeof(Rot4), typeof(WipeMode), typeof(bool), typeof(bool) })]
    [HarmonyPatchCategory(Patches.PatchCategories.Performance)]
    [HarmonyPriority(Priority.Last)]
    internal static class GenSpawn_Spawn_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Thing __result)
        {
            try
            {
                if (!Prefs.DevMode)
                    return;

                if (__result == null || __result.def == null)
                    return;

                if (!__result.def.IsWeapon)
                    return;

                if (!Validation.IsWeapon(__result))
                {
                    if (AutoArmMod.settings?.debugLogging == true && __result.def.defName.Contains("Gun_"))
                    {
                        AutoArmLogger.Debug(() => $"[GenSpawn DEV MODE] Weapon {__result.Label} failed IsProperWeapon check");
                    }
                    return;
                }

                if (__result is ThingWithComps weapon
                    && !WeaponCache.ShouldTrackWeapon(weapon)
                    && AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.Debug(() => $"[GenSpawn DEV MODE] Weapon spawned but not tracked: {weapon.Label} (spawned: {weapon.Spawned}, parent: {weapon.ParentHolder?.GetType().Name ?? "null"})");
                }
            }
            catch (Exception e)
            {
                AutoArmLogger.ErrorPatch(e, "GenSpawn_Spawn_Patch");
            }
        }
    }

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

                if (__instance is ThingWithComps weapon)
                {
                    var map = weapon.Map;
                    if (map == null)
                        return;

                    var cacheManager = map.GetComponent<WeaponCache.AutoArmWeaponMapComponent>();
                    cacheManager?.OnWeaponRemoved(weapon);
                }
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

                if (__instance is Pawn pawn)
                {
                    CooldownMetrics.OnPawnRemoved(pawn);
                    return;
                }

                if (!__instance.def.IsWeapon)
                    return;

                Cleanup.OnWeaponRemoved(__instance);

                if (__instance is ThingWithComps weapon)
                {
                    var map = weapon.Map;
                    if (map == null)
                        return;

                    var cacheManager = map.GetComponent<WeaponCache.AutoArmWeaponMapComponent>();
                    cacheManager?.OnWeaponRemoved(weapon);
                }
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
