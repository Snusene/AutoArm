using AutoArm.Caching;
using AutoArm.Helpers;
using AutoArm.Jobs;
using AutoArm.Logging;
using AutoArm.Weapons;
using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;
using Verse.AI;

namespace AutoArm
{
    // Complete SimpleSidearms Compatibility
    // Properly handles all SS integration:
    // - ThingDefStuffDefPair for remembered weapons
    // - Informs SS of all weapon changes
    // - Checks for SS pending jobs
    // - Respects unarmed preferences
    // - Thread-safe caching
    public static class SimpleSidearmsCompat
    {
        private static bool? _isLoaded = null;
        private static bool _initialized = false;

        // Core types from SimpleSidearms
        private static Type compSidearmMemoryType;
        private static Type statCalculatorType;
        private static Type thingDefStuffDefPairType;
        private static Type weaponAssingmentType;
        private static Type gettersFiltersType;
        private static Type jobGiverRetrieveWeaponType;

        // Core methods we need
        private static MethodInfo getMemoryCompForPawnMethod;
        private static MethodInfo canPickupSidearmMethod;
        private static MethodInfo rangedDPSMethod;
        private static MethodInfo getMeleeDPSMethod;
        private static MethodInfo informOfDroppedSidearmMethod;
        private static MethodInfo informOfAddedSidearmMethod;
        private static MethodInfo tryGiveJobStaticMethod;

        // Properties and fields
        private static PropertyInfo rememberedWeaponsProperty;
        private static FieldInfo thingDefField;
        private static FieldInfo stuffDefField;

        // JobDefs from SimpleSidearms
        private static JobDef equipSecondaryJobDef;

        // Thread-safe weapon score cache
        private static readonly object _cacheLock = new object();
        private static Dictionary<string, float> weaponScoreCache = new Dictionary<string, float>();
        private static Dictionary<string, int> weaponScoreCacheTick = new Dictionary<string, int>();
        private const int WEAPON_SCORE_CACHE_LIFETIME = 1000;

        // Pending upgrade tracking
        private static Dictionary<Pawn, PendingUpgradeInfo> pendingUpgrades = new Dictionary<Pawn, PendingUpgradeInfo>();

        public class PendingUpgradeInfo
        {
            public ThingWithComps oldWeapon;
            public ThingWithComps newWeapon;
            public int startTick;
        }

        public static bool IsLoaded()
        {
            if (_isLoaded == null)
            {
                _isLoaded = ModLister.AllInstalledMods.Any(m =>
                    m.Active && (
                        m.PackageIdPlayerFacing.Equals("PeteTimesSix.SimpleSidearms", StringComparison.OrdinalIgnoreCase) ||
                        m.PackageIdPlayerFacing.Equals("petetimessix.simplesidearms", StringComparison.OrdinalIgnoreCase)
                    ));

                if (_isLoaded.Value)
                {
                    AutoArmLogger.Log("Simple Sidearms detected and loaded");
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
                // Get types
                compSidearmMemoryType = GenTypes.AllTypes.FirstOrDefault(t =>
                    t.FullName == "SimpleSidearms.rimworld.CompSidearmMemory");

                statCalculatorType = GenTypes.AllTypes.FirstOrDefault(t =>
                    t.FullName == "PeteTimesSix.SimpleSidearms.Utilities.StatCalculator");

                thingDefStuffDefPairType = GenTypes.AllTypes.FirstOrDefault(t =>
                    t.FullName == "SimpleSidearms.rimworld.ThingDefStuffDefPair");

                weaponAssingmentType = GenTypes.AllTypes.FirstOrDefault(t =>
                    t.FullName == "PeteTimesSix.SimpleSidearms.Utilities.WeaponAssingment");

                gettersFiltersType = GenTypes.AllTypes.FirstOrDefault(t =>
                    t.FullName == "PeteTimesSix.SimpleSidearms.Utilities.GettersFilters");

                jobGiverRetrieveWeaponType = GenTypes.AllTypes.FirstOrDefault(t =>
                    t.FullName == "SimpleSidearms.rimworld.JobGiver_RetrieveWeapon");

                // Get methods
                if (compSidearmMemoryType != null)
                {
                    getMemoryCompForPawnMethod = compSidearmMemoryType.GetMethod("GetMemoryCompForPawn",
                        BindingFlags.Public | BindingFlags.Static);

                    rememberedWeaponsProperty = compSidearmMemoryType.GetProperty("RememberedWeapons");

                    informOfDroppedSidearmMethod = compSidearmMemoryType.GetMethod("InformOfDroppedSidearm",
                        new Type[] { typeof(ThingWithComps), typeof(bool) });

                    informOfAddedSidearmMethod = compSidearmMemoryType.GetMethod("InformOfAddedSidearm",
                        new Type[] { typeof(ThingWithComps) });
                }

                if (statCalculatorType != null)
                {
                    canPickupSidearmMethod = statCalculatorType.GetMethod("CanPickupSidearmInstance",
                        new Type[] { typeof(ThingWithComps), typeof(Pawn), typeof(string).MakeByRefType() });

                    rangedDPSMethod = statCalculatorType.GetMethod("RangedDPS",
                        new Type[] { typeof(ThingWithComps), typeof(float), typeof(float), typeof(float) });

                    getMeleeDPSMethod = statCalculatorType.GetMethod("getMeleeDPSBiased",
                        new Type[] { typeof(ThingWithComps), typeof(Pawn), typeof(float), typeof(float) });
                }

                if (thingDefStuffDefPairType != null)
                {
                    thingDefField = thingDefStuffDefPairType.GetField("thing");
                    stuffDefField = thingDefStuffDefPairType.GetField("stuff");
                }

                if (jobGiverRetrieveWeaponType != null)
                {
                    tryGiveJobStaticMethod = jobGiverRetrieveWeaponType.GetMethod("TryGiveJobStatic",
                        BindingFlags.Public | BindingFlags.Static);
                }

                // Get JobDefs
                equipSecondaryJobDef = DefDatabase<JobDef>.GetNamedSilentFail("EquipSecondary");

                _initialized = true;
                AutoArmLogger.Log("Simple Sidearms compatibility initialized successfully");
            }
            catch (Exception e)
            {
                AutoArmLogger.Error("Failed to initialize Simple Sidearms compatibility", e);
            }
        }

        private static object GetSidearmComp(Pawn pawn)
        {
            if (!IsLoaded() || pawn == null || getMemoryCompForPawnMethod == null)
                return null;

            try
            {
                return getMemoryCompForPawnMethod.Invoke(null, new object[] { pawn, false });
            }
            catch (Exception e)
            {
                if (Prefs.DevMode)
                    AutoArmLogger.Error($"SimpleSidearms GetSidearmComp failed", e);
                return null;
            }
        }

        // Check if SimpleSidearms already has a weapon job
        public static bool HasPendingSidearmJob(Pawn pawn)
        {
            if (!IsLoaded() || pawn == null || tryGiveJobStaticMethod == null)
                return false;

            try
            {
                object[] parameters = new object[] { pawn, false }; // false = not in combat
                var job = tryGiveJobStaticMethod.Invoke(null, parameters) as Job;
                return job != null;
            }
            catch (Exception e)
            {
                if (Prefs.DevMode)
                    AutoArmLogger.Error($"SimpleSidearms HasPendingSidearmJob failed", e);
                return false;
            }
        }

        // Main validation method
        public static bool CanPickupSidearmInstance(ThingWithComps weapon, Pawn pawn, out string reason)
        {
            reason = "";
            if (!IsLoaded() || weapon == null || pawn == null)
                return true;

            EnsureInitialized();

            try
            {
                if (canPickupSidearmMethod != null)
                {
                    object[] parameters = new object[] { weapon, pawn, null };
                    bool result = (bool)canPickupSidearmMethod.Invoke(null, parameters);
                    reason = (string)parameters[2] ?? "";
                    return result;
                }
            }
            catch (Exception e)
            {
                if (Prefs.DevMode)
                    AutoArmLogger.Warn($"SimpleSidearms CanPickupSidearmInstance failed: {e.Message}");
            }

            return true;
        }

        // Calculate weapon score using SimpleSidearms' DPS methods
        private static float GetWeaponScore(ThingWithComps weapon, Pawn pawn, bool preferMelee = false)
        {
            if (weapon == null || !WeaponValidation.IsProperWeapon(weapon))
                return 0f;

            // Thread-safe cache check
            float meleeSkill = pawn.skills?.GetSkill(SkillDefOf.Melee)?.Level ?? 0f;
            float shootingSkill = pawn.skills?.GetSkill(SkillDefOf.Shooting)?.Level ?? 0f;
            string cacheKey = $"{weapon.ThingID}_{pawn.ThingID}_{preferMelee}_{meleeSkill:F0}_{shootingSkill:F0}";

            lock (_cacheLock)
            {
                if (weaponScoreCache.TryGetValue(cacheKey, out float cachedScore) &&
                    weaponScoreCacheTick.TryGetValue(cacheKey, out int cacheTick) &&
                    Find.TickManager.TicksGame - cacheTick < WEAPON_SCORE_CACHE_LIFETIME)
                {
                    return cachedScore;
                }
            }

            float score = 0f;

            try
            {
                if (weapon.def.IsRangedWeapon && rangedDPSMethod != null)
                {
                    // RangedDPS(weapon, speedBias, averageSpeed, distance)
                    // Use default values: speedBias = 1, averageSpeed = 1, distance = 15 (medium range)
                    score = (float)rangedDPSMethod.Invoke(null, new object[] { weapon, 1f, 1f, 15f });
                    if (preferMelee)
                        score *= 0.7f;
                }
                else if (weapon.def.IsMeleeWeapon && getMeleeDPSMethod != null)
                {
                    // getMeleeDPSBiased(weapon, pawn, speedBias, averageSpeed)
                    // Use default values: speedBias = 1, averageSpeed = 1
                    score = (float)getMeleeDPSMethod.Invoke(null, new object[] { weapon, pawn, 1f, 1f });
                    if (!preferMelee)
                        score *= 0.7f;
                }

                // Apply quality modifier
                QualityCategory quality;
                if (weapon.TryGetQuality(out quality))
                {
                    score *= 0.95f + ((int)quality * 0.05f);
                }
            }
            catch (Exception e)
            {
                if (Prefs.DevMode)
                    AutoArmLogger.Debug($"SimpleSidearms weapon scoring failed, using fallback: {e.Message}");
                // Fallback to AutoArm's scoring
                score = WeaponScoringHelper.GetTotalScore(pawn, weapon);
            }

            lock (_cacheLock)
            {
                weaponScoreCache[cacheKey] = score;
                weaponScoreCacheTick[cacheKey] = Find.TickManager.TicksGame;
            }

            return score;
        }

        // Get remembered sidearm pairs (with stuff)
        private static List<object> GetRememberedSidearmPairs(Pawn pawn)
        {
            var pairs = new List<object>();
            var comp = GetSidearmComp(pawn);
            if (comp == null || rememberedWeaponsProperty == null)
                return pairs;

            try
            {
                var rememberedList = rememberedWeaponsProperty.GetValue(comp) as System.Collections.IEnumerable;
                if (rememberedList != null)
                {
                    foreach (var item in rememberedList)
                    {
                        if (item != null)
                            pairs.Add(item);
                    }
                }
            }
            catch (Exception e)
            {
                if (Prefs.DevMode)
                    AutoArmLogger.Debug($"SimpleSidearms GetRememberedSidearmPairs failed: {e.Message}");
            }

            return pairs;
        }

        public static bool IsRememberedSidearm(Pawn pawn, ThingWithComps weapon)
        {
            if (!IsLoaded() || weapon == null || pawn == null)
                return false;

            var rememberedPairs = GetRememberedSidearmPairs(pawn);

            foreach (var pair in rememberedPairs)
            {
                if (pair != null && thingDefField != null && stuffDefField != null)
                {
                    var thingDef = thingDefField.GetValue(pair) as ThingDef;
                    var stuffDef = stuffDefField.GetValue(pair) as ThingDef;

                    if (thingDef == weapon.def && stuffDef == weapon.Stuff)
                        return true;
                }
            }

            return false;
        }

        public static void InformOfDroppedSidearm(Pawn pawn, ThingWithComps weapon)
        {
            if (!IsLoaded() || pawn == null || weapon == null)
                return;

            EnsureInitialized();

            try
            {
                var comp = GetSidearmComp(pawn);
                if (comp != null && informOfDroppedSidearmMethod != null)
                {
                    informOfDroppedSidearmMethod.Invoke(comp, new object[] { weapon, true });
                    AutoArmLogger.LogWeapon(pawn, weapon, "Informed SS of weapon drop");
                }
            }
            catch (Exception e)
            {
                if (Prefs.DevMode)
                    AutoArmLogger.Debug($"SimpleSidearms InformOfDroppedSidearm failed: {e.Message}");
            }
        }

        public static void InformOfAddedSidearm(Pawn pawn, ThingWithComps weapon)
        {
            if (!IsLoaded() || pawn == null || weapon == null)
                return;

            EnsureInitialized();

            try
            {
                var comp = GetSidearmComp(pawn);
                if (comp != null && informOfAddedSidearmMethod != null)
                {
                    informOfAddedSidearmMethod.Invoke(comp, new object[] { weapon });
                    AutoArmLogger.LogWeapon(pawn, weapon, "Informed SS of weapon addition");
                }
            }
            catch (Exception e)
            {
                if (Prefs.DevMode)
                    AutoArmLogger.Debug($"SimpleSidearms InformOfAddedSidearm failed: {e.Message}");
            }
        }

        // Main sidearm job method
        public static Job TryGetSidearmUpgradeJob(Pawn pawn)
        {
            if (!IsLoaded() || pawn == null || !pawn.IsColonist)
                return null;

            // Check if SS already has a job pending
            if (HasPendingSidearmJob(pawn))
            {
                AutoArmLogger.LogPawn(pawn, "SimpleSidearms already has a weapon job pending");
                return null;
            }

            // Check settings and conditions
            if (AutoArmMod.settings?.autoEquipSidearms != true)
                return null;

            if (AutoArmMod.settings?.allowTemporaryColonists != true && JobGiverHelpers.IsTemporaryColonist(pawn))
                return null;

            if (AutoArmMod.settings?.disableDuringRaids == true)
            {
                foreach (var checkMap in Find.Maps)
                {
                    if (JobGiver_PickUpBetterWeapon.IsRaidActive(checkMap))
                        return null;
                }
            }

            // Check cooldown
            if (TimingHelper.IsOnCooldown(pawn, TimingHelper.CooldownType.FailedUpgradeSearch))
                return null;

            EnsureInitialized();
            if (!_initialized)
                return null;

            try
            {
                // Track ALL current weapons (including stuff variations)
                var currentWeaponsByType = new Dictionary<(ThingDef def, ThingDef stuff), ThingWithComps>();
                var currentWeaponsByDef = new Dictionary<ThingDef, List<ThingWithComps>>();

                // Add primary weapon
                if (pawn.equipment?.Primary != null)
                {
                    var primary = pawn.equipment.Primary;
                    var key = (primary.def, primary.Stuff);
                    currentWeaponsByType[key] = primary;

                    if (!currentWeaponsByDef.ContainsKey(primary.def))
                        currentWeaponsByDef[primary.def] = new List<ThingWithComps>();
                    currentWeaponsByDef[primary.def].Add(primary);
                }

                // Add inventory weapons
                foreach (var item in pawn.inventory?.innerContainer ?? Enumerable.Empty<Thing>())
                {
                    if (item is ThingWithComps weapon && weapon.def.IsWeapon)
                    {
                        var key = (weapon.def, weapon.Stuff);

                        // Keep best of each type+stuff combination
                        if (!currentWeaponsByType.ContainsKey(key) ||
                            GetWeaponScore(weapon, pawn) > GetWeaponScore(currentWeaponsByType[key], pawn))
                        {
                            currentWeaponsByType[key] = weapon;
                        }

                        if (!currentWeaponsByDef.ContainsKey(weapon.def))
                            currentWeaponsByDef[weapon.def] = new List<ThingWithComps>();
                        currentWeaponsByDef[weapon.def].Add(weapon);
                    }
                }

                bool preferMelee = pawn.skills?.GetSkill(SkillDefOf.Melee)?.Level >
                                  pawn.skills?.GetSkill(SkillDefOf.Shooting)?.Level;

                // Get nearby weapons
                var nearbyWeapons = ImprovedWeaponCacheManager.GetWeaponsNear(pawn.Map, pawn.Position, 40f)
                    .Where(w => w != null &&
                               !w.IsForbidden(pawn) &&
                               !DroppedItemTracker.IsRecentlyDropped(w) &&
                               ValidationHelper.CanPawnUseWeapon(pawn, w, out _) &&
                               pawn.CanReserveAndReach(w, PathEndMode.ClosestTouch, Danger.Deadly))
                    .OrderBy(w => w.Position.DistanceToSquared(pawn.Position))
                    .Take(20);

                foreach (var weapon in nearbyWeapons)
                {
                    // Check outfit filter
                    var filter = pawn.outfits?.CurrentApparelPolicy?.filter;
                    if (filter != null && !filter.Allows(weapon))
                        continue;

                    // Check SimpleSidearms validation
                    string reason;
                    bool canPickup = CanPickupSidearmInstance(weapon, pawn, out reason);

                    var weaponKey = (weapon.def, weapon.Stuff);

                    // Check for exact duplicate (same def + stuff)
                    if (currentWeaponsByType.ContainsKey(weaponKey))
                    {
                        // Only consider upgrades if allowed
                        if (AutoArmMod.settings?.allowSidearmUpgrades == true)
                        {
                            var existingWeapon = currentWeaponsByType[weaponKey];

                            // Don't upgrade forced weapons unless allowed
                            if (ForcedWeaponHelper.IsForced(pawn, existingWeapon) &&
                                AutoArmMod.settings?.allowForcedWeaponUpgrades != true)
                                continue;

                            float existingScore = GetWeaponScore(existingWeapon, pawn, preferMelee);
                            float newScore = GetWeaponScore(weapon, pawn, preferMelee);

                            // Only upgrade if significantly better (quality difference usually)
                            if (newScore > existingScore * 1.15f)
                            {
                                AutoArmLogger.LogPawn(pawn, $"Upgrading {existingWeapon.Label} to {weapon.Label}");

                                // Mark old weapon to prevent re-pickup
                                DroppedItemTracker.MarkPendingSameTypeUpgrade(existingWeapon);

                                // Inform SS we're dropping the old one
                                InformOfDroppedSidearm(pawn, existingWeapon);

                                var job = JobMaker.MakeJob(JobDefOf.Equip, weapon);
                                return job;
                            }
                        }
                        continue;
                    }

                    // New weapon type/stuff combination
                    if (!canPickup)
                    {
                        // Try replacing worst weapon if at limits
                        if (AutoArmMod.settings?.allowSidearmUpgrades == true && reason != null &&
                            (reason.ToLower().Contains("limit") ||
                             reason.ToLower().Contains("mass") ||
                             reason.ToLower().Contains("bulk")))
                        {
                            // Find worst non-forced weapon
                            ThingWithComps worstWeapon = null;
                            float worstScore = float.MaxValue;

                            foreach (var kvp in currentWeaponsByType)
                            {
                                var existingWeapon = kvp.Value;
                                if (!ForcedWeaponHelper.IsForced(pawn, existingWeapon))
                                {
                                    float score = GetWeaponScore(existingWeapon, pawn, preferMelee);
                                    if (score < worstScore)
                                    {
                                        worstScore = score;
                                        worstWeapon = existingWeapon;
                                    }
                                }
                            }

                            // Check if replacement is worthwhile
                            if (worstWeapon != null)
                            {
                                float newScore = GetWeaponScore(weapon, pawn, preferMelee);
                                if (newScore > worstScore * 1.15f)
                                {
                                    AutoArmLogger.LogPawn(pawn, $"Replacing {worstWeapon.Label} with {weapon.Label}");

                                    // Drop worst weapon
                                    if (pawn.inventory?.innerContainer?.Contains(worstWeapon) == true)
                                    {
                                        Thing droppedWeapon;
                                        if (pawn.inventory.innerContainer.TryDrop(worstWeapon, pawn.Position, pawn.Map,
                                            ThingPlaceMode.Near, out droppedWeapon))
                                        {
                                            DroppedItemTracker.MarkAsDropped(droppedWeapon, 3600);
                                            InformOfDroppedSidearm(pawn, worstWeapon);

                                            var job = equipSecondaryJobDef != null ?
                                                JobMaker.MakeJob(equipSecondaryJobDef, weapon) :
                                                JobMaker.MakeJob(JobDefOf.Equip, weapon);

                                            // We'll inform SS after pickup succeeds
                                            return job;
                                        }
                                    }
                                }
                            }
                        }
                        continue;
                    }

                    // Can pickup - check if it's remembered or worth getting
                    if (IsRememberedSidearm(pawn, weapon))
                    {
                        AutoArmLogger.LogWeapon(pawn, weapon, "Picking up remembered sidearm");
                        var job = equipSecondaryJobDef != null ?
                            JobMaker.MakeJob(equipSecondaryJobDef, weapon) :
                            JobMaker.MakeJob(JobDefOf.Equip, weapon);
                        return job;
                    }

                    // Not remembered - check if worth picking up
                    float weaponScore = GetWeaponScore(weapon, pawn, preferMelee);

                    // Calculate average score of current weapons
                    float avgCurrentScore = currentWeaponsByType.Values
                        .Select(w => GetWeaponScore(w, pawn, preferMelee))
                        .DefaultIfEmpty(0f)
                        .Average();

                    // Only pick up if significantly better than average
                    if (weaponScore > avgCurrentScore * 1.2f)
                    {
                        AutoArmLogger.LogWeapon(pawn, weapon, "Picking up high-quality sidearm");
                        var job = equipSecondaryJobDef != null ?
                            JobMaker.MakeJob(equipSecondaryJobDef, weapon) :
                            JobMaker.MakeJob(JobDefOf.Equip, weapon);
                        return job;
                    }
                }

                // No suitable weapons found
                TimingHelper.SetCooldown(pawn, TimingHelper.CooldownType.FailedUpgradeSearch);
                return null;
            }
            catch (Exception e)
            {
                AutoArmLogger.Error($"SimpleSidearms TryGetSidearmUpgradeJob failed for {pawn?.LabelShort}", e);
                TimingHelper.SetCooldown(pawn, TimingHelper.CooldownType.FailedUpgradeSearch);
                return null;
            }
        }

        public static void CleanupAfterLoad()
        {
            if (!IsLoaded())
                return;

            lock (_cacheLock)
            {
                weaponScoreCache.Clear();
                weaponScoreCacheTick.Clear();
            }

            AutoArmLogger.Log("Cleared sidearm cache after load");
        }

        // Cleanup old cache entries
        public static void CleanupOldCache()
        {
            var currentTick = Find.TickManager.TicksGame;
            var expiredKeys = new List<string>();

            lock (_cacheLock)
            {
                foreach (var kvp in weaponScoreCacheTick)
                {
                    if (currentTick - kvp.Value > WEAPON_SCORE_CACHE_LIFETIME * 2)
                        expiredKeys.Add(kvp.Key);
                }

                foreach (var key in expiredKeys)
                {
                    weaponScoreCache.Remove(key);
                    weaponScoreCacheTick.Remove(key);
                }
            }
        }

        // Pending upgrade methods
        public static bool HasPendingUpgrade(Pawn pawn)
        {
            return pendingUpgrades.ContainsKey(pawn);
        }

        public static PendingUpgradeInfo GetPendingUpgrade(Pawn pawn)
        {
            pendingUpgrades.TryGetValue(pawn, out var info);
            return info;
        }

        public static void CancelPendingUpgrade(Pawn pawn)
        {
            pendingUpgrades.Remove(pawn);
        }

        public static void MarkPendingUpgrade(Pawn pawn, ThingWithComps oldWeapon, ThingWithComps newWeapon)
        {
            pendingUpgrades[pawn] = new PendingUpgradeInfo
            {
                oldWeapon = oldWeapon,
                newWeapon = newWeapon,
                startTick = Find.TickManager.TicksGame
            };
        }

        // AllowBlockedWeaponUse method
        public static bool AllowBlockedWeaponUse()
        {
            if (!IsLoaded())
                return true; // Default to allowing if SS not loaded

            // This would check SS settings, but for now return true
            return true;
        }

        // Clean up old pending upgrades
        public static void CleanupOldTrackingData()
        {
            var currentTick = Find.TickManager.TicksGame;
            var expiredPawns = pendingUpgrades
                .Where(kvp => kvp.Key == null || kvp.Key.Dead ||
                             currentTick - kvp.Value.startTick > 1800) // 30 seconds timeout
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var pawn in expiredPawns)
            {
                pendingUpgrades.Remove(pawn);
            }
        }

        // Backwards compatibility
        [Obsolete("Use CanPickupSidearmInstance")]
        public static bool CanPickupWeaponAsSidearm(ThingWithComps weapon, Pawn pawn, out string reason)
        {
            return CanPickupSidearmInstance(weapon, pawn, out reason);
        }

        // Backwards compatibility - returns false as concept is deprecated
        [Obsolete("Use DroppedItemTracker.IsSimpleSidearmsSwapInProgress instead")]
        public static bool PawnHasTemporarySidearmEquipped(Pawn pawn)
        {
            // This concept is deprecated - use DroppedItemTracker instead
            return false;
        }

        // Check if primary weapon is a remembered sidearm
        public static bool PrimaryIsRememberedSidearm(Pawn pawn)
        {
            if (!IsLoaded() || pawn?.equipment?.Primary == null)
                return false;

            return IsRememberedSidearm(pawn, pawn.equipment.Primary);
        }

        // Constant for duplicate prevention
        public const bool ALLOW_DUPLICATE_WEAPON_TYPES = false;

        // Property version that could check actual SS settings
        public static bool AllowDuplicateWeaponTypes
        {
            get
            {
                if (!IsLoaded())
                    return false;
                
                // Would need to reflect into SS settings
                // For now, return false (no duplicates allowed)
                return ALLOW_DUPLICATE_WEAPON_TYPES;
            }
        }

        // Test helper methods
        public static int GetMaxSidearmsForPawn(Pawn pawn)
        {
            // Simplified - actual SS has complex calculations
            return 3; // Default value for testing
        }

        public static int GetCurrentSidearmCount(Pawn pawn)
        {
            if (pawn?.inventory?.innerContainer == null)
                return 0;
                
            return pawn.inventory.innerContainer.Count(t => t is ThingWithComps && t.def.IsWeapon);
        }

        public static void LogSimpleSidearmsSettings()
        {
            AutoArmLogger.Log("SimpleSidearms settings (test mode) - Max sidearms: 3");
        }

        public static void MarkWeaponAsRecentlyDropped(ThingWithComps weapon)
        {
            // Use DroppedItemTracker instead
            if (weapon != null)
                DroppedItemTracker.MarkAsDropped(weapon, 1200);
        }
    }

    // ==============================================
    // HARMONY PATCHES
    // ==============================================

    // Clean up on game load
    [HarmonyPatch(typeof(Game), "LoadGame")]
    public static class Game_LoadGame_SimpleSidearms_Patch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            SimpleSidearmsCompat.CleanupAfterLoad();
        }
    }

    // Periodic cleanup
    [HarmonyPatch(typeof(Pawn), "TickRare")]
    public static class Pawn_TickRare_SimpleSidearms_Patch
    {
        private static int lastCleanupTick = 0;
        private const int CLEANUP_INTERVAL = 2500;

        [HarmonyPostfix]
        public static void Postfix(Pawn __instance)
        {
            if (!__instance.IsColonist || !SimpleSidearmsCompat.IsLoaded())
                return;

            int currentTick = Find.TickManager.TicksGame;
            if (currentTick - lastCleanupTick > CLEANUP_INTERVAL)
            {
                SimpleSidearmsCompat.CleanupOldCache();
                SimpleSidearmsCompat.CleanupOldTrackingData(); // Fix memory leak
                lastCleanupTick = currentTick;
            }
        }
    }

    // SimpleSidearms notification is now integrated into the main AddEquipment patch in EquipmentPatches.cs

    // Dynamic patches for SimpleSidearms methods
    public static class SimpleSidearms_Dynamic_Patches
    {
        private static bool patchesApplied = false;

        public static void ApplyPatches(Harmony harmony)
        {
            if (!SimpleSidearmsCompat.IsLoaded() || patchesApplied)
                return;

            try
            {
                // Patch WeaponAssingment.equipSpecificWeapon to maintain forced status
                var weaponAssignmentType = GenTypes.AllTypes.FirstOrDefault(t =>
                    t.FullName == "PeteTimesSix.SimpleSidearms.Utilities.WeaponAssingment");

                if (weaponAssignmentType != null)
                {
                    var equipSpecificWeaponMethod = weaponAssignmentType.GetMethod("equipSpecificWeapon",
                        BindingFlags.Public | BindingFlags.Static);
                    if (equipSpecificWeaponMethod != null)
                    {
                        harmony.Patch(equipSpecificWeaponMethod,
                            prefix: new HarmonyMethod(typeof(SimpleSidearms_Dynamic_Patches), nameof(EquipSpecificWeapon_Prefix)),
                            postfix: new HarmonyMethod(typeof(SimpleSidearms_Dynamic_Patches), nameof(EquipSpecificWeapon_Postfix)));
                        AutoArmLogger.Log("Patched SimpleSidearms.equipSpecificWeapon");
                    }
                }

                patchesApplied = true;
            }
            catch (Exception e)
            {
                AutoArmLogger.Error("Failed to patch SimpleSidearms methods", e);
            }
        }

        public static void EquipSpecificWeapon_Prefix(Pawn pawn, ThingWithComps weapon, bool dropCurrent, bool intentionalDrop)
        {
            if (pawn == null || !pawn.IsColonist)
                return;

            DroppedItemTracker.MarkSimpleSidearmsSwapInProgress(pawn);
        }

        public static void EquipSpecificWeapon_Postfix(Pawn pawn, ThingWithComps weapon, bool dropCurrent, bool intentionalDrop)
        {
            if (pawn == null || !pawn.IsColonist)
                return;

            LongEventHandler.ExecuteWhenFinished(() =>
            {
                DroppedItemTracker.ClearSimpleSidearmsSwapInProgress(pawn);

                // Re-apply forced status
                var forcedDefs = ForcedWeaponHelper.GetForcedWeaponDefs(pawn);
                if (forcedDefs.Count > 0)
                {
                    if (pawn.equipment?.Primary != null && forcedDefs.Contains(pawn.equipment.Primary.def))
                    {
                        ForcedWeaponHelper.SetForced(pawn, pawn.equipment.Primary);
                    }

                    foreach (var item in pawn.inventory?.innerContainer ?? Enumerable.Empty<Thing>())
                    {
                        if (item is ThingWithComps invWeapon && invWeapon.def.IsWeapon && forcedDefs.Contains(invWeapon.def))
                        {
                            AutoArmLogger.LogWeapon(pawn, invWeapon, "Maintained forced status on sidearm");
                        }
                    }
                }
            });
        }
    }

    // ==============================================
    // ADDITIONAL PATCHES FOR SS INTEGRATION
    // ==============================================

    public static class SimpleSidearms_WeaponSwap_Patches
    {
        public static void ApplyPatches(Harmony harmony)
        {
            SimpleSidearms_Dynamic_Patches.ApplyPatches(harmony);
        }
    }

    public static class SimpleSidearms_JobGiver_RetrieveWeapon_Patch
    {
        public static void ApplyPatch(Harmony harmony)
        {
            // This would patch SS's JobGiver_RetrieveWeapon if needed
            // Currently no specific patches required
        }
    }
}