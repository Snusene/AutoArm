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
        private static FieldInfo primaryWeaponModeField;

        // Fields in ThingDefStuffDefPair
        private static FieldInfo thingField;
        private static FieldInfo stuffField;

        // JobDef
        private static JobDef equipSecondaryJobDef;

        // Track pending sidearm registrations
        private static Dictionary<Pawn, List<ThingWithComps>> pendingSidearmRegistrations = new Dictionary<Pawn, List<ThingWithComps>>();

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

            if (AutoArmMod.settings?.debugLogging == true)
            {
                Log.Message("[AutoArm] Starting Simple Sidearms initialization...");
            }

            try
            {
                // Find CompSidearmMemory type
                compSidearmMemoryType = GenTypes.AllTypes
                    .FirstOrDefault(t => t.FullName == "SimpleSidearms.rimworld.CompSidearmMemory");

                if (compSidearmMemoryType == null)
                {
                    if (AutoArmMod.settings?.debugLogging == true)
                    {
                        Log.Message("[AutoArm] Could not find SimpleSidearms.rimworld.CompSidearmMemory type");
                    }
                    return;
                }

                // Find ThingDefStuffDefPair type
                thingDefStuffDefPairType = GenTypes.AllTypes
                    .FirstOrDefault(t => t.FullName == "SimpleSidearms.rimworld.ThingDefStuffDefPair");

                if (thingDefStuffDefPairType != null)
                {
                    // Get fields from ThingDefStuffDefPair
                    thingField = thingDefStuffDefPairType.GetField("thing");
                    stuffField = thingDefStuffDefPairType.GetField("stuff");

                    if (AutoArmMod.settings?.debugLogging == true)
                    {
                        Log.Message($"[AutoArm] Found ThingDefStuffDefPair type with fields: thing={thingField != null}, stuff={stuffField != null}");
                    }
                }

                // Get the RememberedWeapons property
                rememberedWeaponsProperty = compSidearmMemoryType.GetProperty("RememberedWeapons", BindingFlags.Public | BindingFlags.Instance);

                // Or try the field if property fails
                if (rememberedWeaponsProperty == null)
                {
                    rememberedWeaponsField = compSidearmMemoryType.GetField("rememberedWeapons", BindingFlags.Public | BindingFlags.Instance);
                }

                primaryWeaponModeField = compSidearmMemoryType.GetField("primaryWeaponMode", BindingFlags.Public | BindingFlags.Instance);

                if (AutoArmMod.settings?.debugLogging == true)
                {
                    Log.Message($"[AutoArm] Found RememberedWeapons property: {rememberedWeaponsProperty != null}");
                    Log.Message($"[AutoArm] Found rememberedWeapons field: {rememberedWeaponsField != null}");
                    Log.Message($"[AutoArm] Found primaryWeaponMode field: {primaryWeaponModeField != null}");
                }

                // If we don't have access to the weapons list, fail
                if (rememberedWeaponsProperty == null && rememberedWeaponsField == null)
                {
                    if (AutoArmMod.settings?.debugLogging == true)
                    {
                        Log.Message("[AutoArm] Could not find remembered weapons property or field");
                    }
                    return;
                }

                // Find the EquipSecondary JobDef
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    Log.Message("[AutoArm] Searching for EquipSecondary JobDef...");
                }

                // Try multiple approaches to find the JobDef
                equipSecondaryJobDef = DefDatabase<JobDef>.GetNamedSilentFail("EquipSecondary") ??
                                      DefDatabase<JobDef>.GetNamedSilentFail("SimpleSidearms_EquipSecondary") ??
                                      DefDatabase<JobDef>.GetNamedSilentFail("Sidearms_EquipSecondary");

                if (equipSecondaryJobDef == null)
                {
                    // Try to find it by searching all JobDefs
                    equipSecondaryJobDef = DefDatabase<JobDef>.AllDefs
                        .FirstOrDefault(j => j.defName.Contains("EquipSecondary") ||
                                           (j.defName.Contains("Sidearm") && j.defName.Contains("Equip")));

                    if (equipSecondaryJobDef != null && AutoArmMod.settings?.debugLogging == true)
                    {
                        Log.Message($"[AutoArm] Found JobDef by search: {equipSecondaryJobDef.defName}");
                    }
                }

                if (equipSecondaryJobDef == null)
                {
                    Log.Warning("[AutoArm] Could not find EquipSecondary JobDef. Sidearm functionality will be limited.");

                    if (AutoArmMod.settings?.debugLogging == true)
                    {
                        Log.Message("[AutoArm] Available JobDefs with 'sidearm' or 'secondary':");
                        foreach (var jobDef in DefDatabase<JobDef>.AllDefs.Where(j =>
                            j.defName.ToLower().Contains("sidearm") ||
                            j.defName.ToLower().Contains("secondary")))
                        {
                            Log.Message($"  - {jobDef.defName}");
                        }
                    }
                }
                else if (AutoArmMod.settings?.debugLogging == true)
                {
                    Log.Message($"[AutoArm] Found EquipSecondary JobDef: {equipSecondaryJobDef.defName}");
                }

                _initialized = true;
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    Log.Message("[AutoArm] Simple Sidearms compatibility initialized successfully");
                }
            }
            catch (Exception e)
            {
                Log.Warning($"[AutoArm] Failed to initialize Simple Sidearms compatibility: {e.Message}");
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    Log.Warning($"[AutoArm] Stack trace: {e.StackTrace}");
                }
            }
        }

        public static bool CanPickupWeaponAsSidearm(ThingWithComps weapon, Pawn pawn, out string reason)
        {
            if (!IsLoaded() || weapon == null || pawn == null)
            {
                reason = "";
                return true;
            }

            EnsureInitialized();

            try
            {
                // Get the StatCalculator type
                var statCalcType = GenTypes.AllTypes.FirstOrDefault(t =>
                    t.Name == "StatCalculator" &&
                    t.Namespace?.Contains("SimpleSidearms") == true);

                if (statCalcType == null)
                {
                    reason = "";
                    return true;
                }

                // Find CanPickupSidearmInstance method
                var canPickupMethod = statCalcType.GetMethod("CanPickupSidearmInstance",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new Type[] { typeof(ThingWithComps), typeof(Pawn), typeof(string).MakeByRefType() },
                    null);

                if (canPickupMethod == null)
                {
                    reason = "";
                    return true;
                }

                // Call the method
                object[] parameters = new object[] { weapon, pawn, null };
                bool result = (bool)canPickupMethod.Invoke(null, parameters);
                reason = (string)parameters[2] ?? "";

                return result;
            }
            catch (Exception e)
            {
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    Log.Warning($"[AutoArm] Error checking Simple Sidearms compatibility: {e.Message}");
                }
                reason = "";
                return true;
            }
        }

        public static Job TryGetSidearmUpgradeJob(Pawn pawn)
        {
            if (!IsLoaded() || pawn == null || !pawn.IsColonist)
                return null;

            // Check if sidearm auto-equip is enabled
            if (AutoArmMod.settings?.autoEquipSidearms != true)
                return null;

            EnsureInitialized();

            if (!_initialized)
            {
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    Log.Message($"[AutoArm] {pawn.Name}: Simple Sidearms not initialized");
                }
                return null;
            }

            if (equipSecondaryJobDef == null)
            {
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    Log.Message($"[AutoArm] {pawn.Name}: EquipSecondary JobDef is null");
                }
                return null;
            }

            try
            {
                var comp = pawn.AllComps?.FirstOrDefault(c => c.GetType() == compSidearmMemoryType);
                if (comp == null)
                {
                    if (AutoArmMod.settings?.debugLogging == true)
                    {
                        Log.Message($"[AutoArm] {pawn.Name} has no sidearm memory component");
                    }
                    return null;
                }

                // Get current sidearms
                IEnumerable sidearmsList = null;

                if (rememberedWeaponsProperty != null)
                {
                    sidearmsList = rememberedWeaponsProperty.GetValue(comp) as IEnumerable;
                }
                else if (rememberedWeaponsField != null)
                {
                    sidearmsList = rememberedWeaponsField.GetValue(comp) as IEnumerable;
                }

                if (sidearmsList == null)
                {
                    if (AutoArmMod.settings?.debugLogging == true)
                    {
                        Log.Message($"[AutoArm] Could not get sidearm list for {pawn.Name}");
                    }
                    return null;
                }

                // Convert to list of ThingDefs
                var currentSidearmDefs = new List<ThingDef>();
                var forcedSidearmCount = 0;

                if (sidearmsList != null)
                {
                    foreach (var sidearmInfo in sidearmsList)
                    {
                        if (sidearmInfo != null && thingField != null)
                        {
                            var weaponDef = thingField.GetValue(sidearmInfo) as ThingDef;
                            if (weaponDef != null)
                            {
                                currentSidearmDefs.Add(weaponDef);

                                // Count forced sidearms
                                if (ForcedWeaponTracker.IsForcedSidearm(pawn, weaponDef))
                                    forcedSidearmCount++;
                            }
                        }
                    }
                }

                int maxSidearms = GetMaxSidearmsForPawn(pawn);
                int effectiveCurrentCount = currentSidearmDefs.Count;
                int availableSlots = maxSidearms - effectiveCurrentCount;

                if (AutoArmMod.settings?.debugLogging == true)
                {
                    Log.Message($"[AutoArm] {pawn.Name} has {effectiveCurrentCount}/{maxSidearms} sidearms ({forcedSidearmCount} forced)");
                }

                // Check Simple Sidearms limits
                if (availableSlots <= 0)
                {
                    // Try to upgrade existing sidearms
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

                        // Inform Simple Sidearms before dropping
                        var informMethod = comp.GetType().GetMethod("InformOfDroppedSidearm");
                        if (informMethod != null)
                        {
                            informMethod.Invoke(comp, new object[] { worstSidearm, true });
                        }

                        Thing droppedThing;
                        pawn.inventory.innerContainer.TryDrop(worstSidearm, pawn.Position, pawn.Map, ThingPlaceMode.Near, out droppedThing);
                        return JobMaker.MakeJob(equipSecondaryJobDef, betterWeapon);
                    }

                    return null;
                }

                // Look for good weapons to add as sidearms
                return TryFindNewSidearm(pawn, currentSidearmDefs);
            }
            catch (Exception e)
            {
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    Log.Warning($"[AutoArm] Error finding sidearm upgrade: {e.Message}");
                    Log.Warning($"[AutoArm] Stack trace: {e.StackTrace}");
                }
            }

            return null;
        }

        private static float GetBasicWeaponScore(ThingWithComps weapon)
        {
            if (weapon?.def == null)
                return 0f;

            float score = 100f; // Base score

            // Quality is important (up to +120 points)
            if (weapon.TryGetQuality(out QualityCategory qc))
            {
                score += (int)qc * 20f;
            }

            // Condition matters (up to +50 points)
            if (weapon.MaxHitPoints > 0)
            {
                float hpPercent = weapon.HitPoints / (float)weapon.MaxHitPoints;
                score += hpPercent * 50f;

                // Skip badly damaged weapons
                if (hpPercent < 0.3f)
                    return 0f;
            }

            // Slight preference for higher tech
            score += (int)weapon.def.techLevel * 10f;

            // Very basic weapons get penalized
            if (weapon.def.defName == "WoodLog" || weapon.def.defName == "MeleeWeapon_Club")
                score *= 0.5f;

            return score;
        }

        public static int GetMaxSidearmsForPawn(Pawn pawn)
        {
            // Try to get actual Simple Sidearms settings
            try
            {
                // First try to find the SimpleSidearms_Settings type
                var settingsType = GenTypes.AllTypes.FirstOrDefault(t =>
                    t.Name == "SimpleSidearms_Settings" ||
                    (t.Name == "Settings" && t.Namespace?.Contains("SimpleSidearms") == true));

                if (settingsType != null)
                {
                    // Try to find the mod instance to get settings
                    var modType = GenTypes.AllTypes.FirstOrDefault(t =>
                        t.Name == "SimpleSidearmsMod" ||
                        (t.Name.Contains("SimpleSidearms") && t.IsSubclassOf(typeof(Mod))));

                    if (modType != null)
                    {
                        // Look for static settings field or property
                        var settingsField = modType.GetField("settings", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic) ??
                                          modType.GetField("Settings", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

                        if (settingsField != null)
                        {
                            var settings = settingsField.GetValue(null);
                            if (settings != null)
                            {
                                // Try various possible field names
                                var limitField = settingsType.GetField("SidearmLimit") ??
                                               settingsType.GetField("LimitModeSingle") ??
                                               settingsType.GetField("maxSidearms") ??
                                               settingsType.GetField("MaxSidearms");

                                if (limitField != null)
                                {
                                    var value = limitField.GetValue(settings);
                                    if (value is int limit)
                                    {
                                        if (AutoArmMod.settings?.debugLogging == true)
                                        {
                                            Log.Message($"[AutoArm] Simple Sidearms limit from settings: {limit}");
                                        }
                                        return limit;
                                    }
                                    else if (value is float fLimit)
                                    {
                                        if (AutoArmMod.settings?.debugLogging == true)
                                        {
                                            Log.Message($"[AutoArm] Simple Sidearms limit from settings: {(int)fLimit}");
                                        }
                                        return (int)fLimit;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    Log.Warning($"[AutoArm] Error reading Simple Sidearms settings: {e.Message}");
                }
            }

            // Default fallback
            return 3;
        }
        public static int GetCurrentSidearmCount(Pawn pawn)
        {
            if (!IsLoaded() || pawn == null)
                return 0;

            // Quick count from inventory
            if (pawn.inventory?.innerContainer != null)
                return pawn.inventory.innerContainer.Count(t => t.def.IsWeapon);

            return 0;
        }

        private static Job TryFindNewSidearm(Pawn pawn, List<ThingDef> currentSidearmDefs)
        {
            if (AutoArmMod.settings?.debugLogging == true)
            {
                Log.Message($"[AutoArm] TryFindNewSidearm for {pawn.Name}");
            }

            if (currentSidearmDefs == null)
                currentSidearmDefs = new List<ThingDef>();

            // Check for null map
            if (pawn?.Map == null)
            {
                Log.Warning($"[AutoArm] {pawn.Name} has null map");
                return null;
            }

            // Get weapons list with null check
            var weaponsList = pawn.Map.listerThings?.ThingsInGroup(ThingRequestGroup.Weapon);
            if (weaponsList == null)
            {
                Log.Warning($"[AutoArm] No weapon list for {pawn.Name}");
                return null;
            }

            // Get valid weapons
            var validWeapons = weaponsList
                .OfType<ThingWithComps>()
                .Where(w => w != null && w.def != null &&
                           IsValidSidearmCandidate(w, pawn) &&
                           !currentSidearmDefs.Contains(w.def))
                .OrderBy(w => w.Position.DistanceTo(pawn.Position))
                .Take(20)
                .Select(w => new { weapon = w, score = GetBasicWeaponScore(w) })
                .Where(x => x.score > 0)
                .OrderByDescending(x => x.score / (1f + x.weapon.Position.DistanceTo(pawn.Position) / 100f))
                .Select(x => x.weapon)
                .FirstOrDefault();

            if (validWeapons != null && equipSecondaryJobDef != null)
            {
                var job = JobMaker.MakeJob(equipSecondaryJobDef, validWeapons);
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    Log.Message($"[AutoArm] {pawn.Name} will add {validWeapons.Label} as sidearm");
                }
                return job;
            }

            if (AutoArmMod.settings?.debugLogging == true)
            {
                Log.Message($"[AutoArm] No suitable sidearm found for {pawn.Name}");
            }
            return null;
        }

        private static bool IsValidSidearmCandidate(ThingWithComps weapon, Pawn pawn)
        {
            if (weapon == null || weapon.def == null || weapon.Destroyed)
                return false;

            if (weapon.IsForbidden(pawn))
                return false;

            // Don't take weapons from inventory
            if (weapon.ParentHolder is Pawn_InventoryTracker || weapon.ParentHolder is Pawn_EquipmentTracker)
                return false;

            // Check outfit filter
            var filter = pawn.outfits?.CurrentApparelPolicy?.filter;
            if (filter != null && !filter.Allows(weapon.def))
                return false;

            // ADD THIS: Check Simple Sidearms restrictions
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
            if (!IsLoaded() || pawn == null || !pendingSidearmRegistrations.ContainsKey(pawn))
                return;

            var pending = pendingSidearmRegistrations[pawn];
            if (pending == null || pending.Count == 0)
                return;

            EnsureInitialized();
            if (!_initialized)
                return;

            // Get the sidearm memory component
            var comp = pawn.AllComps?.FirstOrDefault(c => c.GetType() == compSidearmMemoryType);
            if (comp == null)
                return;

            // Find InformOfAddedSidearm method
            var informMethod = compSidearmMemoryType.GetMethod("InformOfAddedSidearm");
            if (informMethod == null)
                return;

            // Check inventory for pending weapons and register them
            foreach (var weapon in pending.ToList())
            {
                if (pawn.inventory?.innerContainer?.Contains(weapon) == true)
                {
                    try
                    {
                        informMethod.Invoke(comp, new object[] { weapon });

                        if (AutoArmMod.settings?.debugLogging == true)
                        {
                            Log.Message($"[AutoArm] Manually registered {weapon.Label} as sidearm for {pawn.Name} (fallback mode)");
                        }

                        pending.Remove(weapon);
                    }
                    catch (Exception e)
                    {
                        if (AutoArmMod.settings?.debugLogging == true)
                        {
                            Log.Warning($"[AutoArm] Failed to register sidearm {weapon.Label}: {e.Message}");
                        }
                    }
                }
            }

            // Clean up if empty
            if (pending.Count == 0)
                pendingSidearmRegistrations.Remove(pawn);
        }

        public static void CleanupPendingRegistrations()
        {
            var toRemove = pendingSidearmRegistrations.Keys
                .Where(p => p.DestroyedOrNull() || p.Dead || !p.Spawned)
                .ToList();

            foreach (var pawn in toRemove)
            {
                pendingSidearmRegistrations.Remove(pawn);
            }
        }

        // Check if pawn has no sidearms for emergency pickup
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
                var comp = pawn.AllComps?.FirstOrDefault(c => c.GetType() == compSidearmMemoryType);
                if (comp == null)
                    return true;

                // Get current sidearms
                IEnumerable sidearmsList = null;

                if (rememberedWeaponsProperty != null)
                {
                    sidearmsList = rememberedWeaponsProperty.GetValue(comp) as IEnumerable;
                }
                else if (rememberedWeaponsField != null)
                {
                    sidearmsList = rememberedWeaponsField.GetValue(comp) as IEnumerable;
                }

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