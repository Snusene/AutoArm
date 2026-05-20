using AutoArm.Helpers;
using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.Sound;
using ForcedWeapons = AutoArm.ForcedWeapons;

namespace AutoArm.Patches
{
    [HarmonyPatch(typeof(PawnColumnWorker_Outfit), nameof(PawnColumnWorker_Outfit.DoCell))]
    [HarmonyPatchCategory(PatchCategories.UI)]
    internal static class PawnColumnWorker_Outfit_DoCell_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(Rect rect, Pawn pawn, PawnTable table)
        {
            if (AutoArmMod.settings?.modEnabled != true)
                return true;
            if (pawn?.outfits == null)
                return true;

            try
            {
                bool weaponForced = ForcedWeapons.SomethingIsForced(pawn);
                if (!weaponForced)
                    return true;

                Rect rect2 = rect.ContractedBy(0f, 2f);
                bool apparelForced = pawn.outfits.forcedHandler.SomethingIsForced;
                Rect left = rect2;
                Rect right = default;
                rect2.SplitVerticallyWithMargin(out left, out right, 4f);

                if (pawn.IsQuestLodger())
                {
                    Text.Anchor = TextAnchor.MiddleCenter;
                    Widgets.Label(left, "Unchangeable".Translate().Truncate(left.width));
                    TooltipHandler.TipRegionByKey(left, "QuestRelated_Outfit");
                    Text.Anchor = TextAnchor.UpperLeft;
                }
                else
                {
                    Widgets.Dropdown(left, pawn, p => p.outfits.CurrentApparelPolicy, GenerateMenu, pawn.outfits.CurrentApparelPolicy.label.Truncate(left.width), null, pawn.outfits.CurrentApparelPolicy.label, null, null, paintable: true);
                }

                if (Widgets.ButtonText(right, "ClearForcedApparel".Translate()))
                {
                    if (apparelForced)
                        pawn.outfits.forcedHandler.Reset();
                    ForcedWeapons.ClearForced(pawn);
                }

                if (Mouse.IsOver(right))
                {
                    TooltipHandler.TipRegion(right, new TipSignal(delegate
                    {
                        string text = "ForcedApparel".Translate() + ":\n";
                        if (apparelForced)
                        {
                            foreach (Apparel item in pawn.outfits.forcedHandler.ForcedApparel)
                                text = text + "\n   " + item.LabelCap;
                        }
                        foreach (var weapon in EnumerateForcedWeapons(pawn))
                            text = text + "\n   " + ForcedWeaponLabelHelper.StripForcedSuffix(weapon.LabelCap);
                        return text;
                    }, pawn.GetHashCode() * 612));
                }

                return false;
            }
            catch (System.Exception e)
            {
                AutoArmLogger.ErrorPatch(e, "PawnColumnWorker_Outfit_DoCell_Patch.Prefix");
                return true;
            }
        }

        private static IEnumerable<ThingWithComps> EnumerateForcedWeapons(Pawn pawn)
        {
            var primary = pawn.equipment?.Primary;
            if (primary != null && ForcedWeapons.IsForced(pawn, primary))
                yield return primary;

            var inv = pawn.inventory?.innerContainer;
            if (inv != null)
            {
                foreach (var thing in inv)
                {
                    if (thing is ThingWithComps twc && twc.def.IsWeapon && ForcedWeapons.IsForced(pawn, twc))
                        yield return twc;
                }
            }
        }

        private static IEnumerable<Widgets.DropdownMenuElement<ApparelPolicy>> GenerateMenu(Pawn pawn)
        {
            foreach (ApparelPolicy outfit in Current.Game.outfitDatabase.AllOutfits)
            {
                yield return new Widgets.DropdownMenuElement<ApparelPolicy>
                {
                    option = new FloatMenuOption(outfit.label, delegate
                    {
                        pawn.outfits.CurrentApparelPolicy = outfit;
                    }),
                    payload = outfit
                };
            }
            yield return new Widgets.DropdownMenuElement<ApparelPolicy>
            {
                option = new FloatMenuOption(string.Format("{0}...", "AssignTabEdit".Translate()), delegate
                {
                    Find.WindowStack.Add(new Dialog_ManageApparelPolicies(pawn.outfits.CurrentApparelPolicy));
                })
            };
        }
    }
}
