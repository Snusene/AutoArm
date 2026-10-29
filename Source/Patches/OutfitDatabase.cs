
using AutoArm.Caching;
using AutoArm.Compatibility;
using AutoArm.Jobs;
using AutoArm.UI;
using HarmonyLib;
using RimWorld;
using System;
using Verse;

namespace AutoArm.Patches
{
    [HarmonyPatch(typeof(Window), nameof(Window.PostOpen))]
    [HarmonyPatchCategory(PatchCategories.UI)]
    internal static class SettingsDialogResize_Patch
    {
        private static readonly System.Reflection.FieldInfo modField =
            HarmonyLib.AccessTools.Field(typeof(Dialog_ModSettings), "mod");

        internal static bool swapping;

        [HarmonyPostfix]
        public static void Postfix(Window __instance)
        {
            try
            {
                if (__instance is AutoArmSettingsDialog)
                    return;
                if (!(__instance is Dialog_ModSettings dialog))
                    return;
                if (modField == null)
                    return;
                if (!(modField.GetValue(dialog) is AutoArmMod autoArmMod))
                    return;

                swapping = true;
                try { Find.WindowStack.TryRemove(dialog, doCloseSound: false); }
                finally { swapping = false; }

                Find.WindowStack.Add(new AutoArmSettingsDialog(autoArmMod));
            }
            catch (Exception e)
            {
                AutoArmLogger.ErrorPatch(e, "SettingsDialogResize_Patch");
            }
        }
    }

    [HarmonyPatch(typeof(Dialog_ModSettings), "PreClose")]
    [HarmonyPatchCategory(PatchCategories.UI)]
    internal static class Dialog_ModSettings_PreClose_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Mod ___mod)
        {
            try
            {
                if (!(___mod is AutoArmMod)) return;
                if (SettingsDialogResize_Patch.swapping) return;

                SimpleSidearmsCompat.InvalidateAllCaches();
                WeaponCache.ClearScoreCache();
                PawnValidation.ClearCache();
                JobGiverMapComponent.ClearAllState();
                AutoArm.UI.StatusOverviewDataGatherer.ClearTopWeaponsCache();

                AutoArmLogger.Debug(() => "[Settings] Invalidated all caches after AutoArm settings closed");
            }
            catch (Exception e)
            {
                AutoArmLogger.ErrorPatch(e, "Dialog_ModSettings_PostClose");
            }
        }
    }
}
