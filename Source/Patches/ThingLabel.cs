
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
        private static Dictionary<int, (string label, int frame)> cache = new Dictionary<int, (string, int)>();

        private static int cachedFrameForQuickCheck = -1;

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

                var frame = RealTime.frameCount;
                if (cachedFrameForQuickCheck != frame)
                {
                    cachedFrameForQuickCheck = frame;
                    quickCheckResult = ForcedWeaponLabelHelper.ShouldProcessWeaponLabel();
                }

                if (!quickCheckResult)
                    return true;

                if (!ForcedWeaponLabelHelper.IsWeaponOwnedBySelectedPawn(__instance.thingIDNumber))
                    return true;

                if (cache.TryGetValue(__instance.thingIDNumber, out var cached) && cached.frame == frame)
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

                cache[__instance.thingIDNumber] = (__result, RealTime.frameCount);
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

            int currentFrame = RealTime.frameCount;
            int removedCount = 0;

            var toRemove = ListPool<int>.Get(cache.Count / 4);
            foreach (var kvp in cache)
            {
                if (currentFrame - kvp.Value.frame > 300)
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
}
