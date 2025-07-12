using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Policy;
using Unity.Jobs;
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
        private static Type weaponAssingmentType;
        private static Type gettersFiltersType;
        private static Type statCalculatorType;
        private static Type thingDefStuffDefPairType; // Simple Sidearms' custom type

        // Methods
        private static MethodInfo equipSpecificWeaponFromInventoryMethod;
        private static MethodInfo canPickupSidearmInstanceMethod;
        private static MethodInfo findBestRangedWeaponMethod;
        private static MethodInfo findBestMeleeWeaponMethod;
        private static MethodInfo informOfDroppedSidearmMethod;

        // Properties/Fields
        private static PropertyInfo rememberedWeaponsProperty;
        private static FieldInfo thingField;
        private static FieldInfo stuffField;

        // JobDef
        private static JobDef equipSecondaryJobDef;

        // Track recent pickups - Changed from private to internal for cleanup access
        internal static Dictionary<Pawn, int> lastSidearmPickupTick = new Dictionary<Pawn, int>();

        // Track recently dropped weapons to prevent immediate re-pickup
        private static Dictionary<Thing, int> recentlyDroppedWeapons = new Dictionary<Thing, int>();
        private const int DROPPED_WEAPON_IGNORE_TICKS = 120; // 2 seconds

        // Delegate caching for better performance
        private static class ReflectionCache
        {
            public static MethodInfo GetSidearmCompMethod;
            public static PropertyInfo RememberedWeaponsProperty;

            public static void Initialize()
            {
                try
                {
                    // Just cache the method info, don't try to create delegates
                    // The GetMemoryCompForPawn method might have additional parameters
                    GetSidearmCompMethod = compSidearmMemoryType?.GetMethod("GetMemoryCompForPawn",
                        BindingFlags.Public | BindingFlags.Static);

                    // Cache property info
                    RememberedWeaponsProperty = rememberedWeaponsProperty;

                    if (AutoArmMod.settings?.debugLogging == true)
                    {
                        Log.Message($"[AutoArm] Cached Simple Sidearms methods: GetMemoryComp={GetSidearmCompMethod != null}, RememberedWeapons={RememberedWeaponsProperty != null}");
                    }
                }
                catch (Exception e)
                {
                    if (AutoArmMod.settings?.debugLogging == true)
                    {
                        Log.Warning($"[AutoArm] Failed to cache Simple Sidearms methods: {e.Message}");
                    }
                }
            }
        }

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
                // Find types
                var ssNamespace = "PeteTimesSix.SimpleSidearms";
                compSidearmMemoryType = GenTypes.AllTypes.FirstOrDefault(t =>
                    t.FullName == "SimpleSidearms.rimworld.CompSidearmMemory");
                weaponAssingmentType = GenTypes.AllTypes.FirstOrDefault(t =>
                    t.FullName == $"{ssNamespace}.Utilities.WeaponAssingment");
                gettersFiltersType = GenTypes.AllTypes.FirstOrDefault(t =>
                    t.FullName == $"{ssNamespace}.Utilities.GettersFilters");
                statCalculatorType = GenTypes.AllTypes.FirstOrDefault(t =>
                    t.FullName == $"{ssNamespace}.Utilities.StatCalculator");

                // Find ThingDefStuffDefPair type
                thingDefStuffDefPairType = GenTypes.AllTypes.FirstOrDefault(t =>
                    t.FullName == "SimpleSidearms.rimworld.ThingDefStuffDefPair");

                if (thingDefStuffDefPairType != null)
                {
                    thingField = thingDefStuffDefPairType.GetField("thing");
                    stuffField = thingDefStuffDefPairType.GetField("stuff");
                }

                if (compSidearmMemoryType != null)
                {
                    rememberedWeaponsProperty = compSidearmMemoryType.GetProperty("RememberedWeapons");

                    // Find InformOfDroppedSidearm method
                    informOfDroppedSidearmMethod = compSidearmMemoryType.GetMethod(
                        "InformOfDroppedSidearm",
                        new Type[] { typeof(ThingWithComps), typeof(bool) });
                }

                if (weaponAssingmentType != null)
                {
                    equipSpecificWeaponFromInventoryMethod = weaponAssingmentType.GetMethod(
                        "equipSpecificWeaponFromInventory",
                        new Type[] { typeof(Pawn), typeof(ThingWithComps), typeof(bool), typeof(bool) });
                }

                if (statCalculatorType != null)
                {
                    canPickupSidearmInstanceMethod = statCalculatorType.GetMethod(
                        "CanPickupSidearmInstance",
                        new Type[] { typeof(ThingWithComps), typeof(Pawn), typeof(string).MakeByRefType() });
                }

                if (gettersFiltersType != null)
                {
                    findBestRangedWeaponMethod = gettersFiltersType.GetMethod("findBestRangedWeapon");
                    findBestMeleeWeaponMethod = gettersFiltersType.GetMethod("findBestMeleeWeapon");
                }

                // Find JobDef
                equipSecondaryJobDef = DefDatabase<JobDef>.GetNamedSilentFail("EquipSecondary");

                // Initialize reflection cache
                ReflectionCache.Initialize();

                _initialized = true;

                if (AutoArmMod.settings?.debugLogging == true)
                {
                    Log.Message($"[AutoArm] Simple Sidearms compatibility initialized successfully");
                    Log.Message($"[AutoArm] - CompSidearmMemory: {compSidearmMemoryType != null}");
                    Log.Message($"[AutoArm] - EquipSecondary job: {equipSecondaryJobDef != null}");
                    Log.Message($"[AutoArm] - InformOfDropped method: {informOfDroppedSidearmMethod != null}");
                    Log.Message($"[AutoArm] - Cached methods: {ReflectionCache.GetSidearmCompMethod != null}");
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

        // Mark a weapon as recently dropped to prevent immediate re-pickup
        public static void MarkWeaponAsRecentlyDropped(Thing weapon)
        {
            if (weapon != null)
            {
                recentlyDroppedWeapons[weapon] = Find.TickManager.TicksGame;

                if (AutoArmMod.settings?.debugLogging == true)
                {
                    Log.Message($"[AutoArm] Marked {weapon.Label} as recently dropped - will ignore for {DROPPED_WEAPON_IGNORE_TICKS} ticks");
                }
            }
        }

        // Check if a weapon was recently dropped
        private static bool IsRecentlyDropped(Thing weapon)
        {
            if (weapon == null)
                return false;

            // Clean old entries
            var toRemove = recentlyDroppedWeapons
                .Where(kvp => Find.TickManager.TicksGame - kvp.Value > DROPPED_WEAPON_IGNORE_TICKS)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in toRemove)
            {
                recentlyDroppedWeapons.Remove(key);
            }

            return recentlyDroppedWeapons.ContainsKey(weapon);
        }

        // Helper to inform Simple Sidearms that we dropped a weapon
        private static void InformSimpleSidearmsOfDrop(Pawn pawn, ThingWithComps weapon)
        {
            if (weapon == null || !IsLoaded())
                return;

            // Mark as recently dropped FIRST
            MarkWeaponAsRecentlyDropped(weapon);

            try
            {
                var comp = GetSidearmComp(pawn);
                if (comp != null && informOfDroppedSidearmMethod != null)
                {
                    informOfDroppedSidearmMethod.Invoke(comp, new object[] { weapon, true }); // true = intentional drop

                    if (AutoArmMod.settings?.debugLogging == true)
                    {
                        Log.Message($"[AutoArm] Informed SS that {pawn.Name} dropped {weapon.Label}");
                    }
                }
            }
            catch (Exception e)
            {
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    Log.Warning($"[AutoArm] Failed to inform SS of dropped weapon: {e.Message}");
                }
            }
        }

        public static bool CanPickupWeaponAsSidearm(ThingWithComps weapon, Pawn pawn, out string reason)
        {
            reason = "";
            if (!IsLoaded() || weapon == null || pawn == null)
                return true;

            EnsureInitialized();

            try
            {
                if (canPickupSidearmInstanceMethod != null)
                {
                    object[] parameters = new object[] { weapon, pawn, null };
                    bool result = (bool)canPickupSidearmInstanceMethod.Invoke(null, parameters);
                    reason = (string)parameters[2] ?? "";
                    return result;
                }
            }
            catch (Exception e)
            {
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    Log.Warning($"[AutoArm] Error checking sidearm compatibility: {e.Message}");
                }
            }

            return true;
        }

        public static Job TryGetSidearmUpgradeJob(Pawn pawn)
        {
            if (!IsLoaded() || pawn == null || !pawn.IsColonist)
                return null;

            // Check cooldown
            if (lastSidearmPickupTick.TryGetValue(pawn, out int lastTick))
            {
                if (Find.TickManager.TicksGame - lastTick < 600) // 10 second cooldown
                    return null;
            }

            if (AutoArmMod.settings?.autoEquipSidearms != true)
                return null;

            EnsureInitialized();
            if (!_initialized || equipSecondaryJobDef == null)
                return null;

            try
            {
                // Properly count ALL weapons
                var weaponCounts = new Dictionary<ThingDef, int>();
                var worstQualityWeapons = new Dictionary<ThingDef, ThingWithComps>();

                // Count equipped weapon
                if (pawn.equipment?.Primary != null)
                {
                    var def = pawn.equipment.Primary.def;
                    weaponCounts[def] = weaponCounts.ContainsKey(def) ? weaponCounts[def] + 1 : 1;
                    worstQualityWeapons[def] = pawn.equipment.Primary;
                }

                // Count ALL inventory weapons
                foreach (var item in pawn.inventory?.innerContainer ?? Enumerable.Empty<Thing>())
                {
                    if (item is ThingWithComps weapon && weapon.def.IsWeapon)
                    {
                        var def = weapon.def;
                        weaponCounts[def] = weaponCounts.ContainsKey(def) ? weaponCounts[def] + 1 : 1;

                        // Track worst quality for upgrades
                        if (!worstQualityWeapons.ContainsKey(def))
                        {
                            worstQualityWeapons[def] = weapon;
                        }
                        else
                        {
                            QualityCategory currentQuality, thisQuality;
                            bool hasCurrent = worstQualityWeapons[def].TryGetQuality(out currentQuality);
                            bool hasThis = weapon.TryGetQuality(out thisQuality);

                            if (hasCurrent && hasThis && thisQuality < currentQuality)
                            {
                                worstQualityWeapons[def] = weapon;
                            }
                        }
                    }
                }

                if (AutoArmMod.settings?.debugLogging == true)
                {
                    Log.Message($"[AutoArm] {pawn.Name} weapon counts:");
                    foreach (var kvp in weaponCounts)
                    {
                        Log.Message($"  - {kvp.Key.label}: {kvp.Value}");
                    }
                }

                // Find nearby weapons
                var filter = pawn.outfits?.CurrentApparelPolicy?.filter;
                var nearbyWeapons = ImprovedWeaponCacheManager.GetWeaponsNear(pawn.Map, pawn.Position, 40f)
                    .Where(w => w != null &&
                               !w.IsForbidden(pawn) &&
                               !IsRecentlyDropped(w) && // CHECK FOR RECENTLY DROPPED
                               (filter == null || filter.Allows(w.def)) &&
                               pawn.CanReserveAndReach(w, PathEndMode.ClosestTouch, Danger.Deadly))
                    .OrderBy(w => w.Position.DistanceToSquared(pawn.Position))
                    .Take(20);

                foreach (var weapon in nearbyWeapons)
                {
                    int currentCount = weaponCounts.ContainsKey(weapon.def) ? weaponCounts[weapon.def] : 0;

                    // Skip if we already have ANY of this weapon type (no duplicates!)
                    if (currentCount > 0)
                    {
                        // Only consider quality upgrades if we have exactly 1
                        if (currentCount == 1 && AutoArmMod.settings?.allowSidearmUpgrades == true)
                        {
                            var existingWeapon = worstQualityWeapons[weapon.def];

                            // CHECK IF THIS SIDEARM IS FORCED - NEW!
                            if (ForcedWeaponTracker.IsForcedSidearm(pawn, existingWeapon.def))
                            {
                                if (AutoArmMod.settings?.debugLogging == true)
                                {
                                    Log.Message($"[AutoArm] {pawn.Name} skipping upgrade of forced sidearm {existingWeapon.Label}");
                                }
                                continue; // Skip this upgrade - sidearm is forced
                            }

                            QualityCategory existingQuality, newQuality;
                            bool hasExistingQ = existingWeapon.TryGetQuality(out existingQuality);
                            bool hasNewQ = weapon.TryGetQuality(out newQuality);

                            if (hasExistingQ && hasNewQ && newQuality > existingQuality)
                            {
                                if (AutoArmMod.settings?.debugLogging == true)
                                {
                                    Log.Message($"[AutoArm] {pawn.Name} upgrading {weapon.def.label}: {existingQuality} -> {newQuality}");
                                }

                                // Handle the upgrade based on where the old weapon is
                                if (pawn.equipment?.Primary == existingWeapon)
                                {
                                    // CHECK IF PRIMARY IS FORCED - NEW!
                                    if (ForcedWeaponTracker.IsForced(pawn, existingWeapon))
                                    {
                                        if (AutoArmMod.settings?.debugLogging == true)
                                        {
                                            Log.Message($"[AutoArm] {pawn.Name} skipping upgrade of forced primary weapon {existingWeapon.Label}");
                                        }
                                        continue; // Skip - primary is forced
                                    }

                                    // Drop the equipped weapon first
                                    ThingWithComps droppedWeapon;
                                    if (pawn.equipment.TryDropEquipment(existingWeapon, out droppedWeapon, pawn.Position))
                                    {
                                        InformSimpleSidearmsOfDrop(pawn, droppedWeapon);
                                        lastSidearmPickupTick[pawn] = Find.TickManager.TicksGame;
                                        return JobMaker.MakeJob(equipSecondaryJobDef, weapon);
                                    }
                                }
                                else
                                {
                                    // Old weapon is in inventory - drop it and pick up new one
                                    Thing dropped;
                                    if (pawn.inventory.innerContainer.TryDrop(existingWeapon, pawn.Position, pawn.Map, ThingPlaceMode.Near, out dropped))
                                    {
                                        InformSimpleSidearmsOfDrop(pawn, existingWeapon);
                                        lastSidearmPickupTick[pawn] = Find.TickManager.TicksGame;
                                        return JobMaker.MakeJob(equipSecondaryJobDef, weapon);
                                    }
                                }
                            }
                        }

                        // Skip all duplicates (even if not an upgrade)
                        continue;
                    }

                    // Only pick up NEW weapon types after checking with SS
                    string reason;
                    if (!CanPickupWeaponAsSidearm(weapon, pawn, out reason))
                    {
                        if (AutoArmMod.settings?.debugLogging == true && !string.IsNullOrEmpty(reason))
                        {
                            Log.Message($"[AutoArm] SS rejected {weapon.Label} for {pawn.Name}: {reason}");
                        }
                        continue;
                    }

                    // New weapon type approved by SS - pick it up
                    if (AutoArmMod.settings?.debugLogging == true)
                    {
                        Log.Message($"[AutoArm] {pawn.Name} picking up new weapon type: {weapon.Label}");
                    }

                    lastSidearmPickupTick[pawn] = Find.TickManager.TicksGame;
                    return JobMaker.MakeJob(equipSecondaryJobDef, weapon);
                }

                if (AutoArmMod.settings?.debugLogging == true)
                {
                    Log.Message($"[AutoArm] {pawn.Name} - no sidearm pickups needed");
                }

                return null;
            }
            catch (Exception e)
            {
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    Log.Warning($"[AutoArm] Error in sidearm check: {e.Message}");
                }
                return null;
            }
        }

        // Helper method to clean up old tracking data
        public static void CleanupOldTrackingData()
        {
            // Clean up cooldown tracking for pawns that no longer exist
            var deadPawns = lastSidearmPickupTick.Keys.Where(p => p.Destroyed || p.Dead).ToList();
            foreach (var pawn in deadPawns)
            {
                lastSidearmPickupTick.Remove(pawn);
            }

            // Clean up old dropped weapon entries
            var oldDropped = recentlyDroppedWeapons
                .Where(kvp => Find.TickManager.TicksGame - kvp.Value > DROPPED_WEAPON_IGNORE_TICKS)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var weapon in oldDropped)
            {
                recentlyDroppedWeapons.Remove(weapon);
            }
        }

        private static HashSet<ThingDef> GetCurrentSidearmDefs(Pawn pawn, object comp)
        {
            var sidearmDefs = new HashSet<ThingDef>();

            // NULL CHECK
            if (pawn == null || comp == null)
                return sidearmDefs;

            // Get remembered weapons from Simple Sidearms
            try
            {
                System.Collections.IEnumerable rememberedList = null;

                // Use cached property
                if (ReflectionCache.RememberedWeaponsProperty != null)
                {
                    rememberedList = ReflectionCache.RememberedWeaponsProperty.GetValue(comp) as System.Collections.IEnumerable;
                }
                else if (rememberedWeaponsProperty != null)
                {
                    rememberedList = rememberedWeaponsProperty.GetValue(comp) as System.Collections.IEnumerable;
                }

                if (rememberedList != null)
                {
                    foreach (var item in rememberedList)
                    {
                        try
                        {
                            if (item != null && thingField != null)
                            {
                                var thingDef = thingField.GetValue(item) as ThingDef;
                                if (thingDef != null)
                                {
                                    sidearmDefs.Add(thingDef);
                                }
                            }
                        }
                        catch { }
                    }
                }
            }
            catch (Exception e)
            {
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    Log.Warning($"[AutoArm] Error getting current sidearms: {e.Message}");
                }
            }

            return sidearmDefs;
        }

        private static object GetSidearmComp(Pawn pawn)
        {
            // Try cached method first
            if (ReflectionCache.GetSidearmCompMethod != null)
            {
                try
                {
                    // The method might take additional parameters (like bool fillExistingIfCreating)
                    var parameters = ReflectionCache.GetSidearmCompMethod.GetParameters();
                    if (parameters.Length == 1)
                    {
                        return ReflectionCache.GetSidearmCompMethod.Invoke(null, new object[] { pawn });
                    }
                    else if (parameters.Length == 2)
                    {
                        // Second parameter is likely fillExistingIfCreating
                        return ReflectionCache.GetSidearmCompMethod.Invoke(null, new object[] { pawn, true });
                    }
                }
                catch { }
            }

            // Fallback to searching comps
            if (compSidearmMemoryType == null)
                return null;

            return pawn.AllComps?.FirstOrDefault(c => c.GetType() == compSidearmMemoryType);
        }

        public static int GetMaxSidearmsForPawn(Pawn pawn)
        {
            // Try to get from Simple Sidearms settings
            try
            {
                var settingsType = GenTypes.AllTypes.FirstOrDefault(t =>
                    t.Name == "SimpleSidearms_Settings" ||
                    (t.Name == "Settings" && t.Namespace?.Contains("SimpleSidearms") == true));

                if (settingsType != null)
                {
                    // Try to find the static settings instance
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
                                // Look for sidearm limit field - try various possible names
                                string[] possibleFieldNames = {
                                    "LimitModeAmount_Slots",
                                    "SidearmLimit",
                                    "maxSidearms",
                                    "MaxSidearms",
                                    "LimitModeAmountTotal_Slots"
                                };

                                foreach (var fieldName in possibleFieldNames)
                                {
                                    var limitField = settingsType.GetField(fieldName,
                                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

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
            }
            catch (Exception e)
            {
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    Log.Warning($"[AutoArm] Error getting max sidearms: {e.Message}");
                }
            }

            // Default fallback
            return 3;
        }

        public static int GetCurrentSidearmCount(Pawn pawn)
        {
            if (!IsLoaded() || pawn?.inventory?.innerContainer == null)
                return 0;

            return pawn.inventory.innerContainer.Count(t => t.def.IsWeapon);
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

                var sidearmDefs = GetCurrentSidearmDefs(pawn, comp);
                return sidearmDefs.Count == 0;
            }
            catch
            {
                // On error, fall back to simple inventory check
                return !pawn.inventory.innerContainer.Any(t => t.def.IsWeapon);
            }
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

        // Debug helper to list all Simple Sidearms job defs
        public static void DebugListSidearmJobs()
        {
            Log.Message("\n[AutoArm] === Simple Sidearms Job Defs ===");

            var sidearmJobs = DefDatabase<JobDef>.AllDefs
                .Where(j => j.defName.ToLower().Contains("sidearm") ||
                          j.defName.ToLower().Contains("secondary") ||
                          j.modContentPack?.Name?.Contains("Simple Sidearms") == true)
                .ToList();

            Log.Message($"[AutoArm] Found {sidearmJobs.Count} possible sidearm job defs:");
            foreach (var job in sidearmJobs)
            {
                Log.Message($"  - {job.defName} (from {job.modContentPack?.Name ?? "unknown"})");
            }

            Log.Message("[AutoArm] === End Job Defs ===\n");
        }
    }
}