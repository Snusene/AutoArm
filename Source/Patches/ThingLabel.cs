
using AutoArm.Helpers;
using HarmonyLib;
using System;
using Verse;

namespace AutoArm
{
    [HarmonyPatch(typeof(Thing), "Label", MethodType.Getter)]
    [HarmonyPatch(typeof(Thing), "LabelNoCount", MethodType.Getter)]
    [HarmonyPatch(typeof(Thing), "LabelCap", MethodType.Getter)]
    [HarmonyPatchCategory(Patches.PatchCategories.UI)]
    [HarmonyAfter("PeteTimesSix.SimpleSidearms", "CETeam.CombatExtended")]
    internal static class Thing_LabelPatches
    {
        [HarmonyPostfix]
        public static void Postfix(Thing __instance, ref string __result)
        {
            if (AutoArmMod.settings?.modEnabled != true)
                return;
            if (__instance?.def?.IsWeapon != true)
                return;

            try
            {
                if (!ForcedWeaponLabelHelper.ShouldProcessWeaponLabel() ||
                    !ForcedWeaponLabelHelper.IsWeaponOwnedBySelectedPawn(__instance.thingIDNumber))
                    return;

                ForcedWeaponLabelHelper.AddForcedText(__instance, ref __result);
            }
            catch (Exception e)
            {
                AutoArmLogger.ErrorPatch(e, "Thing_LabelPatches.Postfix");
            }
        }
    }
}
