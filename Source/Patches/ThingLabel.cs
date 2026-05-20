
using AutoArm.Helpers;
using AutoArm.Testing;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using ForcedWeapons = AutoArm.ForcedWeapons;

namespace AutoArm
{
    [HarmonyPatch(typeof(Thing), "Label", MethodType.Getter)]
    [HarmonyPatch(typeof(Thing), "LabelNoCount", MethodType.Getter)]
    [HarmonyPatch(typeof(Thing), "LabelCap", MethodType.Getter)]
    [HarmonyPatchCategory(Patches.PatchCategories.UI)]
    [HarmonyAfter("PeteTimesSix.SimpleSidearms", "CETeam.CombatExtended")]
    internal static class Thing_LabelPatches
    {
        private static Dictionary<int, (string label, int tick)> cache = new Dictionary<int, (string, int)>();

        private static int cachedTickForQuickCheck = -1;

        private static bool quickCheckResult = false;

        [HarmonyPrefix]
        public static bool Prefix(Thing __instance, ref string __result)
        {
            if (AutoArmMod.settings?.modEnabled != true)
                return true;
            if (__instance?.def == null)
                return true;

            try
            {
                if (!__instance.def.IsWeapon)
                    return true;

                var tick = Find.TickManager?.TicksGame ?? -1;
                if (cachedTickForQuickCheck != tick)
                {
                    cachedTickForQuickCheck = tick;
                    quickCheckResult = ForcedWeaponLabelHelper.ShouldProcessWeaponLabel();
                }

                if (!quickCheckResult)
                    return true;

                if (!ForcedWeaponLabelHelper.IsWeaponOwnedBySelectedPawn(__instance.thingIDNumber))
                    return true;

                if (cache.TryGetValue(__instance.thingIDNumber, out var cached) && cached.tick == tick)
                {
                    __result = cached.label;
                    return false;
                }

                return true;
            }
            catch (Exception e)
            {
                AutoArmLogger.ErrorPatch(e, "Thing_LabelPatches.Prefix");
                return true;
            }
        }

        [HarmonyPostfix]
        public static void Postfix(Thing __instance, ref string __result)
        {
            if (AutoArmMod.settings?.modEnabled != true)
                return;
            if (__instance?.def == null)
                return;

            try
            {
                if (!quickCheckResult)
                    return;

                ForcedWeaponLabelHelper.AddForcedText(__instance, ref __result);

                int currentTick = Find.TickManager?.TicksGame ?? 0;
                cache[__instance.thingIDNumber] = (__result, currentTick);
            }
            catch (Exception e)
            {
                AutoArmLogger.ErrorPatch(e, "Thing_LabelPatches.Postfix");
            }
        }

        public static int CleanupLabelCache()
        {
            if (cache.Count == 0)
                return 0;

            int currentTick = Find.TickManager?.TicksGame ?? 0;
            int removedCount = 0;

            var toRemove = ListPool<int>.Get(cache.Count / 4);
            foreach (var kvp in cache)
            {
                if (currentTick - kvp.Value.tick > 60)
                {
                    toRemove.Add(kvp.Key);
                }
            }

            foreach (var key in toRemove)
            {
                cache.Remove(key);
                removedCount++;
            }

            ListPool<int>.Return(toRemove);
            return removedCount;
        }
    }

    [HarmonyPatch(typeof(ThingCategoryDef), "DescendantThingDefs", MethodType.Getter)]
    [HarmonyPatchCategory(Patches.PatchCategories.UI)]
    internal static class ThingCategoryDef_DescendantThingDefs_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ThingCategoryDef __instance, ref IEnumerable<ThingDef> __result)
        {
            try
            {
                if (TestRunner.IsRunningTests)
                    return;

                if (AutoArmMod.settings?.modEnabled != true &&
                    __instance?.defName == "Weapons" &&
                    __instance?.parent?.defName == "Apparel")
                {
                    __result = Enumerable.Empty<ThingDef>();
                }
            }
            catch (Exception e) { AutoArmLogger.ErrorPatch(e, "DescendantThingDefs_Postfix"); }
        }
    }

    [HarmonyPatch(typeof(ThingCategoryDef), "ThisAndChildCategoryDefs", MethodType.Getter)]
    [HarmonyPatchCategory(Patches.PatchCategories.UI)]
    internal static class ThingCategoryDef_ThisAndChildCategoryDefs_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ThingCategoryDef __instance, ref IEnumerable<ThingCategoryDef> __result)
        {
            try
            {
                if (TestRunner.IsRunningTests)
                    return;

                if (AutoArmMod.settings?.modEnabled != true && __instance?.defName == "Apparel")
                {
                    var filtered = new List<ThingCategoryDef>();
                    foreach (var cat in __result)
                    {
                        if (cat?.defName != "Weapons")
                            filtered.Add(cat);
                    }
                    __result = filtered;
                }
            }
            catch (Exception e) { AutoArmLogger.ErrorPatch(e, "ThisAndChildCategoryDefs_Postfix"); }
        }
    }
}
