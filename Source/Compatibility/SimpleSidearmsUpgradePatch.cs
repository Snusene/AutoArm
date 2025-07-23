using HarmonyLib;
using RimWorld;
using System;
using System.Linq;
using System.Reflection;
using Verse;
using Verse.AI;

namespace AutoArm
{
    // Patch to handle drafted state changes during sidearm upgrades
    [HarmonyPatch(typeof(Pawn_DraftController), "set_Drafted")]
    public static class Pawn_DraftController_Drafted_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn_DraftController __instance, bool value)
        {
            if (__instance?.pawn == null || !__instance.pawn.IsColonist)
                return;
                
            // If pawn was drafted during a sidearm upgrade, cancel it
            if (value && SimpleSidearmsCompat.HasPendingUpgrade(__instance.pawn))
            {
                SimpleSidearmsCompat.CancelPendingUpgrade(__instance.pawn);
                
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    Log.Message($"[AutoArm] {__instance.pawn.Name}: Cancelling sidearm upgrade - pawn was drafted");
                }
            }
        }
    }
    // Simple patch to handle sidearm upgrades after equip completes
    [HarmonyPatch(typeof(Pawn_EquipmentTracker), "AddEquipment")]
    public static class Pawn_EquipmentTracker_SidearmUpgrade_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn_EquipmentTracker __instance, ThingWithComps newEq)
        {
            if (__instance?.pawn == null || newEq == null || !__instance.pawn.IsColonist)
                return;
                
            // Check if this is a pending sidearm upgrade
            if (SimpleSidearmsCompat.HasPendingUpgrade(__instance.pawn))
            {
                var upgradeInfo = SimpleSidearmsCompat.GetPendingUpgrade(__instance.pawn);
                if (upgradeInfo != null && upgradeInfo.newWeapon == newEq)
                {
                    // Schedule the completion handling for next tick to ensure everything is stable
                    LongEventHandler.ExecuteWhenFinished(() =>
                    {
                        SimpleSidearmsCompat.HandleUpgradeCompletion(__instance.pawn, newEq);
                    });
                }
            }
        }
    }
    
    // Patch to prevent AutoArm from evaluating weapons during sidearm upgrades
    [HarmonyPatch(typeof(JobGiver_PickUpBetterWeapon), "TryGiveJob")]
    public static class JobGiver_PickUpBetterWeapon_IgnoreDuringUpgrade_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(Pawn pawn)
        {
            // Skip weapon evaluation if pawn has a temporary sidearm equipped for upgrading
            if (SimpleSidearmsCompat.PawnHasTemporarySidearmEquipped(pawn))
            {
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    Log.Message($"[AutoArm] Skipping weapon evaluation for {pawn.Name} - temporary sidearm equipped");
                }
                return false;
            }
            
            return true;
        }
    }
    
    // Patch to detect when SimpleSidearms swaps weapons
    // This is kept separate from CombinedHarmonyPatches.cs because it patches SimpleSidearms-specific methods
    // that may not go through the normal Equip job flow
    [HarmonyPatch]
    public static class SimpleSidearms_WeaponSwap_Patch
    {
        private static MethodBase TargetMethod()
        {
            // Try to find SimpleSidearms' weapon swap method
            var weaponAssignmentType = GenTypes.AllTypes.FirstOrDefault(t =>
                t.FullName == "PeteTimesSix.SimpleSidearms.Utilities.WeaponAssingment");
                
            if (weaponAssignmentType != null)
            {
                // Try various method names that might be used for swapping
                var method = weaponAssignmentType.GetMethod("SetPrimary", 
                    BindingFlags.Public | BindingFlags.Static) ??
                             weaponAssignmentType.GetMethod("TrySwapToSidearm",
                    BindingFlags.Public | BindingFlags.Static) ??
                             weaponAssignmentType.GetMethod("equipSpecificWeaponFromInventory",
                    BindingFlags.Public | BindingFlags.Static);
                    
                if (method != null)
                {
                    AutoArmDebug.Log($"Found SimpleSidearms swap method: {method.Name}");
                    return method;
                }
            }
            
            // Fallback - return a dummy method that won't be patched
            return typeof(SimpleSidearms_WeaponSwap_Patch).GetMethod("DummyMethod", 
                BindingFlags.NonPublic | BindingFlags.Static);
        }
        
        private static void DummyMethod(Pawn pawn, ThingWithComps weapon) { } // Never called, but needs same params as Postfix
        
        [HarmonyPostfix]
        public static void Postfix(Pawn pawn, ThingWithComps weapon)
        {
            if (pawn == null || weapon == null || !pawn.IsColonist)
                return;
                
            // When SimpleSidearms swaps weapons, maintain forced status
            // Check if any weapon of this type is forced (primary or sidearm)
            if (ForcedWeaponHelper.IsWeaponDefForced(pawn, weapon.def))
            {
                // This weapon type is forced - maintain that status
                if (pawn.equipment?.Primary == weapon)
                {
                    ForcedWeaponHelper.SetForced(pawn, weapon);
                    AutoArmDebug.LogWeapon(pawn, weapon, "SimpleSidearms swap - maintaining forced status on primary");
                }
            }
            
            // Also check if we need to mark the new sidearm as forced
            // (when a forced primary is moved to inventory)
            if (pawn.inventory?.innerContainer?.Contains(weapon) == true)
            {
                if (ForcedWeaponHelper.IsWeaponDefForced(pawn, weapon.def))
                {
                    ForcedWeaponHelper.AddForcedDef(pawn, weapon.def);
                    AutoArmDebug.LogWeapon(pawn, weapon, "SimpleSidearms swap - maintaining forced status on sidearm");
                }
            }
        }
    }
    
    // Patch to maintain forced status when weapons are moved to inventory
    // Note: This functionality is already handled in CombinedHarmonyPatches.cs
    // in the Pawn_InventoryTracker_TryAddItemNotForSale_Patch
    
    // Clean up stuck upgrades during rare ticks
    [HarmonyPatch(typeof(Pawn), "TickRare")]
    public static class Pawn_TickRare_CleanupUpgrades_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn __instance)
        {
            if (!__instance.IsColonist || !SimpleSidearmsCompat.IsLoaded())
                return;
                
            // Check for stuck upgrades (older than 10 seconds)
            if (SimpleSidearmsCompat.HasPendingUpgrade(__instance))
            {
                var upgradeInfo = SimpleSidearmsCompat.GetPendingUpgrade(__instance);
                if (upgradeInfo != null && Find.TickManager.TicksGame - upgradeInfo.swapStartTick > 600)
                {
                    if (AutoArmMod.settings?.debugLogging == true)
                    {
                        Log.Warning($"[AutoArm] Cleaning up stuck upgrade for {__instance.Name}");
                    }
                    
                    // Try to restore original state
                    if (SimpleSidearmsCompat.PawnHasTemporarySidearmEquipped(__instance))
                    {
                        var currentPrimary = __instance.equipment?.Primary;
                        if (currentPrimary != null && currentPrimary == upgradeInfo.oldWeapon)
                        {
                            // Move the old weapon back to inventory
                            SimpleSidearmsCompat.TrySwapPrimaryToSidearm(__instance, currentPrimary);
                        }
                        
                        // Restore original primary if needed
                        if (upgradeInfo.originalPrimary != null && !upgradeInfo.originalPrimary.Destroyed &&
                            __instance.inventory?.innerContainer?.Contains(upgradeInfo.originalPrimary) == true)
                        {
                            __instance.inventory.innerContainer.Remove(upgradeInfo.originalPrimary);
                            __instance.equipment.AddEquipment(upgradeInfo.originalPrimary);
                        }
                    }
                    
                    SimpleSidearmsCompat.CancelPendingUpgrade(__instance);
                }
            }
        }
    }
}
