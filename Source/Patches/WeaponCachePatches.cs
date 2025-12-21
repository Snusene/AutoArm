
using AutoArm.Caching;
using AutoArm.Helpers;
using AutoArm.Logging;
using AutoArm.Weapons;
using HarmonyLib;
using RimWorld;
using System;
using Verse;

namespace AutoArm
{

    /// <summary>
    /// Track weapon spawns
    /// </summary>
    [HarmonyPatch(typeof(Thing), "SpawnSetup")]
    [HarmonyPatch(new Type[] { typeof(Map), typeof(bool) })]
    [HarmonyPatchCategory(Patches.PatchCategories.Performance)]
    [HarmonyPriority(Priority.Last)]
    [HarmonyAfter("PeteTimesSix.SimpleSidearms", "CETeam.CombatExtended", "LWM.DeepStorage")]
    public static class Thing_SpawnSetup_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Thing __instance)
        {
            if (__instance == null || __instance.def == null)
                return;

            if (AutoArmMod.settings?.modEnabled == false && Current.ProgramState == ProgramState.Playing)
                return;

            if (!__instance.def.IsWeapon)
                return;

            if (!WeaponValidation.IsWeapon(__instance))
            {
                if (AutoArmMod.settings?.debugLogging == true && __instance.def.defName.Contains("Gun_"))
                {
                    AutoArmLogger.Debug(() => $"[SpawnSetup] Weapon {__instance.Label} failed IsProperWeapon check");
                }
                return;
            }

            if (__instance is ThingWithComps weapon)
            {
                if (WeaponCacheManager.ShouldTrackWeapon(weapon))
                {
                    WeaponCacheManager.AddWeaponToCache(weapon);

                }
                else if (AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.Debug(() => $"[SpawnSetup] Weapon spawned but not tracked: {weapon.Label} (spawned: {weapon.Spawned}, parent: {weapon.ParentHolder?.GetType().Name ?? "null"})");
                }
            }
        }
    }

    /// <summary>
    /// Dev spawns
    /// Dev mode only
    /// </summary>
    [HarmonyPatch(typeof(GenSpawn), nameof(GenSpawn.Spawn))]
    [HarmonyPatch(new Type[] { typeof(Thing), typeof(IntVec3), typeof(Map), typeof(Rot4), typeof(WipeMode), typeof(bool), typeof(bool) })]
    [HarmonyPatchCategory(Patches.PatchCategories.Performance)]
    [HarmonyPriority(Priority.Last)]
    public static class GenSpawn_Spawn_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Thing __result)
        {
            if (!Prefs.DevMode)
                return;

            if (__result == null || __result.def == null)
                return;

            if (__result.def.defName == "Gun_AssaultRifle")
            {
            }

            if (!__result.def.IsWeapon)
                return;

            if (!WeaponValidation.IsWeapon(__result))
            {
                if (AutoArmMod.settings?.debugLogging == true && __result.def.defName.Contains("Gun_"))
                {
                    AutoArmLogger.Debug(() => $"[GenSpawn DEV MODE] Weapon {__result.Label} failed IsProperWeapon check");
                }
                return;
            }

            if (__result is ThingWithComps weapon)
            {
                if (WeaponCacheManager.ShouldTrackWeapon(weapon))
                {
                    WeaponCacheManager.AddWeaponToCache(weapon);
                }
                else if (AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.Debug(() => $"[GenSpawn DEV MODE] Weapon spawned but not tracked: {weapon.Label} (spawned: {weapon.Spawned}, parent: {weapon.ParentHolder?.GetType().Name ?? "null"})");
                }
            }
        }
    }

    /// <summary>
    /// Track destroyed weapons
    /// O(1) cleanup
    /// </summary>
    [HarmonyPatch(typeof(Thing), nameof(Thing.Destroy))]
    [HarmonyPatchCategory(Patches.PatchCategories.Performance)]
    [HarmonyPriority(Priority.Last)]
    public static class Thing_Destroy_WeaponCache_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(Thing __instance)
        {
            if (__instance == null || __instance.def == null)
                return;

            if (AutoArmMod.settings?.modEnabled != true)
                return;

            if (!__instance.def.IsWeapon)
                return;

            // Central cleanup
            Cleanup.OnWeaponRemoved(__instance);

            if (__instance is ThingWithComps weapon)
            {
                var map = weapon.Map;
                if (map == null)
                    return;

                var cacheManager = map.GetComponent<WeaponCacheManager.AutoArmWeaponMapComponent>();
                cacheManager?.OnWeaponRemoved(weapon);
            }
        }
    }

    /// <summary>
    /// Track despawned weapons
    /// O(1) cleanup
    /// </summary>
    [HarmonyPatch(typeof(Thing), nameof(Thing.DeSpawn), new[] { typeof(DestroyMode) })]
    [HarmonyPatchCategory(Patches.PatchCategories.Performance)]
    [HarmonyPriority(Priority.Last)]
    public static class Thing_DeSpawn_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(Thing __instance)
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

            // Central cleanup
            Cleanup.OnWeaponRemoved(__instance);

            if (__instance is ThingWithComps weapon)
            {
                var map = weapon.Map;
                if (map == null)
                    return;

                var cacheManager = map.GetComponent<WeaponCacheManager.AutoArmWeaponMapComponent>();
                cacheManager?.OnWeaponRemoved(weapon);
            }
        }
    }

    /// <summary>
    /// Track forbid changes
    /// Logging verification
    /// </summary>
    [HarmonyPatch(typeof(CompForbiddable), "Forbidden", MethodType.Setter)]
    [HarmonyPatchCategory(Patches.PatchCategories.Performance)]
    [HarmonyPriority(Priority.Last)]
    public static class CompForbiddable_Forbidden_Set_Patch
    {
        private static bool _firstCall = true;

        [HarmonyPostfix]
        public static void Postfix(CompForbiddable __instance, bool value)
        {
            if (_firstCall)
            {
                _firstCall = false;
                AutoArmLogger.Debug(() => "CompForbiddable_Forbidden setter patch is active");
            }

            if (__instance?.parent != null && __instance.parent.def != null && __instance.parent.def.IsWeapon)
            {
                WeaponCacheManager.NotifyForbiddenStatusChanged(__instance.parent);
            }
        }
    }


    /// <summary>
    /// Track skill changes
    /// Cache hit optimization
    /// </summary>
    [HarmonyPatch(typeof(SkillRecord), nameof(SkillRecord.Level), MethodType.Setter)]
    [HarmonyPatchCategory(Patches.PatchCategories.Performance)]
    public static class SkillRecord_Level_Set_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(SkillRecord __instance)
        {
            if (__instance == null)
                return;

            if (AutoArmMod.settings?.modEnabled != true)
                return;

            if (__instance.def != SkillDefOf.Shooting && __instance.def != SkillDefOf.Melee)
                return;

            if (__instance?.Pawn != null && PawnValidationCache.CanConsiderWeapons(__instance.Pawn))
            {
                WeaponCacheManager.MarkPawnSkillsChanged(__instance.Pawn);

                AutoArmLogger.Debug(() => $"[{__instance.Pawn.LabelShort}] skill {__instance.def.defName} changed to level {__instance.Level}, invalidating weapon score cache");
            }
        }
    }
}
