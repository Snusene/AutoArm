// AutoArm RimWorld 1.5+ mod - automatic weapon management
// This file: Harmony patches for weapon cache management
// Tracks weapon spawning, despawning, and position changes

using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace AutoArm
{
    [HarmonyPatch(typeof(Thing), "SpawnSetup")]
    public static class Thing_SpawnSetup_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Thing __instance)
        {
            if (__instance == null || __instance.def == null)
                return;

            // Use proper weapon validation to match main logic
            if (!WeaponValidation.IsProperWeapon(__instance))
                return;

            if (__instance is ThingWithComps weapon && weapon.Map != null)
            {
                ImprovedWeaponCacheManager.AddWeaponToCache(weapon);
            }
        }
    }

    [HarmonyPatch(typeof(Thing), "DeSpawn")]
    public static class Thing_DeSpawn_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(Thing __instance)
        {
            if (__instance == null || __instance.def == null)
                return;

            if (!WeaponValidation.IsProperWeapon(__instance))
                return;

            if (__instance is ThingWithComps weapon)
            {
                ImprovedWeaponCacheManager.RemoveWeaponFromCache(weapon);
            }
        }
    }

    [HarmonyPatch(typeof(Thing), "set_Position")]
    public static class Thing_SetPosition_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(Thing __instance, IntVec3 value)
        {
            if (__instance == null || __instance.def == null || !__instance.Spawned)
                return;

            if (!WeaponValidation.IsProperWeapon(__instance))
                return;

            if (__instance is ThingWithComps weapon && weapon.Map != null)
            {
                ImprovedWeaponCacheManager.UpdateWeaponPosition(weapon, __instance.Position, value);
            }
        }
    }

    [HarmonyPatch(typeof(ThinkNode_JobGiver), "TryIssueJobPackage")]
    public static class ThinkNode_JobGiver_TryIssueJobPackage_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ThinkNode_JobGiver __instance, Pawn pawn, JobIssueParams jobParams, ThinkResult __result)
        {
            if (__instance == null || pawn == null)
                return;

            // Check if mod is enabled AND debug logging is enabled
            if (AutoArmMod.settings?.modEnabled != true || AutoArmMod.settings?.debugLogging != true)
                return;

            if (__instance is JobGiver_PickUpBetterWeapon && __result.Job != null)
            {
                AutoArmLogger.LogPawn(pawn, $"{__instance.GetType().Name} issued job: {__result.Job.def.defName} targeting {__result.Job.targetA.Thing?.Label}");
            }
        }
    }
}
