using AutoArm.Jobs;
using HarmonyLib;
using RimWorld;
using System;
using Verse;

namespace AutoArm.UI
{
    [HarmonyPatch(typeof(FloatMenuOptionProvider_Equip), "GetSingleOptionFor")]
    [HarmonyPatchCategory(Patches.PatchCategories.UI)]
    internal static class FloatMenuOptionProvider_Equip_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Thing clickedThing, FloatMenuContext context, ref FloatMenuOption __result)
        {
            try
            {
                if (__result == null || __result.action == null)
                    return;
                if (AutoArmMod.settings?.modEnabled != true)
                    return;
                if (AutoArmMod.settings?.showForcedLabels != true)
                    return;
                if (clickedThing == null || !clickedThing.def.IsWeapon || !(clickedThing is ThingWithComps))
                    return;
                if (context?.FirstSelectedPawn == null)
                    return;

                string oldPrefix = "Equip".Translate(clickedThing.LabelShort).ToString();
                var label = __result.Label;
                if (label == null || !label.StartsWith(oldPrefix, StringComparison.Ordinal))
                    return;

                string newPrefix = "ForceEquipApparel".Translate(clickedThing.LabelShort, clickedThing).ToString();
                __result.Label = newPrefix + label.Substring(oldPrefix.Length);
            }
            catch (Exception e)
            {
                AutoArmLogger.ErrorPatch(e, "FloatMenuOptionProvider_Equip_Patch.Postfix");
            }
        }
    }
}
