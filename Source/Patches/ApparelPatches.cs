// AutoArm RimWorld 1.5+ mod - automatic weapon management
// This file: Harmony patches for apparel changes that affect body size
// Clears weapon blacklist when pawn's effective body size changes

using AutoArm.Definitions;
using AutoArm.Logging;
using AutoArm.Weapons;
using HarmonyLib;
using RimWorld;
using Verse;

namespace AutoArm
{
    /// <summary>
    /// Clear weapon blacklist when pawn equips/removes apparel that changes body size
    /// This allows pawns to retry body-size restricted weapons after equipping power armor
    /// </summary>
    [HarmonyPatch(typeof(Pawn_ApparelTracker), "Wear")]
    public static class Pawn_ApparelTracker_Wear_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn ___pawn, Apparel newApparel)
        {
            // Check if mod is enabled
            if (AutoArmMod.settings?.modEnabled != true)
                return;
                
            if (___pawn == null || !___pawn.IsColonist || newApparel == null)
                return;
                
            // Check if this apparel changes body size (power armor, exoskeletons, etc.)
            if (ApparelChangesBodySize(newApparel))
            {
                // Clear the weapon blacklist for this pawn
                WeaponBlacklist.ClearBlacklist(___pawn);
                
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.Debug($"[{___pawn.LabelShort}] Equipped {newApparel.Label} - cleared weapon blacklist (body size may have changed)");
                }
            }
        }
        
        private static bool ApparelChangesBodySize(Apparel apparel)
        {
            if (apparel?.def == null)
                return false;
                
            // Check if it's power armor or similar
            var defName = apparel.def.defName.ToLower();
            if (defName.Contains("power") || 
                defName.Contains("exo") || 
                defName.Contains("marine") ||
                defName.Contains("cataphract") ||
                defName.Contains("grenadier") ||
                defName.Contains("phoenix") ||
                defName.Contains("prestige"))
            {
                return true;
            }
            
            // Check apparel tags
            if (apparel.def.apparel?.tags != null)
            {
                foreach (var tag in apparel.def.apparel.tags)
                {
                    var tagLower = tag.ToLower();
                    if (tagLower.Contains("power") || 
                        tagLower.Contains("exo") || 
                        tagLower.Contains("marine"))
                    {
                        return true;
                    }
                }
            }
            
            // Check if it has stat offsets that affect body size or manipulation
            if (apparel.def.equippedStatOffsets != null)
            {
                foreach (var statOffset in apparel.def.equippedStatOffsets)
                {
                    // Some mods use manipulation capacity to represent powered armor strength
                    if (statOffset.stat == PawnCapacityDefOf.Manipulation && statOffset.value > 0)
                    {
                        return true;
                    }
                }
            }
            
            // Check for specific apparel body part groups (torso coverage usually means armor)
            if (apparel.def.apparel?.bodyPartGroups != null)
            {
                bool coversTorso = false;
                bool coversArms = false;
                
                foreach (var group in apparel.def.apparel.bodyPartGroups)
                {
                    if (group.defName == "Torso")
                        coversTorso = true;
                    if (group.defName == "Arms" || group.defName == "Shoulders")
                        coversArms = true;
                }
                
                // Full body coverage armor likely affects effective body size
                if (coversTorso && coversArms)
                {
                    // Check if it's heavy armor (high armor rating or mass)
                    if (apparel.GetStatValue(StatDefOf.ArmorRating_Sharp) > 0.8f ||
                        apparel.GetStatValue(StatDefOf.Mass) > 10f)
                    {
                        return true;
                    }
                }
            }
            
            return false;
        }
    }
    
    /// <summary>
    /// Also clear blacklist when removing power armor
    /// </summary>
    [HarmonyPatch(typeof(Pawn_ApparelTracker), "Remove")]
    public static class Pawn_ApparelTracker_Remove_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn ___pawn, Apparel apparel)
        {
            // Check if mod is enabled
            if (AutoArmMod.settings?.modEnabled != true)
                return;
                
            if (___pawn == null || !___pawn.IsColonist || apparel == null)
                return;
                
            // Check if this apparel changes body size
            if (Pawn_ApparelTracker_Wear_Patch.ApparelChangesBodySize(apparel))
            {
                // Clear the weapon blacklist for this pawn
                WeaponBlacklist.ClearBlacklist(___pawn);
                
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.Debug($"[{___pawn.LabelShort}] Removed {apparel.Label} - cleared weapon blacklist (body size may have changed)");
                }
            }
        }
        
        private static bool ApparelChangesBodySize(Apparel apparel)
        {
            return Pawn_ApparelTracker_Wear_Patch.ApparelChangesBodySize(apparel);
        }
    }
}
