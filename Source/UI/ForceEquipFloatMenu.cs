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
            if (__result == null || __result.action == null)
                return;
            if (AutoArmMod.settings?.modEnabled != true)
                return;
            if (clickedThing == null || !clickedThing.def.IsWeapon)
                return;

            var twc = clickedThing as ThingWithComps;
            if (twc == null)
                return;

            var pawn = context?.FirstSelectedPawn;
            if (pawn == null)
                return;

            __result.Label = "ForceEquipApparel".Translate(clickedThing.LabelShort, clickedThing);

            var originalAction = __result.action;
            __result.action = delegate
            {
                AutoEquipState.SetWeaponToForce(pawn, twc);
                originalAction();
            };
        }
    }
}
