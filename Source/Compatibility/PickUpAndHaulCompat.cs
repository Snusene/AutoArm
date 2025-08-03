using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using AutoArm.Helpers; using AutoArm.Logging; using AutoArm.Weapons;

namespace AutoArm
{
    /// <summary>
    /// Pick Up and Haul compatibility module
    /// Only prevents weapon hoarding when SimpleSidearms is also installed
    /// Does NOT interfere with normal hauling operations
    /// 
    /// IMPORTANT: We do NOT check outfit filters for hauled weapons because:
    /// - Inventory is used for cargo transport, not just equipment
    /// - A pacifist with "no weapons" outfit should still haul weapons to stockpiles
    /// - Outfit filters control what pawns WEAR/WIELD, not what they CARRY
    /// </summary>
    public static class PickUpAndHaulCompat
    {
        private static bool? _isLoaded = null;
        private static bool _initialized = false;

        // Job defs from Pick Up and Haul
        internal static JobDef haulToInventoryJobDef;
        internal static JobDef unloadInventoryJobDef;
        
        // Track pawns currently hauling to avoid validation spam
        internal static HashSet<Pawn> pawnsCurrentlyHauling = new HashSet<Pawn>();

        public static bool IsLoaded()
        {
            if (_isLoaded == null)
            {
                _isLoaded = ModLister.AllInstalledMods.Any(m =>
                    m.Active && (
                        m.PackageIdPlayerFacing.Equals("mehni.pickupandhaul", StringComparison.OrdinalIgnoreCase) ||
                        m.PackageIdPlayerFacing.Equals("Mehni.PickUpAndHaul", StringComparison.OrdinalIgnoreCase)
                    ));

                if (_isLoaded.Value)
                {
                    AutoArmLogger.Debug("Pick Up and Haul detected and loaded");
                }
            }
            return _isLoaded.Value;
        }

        private static void EnsureInitialized()
        {
            if (_initialized || !IsLoaded())
                return;

            try
            {
                // Find Pick Up and Haul job defs
                haulToInventoryJobDef = DefDatabase<JobDef>.GetNamedSilentFail("HaulToInventory");
                unloadInventoryJobDef = DefDatabase<JobDef>.GetNamedSilentFail("UnloadYourHauledInventory");

                _initialized = true;
                if (haulToInventoryJobDef != null && unloadInventoryJobDef != null)
                {
                    AutoArmLogger.Debug("Pick Up and Haul compatibility initialized successfully");
                }
                else
                {
                    AutoArmLogger.Warn($"Pick Up and Haul partial initialization - HaulToInventory: {haulToInventoryJobDef != null}, UnloadInventory: {unloadInventoryJobDef != null}");
                }
            }
            catch (Exception e)
            {
                AutoArmLogger.Error("Failed to initialize Pick Up and Haul compatibility", e);
            }
        }

        /// <summary>
        /// Check if pawn is currently doing a Pick Up and Haul job
        /// </summary>
        public static bool IsPawnHaulingToInventory(Pawn pawn)
        {
            if (!IsLoaded() || pawn?.CurJob == null)
                return false;

            EnsureInitialized();

            return pawn.CurJob.def == haulToInventoryJobDef ||
                   pawn.CurJob.def == unloadInventoryJobDef ||
                   pawn.CurJob.def?.defName == "HaulToInventory" ||
                   pawn.CurJob.def?.defName == "UnloadYourHauledInventory";
        }

        /// <summary>
        /// Validate weapons in inventory ONLY for SimpleSidearms limits
        /// Does NOT check outfit filters because haulers need to haul regardless of outfit
        /// Only runs when both Pick Up and Haul AND SimpleSidearms are loaded together
        /// to avoid interfering with normal hauling when SimpleSidearms isn't present
        /// </summary>
        public static void ValidateInventoryWeapons(Pawn pawn)
        {
            // Only run if both mods are loaded
            if (!IsLoaded() || !SimpleSidearmsCompat.IsLoaded())
                return;
                
            if (pawn == null || !pawn.IsColonist || pawn.inventory?.innerContainer == null)
                return;

            if (AutoArmMod.settings?.modEnabled != true)
                return;

            // Skip if pawn can't use weapons anyway
            if (pawn.WorkTagIsDisabled(WorkTags.Violent))
                return;

            // Only check for duplicate weapons and SimpleSidearms limits
            // Do NOT check outfit filters - that would break hauling

            // Track validation statistics
            int weaponsChecked = 0;
            int duplicatesDropped = 0;
            int limitsExceeded = 0;

            var weaponsToCheck = pawn.inventory.innerContainer
                .OfType<ThingWithComps>()
                .Where(t => t.def.IsWeapon && WeaponValidation.IsProperWeapon(t))
                .ToList();
                
            weaponsChecked = weaponsToCheck.Count;

            // Count weapons by type (including primary)
            var weaponCounts = new System.Collections.Generic.Dictionary<ThingDef, int>();
            
            if (pawn.equipment?.Primary != null)
            {
                var def = pawn.equipment.Primary.def;
                weaponCounts[def] = 1;
            }

            foreach (var weapon in weaponsToCheck)
            {
                var def = weapon.def;
                weaponCounts[def] = weaponCounts.ContainsKey(def) ? weaponCounts[def] + 1 : 1;
            }

            // Check for duplicates if SimpleSidearms doesn't allow them
            if (!SimpleSidearmsCompat.ALLOW_DUPLICATE_WEAPON_TYPES)
            {
                foreach (var def in weaponCounts.Keys.ToList())
                {
                    if (weaponCounts[def] > 1)
                    {
                        // Keep the best one, drop the rest
                        var sameTypeWeapons = weaponsToCheck.Where(w => w.def == def).ToList();
                        if (sameTypeWeapons.Count > 1)
                        {
                            // Sort by score, keep the best
                            var sortedWeapons = sameTypeWeapons
                                .OrderByDescending(w => WeaponScoringHelper.GetTotalScore(pawn, w))
                                .ToList();

                            // Drop all but the best
                            for (int i = 1; i < sortedWeapons.Count; i++)
                            {
                                var weaponToDrop = sortedWeapons[i];
                                
                                // Don't drop forced weapons
                                if (ForcedWeaponHelper.IsWeaponDefForced(pawn, weaponToDrop.def))
                                    continue;
                                    
                                Thing droppedWeapon;
                                if (pawn.inventory.innerContainer.TryDrop(weaponToDrop, pawn.Position, pawn.Map,
                                    ThingPlaceMode.Near, out droppedWeapon))
                                {
                                    if (droppedWeapon != null)
                                    {
                                        DroppedItemTracker.MarkAsDropped(droppedWeapon, 1200); // 20 seconds
                                        duplicatesDropped++;
                                        if (Prefs.DevMode)
                                            AutoArmLogger.Debug($"[PickUpAndHaul] {pawn.LabelShort} dropped duplicate weapon: {weaponToDrop.Label}");
                                    }
                                }
                            }
                        }
                    }
                }
            }

            // Check SimpleSidearms weight/slot limits ONLY
            foreach (var weapon in weaponsToCheck.ToList())
            {
                string reason;
                if (!SimpleSidearmsCompat.CanPickupSidearmInstance(weapon, pawn, out reason))
                {
                    // Check if the reason is actually about limits, not outfit filters
                    if (reason != null && (
                        reason.IndexOf("weight", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        reason.IndexOf("mass", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        reason.IndexOf("slot", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        reason.IndexOf("space", StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        // Don't drop forced weapons
                        if (ForcedWeaponHelper.IsWeaponDefForced(pawn, weapon.def))
                            continue;

                        // Drop weapons that exceed weight/slot limits
                        Thing droppedWeapon;
                        if (pawn.inventory.innerContainer.TryDrop(weapon, pawn.Position, pawn.Map,
                            ThingPlaceMode.Near, out droppedWeapon))
                        {
                            if (droppedWeapon != null)
                            {
                                DroppedItemTracker.MarkAsDropped(droppedWeapon, 1200); // 20 seconds
                                SimpleSidearmsCompat.InformOfDroppedSidearm(pawn, weapon);
                                limitsExceeded++;
                                if (Prefs.DevMode)
                                    AutoArmLogger.Debug($"[PickUpAndHaul] {pawn.LabelShort} dropped weapon exceeding limits: {weapon.Label} - {reason}");
                            }
                        }
                    }
                }
            }
            
            // Log summary if anything was dropped
            if ((duplicatesDropped > 0 || limitsExceeded > 0) && AutoArmMod.settings?.debugLogging == true)
            {
                AutoArmLogger.Debug($"[PickUpAndHaul] {pawn.LabelShort} validation complete: {weaponsChecked} weapons checked, {duplicatesDropped} duplicates dropped, {limitsExceeded} exceeded limits");
            }
        }
        
        /// <summary>
        /// Cleanup method for save/load
        /// </summary>
        public static void CleanupAfterLoad()
        {
            pawnsCurrentlyHauling.Clear();
        }
    }

    /// <summary>
    /// Patch to validate inventory after Pick Up and Haul jobs complete
    /// Only runs if SimpleSidearms is also loaded
    /// </summary>
    public static class PickUpAndHaul_JobEnd_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn ___pawn, JobCondition condition, Job ___curJob)
        {
            // Only run if both mods are loaded
            if (!PickUpAndHaulCompat.IsLoaded() || !SimpleSidearmsCompat.IsLoaded())
                return;
                
            if (___pawn == null || ___curJob == null)
                return;

            // Only check colonists who can use weapons
            if (!___pawn.IsColonist || ___pawn.WorkTagIsDisabled(WorkTags.Violent))
                return;

            // Check if this was a Pick Up and Haul job
            bool isPUAHJob = ___curJob.def == PickUpAndHaulCompat.haulToInventoryJobDef ||
                            ___curJob.def == PickUpAndHaulCompat.unloadInventoryJobDef ||
                            ___curJob.def?.defName == "HaulToInventory" ||
                            ___curJob.def?.defName == "UnloadYourHauledInventory";
            
            if (!isPUAHJob)
                return;
                
            // Remove from tracking
            PickUpAndHaulCompat.pawnsCurrentlyHauling.Remove(___pawn);

            // Validate on completion AND interruption to catch all cases
            if (condition == JobCondition.Succeeded ||
                condition == JobCondition.InterruptForced ||
                condition == JobCondition.InterruptOptional)
            {
                // Schedule validation for next tick to ensure inventory is stable
                LongEventHandler.ExecuteWhenFinished(() =>
                {
                    if (___pawn != null && ___pawn.Spawned && !___pawn.Dead)
                    {
                        PickUpAndHaulCompat.ValidateInventoryWeapons(___pawn);
                    }
                });
            }
        }
    }
    
    /// <summary>
    /// Patch to track when PUAH jobs start
    /// </summary>
    public static class PickUpAndHaul_JobStart_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn ___pawn, Job newJob)
        {
            if (!PickUpAndHaulCompat.IsLoaded() || !SimpleSidearmsCompat.IsLoaded())
                return;
                
            if (___pawn == null || newJob == null || !___pawn.IsColonist)
                return;
                
            bool isPUAHJob = newJob.def == PickUpAndHaulCompat.haulToInventoryJobDef ||
                            newJob.def?.defName == "HaulToInventory";
                            
            if (isPUAHJob)
            {
                PickUpAndHaulCompat.pawnsCurrentlyHauling.Add(___pawn);
                if (Prefs.DevMode && AutoArmMod.settings?.debugLogging == true)
                    AutoArmLogger.Debug($"[PickUpAndHaul] {___pawn.LabelShort} started hauling job");
            }
        }
    }
    
    /// <summary>
    /// Patch to validate immediately when items are added to inventory
    /// This catches weapons as they're hauled
    /// </summary>
    public static class InventoryTracker_AddItem_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn_InventoryTracker __instance, Thing item)
        {
            // Only run if both mods are loaded
            if (!PickUpAndHaulCompat.IsLoaded() || !SimpleSidearmsCompat.IsLoaded())
                return;
                
            if (item == null || !item.def.IsWeapon || __instance?.pawn == null)
                return;
                
            var pawn = __instance.pawn;
            
            // Skip non-colonists and violence-incapable
            if (!pawn.IsColonist || pawn.WorkTagIsDisabled(WorkTags.Violent))
                return;
                
            // Only validate if pawn is actively hauling (to avoid spam during normal operations)
            if (PickUpAndHaulCompat.pawnsCurrentlyHauling.Contains(pawn))
            {
                // Check if the weapon was actually added to inventory
                if (pawn.inventory?.innerContainer?.Contains(item) == true)
                {
                    // Immediate validation for actively hauling pawns
                    PickUpAndHaulCompat.ValidateInventoryWeapons(pawn);
                }
            }
        }
    }

    // REMOVED: PickUpAndHaul_PreventExcessPickup_Patch
    // This was preventing pawns from hauling weapons, which breaks the mod's core functionality
}