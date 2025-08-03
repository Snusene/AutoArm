// AutoArm RimWorld 1.5+ mod - automatic weapon management
// This file: Harmony patches for child weapon restrictions
// Allows children to equip weapons based on mod settings

using HarmonyLib;
using RimWorld;
using Verse;

namespace AutoArm
{
    // ============================================================================
    // CHILD WEAPON RESTRICTION PATCHES
    // ============================================================================

    /// <summary>
    /// Patches to allow children to use weapons based on mod settings
    /// </summary>
    [HarmonyPatch(typeof(Pawn))]
    [HarmonyPatch("WorkTagIsDisabled")]
    public static class Patch_Pawn_WorkTagIsDisabled
    {
        [HarmonyPostfix]
        public static void Postfix(ref bool __result, WorkTags w, Pawn __instance)
        {
            // Only modify weapon-related work tags for colonists
            if (!__result || !w.HasFlag(WorkTags.Violent) || !__instance.IsColonist)
                return;

            // Check if mod is enabled and allows children
            if (!AutoArmMod.settings?.modEnabled ?? true)
                return;
            
            if (!AutoArmMod.settings?.allowChildrenToEquipWeapons ?? true)
                return;

            // Check if pawn is within our allowed age range
            int pawnAge = __instance.ageTracker?.AgeBiologicalYears ?? 0;
            int minAge = AutoArmMod.settings?.childrenMinAge ?? 13;

            if (pawnAge >= minAge && pawnAge < 13)
            {
                // Override the restriction if they're old enough by our settings but not by vanilla
                __result = false;
            }
        }
    }

    /// <summary>
    /// Patch equipment tracker to allow children to equip weapons
    /// NOTE: Disabled - CanEquip method doesn't exist in RimWorld 1.5/1.6
    /// </summary>
    /*
    [HarmonyPatch(typeof(Pawn_EquipmentTracker))]
    [HarmonyPatch("CanEquip")]
    public static class Patch_EquipmentTracker_CanEquip
    {
        [HarmonyPostfix]
        public static void Postfix(ref bool __result, Thing thing, Pawn ___pawn, ref string cantReason)
        {
            // If already allowed, don't change
            if (__result)
                return;

            // Check if it's a weapon
            if (!thing.def.IsWeapon)
                return;

            // Check mod settings
            if (!AutoArmMod.settings?.modEnabled ?? true)
                return;
            
            if (!AutoArmMod.settings?.allowChildrenToEquipWeapons ?? true)
                return;

            // Check age
            int pawnAge = ___pawn.ageTracker?.AgeBiologicalYears ?? 0;
            int minAge = AutoArmMod.settings?.childrenMinAge ?? 13;

            // If pawn is between our min age and 13, and the only reason they can't equip is age
            if (pawnAge >= minAge && pawnAge < 13)
            {
                // Check if the reason is age-related
                if (cantReason != null && cantReason.Contains("ChildNoEquip"))
                {
                    __result = true;
                    cantReason = null;
                }
            }
        }
    }
    */
}
