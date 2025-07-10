using HarmonyLib;
using RimWorld;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Verse;
using Verse.AI;

namespace AutoArm
{
    public static class SimpleSidearmsCompat
    {
        private static bool? _isLoaded = null;
        private static bool _initialized = false;

        // Types
        private static Type compSidearmMemoryType;
        private static Type thingDefStuffDefPairType;

        // Properties and Fields
        private static PropertyInfo rememberedWeaponsProperty;
        private static FieldInfo rememberedWeaponsField;
        private static FieldInfo thingField;
        private static FieldInfo stuffField;

        // JobDef
        private static JobDef equipSecondaryJobDef;

        public static bool IsLoaded()
        {
            if (_isLoaded == null)
            {
                _isLoaded = ModLister.AllInstalledMods.Any(m =>
                    m.Active && m.PackageIdPlayerFacing == "PeteTimesSix.SimpleSidearms");

                if (AutoArmMod.settings?.debugLogging == true)
                {
                    Log.Message($"[AutoArm] Simple Sidearms detection result: {_isLoaded}");
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
                // Find CompSidearmMemory type
                compSidearmMemoryType = GenTypes.AllTypes
                    .FirstOrDefault(t => t.FullName == "SimpleSidearms.rimworld.CompSidearmMemory");

                if (compSidearmMemoryType == null)
                {
                    if (AutoArmMod.settings?.debugLogging == true)
                    {
                        Log.Message("[AutoArm] Could not find CompSidearmMemory type");
                    }
                    return;
                }

                // Find ThingDefStuffDefPair type
                thingDefStuffDefPairType = GenTypes.AllTypes
                    .FirstOrDefault(t => t.FullName == "SimpleSidearms.rimworld.ThingDefStuffDefPair");

                if (thingDefStuffDefPairType != null)
                {
                    thingField = thingDefStuffDefPairType.GetField("thing");
                    stuffField = thingDefStuffDefPairType.GetField("stuff");
                }

                // Get the RememberedWeapons property or field
                rememberedWeaponsProperty = compSidearmMemoryType.GetProperty("RememberedWeapons", BindingFlags.Public | BindingFlags.Instance);
                if (rememberedWeaponsProperty == null)
                {
                    rememberedWeaponsField = compSidearmMemoryType.GetField("rememberedWeapons", BindingFlags.Public | BindingFlags.Instance);
                }

                // Find the EquipSecondary JobDef
                string[] possibleJobNames = { "EquipSecondary", "SimpleSidearms_EquipSecondary", "Sidearms_EquipSecondary" };

                foreach (var name in possibleJobNames)
                {
                    equipSecondaryJobDef = DefDatabase<JobDef>.GetNamedSilentFail(name);
                    if (equipSecondaryJobDef != null)
                        break;
                }

                // If not found, search by pattern
                if (equipSecondaryJobDef == null)
                {
                    equipSecondaryJobDef = DefDatabase<JobDef>.AllDefs
                        .FirstOrDefault(j => j.defName.Contains("EquipSecondary") ||
                                           (j.defName.Contains("Sidearm") && j.defName.Contains("Equip")));
                }

                _initialized = true;

                if (AutoArmMod.settings?.debugLogging == true)
                {
                    Log.Message($"[AutoArm] Simple Sidearms compatibility initialized: " +
                              $"MemoryComp={compSidearmMemoryType != null}, " +
                              $"JobDef={equipSecondaryJobDef?.defName ?? "null"}");
                }
            }
            catch (Exception e)
            {
                Log.Warning($"[AutoArm] Failed to initialize Simple Sidearms compatibility: {e.Message}");
            }
        }

        private static IEnumerable GetRememberedWeapons(ThingComp comp)
        {
            if (comp == null) return null;

            if (rememberedWeaponsProperty != null)
                return rememberedWeaponsProperty.GetValue(comp) as IEnumerable;

            if (rememberedWeaponsField != null)
                return rememberedWeaponsField.GetValue(comp) as IEnumerable;

            return null;
        }

        private static ThingComp GetSidearmComp(Pawn pawn)
        {
            if (!IsLoaded() || pawn == null) return null;

            EnsureInitialized();

            if (compSidearmMemoryType == null) return null;

            return pawn.AllComps?.FirstOrDefault(c => c.GetType() == compSidearmMemoryType);
        }

        public static bool CanPickupWeaponAsSidearm(ThingWithComps weapon, Pawn pawn, out string reason)
        {
            reason = "";

            if (!IsLoaded() || weapon == null || pawn == null)
                return true;

            EnsureInitialized();

            try
            {
                // Get the StatCalculator type
                var statCalcType = GenTypes.AllTypes.FirstOrDefault(t =>
                    t.Name == "StatCalculator" &&
                    t.Namespace?.Contains("SimpleSidearms") == true);

                if (statCalcType == null)
                    return true;

                // Find CanPickupSidearmInstance method
                var canPickupMethod = statCalcType.GetMethod("CanPickupSidearmInstance",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new Type[] { typeof(ThingWithComps), typeof(Pawn), typeof(string).MakeByRefType() },
                    null);

                if (canPickupMethod == null)
                    return true;

                // Call the method
                object[] parameters = new object[] { weapon, pawn, null };
                bool result = (bool)canPickupMethod.Invoke(null, parameters);
                reason = (string)parameters[2] ?? "";

                return result;
            }
            catch
            {
                return true;
            }
        }

        public static Job TryGetSidearmUpgradeJob(Pawn pawn)
        {
            if (!IsLoaded() || pawn == null || !pawn.IsColonist || equipSecondaryJobDef == null)
                return null;

            // Check if sidearm auto-equip is enabled
            if (AutoArmMod.settings?.autoEquipSidearms != true)
                return null;

            EnsureInitialized();

            if (!_initialized)
                return null;

            try
            {
                var comp = GetSidearmComp(pawn);
                if (comp == null)
                    return null;

                // Get current sidearms
                var currentSidearmDefs = new HashSet<ThingDef>();
                var sidearmsList = GetRememberedWeapons(comp);

                if (sidearmsList != null && thingField != null)
                {
                    foreach (var sidearmInfo in sidearmsList)
                    {
                        if (sidearmInfo != null)
                        {
                            var weaponDef = thingField.GetValue(sidearmInfo) as ThingDef;
                            if (weaponDef != null)
                            {
                                currentSidearmDefs.Add(weaponDef);
                            }
                        }
                    }
                }

                int maxSidearms = GetMaxSidearmsForPawn(pawn);
                int availableSlots = maxSidearms - currentSidearmDefs.Count;

                if (AutoArmMod.settings?.debugLogging == true)
                {
                    Log.Message($"[AutoArm] {pawn.Name} has {currentSidearmDefs.Count}/{maxSidearms} sidearms");
                }

                // Check Simple Sidearms limits
                if (availableSlots <= 0)
                {
                    // Try to upgrade existing sidearms
                    return TryUpgradeExistingSidearm(pawn, currentSidearmDefs);
                }

                // Look for good weapons to add as sidearms
                return TryFindNewSidearm(pawn, currentSidearmDefs);
            }
            catch (Exception e)
            {
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    Log.Warning($"[AutoArm] Error finding sidearm upgrade: {e.Message}");
                }
                return null;
            }
        }

        private static Job TryUpgradeExistingSidearm(Pawn pawn, HashSet<ThingDef> currentSidearmDefs)
        {
            var worstSidearm = pawn.inventory.innerContainer
                .OfType<ThingWithComps>()
                .Where(t => t.def.IsWeapon && currentSidearmDefs.Contains(t.def) &&
                           !ForcedWeaponTracker.IsForcedSidearm(pawn, t.def))
                .OrderBy(w => GetBasicWeaponScore(w))
                .FirstOrDefault();

            if (worstSidearm == null) return null;

            float worstScore = GetBasicWeaponScore(worstSidearm);

            var betterWeapon = pawn.Map.listerThings.ThingsInGroup(ThingRequestGroup.Weapon)
                .OfType<ThingWithComps>()
                .Where(w => IsValidSidearmCandidate(w, pawn) &&
                           !currentSidearmDefs.Contains(w.def) &&
                           GetBasicWeaponScore(w) > worstScore * 1.15f)
                .OrderByDescending(w => GetBasicWeaponScore(w))
                .FirstOrDefault();

            if (betterWeapon != null)
            {
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    Log.Message($"[AutoArm] {pawn.Name} upgrading sidearm {worstSidearm.Label} to {betterWeapon.Label}");
                }

                // Drop the worst sidearm
                Thing droppedThing;
                pawn.inventory.innerContainer.TryDrop(worstSidearm, pawn.Position, pawn.Map, ThingPlaceMode.Near, out droppedThing);

                return JobMaker.MakeJob(equipSecondaryJobDef, betterWeapon);
            }

            return null;
        }

        private static Job TryFindNewSidearm(Pawn pawn, HashSet<ThingDef> currentSidearmDefs)
        {
            if (pawn?.Map == null)
                return null;

            var validWeapon = pawn.Map.listerThings.ThingsInGroup(ThingRequestGroup.Weapon)
                .OfType<ThingWithComps>()
                .Where(w => w != null && w.def != null &&
                           IsValidSidearmCandidate(w, pawn) &&
                           !currentSidearmDefs.Contains(w.def))
                .OrderBy(w => w.Position.DistanceTo(pawn.Position))
                .Take(20)
                .Where(w => GetBasicWeaponScore(w) > 0)
                .OrderByDescending(w => GetBasicWeaponScore(w) / (1f + w.Position.DistanceTo(pawn.Position) / 100f))
                .FirstOrDefault();

            if (validWeapon != null && equipSecondaryJobDef != null)
            {
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    Log.Message($"[AutoArm] {pawn.Name} will add {validWeapon.Label} as sidearm");
                }
                return JobMaker.MakeJob(equipSecondaryJobDef, validWeapon);
            }

            return null;
        }

        private static float GetBasicWeaponScore(ThingWithComps weapon)
        {
            if (weapon?.def == null)
                return 0f;

            float score = 100f;

            // Quality
            if (weapon.TryGetQuality(out QualityCategory qc))
            {
                score += (int)qc * 20f;
            }

            // Condition
            if (weapon.MaxHitPoints > 0)
            {
                float hpPercent = weapon.HitPoints / (float)weapon.MaxHitPoints;
                score += hpPercent * 50f;

                if (hpPercent < 0.3f)
                    return 0f;
            }

            // Tech level
            score += (int)weapon.def.techLevel * 10f;

            // Penalize basic weapons
            if (weapon.def.defName == "WoodLog" || weapon.def.defName == "MeleeWeapon_Club")
                score *= 0.5f;

            return score;
        }

        private static bool IsValidSidearmCandidate(ThingWithComps weapon, Pawn pawn)
        {
            if (weapon == null || weapon.def == null || weapon.Destroyed || weapon.IsForbidden(pawn))
                return false;

            // Don't take weapons from inventory
            if (weapon.ParentHolder is Pawn_InventoryTracker || weapon.ParentHolder is Pawn_EquipmentTracker)
                return false;

            // Check outfit filter
            var filter = pawn.outfits?.CurrentApparelPolicy?.filter;
            if (filter != null && !filter.Allows(weapon.def))
                return false;

            // Check Simple Sidearms restrictions
            if (!CanPickupWeaponAsSidearm(weapon, pawn, out string reason))
            {
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    Log.Message($"[AutoArm] {pawn.Name}: {weapon.def.defName} blocked by Simple Sidearms: {reason}");
                }
                return false;
            }

            return pawn.CanReserveAndReach(weapon, PathEndMode.ClosestTouch, Danger.Deadly);
        }

        public static int GetMaxSidearmsForPawn(Pawn pawn)
        {
            // Try to get actual Simple Sidearms settings
            try
            {
                var settingsType = GenTypes.AllTypes.FirstOrDefault(t =>
                    t.Name == "SimpleSidearms_Settings" ||
                    (t.Name == "Settings" && t.Namespace?.Contains("SimpleSidearms") == true));

                if (settingsType != null)
                {
                    var modType = GenTypes.AllTypes.FirstOrDefault(t =>
                        t.Name == "SimpleSidearmsMod" ||
                        (t.Name.Contains("SimpleSidearms") && t.IsSubclassOf(typeof(Mod))));

                    if (modType != null)
                    {
                        var settingsField = modType.GetField("settings", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic) ??
                                          modType.GetField("Settings", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

                        if (settingsField != null)
                        {
                            var settings = settingsField.GetValue(null);
                            if (settings != null)
                            {
                                var limitField = settingsType.GetField("SidearmLimit") ??
                                               settingsType.GetField("LimitModeSingle") ??
                                               settingsType.GetField("maxSidearms") ??
                                               settingsType.GetField("MaxSidearms");

                                if (limitField != null)
                                {
                                    var value = limitField.GetValue(settings);
                                    if (value is int limit)
                                        return limit;
                                    else if (value is float fLimit)
                                        return (int)fLimit;
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                // Default fallback
            }

            return 3;
        }

        public static int GetCurrentSidearmCount(Pawn pawn)
        {
            if (!IsLoaded() || pawn?.inventory?.innerContainer == null)
                return 0;

            return pawn.inventory.innerContainer.Count(t => t.def.IsWeapon);
        }

        public static bool ShouldSkipAutoEquip(Pawn pawn)
        {
            // Don't skip anyone - let Simple Sidearms handle the heavy lifting
            return false;
        }

        public static bool ShouldUpgradeMainWeapon(Pawn pawn, ThingWithComps newWeapon, float currentScore, float newScore)
        {
            // Always allow main weapon upgrades - Simple Sidearms will manage switching between them
            return true;
        }

        public static bool IsSimpleSidearmsSwitch(Pawn pawn, Thing weapon)
        {
            // Would need to track this through Harmony patches
            return false;
        }

        public static void CheckPendingSidearmRegistrations(Pawn pawn)
        {
            // Removed - not needed with simplified system
        }

        public static void CleanupPendingRegistrations()
        {
            // Removed - not needed with simplified system
        }

        public static bool HasNoSidearms(Pawn pawn)
        {
            if (!IsLoaded() || pawn == null)
                return false;

            // Quick check - no weapons in inventory at all
            if (pawn.inventory?.innerContainer == null ||
                !pawn.inventory.innerContainer.Any(t => t.def.IsWeapon))
                return true;

            // More thorough check using Simple Sidearms data
            EnsureInitialized();
            if (!_initialized)
                return true;

            try
            {
                var comp = GetSidearmComp(pawn);
                if (comp == null)
                    return true;

                var sidearmsList = GetRememberedWeapons(comp);
                if (sidearmsList == null)
                    return true;

                // Check if list is empty
                foreach (var item in sidearmsList)
                {
                    return false; // Has at least one sidearm
                }

                return true; // Empty list
            }
            catch
            {
                // On error, fall back to simple inventory check
                return !pawn.inventory.innerContainer.Any(t => t.def.IsWeapon);
            }
        }
    }
}