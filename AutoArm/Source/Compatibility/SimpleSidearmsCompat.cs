using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Policy;
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

        // Properties/Fields
        private static PropertyInfo rememberedWeaponsProperty;
        private static FieldInfo thingField;
        private static FieldInfo stuffField;

        // JobDef
        private static JobDef equipSecondaryJobDef;

        // Track recent pickups - Changed from private to internal for cleanup access
        internal static Dictionary<Pawn, int> lastSidearmPickupTick = new Dictionary<Pawn, int>();
        internal static Dictionary<Pawn, Dictionary<string, int>> recentSidearmPickups = new Dictionary<Pawn, Dictionary<string, int>>();

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

        private static string GetWeaponCategory(ThingDef weaponDef)
        {
            // NULL CHECK
            if (weaponDef == null || weaponDef.defName == null)
                return "unknown";

            // Group similar weapons together
            string defNameLower = weaponDef.defName.ToLower();
            if (defNameLower.Contains("knife"))
                return "knife";
            if (defNameLower.Contains("sword"))
                return "sword";
            if (defNameLower.Contains("pistol") || defNameLower.Contains("revolver"))
                return "pistol";
            if (defNameLower.Contains("rifle"))
                return "rifle";

            // Default to the weapon's def name
            return weaponDef.defName;
        }

        public static Job TryGetSidearmUpgradeJob(Pawn pawn)
        {
            if (!IsLoaded() || pawn == null || !pawn.IsColonist)
                return null;

            // Check cooldown - don't pick up another sidearm for 10 seconds after last pickup
            if (lastSidearmPickupTick.TryGetValue(pawn, out int lastTick))
            {
                if (Find.TickManager.TicksGame - lastTick < 600) // 10 second cooldown
                {
                    if (AutoArmMod.settings?.debugLogging == true)
                    {
                        Log.Message($"[AutoArm] {pawn.Name} - Sidearm pickup on cooldown");
                    }
                    return null;
                }
            }

            // Clean up old entries from recent pickups - REMOVED, now handled by MemoryCleanupManager

            if (AutoArmMod.settings?.autoEquipSidearms != true)
                return null;

            EnsureInitialized();
            if (!_initialized || equipSecondaryJobDef == null)
            {
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    Log.Message($"[AutoArm] Sidearm check failed - not initialized or no job def");
                }
                return null;
            }

            if (AutoArmMod.settings?.debugLogging == true)
            {
                Log.Message($"[AutoArm] === Starting sidearm check for {pawn.Name} ===");
            }

            try
            {
                var comp = GetSidearmComp(pawn);
                if (comp == null)
                {
                    if (AutoArmMod.settings?.debugLogging == true)
                    {
                        Log.Message($"[AutoArm] {pawn.Name} has no sidearm memory component");
                    }
                    return null;
                }

                // Get current sidearms
                var currentSidearmDefs = GetCurrentSidearmDefs(pawn, comp);

                // Log current sidearms in detail
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    var inventoryWeapons = pawn.inventory?.innerContainer?.Where(t => t.def.IsWeapon).ToList() ?? new List<Thing>();
                    if (inventoryWeapons.Any())
                    {
                        Log.Message($"[AutoArm] {pawn.Name} current inventory weapons:");
                        foreach (var weapon in inventoryWeapons)
                        {
                            // Fix: Handle quality check outside of string interpolation
                            QualityCategory qc;
                            string qualityStr = weapon.TryGetQuality(out qc) ? qc.ToString() : "none";
                            Log.Message($"  - {weapon.Label} (quality: {qualityStr})");
                        }
                    }
                    else
                    {
                        Log.Message($"[AutoArm] {pawn.Name} has no weapons in inventory");
                    }
                    Log.Message($"[AutoArm] {pawn.Name} remembered sidearm types: {string.Join(", ", currentSidearmDefs.Select(d => d.label))}");
                }

                // Check if we have room for more sidearms
                int maxSidearms = GetMaxSidearmsForPawn(pawn);
                int currentCount = pawn.inventory?.innerContainer?.Count(t => t.def.IsWeapon) ?? 0;

                if (AutoArmMod.settings?.debugLogging == true)
                {
                    Log.Message($"[AutoArm] {pawn.Name} sidearm check: {currentCount}/{maxSidearms} slots used");
                }

                Job result = null;

                // Always try to upgrade existing sidearms first
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    Log.Message($"[AutoArm] {pawn.Name} checking for sidearm upgrades...");
                }
                result = TryUpgradeExistingSidearm(pawn, currentSidearmDefs);

                // If no upgrade found and we have room, try to find new sidearms
                if (result == null && currentCount < maxSidearms)
                {
                    if (AutoArmMod.settings?.debugLogging == true)
                    {
                        Log.Message($"[AutoArm] {pawn.Name} has room for more sidearms, looking for new ones...");
                    }
                    result = TryFindNewSidearm(pawn, currentSidearmDefs);
                }
                else if (result == null && AutoArmMod.settings?.debugLogging == true)
                {
                    Log.Message($"[AutoArm] {pawn.Name} sidearm slots full and no upgrades found");
                }

                if (AutoArmMod.settings?.debugLogging == true)
                {
                    Log.Message($"[AutoArm] {pawn.Name} sidearm check result: {(result != null ? $"Found job targeting {result.targetA.Thing?.Label}" : "No job found")}");
                }

                // Set cooldown if we're returning a job
                if (result != null)
                {
                    lastSidearmPickupTick[pawn] = Find.TickManager.TicksGame;
                }

                return result;
            }
            catch (Exception e)
            {
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    Log.Warning($"[AutoArm] Error finding sidearm upgrade: {e.Message}");
                    Log.Warning($"[AutoArm] Stack trace: {e.StackTrace}");
                }
                return null;
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

        // ... rest of the methods remain the same but with appropriate null checks added ...

        private static Job TryFindNewSidearm(Pawn pawn, HashSet<ThingDef> currentSidearmDefs)
        {
            // NULL CHECK
            if (pawn == null || pawn.Map == null || pawn.jobs == null)
                return null;

            if (AutoArmMod.settings?.debugLogging == true)
            {
                Log.Message($"[AutoArm] {pawn.Name} looking for new sidearm...");

                // Log current inventory weapons
                var currentInv = pawn.inventory?.innerContainer
                    .OfType<ThingWithComps>()
                    .Where(t => t.def.IsWeapon)
                    .ToList();

                if (currentInv?.Any() == true)
                {
                    Log.Message($"[AutoArm] Current inventory: {string.Join(", ", currentInv.Select(w => w.Label))}");
                }

                // Log cooldown status
                if (lastSidearmPickupTick.TryGetValue(pawn, out int lastTick))
                {
                    int ticksAgo = Find.TickManager.TicksGame - lastTick;
                    Log.Message($"[AutoArm] Last sidearm pickup was {ticksAgo} ticks ago");
                }
            }

            // Get current inventory weapons to prevent duplicates
            var currentInventoryWeapons = pawn.inventory?.innerContainer
                .OfType<ThingWithComps>()
                .Where(t => t.def?.IsWeapon == true)
                .Select(t => t.def)
                .ToHashSet() ?? new HashSet<ThingDef>();

            // CHECK FOR PENDING PICKUP JOBS
            if (pawn.jobs?.curJob?.def == equipSecondaryJobDef)
            {
                var pendingPickup = pawn.jobs.curJob.targetA.Thing as ThingWithComps;
                if (pendingPickup?.def != null)
                {
                    currentInventoryWeapons.Add(pendingPickup.def);

                    if (AutoArmMod.settings?.debugLogging == true)
                    {
                        Log.Message($"[AutoArm] {pawn.Name} is already picking up {pendingPickup.Label}, excluding {pendingPickup.def.defName} from search");
                    }
                }
            }

            // Also check queued jobs
            if (pawn.jobs?.jobQueue != null)
            {
                foreach (var queuedJob in pawn.jobs.jobQueue)
                {
                    if (queuedJob.job?.def == equipSecondaryJobDef)
                    {
                        var queuedPickup = queuedJob.job.targetA.Thing as ThingWithComps;
                        if (queuedPickup?.def != null)
                        {
                            currentInventoryWeapons.Add(queuedPickup.def);
                        }
                    }
                }
            }

            // Get weapons within reasonable distance using the cache
            var nearbyWeapons = ImprovedWeaponCacheManager.GetWeaponsNear(pawn.Map, pawn.Position, 40f)
                .Where(w => IsValidSidearmCandidate(w, pawn, currentSidearmDefs) &&
                           !currentInventoryWeapons.Contains(w.def))  // Prevent duplicates
                .OrderBy(w => w.Position.DistanceToSquared(pawn.Position))
                .Take(20)
                .ToList();

            if (AutoArmMod.settings?.debugLogging == true)
            {
                Log.Message($"[AutoArm] {pawn.Name} found {nearbyWeapons.Count} candidate weapons");
            }

            // Score and find best
            ThingWithComps bestWeapon = null;
            float bestScore = 0f;

            foreach (var weapon in nearbyWeapons)
            {
                float score = GetSidearmScore(weapon, pawn, currentSidearmDefs);

                if (AutoArmMod.settings?.debugLogging == true && nearbyWeapons.Count <= 5)
                {
                    Log.Message($"[AutoArm] {pawn.Name}: {weapon.Label} score: {score:F1}");
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    bestWeapon = weapon;
                }
            }

            if (bestWeapon != null)
            {
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    Log.Message($"[AutoArm] {pawn.Name} will pick up {bestWeapon.Label} as new sidearm (score: {bestScore:F1})");
                }

                // Track by category
                string category = GetWeaponCategory(bestWeapon.def);
                if (!recentSidearmPickups.ContainsKey(pawn))
                    recentSidearmPickups[pawn] = new Dictionary<string, int>();
                recentSidearmPickups[pawn][category] = Find.TickManager.TicksGame;

                lastSidearmPickupTick[pawn] = Find.TickManager.TicksGame;
                return JobMaker.MakeJob(equipSecondaryJobDef, bestWeapon);
            }

            if (AutoArmMod.settings?.debugLogging == true)
            {
                Log.Message($"[AutoArm] {pawn.Name} - no suitable sidearm found");
            }

            return null;
        }

        // ... rest of the methods remain the same structure but with null checks added ...
        // I'll show the pattern for one more method:

        private static bool IsValidSidearmCandidate(ThingWithComps weapon, Pawn pawn, HashSet<ThingDef> currentSidearmDefs)
        {
            // NULL CHECKS
            if (weapon == null || weapon.def == null || weapon.Destroyed)
                return false;

            if (pawn == null || pawn.Map == null)
                return false;

            if (weapon.IsForbidden(pawn))
                return false;

            // Check parent holder
            if (weapon.ParentHolder is Pawn_InventoryTracker || weapon.ParentHolder is Pawn_EquipmentTracker)
                return false;

            // Don't pick up recently picked up categories
            string weaponCategory = GetWeaponCategory(weapon.def);
            if (recentSidearmPickups.TryGetValue(pawn, out var recent) &&
                recent.TryGetValue(weaponCategory, out int pickupTick) &&
                Find.TickManager.TicksGame - pickupTick < 300)
            {
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    Log.Message($"[AutoArm] {pawn.Name}: {weapon.Label} skipped - recently picked up a {weaponCategory}");
                }
                return false;
            }

            // Don't pick up duplicates
            if (currentSidearmDefs != null && currentSidearmDefs.Contains(weapon.def))
            {
                // Check if we already have this weapon type in inventory
                var existingWeaponOfType = pawn.inventory?.innerContainer
                    .OfType<ThingWithComps>()
                    .FirstOrDefault(t => t.def == weapon.def);

                if (existingWeaponOfType != null)
                {
                    // Compare qualities
                    QualityCategory existingQuality;
                    QualityCategory newQuality;

                    bool hasExistingQuality = existingWeaponOfType.TryGetQuality(out existingQuality);
                    bool hasNewQuality = weapon.TryGetQuality(out newQuality);

                    // If both have quality, only skip if new isn't better
                    if (hasExistingQuality && hasNewQuality)
                    {
                        if (newQuality <= existingQuality)
                            return false; // Not an upgrade
                    }
                    else
                    {
                        return false; // Can't compare quality
                    }
                }
                else
                {
                    return false; // We track this type but don't have it
                }
            }

            // Check if Simple Sidearms allows this
            string reason;
            if (!CanPickupWeaponAsSidearm(weapon, pawn, out reason))
            {
                if (AutoArmMod.settings?.debugLogging == true && !string.IsNullOrEmpty(reason))
                {
                    Log.Message($"[AutoArm] {pawn.Name}: {weapon.Label} rejected by SS - {reason}");
                }
                return false;
            }

            // Check outfit filter
            var filter = pawn.outfits?.CurrentApparelPolicy?.filter;
            if (filter != null && !filter.Allows(weapon.def))
                return false;

            // Check reachability
            if (!pawn.CanReserveAndReach(weapon, PathEndMode.ClosestTouch, Danger.Deadly))
                return false;

            return true;
        }

        // ... implement null checks for all remaining methods following same pattern ...

        private static Job TryUpgradeExistingSidearm(Pawn pawn, HashSet<ThingDef> currentSidearmDefs)
        {
            if (AutoArmMod.settings?.debugLogging == true)
            {
                Log.Message($"[AutoArm] {pawn.Name} - Trying to upgrade existing sidearms");
            }

            // Check if sidearm upgrades are allowed
            if (!AutoArmMod.settings?.allowSidearmUpgrades ?? false)
            {
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    Log.Message($"[AutoArm] {pawn.Name} - sidearm upgrades disabled in settings");
                }
                return null;
            }

            // Find worst current sidearm
            var worstSidearm = pawn.inventory?.innerContainer
                .OfType<ThingWithComps>()
                .Where(t => t.def.IsWeapon && !ForcedWeaponTracker.IsForcedSidearm(pawn, t.def))
                .OrderBy(w => GetWeaponScore(w))
                .FirstOrDefault();

            if (worstSidearm == null)
            {
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    Log.Message($"[AutoArm] {pawn.Name} - no upgradeable sidearms");
                }
                return null;
            }

            float worstScore = GetWeaponScore(worstSidearm);

            if (AutoArmMod.settings?.debugLogging == true)
            {
                Log.Message($"[AutoArm] {pawn.Name} worst sidearm: {worstSidearm.Label} (score: {worstScore:F1})");
            }

            // Look for better weapons on the ground
            var betterWeapon = ImprovedWeaponCacheManager.GetWeaponsNear(pawn.Map, pawn.Position, 40f)
                .Where(w => IsValidSidearmCandidate(w, pawn, currentSidearmDefs) &&
                           GetWeaponScore(w) > worstScore * 1.15f) // 15% improvement threshold
                .OrderByDescending(w => GetWeaponScore(w) / (1f + w.Position.DistanceTo(pawn.Position) / 100f))
                .FirstOrDefault();

            // ADD THIS CHECK - Don't pick up if we're already picking up this weapon type
            if (betterWeapon != null && pawn.jobs?.curJob?.def == equipSecondaryJobDef)
            {
                var pendingPickup = pawn.jobs.curJob.targetA.Thing as ThingWithComps;
                if (pendingPickup?.def == betterWeapon.def)
                {
                    if (AutoArmMod.settings?.debugLogging == true)
                    {
                        Log.Message($"[AutoArm] {pawn.Name} is already picking up a {betterWeapon.def.defName}, skipping duplicate");
                    }
                    return null;
                }
            }

            if (betterWeapon != null)
            {
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    Log.Message($"[AutoArm] {pawn.Name} will replace {worstSidearm.Label} with {betterWeapon.Label}");
                }

                // Check if it's the same weapon type (quality upgrade)
                if (worstSidearm.def == betterWeapon.def)
                {
                    if (AutoArmMod.settings?.debugLogging == true)
                    {
                        Log.Message($"[AutoArm] {pawn.Name} - Same weapon type upgrade, will drop old {worstSidearm.Label} first");
                    }

                    // Track by category
                    string category = GetWeaponCategory(betterWeapon.def);
                    if (!recentSidearmPickups.ContainsKey(pawn))
                        recentSidearmPickups[pawn] = new Dictionary<string, int>();
                    recentSidearmPickups[pawn][category] = Find.TickManager.TicksGame;

                    // Drop the old weapon first
                    Thing droppedThing;
                    if (pawn.inventory.innerContainer.TryDrop(worstSidearm, pawn.Position, pawn.Map, ThingPlaceMode.Near, out droppedThing))
                    {
                        // SET COOLDOWN HERE!
                        lastSidearmPickupTick[pawn] = Find.TickManager.TicksGame;

                        // Now pick up the better weapon
                        return JobMaker.MakeJob(equipSecondaryJobDef, betterWeapon);
                    }
                    else
                    {
                        if (AutoArmMod.settings?.debugLogging == true)
                        {
                            Log.Warning($"[AutoArm] {pawn.Name} - Failed to drop {worstSidearm.Label}");
                        }
                        return null;
                    }
                }
                else
                {
                    // Track by category for different weapon types too
                    string category = GetWeaponCategory(betterWeapon.def);
                    if (!recentSidearmPickups.ContainsKey(pawn))
                        recentSidearmPickups[pawn] = new Dictionary<string, int>();
                    recentSidearmPickups[pawn][category] = Find.TickManager.TicksGame;

                    // Different weapon type - use existing logic
                    if (equipSpecificWeaponFromInventoryMethod != null)
                    {
                        try
                        {
                            // Equip the worst sidearm from inventory to primary
                            bool dropCurrent = false;
                            bool intentionalDrop = false;
                            equipSpecificWeaponFromInventoryMethod.Invoke(null,
                                new object[] { pawn, worstSidearm, dropCurrent, intentionalDrop });

                            // SET COOLDOWN HERE!
                            lastSidearmPickupTick[pawn] = Find.TickManager.TicksGame;

                            // Then pick up the better weapon as a sidearm
                            return JobMaker.MakeJob(equipSecondaryJobDef, betterWeapon);
                        }
                        catch (Exception e)
                        {
                            if (AutoArmMod.settings?.debugLogging == true)
                            {
                                Log.Warning($"[AutoArm] Error swapping sidearms: {e.Message}");
                            }
                        }
                    }

                    // SET COOLDOWN FOR FALLBACK TOO!
                    lastSidearmPickupTick[pawn] = Find.TickManager.TicksGame;

                    // Fallback: just pick up the better weapon
                    return JobMaker.MakeJob(equipSecondaryJobDef, betterWeapon);
                }
            }

            return null;
        }

        private static float GetSidearmScore(ThingWithComps weapon, Pawn pawn, HashSet<ThingDef> currentSidearmDefs)
        {
            float score = GetWeaponScore(weapon);

            // Bonus for weapon types we don't have
            bool hasRangedSidearm = currentSidearmDefs.Any(def => def.IsRangedWeapon);
            bool hasMeleeSidearm = currentSidearmDefs.Any(def => def.IsMeleeWeapon);

            if (weapon.def.IsRangedWeapon && !hasRangedSidearm)
                score *= 1.5f; // 50% bonus for diversification
            else if (weapon.def.IsMeleeWeapon && !hasMeleeSidearm)
                score *= 1.5f;

            // Skill-based adjustments
            float shootingSkill = pawn.skills?.GetSkill(SkillDefOf.Shooting)?.Level ?? 0f;
            float meleeSkill = pawn.skills?.GetSkill(SkillDefOf.Melee)?.Level ?? 0f;

            if (weapon.def.IsRangedWeapon && shootingSkill > 5)
                score *= 1f + (shootingSkill / 20f);
            else if (weapon.def.IsMeleeWeapon && meleeSkill > 5)
                score *= 1f + (meleeSkill / 20f);

            return score;
        }

        private static float GetWeaponScore(ThingWithComps weapon)
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
                score *= hpPercent;

                // Don't pick up badly damaged weapons
                if (hpPercent < 0.3f)
                    return 0f;
            }

            // Tech level
            score += (int)weapon.def.techLevel * 10f;

            // Penalize basic/primitive weapons
            if (weapon.def.defName == "WoodLog" || weapon.def.defName == "MeleeWeapon_Club")
                score *= 0.5f;

            // Damage potential (simplified)
            if (weapon.def.IsRangedWeapon)
            {
                float dps = JobGiverHelpers.GetRangedWeaponDPS(weapon.def, weapon);
                score += dps * 5f;
            }
            else if (weapon.def.IsMeleeWeapon)
            {
                float meleeDPS = weapon.GetStatValue(StatDefOf.MeleeWeapon_CooldownMultiplier);
                float meleeDamage = weapon.GetStatValue(StatDefOf.MeleeWeapon_DamageMultiplier);
                score += (meleeDPS + meleeDamage) * 10f;
            }

            return score;
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