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
    // SimpleSidearms Compatibility
    // When SimpleSidearms is installed, we treat it as the authority for ALL weapon management.
    // This means:
    // 1. All weapons (primary and inventory) go through CanPickupSidearmInstance validation
    // 2. SimpleSidearms handles outfit filters, weight limits, slot limits, etc.
    // 3. AutoArm focuses only on scoring and choosing the best allowed weapons
    // 4. We respect SimpleSidearms' concept that any weapon can be swapped between primary/sidearm roles
    public static class SimpleSidearmsCompat
    {
        private static bool? _isLoaded = null;
        private static bool _initialized = false;

        // Core types and methods still needed
        private static Type compSidearmMemoryType;
        private static Type statCalculatorType;
        private static Type thingDefStuffDefPairType;

        private static MethodInfo canPickupSidearmInstanceMethod;
        private static MethodInfo rangedDPSAverageMethod;
        private static MethodInfo getMeleeDPSBiasedMethod;
        private static MethodInfo informOfDroppedSidearmMethod;
        private static PropertyInfo rememberedWeaponsProperty;
        private static FieldInfo thingField;
        private static FieldInfo stuffField;
        private static JobDef equipSecondaryJobDef;

        // Track pawns with sidearms temporarily in primary slot
        private static HashSet<Pawn> pawnsWithTemporarySidearmEquipped = new HashSet<Pawn>();

        // Track recently checked upgrade combinations
        private static Dictionary<string, int> recentlyCheckedUpgrades = new Dictionary<string, int>();
        private const int UPGRADE_CHECK_COOLDOWN = 2500;

        // Prevent duplicate weapon types in inventory
        public const bool ALLOW_DUPLICATE_WEAPON_TYPES = false;

        // Track when we last logged "forced weapon" for each pawn
        private static Dictionary<Pawn, int> lastForcedWeaponLogTick = new Dictionary<Pawn, int>();
        private const int FORCED_WEAPON_LOG_COOLDOWN = 10000;

        // Track when we last logged "worse than equipped" messages
        private static Dictionary<string, int> lastWorseWeaponLogTick = new Dictionary<string, int>();
        private const int WORSE_WEAPON_LOG_COOLDOWN = 5000; // Log at most once per 5 seconds per weapon type

        // Track SimpleSidearms validation messages to reduce spam
        private static Dictionary<string, int> validationMessageCount = new Dictionary<string, int>();
        private static Dictionary<string, int> lastValidationSummaryTick = new Dictionary<string, int>();
        private const int VALIDATION_SUMMARY_INTERVAL = 10000; // Log summary every 10 seconds

        // Track pending sidearm upgrades
        private static Dictionary<Pawn, SidearmUpgradeInfo> pendingSidearmUpgrades = new Dictionary<Pawn, SidearmUpgradeInfo>();

        // Track weapon scores to avoid recalculating
        private static Dictionary<string, float> weaponScoreCache = new Dictionary<string, float>();
        private static Dictionary<string, int> weaponScoreCacheTick = new Dictionary<string, int>();
        private const int WEAPON_SCORE_CACHE_LIFETIME = 1000;

        // Reflection cache keys
        private const string REFLECTION_KEY_GETSIDEARMCOMP = "SimpleSidearms.GetSidearmComp";
        private const string REFLECTION_KEY_SKIPDANGEROUS = "SimpleSidearms.SkipDangerous";
        private const string REFLECTION_KEY_SKIPEMP = "SimpleSidearms.SkipEMP";
        private const string REFLECTION_KEY_ALLOWBLOCKED = "SimpleSidearms.AllowBlocked";

        // Failed search tracking for performance optimization
        private static Dictionary<int, FailedSearchInfo> failedSearches = new Dictionary<int, FailedSearchInfo>();
        private static readonly object failedSearchLock = new object();
        private const int MIN_BACKOFF_TICKS = 1800; // 30 seconds
        private const int MAX_BACKOFF_TICKS = 36000; // 10 hours
        private const int CLEANUP_INTERVAL = 2500; // Clean up every ~40 seconds
        private static int lastCleanupTick = 0;

        private class FailedSearchInfo
        {
            public int FailCount;
            public int LastFailTick;
            public HashSet<string> FailedWeaponIds;
            public int NextAllowedSearchTick;

            public FailedSearchInfo()
            {
                FailedWeaponIds = new HashSet<string>();
            }
        }

        public class SidearmUpgradeInfo
        {
            public ThingWithComps oldWeapon;
            public ThingWithComps newWeapon;
            public ThingWithComps originalPrimary;
            public bool isTemporarySwap;
            public int swapStartTick;
        }

        public static bool IsLoaded()
        {
            if (_isLoaded == null)
            {
                // Use exact package ID matching to avoid false positives
                _isLoaded = ModLister.AllInstalledMods.Any(m =>
                    m.Active && (
                        m.PackageIdPlayerFacing.Equals("PeteTimesSix.SimpleSidearms", StringComparison.OrdinalIgnoreCase) ||
                        m.PackageIdPlayerFacing.Equals("petetimessix.simplesidearms", StringComparison.OrdinalIgnoreCase)
                    ));

                if (_isLoaded.Value)
                {
                    // Log which specific mod was detected for debugging
                    var detectedMod = ModLister.AllInstalledMods.FirstOrDefault(m =>
                        m.Active && (
                            m.PackageIdPlayerFacing.Equals("PeteTimesSix.SimpleSidearms", StringComparison.OrdinalIgnoreCase) ||
                            m.PackageIdPlayerFacing.Equals("petetimessix.simplesidearms", StringComparison.OrdinalIgnoreCase)
                        ));

                    if (detectedMod != null)
                    {
                        AutoArmLogger.Log($"Simple Sidearms detected: {detectedMod.PackageIdPlayerFacing} ({detectedMod.Name})");
                    }
                    else
                    {
                        AutoArmLogger.Log("Simple Sidearms detected and loaded");
                    }
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
                var ssNamespace = "PeteTimesSix.SimpleSidearms";
                compSidearmMemoryType = GenTypes.AllTypes.FirstOrDefault(t =>
                    t.FullName == "SimpleSidearms.rimworld.CompSidearmMemory");
                statCalculatorType = GenTypes.AllTypes.FirstOrDefault(t =>
                    t.FullName == $"{ssNamespace}.Utilities.StatCalculator");
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
                    informOfDroppedSidearmMethod = compSidearmMemoryType.GetMethod(
                        "InformOfDroppedSidearm",
                        new Type[] { typeof(ThingWithComps), typeof(bool) });

                    // Cache GetMemoryCompForPawn method
                    var getMemoryCompMethod = compSidearmMemoryType.GetMethod("GetMemoryCompForPawn",
                        BindingFlags.Public | BindingFlags.Static);
                    if (getMemoryCompMethod != null)
                    {
                        ReflectionHelper.CacheMethod(REFLECTION_KEY_GETSIDEARMCOMP, getMemoryCompMethod);
                    }
                }

                if (statCalculatorType != null)
                {
                    canPickupSidearmInstanceMethod = statCalculatorType.GetMethod(
                        "CanPickupSidearmInstance",
                        new Type[] { typeof(ThingWithComps), typeof(Pawn), typeof(string).MakeByRefType() });

                    rangedDPSAverageMethod = statCalculatorType.GetMethod(
                        "RangedDPSAverage",
                        new Type[] { typeof(ThingWithComps), typeof(float), typeof(float) });

                    getMeleeDPSBiasedMethod = statCalculatorType.GetMethod(
                        "getMeleeDPSBiased",
                        new Type[] { typeof(ThingWithComps), typeof(Pawn), typeof(float), typeof(float) });
                }

                equipSecondaryJobDef = DefDatabase<JobDef>.GetNamedSilentFail("EquipSecondary");

                // Cache settings access methods
                CacheSettingsAccess();

                _initialized = true;
                AutoArmLogger.Log("Simple Sidearms compatibility initialized successfully");
            }
            catch (Exception e)
            {
                Log.Warning($"[AutoArm] Failed to initialize Simple Sidearms compatibility: {e.Message}");
                AutoArmLogger.Log($"Stack trace: {e.StackTrace}");
            }
        }

        private static void CacheSettingsAccess()
        {
            try
            {
                var settingsType = GenTypes.AllTypes.FirstOrDefault(t =>
                    string.Equals(t.Name, "SimpleSidearms_Settings", StringComparison.Ordinal) ||
                    (string.Equals(t.Name, "Settings", StringComparison.Ordinal) && t.Namespace != null && t.Namespace.IndexOf("SimpleSidearms", StringComparison.Ordinal) >= 0));

                if (settingsType != null)
                {
                    var modType = GenTypes.AllTypes.FirstOrDefault(t =>
                        string.Equals(t.Name, "SimpleSidearmsMod", StringComparison.Ordinal) ||
                        (t.Name.IndexOf("SimpleSidearms", StringComparison.Ordinal) >= 0 && t.IsSubclassOf(typeof(Mod))));

                    if (modType != null)
                    {
                        var settingsField = modType.GetField("settings", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic) ??
                                          modType.GetField("Settings", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

                        if (settingsField != null)
                        {
                            ReflectionHelper.CacheFieldGetter(REFLECTION_KEY_SKIPDANGEROUS,
                                () =>
                                {
                                    var settings = settingsField.GetValue(null);
                                    var field = settingsType.GetField("SkipDangerousWeapons", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                                    return field?.GetValue(settings) ?? true;
                                });

                            ReflectionHelper.CacheFieldGetter(REFLECTION_KEY_SKIPEMP,
                                () =>
                                {
                                    var settings = settingsField.GetValue(null);
                                    var field = settingsType.GetField("SkipEMPWeapons", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                                    return field?.GetValue(settings) ?? false;
                                });

                            ReflectionHelper.CacheFieldGetter(REFLECTION_KEY_ALLOWBLOCKED,
                                () =>
                                {
                                    var settings = settingsField.GetValue(null);
                                    var field = settingsType.GetField("AllowBlockedWeaponUse", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                                    return field?.GetValue(settings) ?? false;
                                });
                        }
                    }
                }
            }
            catch (Exception e)
            {
                AutoArmLogger.Log($"WARNING: Failed to cache SimpleSidearms settings access: {e.Message}");
            }
        }

        public static void MarkWeaponAsRecentlyDropped(Thing weapon)
        {
            if (weapon != null)
            {
                DroppedItemTracker.MarkAsDropped(weapon);
                // Logging is already done in DroppedItemTracker.MarkAsDropped
            }
        }

        public static void InformOfDroppedSidearm(Pawn pawn, ThingWithComps weapon)
        {
            InformSimpleSidearmsOfDrop(pawn, weapon);
        }

        private static void InformSimpleSidearmsOfDrop(Pawn pawn, ThingWithComps weapon)
        {
            if (weapon == null || !IsLoaded())
                return;

            // Don't double-mark as dropped, this is already done elsewhere
            // MarkWeaponAsRecentlyDropped(weapon);

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
                AutoArmLogger.Log($"WARNING: Failed to inform SS of dropped weapon: {e.Message}");
            }
        }

        public static bool CanPickupSidearmInstance(ThingWithComps weapon, Pawn pawn, out string reason)
        {
            reason = "";
            if (!IsLoaded() || weapon == null || pawn == null)
                return true;

            EnsureInitialized();

            // When SimpleSidearms is loaded, it handles all weapon validation
            // including outfit filters, weight limits, slot limits, etc.
            // We delegate all weapon pickup decisions to SimpleSidearms
            try
            {
                if (canPickupSidearmInstanceMethod != null)
                {
                    object[] parameters = new object[] { weapon, pawn, null };
                    bool result = (bool)canPickupSidearmInstanceMethod.Invoke(null, parameters);
                    reason = (string)parameters[2] ?? "";

                    // Debug logging to track what SimpleSidearms is allowing
                    // ENHANCED FILTERING to prevent spam
                    if (AutoArmMod.settings?.debugLogging == true &&
                        !pawn.WorkTagIsDisabled(WorkTags.Violent)) // Skip logging for violence-incapable pawns
                    {
                        // Create a key for this validation result
                        string validationKey = $"{pawn.ThingID}_{result}_{reason?.ToLower().GetHashCode()}";

                        // Track how many times we've seen this validation
                        if (!validationMessageCount.ContainsKey(validationKey))
                            validationMessageCount[validationKey] = 0;
                        validationMessageCount[validationKey]++;

                        // Only log if it's not a common rejection reason
                        bool shouldLog = false;
                        if (!result && reason != null)
                        {
                            var lowerReason = reason.ToLower();
                            // Expanded list of common rejections to skip
                            if (!(lowerReason.IndexOf("not a tool", StringComparison.Ordinal) >= 0 ||
                                lowerReason.IndexOf("incapable of violence", StringComparison.Ordinal) >= 0 ||
                                lowerReason.IndexOf("cannot manipulate", StringComparison.Ordinal) >= 0 ||
                                lowerReason.IndexOf("too heavy", StringComparison.Ordinal) >= 0 ||
                                lowerReason.IndexOf("weight", StringComparison.Ordinal) >= 0 ||
                                lowerReason.IndexOf("slots full", StringComparison.Ordinal) >= 0 ||
                                lowerReason.IndexOf("mass", StringComparison.Ordinal) >= 0 ||
                                lowerReason.IndexOf("already have", StringComparison.Ordinal) >= 0 ||
                                lowerReason.IndexOf("not allowed", StringComparison.Ordinal) >= 0 ||
                                lowerReason.IndexOf("outfit", StringComparison.Ordinal) >= 0 ||
                                lowerReason.IndexOf("filter", StringComparison.Ordinal) >= 0))
                            {
                                // Only log unusual rejections
                                shouldLog = true;
                            }
                        }

                        // For allowed cases, only log unusual ones
                        if (result && !string.Equals(reason, "No issue", StringComparison.Ordinal))
                        {
                            shouldLog = true; // Log non-standard approvals
                        }

                        // Check if we should log a summary instead
                        int currentTick = Find.TickManager.TicksGame;
                        if (!lastValidationSummaryTick.TryGetValue(pawn.ThingID, out int lastSummaryTick) ||
                            currentTick - lastSummaryTick > VALIDATION_SUMMARY_INTERVAL)
                        {
                            // Log a summary of validations for this pawn
                            int totalValidations = 0;
                            foreach (var kvp in validationMessageCount)
                            {
                                if (kvp.Key.StartsWith(pawn.ThingID + "_", StringComparison.Ordinal))
                                    totalValidations += kvp.Value;
                            }

                            if (totalValidations > 10) // Only log summary if there's been significant activity
                            {
                                AutoArmLogger.LogPawn(pawn, $"SimpleSidearms validations in last {VALIDATION_SUMMARY_INTERVAL / 60} seconds: {totalValidations} checks");
                                lastValidationSummaryTick[pawn.ThingID] = currentTick;

                                // Clear old counts for this pawn
                                var keysToRemove = validationMessageCount.Keys.Where(k => k.StartsWith(pawn.ThingID + "_", StringComparison.Ordinal)).ToList();
                                foreach (var key in keysToRemove)
                                    validationMessageCount.Remove(key);
                            }
                        }

                        // Only log individual messages for unusual cases
                        if (shouldLog)
                        {
                            AutoArmLogger.LogWeapon(pawn, weapon,
                                $"SimpleSidearms validation: {(result ? "ALLOWED" : "DENIED")} - {reason}");
                        }
                    }

                    return result;
                }
            }
            catch (Exception e)
            {
                AutoArmLogger.Log($"WARNING: Error checking sidearm compatibility: {e.Message}");
            }

            return true;
        }

        // Compatibility wrapper for old method name
        [Obsolete("Use CanPickupSidearmInstance instead")]
        public static bool CanPickupWeaponAsSidearm(ThingWithComps weapon, Pawn pawn, out string reason)
        {
            return CanPickupSidearmInstance(weapon, pawn, out reason);
        }

        public static bool PawnHasTemporarySidearmEquipped(Pawn pawn)
        {
            return pawn != null && pawnsWithTemporarySidearmEquipped.Contains(pawn);
        }

        public static bool IsRememberedSidearm(Pawn pawn, ThingWithComps weapon)
        {
            if (!IsLoaded() || pawn == null || weapon == null)
                return false;

            EnsureInitialized();
            if (!_initialized)
                return false;

            try
            {
                var comp = GetSidearmComp(pawn);
                if (comp == null)
                    return false;

                var rememberedSidearms = GetCurrentSidearmDefs(pawn, comp);
                return rememberedSidearms.Contains(weapon.def);
            }
            catch
            {
                return false;
            }
        }

        public static bool PrimaryIsRememberedSidearm(Pawn pawn)
        {
            if (!IsLoaded() || pawn?.equipment?.Primary == null)
                return false;

            // NOTE: This method is named confusingly - it doesn't check if the primary weapon is a remembered sidearm.
            // Instead, it checks if the pawn currently has a sidearm temporarily equipped as primary.
            // This happens when AutoArm swaps a sidearm to primary for upgrading purposes.
            // The method is used to prevent AutoArm from evaluating weapons during sidearm upgrades.
            return PawnHasTemporarySidearmEquipped(pawn);
        }

        public static bool TrySwapSidearmToPrimary(Pawn pawn, ThingWithComps sidearm)
        {
            if (!IsLoaded() || pawn == null || sidearm == null)
                return false;

            EnsureInitialized();

            try
            {
                if (pawn.inventory?.innerContainer?.Contains(sidearm) == true)
                {
                    pawn.inventory.innerContainer.Remove(sidearm);

                    ThingWithComps previousPrimary = pawn.equipment?.Primary;

                    if (previousPrimary != null)
                    {
                        pawn.equipment.Remove(previousPrimary);
                        pawn.inventory.innerContainer.TryAdd(previousPrimary);
                    }

                    pawn.equipment.AddEquipment(sidearm);
                    pawnsWithTemporarySidearmEquipped.Add(pawn);

                    AutoArmLogger.LogWeapon(pawn, sidearm, "Swapped sidearm to primary");
                    return true;
                }
            }
            catch (Exception e)
            {
                Log.Error($"[AutoArm] Failed to swap sidearm to primary: {e.Message}");
            }

            return false;
        }

        public static bool TrySwapPrimaryToSidearm(Pawn pawn, ThingWithComps weapon)
        {
            if (!IsLoaded() || pawn == null || weapon == null)
                return false;

            EnsureInitialized();

            try
            {
                if (pawn.equipment?.Primary == weapon)
                {
                    pawn.equipment.Remove(weapon);

                    if (pawn.inventory.innerContainer.TryAdd(weapon))
                    {
                        pawnsWithTemporarySidearmEquipped.Remove(pawn);
                        AutoArmLogger.LogWeapon(pawn, weapon, "Moved back to inventory");
                        return true;
                    }
                }
            }
            catch (Exception e)
            {
                Log.Error($"[AutoArm] Failed to swap primary to sidearm: {e.Message}");
            }

            return false;
        }

        private static float GetWeaponScore(ThingWithComps weapon, Pawn pawn, bool preferMelee = false)
        {
            if (weapon == null || !weapon.def.IsWeapon)
                return 0f;

            string cacheKey = $"{weapon.ThingID}_{pawn.ThingID}_{preferMelee}";
            if (weaponScoreCache.TryGetValue(cacheKey, out float cachedScore) &&
                weaponScoreCacheTick.TryGetValue(cacheKey, out int cacheTick) &&
                Find.TickManager.TicksGame - cacheTick < WEAPON_SCORE_CACHE_LIFETIME)
            {
                return cachedScore;
            }

            float score = 0f;

            try
            {
                if (weapon.def.IsRangedWeapon && rangedDPSAverageMethod != null)
                {
                    // Use SS's ranged DPS calculation
                    // Parameters: weapon, speedBias (1.0 = neutral), averageSpeed (4.5 is typical)
                    score = (float)rangedDPSAverageMethod.Invoke(null, new object[] { weapon, 1.0f, 4.5f });

                    // Apply preference modifier
                    if (preferMelee)
                        score *= 0.7f; // Reduce ranged score if preferring melee
                }
                else if (weapon.def.IsMeleeWeapon && getMeleeDPSBiasedMethod != null)
                {
                    // Use SS's melee DPS calculation
                    // Parameters: weapon, pawn, speedBias (1.0 = neutral), averageSpeed (1.8 is typical)
                    score = (float)getMeleeDPSBiasedMethod.Invoke(null, new object[] { weapon, pawn, 1.0f, 1.8f });

                    // Apply preference modifier
                    if (!preferMelee && weapon.def.IsRangedWeapon)
                        score *= 1.3f; // Boost melee score if preferring ranged but weapon can do both
                }

                // Apply quality modifier
                QualityCategory quality;
                if (weapon.TryGetQuality(out quality))
                {
                    score *= 0.9f + ((int)quality * 0.05f); // 0.9 to 1.25 multiplier
                }
            }
            catch (Exception e)
            {
                AutoArmLogger.Log($"WARNING: Failed to calculate weapon score: {e.Message}");
                // Fallback to basic market value scoring
                score = weapon.MarketValue * 0.01f;
            }

            weaponScoreCache[cacheKey] = score;
            weaponScoreCacheTick[cacheKey] = Find.TickManager.TicksGame;

            return score;
        }

        private static ThingWithComps GetWorstWeapon(Pawn pawn, out float worstScore, bool excludeForced = true)
        {
            ThingWithComps worstWeapon = null;
            worstScore = float.MaxValue;

            // Check primary
            if (pawn.equipment?.Primary != null)
            {
                if (!excludeForced || !ForcedWeaponHelper.IsForced(pawn, pawn.equipment.Primary))
                {
                    float score = GetWeaponScore(pawn.equipment.Primary, pawn);
                    if (score < worstScore)
                    {
                        worstScore = score;
                        worstWeapon = pawn.equipment.Primary;
                    }
                }
            }

            // Check inventory
            foreach (var item in pawn.inventory?.innerContainer ?? Enumerable.Empty<Thing>())
            {
                if (item is ThingWithComps weapon && weapon.def.IsWeapon)
                {
                    if (!excludeForced || !ForcedWeaponHelper.IsWeaponDefForced(pawn, weapon.def))
                    {
                        float score = GetWeaponScore(weapon, pawn);
                        if (score < worstScore)
                        {
                            worstScore = score;
                            worstWeapon = weapon;
                        }
                    }
                }
            }

            return worstWeapon;
        }

        private static int GetTotalWeaponCount(Pawn pawn)
        {
            int count = 0;

            if (pawn.equipment?.Primary != null)
                count++;

            count += pawn.inventory?.innerContainer?.Count(t => t.def.IsWeapon) ?? 0;

            return count;
        }

        // Failed search tracking methods
        private static bool IsOnSidearmCooldown(Pawn pawn)
        {
            if (pawn == null) return false;

            lock (failedSearchLock)
            {
                if (failedSearches.TryGetValue(pawn.thingIDNumber, out var info))
                {
                    return Find.TickManager.TicksGame < info.NextAllowedSearchTick;
                }
            }
            return false;
        }

        private static void RecordFailedSearch(Pawn pawn, List<string> attemptedWeaponIds = null)
        {
            if (pawn == null) return;

            lock (failedSearchLock)
            {
                if (!failedSearches.TryGetValue(pawn.thingIDNumber, out var info))
                {
                    info = new FailedSearchInfo();
                    failedSearches[pawn.thingIDNumber] = info;
                }

                info.FailCount++;
                info.LastFailTick = Find.TickManager.TicksGame;
                
                // Add attempted weapon IDs to the failed list
                if (attemptedWeaponIds != null)
                {
                    foreach (var id in attemptedWeaponIds)
                    {
                        info.FailedWeaponIds.Add(id);
                    }
                }

                // Calculate exponential backoff
                int backoffTicks = GetBackoffTicks(info.FailCount);
                info.NextAllowedSearchTick = Find.TickManager.TicksGame + backoffTicks;

                if (AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.LogPawn(pawn, $"[SimpleSidearms] Failed search #{info.FailCount}, next retry in {backoffTicks} ticks ({backoffTicks / 60}s)");
                }
            }
        }

        private static void ClearFailedSearch(Pawn pawn, string successfulWeaponId = null)
        {
            if (pawn == null) return;

            lock (failedSearchLock)
            {
                if (failedSearches.TryGetValue(pawn.thingIDNumber, out var info))
                {
                    if (successfulWeaponId != null)
                    {
                        // Only remove the specific weapon from failed list
                        info.FailedWeaponIds.Remove(successfulWeaponId);
                        
                        // If we still have failed weapons, don't clear the whole entry
                        if (info.FailedWeaponIds.Count > 0)
                        {
                            // Reduce fail count but don't reset completely
                            info.FailCount = Math.Max(1, info.FailCount - 1);
                            return;
                        }
                    }
                    
                    // Clear the entire entry
                    failedSearches.Remove(pawn.thingIDNumber);
                    
                    if (AutoArmMod.settings?.debugLogging == true)
                    {
                        AutoArmLogger.LogPawn(pawn, "[SimpleSidearms] Cleared failed search tracking");
                    }
                }
            }
        }

        private static int GetBackoffTicks(int failCount)
        {
            // Exponential backoff: 30s, 1m, 2m, 4m, 8m, etc. up to 10 hours
            int backoff = MIN_BACKOFF_TICKS * (1 << Math.Min(failCount - 1, 10));
            return Math.Min(backoff, MAX_BACKOFF_TICKS);
        }

        private static void CleanupOldFailedSearches()
        {
            int currentTick = Find.TickManager.TicksGame;
            
            // Only cleanup periodically
            if (currentTick - lastCleanupTick < CLEANUP_INTERVAL)
                return;
                
            lastCleanupTick = currentTick;

            lock (failedSearchLock)
            {
                var cutoffTick = currentTick - MAX_BACKOFF_TICKS;
                var toRemove = new List<int>();

                foreach (var kvp in failedSearches)
                {
                    // Remove entries older than max backoff
                    if (kvp.Value.LastFailTick < cutoffTick)
                    {
                        toRemove.Add(kvp.Key);
                    }
                    // Also remove entries for dead/destroyed pawns
                    else
                    {
                        var pawn = Find.CurrentMap?.mapPawns?.AllPawnsSpawned?.FirstOrDefault(p => p.thingIDNumber == kvp.Key);
                        if (pawn == null || pawn.Dead || pawn.Destroyed)
                        {
                            toRemove.Add(kvp.Key);
                        }
                    }
                }

                foreach (var id in toRemove)
                {
                    failedSearches.Remove(id);
                }

                if (toRemove.Count > 0 && AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.Log($"[SimpleSidearms] Cleaned up {toRemove.Count} old failed search entries");
                }
            }
        }

        private static bool HasFailedWeapon(Pawn pawn, string weaponId)
        {
            if (pawn == null || weaponId == null) return false;

            lock (failedSearchLock)
            {
                if (failedSearches.TryGetValue(pawn.thingIDNumber, out var info))
                {
                    return info.FailedWeaponIds.Contains(weaponId);
                }
            }
            return false;
        }

        public static Job TryGetSidearmUpgradeJob(Pawn pawn)
        {
            if (!IsLoaded() || pawn == null || !pawn.IsColonist)
                return null;

            // Perform periodic cleanup
            CleanupOldFailedSearches();

            // Check if on sidearm search cooldown (exponential backoff)
            if (IsOnSidearmCooldown(pawn))
            {
                return null;
            }

            // Check if temporary colonists are allowed
            if (AutoArmMod.settings?.allowTemporaryColonists != true && JobGiverHelpers.IsTemporaryColonist(pawn))
                return null;

            // Check if raids are happening and setting is enabled - should be consistent with primary weapons
            if (AutoArmMod.settings?.disableDuringRaids == true)
            {
                // Check for active raids on ANY map (not just current)
                foreach (var checkMap in Find.Maps)
                {
                    if (JobGiver_PickUpBetterWeapon.IsRaidActive(checkMap))
                    {
                        AutoArmLogger.LogPawn(pawn, $"[SIDEARM] Raid active on map {checkMap.uniqueID} and disableDuringRaids is true, skipping sidearm upgrade check");
                        return null;
                    }
                }
            }

            // Use timing helper for failed searches as secondary check
            if (TimingHelper.IsOnCooldown(pawn, TimingHelper.CooldownType.FailedUpgradeSearch))
                return null;

            if (AutoArmMod.settings?.autoEquipSidearms != true)
            {
                AutoArmLogger.Log("Sidearm auto-equip disabled in settings");
                return null;
            }

            EnsureInitialized();
            if (!_initialized)
            {
                AutoArmLogger.Log("SimpleSidearms not initialized properly");
                return null;
            }

            if (equipSecondaryJobDef == null)
            {
                AutoArmLogger.Log("Warning: EquipSecondary JobDef not found, will use vanilla Equip");
            }

            // Track attempted weapons for failed search recording - moved outside try block
            var attemptedWeaponIds = new List<string>();

            try
            {

                // Build weapon counts per def
                var weaponCounts = new Dictionary<ThingDef, int>();
                var allWeapons = new List<ThingWithComps>();

                // Count primary weapon
                if (pawn.equipment?.Primary != null)
                {
                    var def = pawn.equipment.Primary.def;
                    weaponCounts[def] = 1;
                    allWeapons.Add(pawn.equipment.Primary);
                }

                // Count inventory weapons
                foreach (var item in pawn.inventory?.innerContainer ?? Enumerable.Empty<Thing>())
                {
                    if (item is ThingWithComps weapon && weapon.def.IsWeapon)
                    {
                        var def = weapon.def;
                        weaponCounts[def] = weaponCounts.ContainsKey(def) ? weaponCounts[def] + 1 : 1;
                        allWeapons.Add(weapon);
                    }
                }

                // Removed spammy debug log for current weapon counts - this happens frequently during sidearm checks

                bool preferMelee = pawn.skills?.GetSkill(SkillDefOf.Melee)?.Level > pawn.skills?.GetSkill(SkillDefOf.Shooting)?.Level;

                // Pre-filter by outfit AND recently dropped to prevent pickup/drop loops
                var nearbyWeapons = ImprovedWeaponCacheManager.GetWeaponsNear(pawn.Map, pawn.Position, 40f)
                    .Where(w => w != null &&
                               !w.IsForbidden(pawn) &&
                               !DroppedItemTracker.IsRecentlyDropped(w) &&
                               !DroppedItemTracker.WasDroppedFromPrimaryUpgrade(w) && // Extra check for primary upgrades
                               pawn.CanReserveAndReach(w, PathEndMode.ClosestTouch, Danger.Deadly))
                    .OrderBy(w => w.Position.DistanceToSquared(pawn.Position))
                    .Take(20);

                foreach (var weapon in nearbyWeapons)
                {
                    // Skip weapons that have previously failed for this pawn
                    if (HasFailedWeapon(pawn, weapon.ThingID))
                    {
                        continue;
                    }

                    // Track this weapon as attempted
                    attemptedWeaponIds.Add(weapon.ThingID);

                    // Outfit filter check FIRST - respect the player's configuration
                    var filter = pawn.outfits?.CurrentApparelPolicy?.filter;
                    if (filter != null && !filter.Allows(weapon))
                    {
                        continue;
                    }

                    // Body size check SECOND - before any other validation
                    // This prevents picking up body-size restricted weapons as sidearms
                    try
                    {
                        if (!EquipmentUtility.CanEquip(weapon, pawn))
                        {
                            // Check if it's likely a body size issue
                            if (weapon.def.defName.IndexOf("Mech", StringComparison.Ordinal) >= 0 ||
                                weapon.def.defName.IndexOf("Heavy", StringComparison.Ordinal) >= 0 ||
                                weapon.def.defName.IndexOf("Exo", StringComparison.Ordinal) >= 0 ||
                                weapon.GetStatValue(StatDefOf.Mass) > 5.0f)
                            {
                                AutoArmLogger.LogPawn(pawn, $"Skipping {weapon.Label} for sidearm - body size restriction (pawn size: {pawn.BodySize:F1})");
                            }
                            continue; // Skip this weapon entirely
                        }
                    }
                    catch (Exception ex)
                    {
                        // If we can't validate, skip the weapon
                        AutoArmLogger.LogPawn(pawn, $"Skipping {weapon.Label} - validation error: {ex.Message}");
                        continue;
                    }

                    int currentCount = weaponCounts.ContainsKey(weapon.def) ? weaponCounts[weapon.def] : 0;
                    bool isForced = ForcedWeaponHelper.IsWeaponDefForced(pawn, weapon.def);

                    // Safety check - we should never have 2+ of the same type (unless duplicates are allowed)
                    if (currentCount >= 2)
                    {
                        AutoArmLogger.LogPawn(pawn, $"WARNING: Already have {currentCount} of {weapon.def.defName}");
                        
                        // If we somehow have duplicates and upgrades are allowed, still check if we should replace the worst
                        if (AutoArmMod.settings?.allowSidearmUpgrades == true && !ALLOW_DUPLICATE_WEAPON_TYPES)
                        {
                            // Find the worst weapon of this type
                            float worstScoreOfType = float.MaxValue;
                            ThingWithComps worstOfType = null;
                            
                            if (pawn.equipment?.Primary?.def == weapon.def)
                            {
                                float score = GetWeaponScore(pawn.equipment.Primary, pawn, preferMelee);
                                if (score < worstScoreOfType)
                                {
                                    worstScoreOfType = score;
                                    worstOfType = pawn.equipment.Primary;
                                }
                            }
                            
                            foreach (var item in pawn.inventory?.innerContainer ?? Enumerable.Empty<Thing>())
                            {
                                if (item is ThingWithComps invWeapon && invWeapon.def == weapon.def)
                                {
                                    float score = GetWeaponScore(invWeapon, pawn, preferMelee);
                                    if (score < worstScoreOfType)
                                    {
                                        worstScoreOfType = score;
                                        worstOfType = invWeapon;
                                    }
                                }
                            }
                            
                            if (worstOfType != null)
                            {
                                float newScore = GetWeaponScore(weapon, pawn, preferMelee);
                                if (newScore > worstScoreOfType * 1.15f) // 15% better
                                {
                                    AutoArmLogger.LogPawn(pawn, $"Will replace duplicate {worstOfType.Label} (score: {worstScoreOfType:F2}) with {weapon.Label} (score: {newScore:F2})");
                                    
                                    // Drop the worst duplicate
                                    if (pawn.inventory?.innerContainer?.Contains(worstOfType) == true)
                                    {
                                        Thing droppedWeapon;
                                        if (pawn.inventory.innerContainer.TryDrop(worstOfType, pawn.Position, pawn.Map,
                                            ThingPlaceMode.Near, out droppedWeapon))
                                        {
                                            if (droppedWeapon != null)
                                            {
                                                DroppedItemTracker.MarkAsDropped(droppedWeapon, 3600); // 60 seconds
                                                InformOfDroppedSidearm(pawn, worstOfType);
                                                
                                                AutoArmLogger.LogPawn(pawn, $"Dropped duplicate {worstOfType.Label}");
                                                
                                                // Return job to pick up the better weapon
                                                if (equipSecondaryJobDef != null)
                                                {
                                                    var pickupJob = JobMaker.MakeJob(equipSecondaryJobDef, weapon);
                                                    ClearFailedSearch(pawn, weapon.ThingID);
                                                    return pickupJob;
                                                }
                                                else
                                                {
                                                    var equipJob = JobMaker.MakeJob(JobDefOf.Equip, weapon);
                                                    equipJob.count = 1;
                                                    ClearFailedSearch(pawn, weapon.ThingID);
                                                    return equipJob;
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        continue;
                    }

                    // Skip forced weapon types - unless it's a same-type upgrade allowed by settings
                    if (isForced)
                    {
                        // Check if we're allowed to upgrade forced weapons
                        if (AutoArmMod.settings?.allowForcedWeaponUpgrades != true)
                        {
                            int tick = Find.TickManager.TicksGame;
                            if (!lastForcedWeaponLogTick.TryGetValue(pawn, out int lastLog) ||
                                tick - lastLog > FORCED_WEAPON_LOG_COOLDOWN)
                            {
                                AutoArmLogger.LogPawn(pawn, $"Skipping forced weapon type: {weapon.def.defName}");
                                lastForcedWeaponLogTick[pawn] = tick;
                            }
                            continue;
                        }
                        else if (currentCount == 0)
                        {
                            // If we have 0 of this forced type, we can't pick up a new one even with upgrades enabled
                            // (forced weapons should already exist in inventory)
                            AutoArmLogger.LogPawn(pawn, $"Not picking up new forced weapon type: {weapon.def.defName}");
                            continue;
                        }
                        // If currentCount == 1 and upgrades are allowed, we'll check for upgrades below
                    }

                    // Check if pawn already has this weapon type as primary
                    // Only skip if it's NOT a remembered sidearm (i.e., it's a true primary weapon)
                    if (pawn.equipment?.Primary?.def == weapon.def && !IsRememberedSidearm(pawn, pawn.equipment.Primary))
                    {
                        // Don't pick up worse versions of our true primary weapon
                        float primaryScore = GetWeaponScore(pawn.equipment.Primary, pawn, preferMelee);
                        float weaponScore = GetWeaponScore(weapon, pawn, preferMelee);

                        if (weaponScore <= primaryScore)
                        {
                            // Throttle "worse than equipped primary" messages
                            string worseWeaponKey = $"{pawn.ThingID}_{weapon.def.defName}_primary";
                            int currentTick = Find.TickManager.TicksGame;

                            if (!lastWorseWeaponLogTick.TryGetValue(worseWeaponKey, out int lastLogTick) ||
                                currentTick - lastLogTick > WORSE_WEAPON_LOG_COOLDOWN)
                            {
                                AutoArmLogger.LogPawn(pawn, $"Skipping {weapon.Label} - worse than equipped primary {pawn.equipment.Primary.Label}");
                                lastWorseWeaponLogTick[worseWeaponKey] = currentTick;
                            }
                            continue;
                        }
                    }

                    if (currentCount == 0)
                    {
                        // No weapon of this type - check if we can pick it up
                        string reason;
                        if (!CanPickupSidearmInstance(weapon, pawn, out reason))
                        {
                            // Check if we should replace worst weapon instead
                            if (AutoArmMod.settings?.allowSidearmUpgrades == true &&
                                reason != null &&
                                (reason.IndexOf("heavy", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 reason.IndexOf("weight", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 reason.IndexOf("mass", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 reason.IndexOf("slots full", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 reason.IndexOf("slot", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 reason.IndexOf("space", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 reason.IndexOf("capacity", StringComparison.OrdinalIgnoreCase) >= 0))
                            {
                                // Check if this weapon is better than our worst weapon
                                float newScore = GetWeaponScore(weapon, pawn, preferMelee);
                                float worstScore;
                                var worstWeapon = GetWorstWeapon(pawn, out worstScore, excludeForced: true);

                                if (worstWeapon != null && newScore > worstScore * 1.15f) // 15% better
                                {
                                    // CRITICAL FIX: Ensure we're not creating duplicates
                                    if (!ALLOW_DUPLICATE_WEAPON_TYPES && worstWeapon.def == weapon.def)
                                    {
                                        AutoArmLogger.LogPawn(pawn, $"Not replacing {worstWeapon.Label} - same weapon type");
                                        continue;
                                    }

                                    // CRITICAL FIX: Actually validate the replacement is possible
                                    // Temporarily remove the worst weapon to check if new weapon would be allowed
                                    bool worstIsInInventory = pawn.inventory?.innerContainer?.Contains(worstWeapon) == true;
                                    bool worstIsPrimary = pawn.equipment?.Primary == worstWeapon;
                                    
                                    // Simulate removing the worst weapon
                                    float removedMass = worstWeapon.GetStatValue(StatDefOf.Mass);
                                    
                                    // HACK: We can't easily simulate the removal, so we check if the new weapon
                                    // is lighter or the same weight as the worst weapon
                                    float newWeaponMass = weapon.GetStatValue(StatDefOf.Mass);
                                    
                                    if (newWeaponMass > removedMass && reason.IndexOf("weight", StringComparison.OrdinalIgnoreCase) >= 0)
                                    {
                                        AutoArmLogger.LogPawn(pawn, $"Not replacing {worstWeapon.Label} - new weapon is heavier ({newWeaponMass:F1}kg vs {removedMass:F1}kg)");
                                        continue;
                                    }

                                    // Additional validation
                                    try
                                    {
                                        if (!EquipmentUtility.CanEquip(weapon, pawn))
                                        {
                                            AutoArmLogger.LogPawn(pawn, $"Not replacing {worstWeapon.Label} - new weapon has equipment restrictions");
                                            continue;
                                        }
                                    }
                                    catch
                                    {
                                        AutoArmLogger.LogPawn(pawn, $"Not replacing {worstWeapon.Label} - couldn't validate equipment compatibility");
                                        continue;
                                    }

                                    AutoArmLogger.LogPawn(pawn, $"Will replace worst weapon {worstWeapon.Label} (score: {worstScore:F2}) with {weapon.Label} (score: {newScore:F2})");

                                    // CRITICAL FIX: Ensure proper job sequencing
                                    if (worstIsPrimary)
                                    {
                                        // For primary weapons, mark for drop prevention
                                        DroppedItemTracker.MarkPendingDrop(worstWeapon);
                                        InformOfDroppedSidearm(pawn, worstWeapon);
                                        
                                        // Use standard equip job - it will drop current and equip new
                                        var equipJob = JobMaker.MakeJob(JobDefOf.Equip, weapon);
                                        equipJob.count = 1;
                                        
                                        AutoArmLogger.LogPawn(pawn, $"Replacing primary weapon directly");
                                        ClearFailedSearch(pawn, weapon.ThingID);
                                        return equipJob;
                                    }
                                    else if (worstIsInInventory)
                                    {
                                        // CRITICAL FIX: For inventory replacements, we need a different approach
                                        // First drop the worst weapon, then pick up the new one
                                        
                                        // Drop the worst weapon immediately
                                        Thing droppedWeapon;
                                        if (pawn.inventory.innerContainer.TryDrop(worstWeapon, pawn.Position, pawn.Map,
                                            ThingPlaceMode.Near, out droppedWeapon))
                                        {
                                            if (droppedWeapon != null)
                                            {
                                                // Mark as dropped with long cooldown
                                                DroppedItemTracker.MarkAsDropped(droppedWeapon, 3600); // 60 seconds
                                                InformOfDroppedSidearm(pawn, worstWeapon);
                                                
                                                AutoArmLogger.LogPawn(pawn, $"Dropped worst inventory weapon {worstWeapon.Label}");
                                                
                                                // Now create job to pick up the new weapon
                                                if (equipSecondaryJobDef != null)
                                                {
                                                    var pickupJob = JobMaker.MakeJob(equipSecondaryJobDef, weapon);
                                                    AutoArmLogger.LogPawn(pawn, $"Created job to pick up replacement weapon {weapon.Label}");
                                                    ClearFailedSearch(pawn, weapon.ThingID);
                                                    return pickupJob;
                                                    }
                                                    else
                                                    {
                                                    // Fallback to regular equip
                                                    var equipJob = JobMaker.MakeJob(JobDefOf.Equip, weapon);
                                                    equipJob.count = 1;
                                                    AutoArmLogger.LogPawn(pawn, $"Created fallback equip job for replacement weapon {weapon.Label}");
                                                    ClearFailedSearch(pawn, weapon.ThingID);
                                                        return equipJob;
                                                        }
                                                    }
                                                    else
                                                    {
                                                        AutoArmLogger.LogPawn(pawn, $"ERROR: Failed to drop worst weapon {worstWeapon.Label}");
                                        }
                                        }
                                        else
                                        {
                                            AutoArmLogger.LogPawn(pawn, $"ERROR: Could not drop worst weapon {worstWeapon.Label} from inventory");
                                        }
                                    }
                                    else
                                    {
                                        AutoArmLogger.LogPawn(pawn, $"ERROR: Worst weapon {worstWeapon.Label} not found in equipment or inventory");
                                    }
                                }
                                else
                                {
                                    // Worst weapon doesn't meet the 15% threshold for replacement
                                    AutoArmLogger.LogPawn(pawn, $"Weapon {weapon.Label} (score: {newScore:F2}) not significantly better than worst {worstWeapon?.Label} (score: {worstScore:F2})");
                                }
                            }
                            continue;
                        }

                        AutoArmLogger.LogWeapon(pawn, weapon, "Picking up new sidearm type");

                        if (equipSecondaryJobDef == null)
                        {
                            Log.Error("[AutoArm] EquipSecondary JobDef not found - Simple Sidearms may not be properly initialized");
                            return null;
                        }

                        ClearFailedSearch(pawn, weapon.ThingID);
                        return JobMaker.MakeJob(equipSecondaryJobDef, weapon);
                    }
                    else if (currentCount == 1 && AutoArmMod.settings?.allowSidearmUpgrades == true)
                    {
                        // CRITICAL: Check if this weapon type is forced BEFORE doing any upgrade logic
                        // This prevents forced sidearms from being upgraded
                        if (isForced && AutoArmMod.settings?.allowForcedWeaponUpgrades != true)
                        {
                            AutoArmLogger.LogPawn(pawn, $"Not checking upgrades for {weapon.def.defName} - weapon type is forced and upgrades disabled");
                            continue;
                        }

                        // Have exactly 1 of this type - find the existing weapon and check if new one is better
                        ThingWithComps existingWeapon = null;
                        float existingScore = 0f;

                        // Check primary weapon
                        if (pawn.equipment?.Primary?.def == weapon.def)
                        {
                            existingWeapon = pawn.equipment.Primary;
                            existingScore = GetWeaponScore(existingWeapon, pawn, preferMelee);
                        }
                        // Check inventory
                        else
                        {
                            foreach (var item in pawn.inventory?.innerContainer ?? Enumerable.Empty<Thing>())
                            {
                                if (item is ThingWithComps invWeapon && invWeapon.def == weapon.def)
                                {
                                    existingWeapon = invWeapon;
                                    existingScore = GetWeaponScore(invWeapon, pawn, preferMelee);
                                    break; // We know there's only 1
                                }
                            }
                        }

                        if (existingWeapon == null)
                        {
                            AutoArmLogger.LogPawn(pawn, $"ERROR: Count says 1 but couldn't find {weapon.def.defName}");
                            continue;
                        }

                        // Double-check if the specific existing weapon is forced - extra safety
                        if (ForcedWeaponHelper.IsForced(pawn, existingWeapon) && AutoArmMod.settings?.allowForcedWeaponUpgrades != true)
                        {
                            AutoArmLogger.LogPawn(pawn, $"Not upgrading {existingWeapon.Label} - specific weapon is forced and upgrades disabled");
                            continue;
                        }

                        float newScore = GetWeaponScore(weapon, pawn, preferMelee);

                        // Only upgrade if significantly better (15% threshold)
                        if (newScore <= existingScore * 1.15f)
                        {
                            // Throttle "worse than equipped" messages
                            string worseWeaponKey = $"{pawn.ThingID}_{weapon.def.defName}";
                            int currentTick = Find.TickManager.TicksGame;

                            if (!lastWorseWeaponLogTick.TryGetValue(worseWeaponKey, out int lastLogTick) ||
                                currentTick - lastLogTick > WORSE_WEAPON_LOG_COOLDOWN)
                            {
                                AutoArmLogger.LogPawn(pawn, $"Skipping {weapon.Label} - not significantly better than equipped {existingWeapon.Label} ({newScore:F2} vs {existingScore:F2})");
                                lastWorseWeaponLogTick[worseWeaponKey] = currentTick;
                            }
                            continue;
                        }

                        // Check cooldowns
                        string upgradeKey = $"{pawn.ThingID}_{existingWeapon.ThingID}_{weapon.ThingID}";
                        if (recentlyCheckedUpgrades.TryGetValue(upgradeKey, out int lastCheckTick))
                        {
                            if (Find.TickManager.TicksGame - lastCheckTick < UPGRADE_CHECK_COOLDOWN)
                            {
                                continue;
                            }
                        }
                        recentlyCheckedUpgrades[upgradeKey] = Find.TickManager.TicksGame;

                        // Check if SS allows this upgrade
                        string upgradeReason;
                        bool canPickup = CanPickupSidearmInstance(weapon, pawn, out upgradeReason);

                        // If SimpleSidearms rejects this weapon, we must respect that
                        if (!canPickup)
                        {
                            // Log why it was rejected for debugging
                            if (upgradeReason != null)
                            {
                                AutoArmLogger.LogPawn(pawn, $"SimpleSidearms rejected upgrade to {weapon.Label}: {upgradeReason}");
                            }
                            continue;
                        }

                        AutoArmLogger.LogPawn(pawn, $"Found same-type upgrade: {existingWeapon.Label} (score: {existingScore:F2}) -> {weapon.Label} (score: {newScore:F2})");

                        // Check if this is a primary weapon upgrade
                        if (pawn.equipment?.Primary == existingWeapon)
                        {
                            // Primary weapon upgrade - use standard equip job
                            AutoArmLogger.LogPawn(pawn, "Upgrading primary weapon through sidearm logic");

                            // Mark the old weapon to prevent SimpleSidearms from saving it
                            DroppedItemTracker.MarkPendingSameTypeUpgrade(existingWeapon);

                            // Use standard equip job which will handle the swap
                            ClearFailedSearch(pawn, weapon.ThingID);
                            return JobMaker.MakeJob(JobDefOf.Equip, weapon);
                        }
                        else
                        {
                            // Inventory weapon upgrade - use swap method
                            AutoArmLogger.LogPawn(pawn, $"Will upgrade sidearm using swap method");

                            pendingSidearmUpgrades[pawn] = new SidearmUpgradeInfo
                            {
                                oldWeapon = existingWeapon,
                                newWeapon = weapon,
                                originalPrimary = pawn.equipment?.Primary,
                                isTemporarySwap = true,
                                swapStartTick = Find.TickManager.TicksGame
                            };

                            if (TrySwapSidearmToPrimary(pawn, existingWeapon))
                            {
                                var equipJob = JobMaker.MakeJob(JobDefOf.Equip, weapon);
                                equipJob.count = 1;

                                AutoArmLogger.LogPawn(pawn, "Created swap-based upgrade job");
                                ClearFailedSearch(pawn, weapon.ThingID);
                                return equipJob;
                            }
                            else
                            {
                                pendingSidearmUpgrades.Remove(pawn);
                                Log.Warning($"[AutoArm] Failed to swap sidearm for upgrade");
                            }
                        }
                    }
                }

                // Track failed search with exponential backoff
                if (attemptedWeaponIds.Count > 0)
                {
                    RecordFailedSearch(pawn, attemptedWeaponIds);
                }
                else
                {
                    // No weapons were even attempted - still record failed search but without specific weapon IDs
                    RecordFailedSearch(pawn, null);
                }
                
                // Also use old timing helper as secondary tracking
                TimingHelper.SetCooldown(pawn, TimingHelper.CooldownType.FailedUpgradeSearch);
                return null;
            }
            catch (Exception e)
            {
                AutoArmLogger.Log($"ERROR in sidearm check: {e.Message}\n{e.StackTrace}");
                
                // Record failed search on exception too
                RecordFailedSearch(pawn, attemptedWeaponIds.Count > 0 ? attemptedWeaponIds : null);
                return null;
            }
        }

        public static void CleanupOldTrackingData()
        {
            // Clean up dead pawns
            var deadPawns = pendingSidearmUpgrades.Keys.Where(p => p.Destroyed || p.Dead).ToList();
            foreach (var pawn in deadPawns)
            {
                pendingSidearmUpgrades.Remove(pawn);
                pawnsWithTemporarySidearmEquipped.Remove(pawn);
                lastForcedWeaponLogTick.Remove(pawn);
            }

            // Clean up old upgrade checks
            var oldUpgradeChecks = recentlyCheckedUpgrades
                .Where(kvp => Find.TickManager.TicksGame - kvp.Value > UPGRADE_CHECK_COOLDOWN * 2)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in oldUpgradeChecks)
            {
                recentlyCheckedUpgrades.Remove(key);
            }

            // Clean up stuck pending upgrades
            var stuckSwapPawns = pawnsWithTemporarySidearmEquipped
                .Where(p => p.DestroyedOrNull() || p.Dead ||
                           !pendingSidearmUpgrades.ContainsKey(p))
                .ToList();
            foreach (var pawn in stuckSwapPawns)
            {
                pawnsWithTemporarySidearmEquipped.Remove(pawn);
            }

            // Clean up old forced weapon log entries
            var oldForcedLogs = lastForcedWeaponLogTick
                .Where(kvp => Find.TickManager.TicksGame - kvp.Value > FORCED_WEAPON_LOG_COOLDOWN * 2)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var pawn in oldForcedLogs)
            {
                lastForcedWeaponLogTick.Remove(pawn);
            }

            // Clean up old worse weapon log entries
            var oldWorseWeaponLogs = lastWorseWeaponLogTick
                .Where(kvp => Find.TickManager.TicksGame - kvp.Value > WORSE_WEAPON_LOG_COOLDOWN * 2)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in oldWorseWeaponLogs)
            {
                lastWorseWeaponLogTick.Remove(key);
            }

            // Clean up old validation summaries
            var oldValidationSummaries = lastValidationSummaryTick
                .Where(kvp => Find.TickManager.TicksGame - kvp.Value > VALIDATION_SUMMARY_INTERVAL * 2)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in oldValidationSummaries)
            {
                lastValidationSummaryTick.Remove(key);
            }

            // Clean up old validation counts
            if (validationMessageCount.Count > 1000) // Prevent unbounded growth
            {
                validationMessageCount.Clear();
            }

            // Clean up weapon score cache
            var oldScoreCache = weaponScoreCacheTick
                .Where(kvp => Find.TickManager.TicksGame - kvp.Value > WEAPON_SCORE_CACHE_LIFETIME * 2)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in oldScoreCache)
            {
                weaponScoreCache.Remove(key);
                weaponScoreCacheTick.Remove(key);
            }

            // DroppedItemTracker handles its own cleanup via CleanupHelper
        }

        public static void CleanupAfterLoad()
        {
            if (!IsLoaded())
                return;

            // Clear all temporary upgrade state
            pendingSidearmUpgrades.Clear();
            pawnsWithTemporarySidearmEquipped.Clear();
            recentlyCheckedUpgrades.Clear();

            lastForcedWeaponLogTick.Clear();
            weaponScoreCache.Clear();
            weaponScoreCacheTick.Clear();
            lastWorseWeaponLogTick.Clear();
            validationMessageCount.Clear();
            lastValidationSummaryTick.Clear();
            
            // Clear failed search tracking
            lock (failedSearchLock)
            {
                failedSearches.Clear();
            }

            AutoArmLogger.Log("Cleared sidearm upgrade state after load");
        }
        
        /// <summary>
        /// Get failed search statistics for debugging
        /// </summary>
        public static string GetFailedSearchStats()
        {
            if (!IsLoaded())
                return "SimpleSidearms not loaded";
                
            lock (failedSearchLock)
            {
                if (failedSearches.Count == 0)
                    return "No failed searches tracked";
                    
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"Failed search tracking ({failedSearches.Count} pawns):");
                
                foreach (var kvp in failedSearches)
                {
                    var info = kvp.Value;
                    var pawn = Find.CurrentMap?.mapPawns?.AllPawnsSpawned?.FirstOrDefault(p => p.thingIDNumber == kvp.Key);
                    var pawnName = pawn?.LabelShort ?? $"Unknown({kvp.Key})";
                    
                    var timeUntilRetry = info.NextAllowedSearchTick - Find.TickManager.TicksGame;
                    sb.AppendLine($"  {pawnName}: {info.FailCount} failures, {info.FailedWeaponIds.Count} failed weapons, retry in {timeUntilRetry} ticks ({timeUntilRetry / 60}s)");
                }
                
                return sb.ToString();
            }
        }

        /// <summary>
        /// Clear all failed search tracking for SimpleSidearms
        /// </summary>
        public static void ClearFailedSearchTracking()
        {
            if (!IsLoaded())
                return;
                
            // Clear upgrade tracking
            recentlyCheckedUpgrades.Clear();
            pendingSidearmUpgrades.Clear();
            pawnsWithTemporarySidearmEquipped.Clear();
            
            // Clear validation tracking
            validationMessageCount.Clear();
            lastValidationSummaryTick.Clear();
            
            // Clear forced weapon tracking
            lastForcedWeaponLogTick.Clear();
            lastWorseWeaponLogTick.Clear();
            
            // Clear weapon score cache
            weaponScoreCache.Clear();
            weaponScoreCacheTick.Clear();
            
            // Clear exponential backoff tracking
            lock (failedSearchLock)
            {
                failedSearches.Clear();
            }
            
            AutoArmLogger.Log("Cleared SimpleSidearms failed search tracking");
        }

        public static bool HasPendingUpgrade(Pawn pawn)
        {
            return pawn != null && pendingSidearmUpgrades.ContainsKey(pawn);
        }

        public static void CancelPendingUpgrade(Pawn pawn)
        {
            if (pawn != null)
            {
                pendingSidearmUpgrades.Remove(pawn);
                pawnsWithTemporarySidearmEquipped.Remove(pawn);

                AutoArmLogger.Log($"Cancelled pending upgrade for {pawn.Name}");
            }
        }

        public static SidearmUpgradeInfo GetPendingUpgrade(Pawn pawn)
        {
            if (pawn == null || !pendingSidearmUpgrades.ContainsKey(pawn))
                return null;
            return pendingSidearmUpgrades[pawn];
        }

        private static HashSet<ThingDef> GetCurrentSidearmDefs(Pawn pawn, object comp)
        {
            var sidearmDefs = new HashSet<ThingDef>();

            if (pawn == null || comp == null)
                return sidearmDefs;

            try
            {
                System.Collections.IEnumerable rememberedList = null;

                if (rememberedWeaponsProperty != null)
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
                AutoArmLogger.Log($"WARNING: Error getting current sidearms: {e.Message}");
            }

            return sidearmDefs;
        }

        private static object GetSidearmComp(Pawn pawn)
        {
            // Try cached method first
            var method = ReflectionHelper.GetCachedMethod(REFLECTION_KEY_GETSIDEARMCOMP);
            if (method != null)
            {
                try
                {
                    var parameters = method.GetParameters();
                    if (parameters.Length == 1)
                    {
                        return method.Invoke(null, new object[] { pawn });
                    }
                    else if (parameters.Length == 2)
                    {
                        return method.Invoke(null, new object[] { pawn, true });
                    }
                }
                catch { }
            }

            // Fallback to finding component directly
            if (compSidearmMemoryType == null)
                return null;

            return pawn.AllComps?.FirstOrDefault(c => c.GetType() == compSidearmMemoryType);
        }

        public static bool ShouldSkipDangerousWeapons()
        {
            if (!IsLoaded())
                return false;

            EnsureInitialized();
            var cached = SettingsCacheHelper.GetCachedSetting<bool?>("SimpleSidearms.SkipDangerous", 600);
            if (cached != null)
                return cached.Value;

            var value = ReflectionHelper.GetCachedFieldValue<bool>(REFLECTION_KEY_SKIPDANGEROUS);
            SettingsCacheHelper.SetCachedSetting("SimpleSidearms.SkipDangerous", value);
            return value;
        }

        public static bool ShouldSkipEMPWeapons()
        {
            if (!IsLoaded())
                return false;

            EnsureInitialized();
            var cached = SettingsCacheHelper.GetCachedSetting<bool?>("SimpleSidearms.SkipEMP", 600);
            if (cached != null)
                return cached.Value;

            var value = ReflectionHelper.GetCachedFieldValue<bool>(REFLECTION_KEY_SKIPEMP);
            SettingsCacheHelper.SetCachedSetting("SimpleSidearms.SkipEMP", value);
            return value;
        }

        public static bool AllowBlockedWeaponUse()
        {
            if (!IsLoaded())
                return true;

            EnsureInitialized();
            var cached = SettingsCacheHelper.GetCachedSetting<bool?>("SimpleSidearms.AllowBlocked", 600);
            if (cached != null)
                return cached.Value;

            var value = ReflectionHelper.GetCachedFieldValue<bool>(REFLECTION_KEY_ALLOWBLOCKED);
            SettingsCacheHelper.SetCachedSetting("SimpleSidearms.AllowBlocked", value);
            return value;
        }

        public static bool ShouldUpgradeMainWeapon(Pawn pawn, ThingWithComps newWeapon, float currentScore, float newScore)
        {
            return true;
        }

        public static void HandleUpgradeCompletion(Pawn pawn, ThingWithComps equippedWeapon)
        {
            if (!IsLoaded() || pawn == null || equippedWeapon == null)
                return;

            if (!pendingSidearmUpgrades.TryGetValue(pawn, out var upgradeInfo))
                return;

            if (upgradeInfo.newWeapon != equippedWeapon)
                return;

            try
            {
                if (upgradeInfo.isTemporarySwap)
                {
                    // This is a same-type upgrade, not a replacement
                    AutoArmLogger.Log($"Completing sidearm upgrade for {pawn.Name}");

                    if (upgradeInfo.oldWeapon != null && !upgradeInfo.oldWeapon.Destroyed)
                    {
                        // Mark as recently dropped
                        DroppedItemTracker.MarkAsDropped(upgradeInfo.oldWeapon);
                    }

                    if (pawn.equipment?.Primary == equippedWeapon)
                    {
                        pawn.equipment.Remove(equippedWeapon);
                        if (!pawn.inventory.innerContainer.TryAdd(equippedWeapon))
                        {
                            GenThing.TryDropAndSetForbidden(equippedWeapon, pawn.Position, pawn.Map,
                                ThingPlaceMode.Near, out _, false);
                            Log.Warning($"[AutoArm] Failed to add upgraded weapon to inventory, dropped it");
                        }
                        else
                        {
                            AutoArmLogger.Log($"Moved upgraded weapon {equippedWeapon.Label} to inventory");
                        }
                    }

                    // Restore original primary for upgrades
                    if (upgradeInfo.originalPrimary != null && !upgradeInfo.originalPrimary.Destroyed)
                    {
                        if (pawn.inventory?.innerContainer?.Contains(upgradeInfo.originalPrimary) == true)
                        {
                            pawn.inventory.innerContainer.Remove(upgradeInfo.originalPrimary);
                            pawn.equipment.AddEquipment(upgradeInfo.originalPrimary);

                            AutoArmLogger.Log($"Restored original primary weapon {upgradeInfo.originalPrimary.Label}");
                        }
                    }

                    // Use NotificationHelper for upgrades
                    NotificationHelper.NotifySidearmEquipped(pawn, equippedWeapon, upgradeInfo.oldWeapon);
                }
                else
                {
                    // This is a replacement operation - different handling
                    AutoArmLogger.Log($"Completing sidearm replacement for {pawn.Name}");

                    if (upgradeInfo.oldWeapon != null && !upgradeInfo.oldWeapon.Destroyed)
                    {
                        // Mark as recently dropped with longer cooldown
                        DroppedItemTracker.MarkAsDropped(upgradeInfo.oldWeapon, 1200); // 20 seconds
                    }

                    // For replacements, we don't restore the original primary
                    // The new weapon stays as primary or goes to inventory based on SimpleSidearms' preference

                    // Use NotificationHelper for replacements
                    // Note: Using SendNotification directly since there's no specific replacement method
                    NotificationHelper.SendNotification("AutoArm_ReplacedWeapon", pawn,
                        pawn.LabelShort.CapitalizeFirst().Named("PAWN"),
                        (upgradeInfo.oldWeapon?.Label ?? "old weapon").Named("OLD"),
                        (equippedWeapon.Label ?? "new weapon").Named("NEW"));
                }
            }
            catch (Exception e)
            {
                Log.Error($"[AutoArm] Error completing sidearm upgrade/replacement: {e.Message}");
            }
            finally
            {
                pendingSidearmUpgrades.Remove(pawn);
                pawnsWithTemporarySidearmEquipped.Remove(pawn);

                // Set a cooldown after upgrade to prevent immediate weapon switching
                TimingHelper.SetCooldown(pawn, TimingHelper.CooldownType.WeaponSearch);
                AutoArmLogger.Log($"Set post-upgrade cooldown for {pawn.Name}");
            }
        }

        // Methods that are called from elsewhere in the mod
        // Get the actual slot limit from SimpleSidearms settings
        private static int GetSimpleSidearmsSlotLimit(bool forRanged = false, bool forMelee = false, bool total = false)
        {
            if (!IsLoaded())
                return int.MaxValue;

            // Check cache first
            string cacheKey = $"SimpleSidearms.SlotLimit_{total}_{forRanged}_{forMelee}";
            var cached = SettingsCacheHelper.GetCachedSetting<int?>(cacheKey, 1200); // Cache for 20 seconds
            if (cached.HasValue)
                return cached.Value;

            try
            {
                var settingsType = GenTypes.AllTypes.FirstOrDefault(t =>
                    t.Name == "SimpleSidearms_Settings" ||
                    (t.Name == "Settings" && t.Namespace != null && t.Namespace.Contains("SimpleSidearms")));

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
                                // Check if using separate modes
                                var separateModesField = settingsType.GetField("SeparateModes", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                                bool separateModes = false;
                                if (separateModesField != null)
                                {
                                    var value = separateModesField.GetValue(settings);
                                    if (value is bool)
                                        separateModes = (bool)value;
                                }

                                string fieldName;
                                if (!separateModes)
                                {
                                    fieldName = "LimitModeAmount_Slots";
                                }
                                else if (total)
                                {
                                    fieldName = "LimitModeAmountTotal_Slots";
                                }
                                else if (forRanged)
                                {
                                    fieldName = "LimitModeAmountRanged_Slots";
                                }
                                else if (forMelee)
                                {
                                    fieldName = "LimitModeAmountMelee_Slots";
                                }
                                else
                                {
                                    fieldName = "LimitModeAmount_Slots";
                                }

                                var limitField = settingsType.GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                                if (limitField != null)
                                {
                                    var value = limitField.GetValue(settings);
                                    if (value is int)
                                    {
                                        int limit = (int)value;
                                        SettingsCacheHelper.SetCachedSetting(cacheKey, limit);
                                        return limit;
                                    }
                                    else if (value is float)
                                    {
                                        int limit = (int)(float)value;
                                        SettingsCacheHelper.SetCachedSetting(cacheKey, limit);
                                        return limit;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                AutoArmLogger.Log($"WARNING: Failed to get SimpleSidearms slot limit: {e.Message}");
            }

            // Default to a reasonable limit if we can't read settings
            int defaultLimit = 3; // Basic preset default
            SettingsCacheHelper.SetCachedSetting(cacheKey, defaultLimit);
            return defaultLimit;
        }

        // Get the actual weight limit from SimpleSidearms settings
        private static float GetSimpleSidearmsWeightLimit(bool forRanged = false, bool forMelee = false)
        {
            if (!IsLoaded())
                return float.MaxValue;

            // Check cache first
            string cacheKey = $"SimpleSidearms.WeightLimit_{forRanged}_{forMelee}";
            var cached = SettingsCacheHelper.GetCachedSetting<float?>(cacheKey, 1200); // Cache for 20 seconds
            if (cached.HasValue)
                return cached.Value;

            try
            {
                var settingsType = GenTypes.AllTypes.FirstOrDefault(t =>
                    t.Name == "SimpleSidearms_Settings" ||
                    (t.Name == "Settings" && t.Namespace != null && t.Namespace.Contains("SimpleSidearms")));

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
                                // Check if using separate modes
                                var separateModesField = settingsType.GetField("SeparateModes", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                                bool separateModes = false;
                                if (separateModesField != null)
                                {
                                    var value = separateModesField.GetValue(settings);
                                    if (value is bool)
                                        separateModes = (bool)value;
                                }

                                string fieldName;
                                if (!separateModes)
                                {
                                    fieldName = "LimitModeSingle_AbsoluteMass";
                                }
                                else if (forRanged)
                                {
                                    fieldName = "LimitModeSingleRanged_AbsoluteMass";
                                }
                                else if (forMelee)
                                {
                                    fieldName = "LimitModeSingleMelee_AbsoluteMass";
                                }
                                else
                                {
                                    fieldName = "LimitModeSingle_AbsoluteMass";
                                }

                                var limitField = settingsType.GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                                if (limitField != null)
                                {
                                    var value = limitField.GetValue(settings);
                                    if (value is float)
                                    {
                                        float limit = (float)value;
                                        SettingsCacheHelper.SetCachedSetting(cacheKey, limit);
                                        return limit;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                AutoArmLogger.Log($"WARNING: Failed to get SimpleSidearms weight limit: {e.Message}");
            }

            // Default to a reasonable limit if we can't read settings
            float defaultLimit = 2.7f; // Basic preset default
            SettingsCacheHelper.SetCachedSetting(cacheKey, defaultLimit);
            return defaultLimit;
        }

        public static void LogSimpleSidearmsSettings()
        {
            if (!IsLoaded())
                return;

            try
            {
                var settingsType = GenTypes.AllTypes.FirstOrDefault(t =>
                    t.Name == "SimpleSidearms_Settings" ||
                    (t.Name == "Settings" && t.Namespace != null && t.Namespace.Contains("SimpleSidearms")));

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
                                // Try to get weight limit fields
                                string[] weightFields = {
                                    "LimitModeSingle_AbsoluteMass",
                                    "LimitModeSingleRanged_AbsoluteMass",
                                    "LimitModeSingleMelee_AbsoluteMass",
                                    "LimitModeAmount_AbsoluteMass",
                                    "LimitModeAmountRanged_AbsoluteMass",
                                    "LimitModeAmountMelee_AbsoluteMass"
                                };

                                Log.Message("[AutoArm] Checking SimpleSidearms detailed settings:");

                                // Check if using separate modes
                                var separateModesField = settingsType.GetField("SeparateModes", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                                if (separateModesField != null)
                                {
                                    var separateModes = separateModesField.GetValue(settings);
                                    Log.Message($"  SeparateModes: {separateModes}");
                                }

                                // Check limit mode
                                var limitModeField = settingsType.GetField("LimitModeSingle", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance) ??
                                                   settingsType.GetField("ActiveTab", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance) ??
                                               settingsType.GetField("LimitMode", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                                if (limitModeField != null)
                                {
                                    var limitMode = limitModeField.GetValue(settings);
                                    Log.Message($"  Limit mode: {limitMode}");
                                }

                                // Check all fields
                                var allFields = settingsType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                                foreach (var field in allFields)
                                {
                                    if (field.Name.IndexOf("Limit", StringComparison.Ordinal) >= 0 || field.Name.IndexOf("Slot", StringComparison.Ordinal) >= 0 || field.Name.IndexOf("Mode", StringComparison.Ordinal) >= 0)
                                    {
                                        try
                                        {
                                            var value = field.GetValue(settings);
                                            Log.Message($"  {field.Name}: {value}");
                                        }
                                        catch { }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Log.Warning($"[AutoArm] Failed to log SimpleSidearms settings: {e.Message}");
            }
        }

        public static int GetMaxSidearmsForPawn(Pawn pawn)
        {
            // Use our cached method for better performance and consistency
            return GetSimpleSidearmsSlotLimit(false, false, true);
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

            if (pawn.inventory?.innerContainer == null ||
                !pawn.inventory.innerContainer.Any(t => t.def.IsWeapon))
                return true;

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
                return !pawn.inventory.innerContainer.Any(t => t.def.IsWeapon);
            }
        }
    }

    // ==============================================
    // HARMONY PATCHES FROM SimpleSidearmsUpgradePatch.cs
    // ==============================================

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

    // Patch SimpleSidearms weapon swap methods dynamically
    public static class SimpleSidearms_WeaponSwap_Patches
    {
        private static bool patchesApplied = false;

        public static void ApplyPatches(Harmony harmony)
        {
            if (!SimpleSidearmsCompat.IsLoaded() || patchesApplied)
                return;

            try
            {
                // Find the WeaponAssingment type
                var weaponAssignmentType = GenTypes.AllTypes.FirstOrDefault(t =>
                    t.FullName == "PeteTimesSix.SimpleSidearms.Utilities.WeaponAssingment");

                if (weaponAssignmentType != null)
                {
                    // Patch equipSpecificWeapon - the core method
                    var equipSpecificWeaponMethod = weaponAssignmentType.GetMethod("equipSpecificWeapon",
                        BindingFlags.Public | BindingFlags.Static);
                    if (equipSpecificWeaponMethod != null)
                    {
                        harmony.Patch(equipSpecificWeaponMethod,
                            prefix: new HarmonyMethod(typeof(SimpleSidearms_WeaponSwap_Patches), nameof(EquipSpecificWeapon_Prefix)),
                            postfix: new HarmonyMethod(typeof(SimpleSidearms_WeaponSwap_Patches), nameof(EquipSpecificWeapon_Postfix)));
                        AutoArmLogger.Log("Patched SimpleSidearms.equipSpecificWeapon");
                    }

                    // Patch equipSpecificWeaponFromInventory
                    var equipFromInventoryMethod = weaponAssignmentType.GetMethod("equipSpecificWeaponFromInventory",
                        BindingFlags.Public | BindingFlags.Static, null,
                        new Type[] { typeof(Pawn), typeof(ThingWithComps), typeof(bool), typeof(bool) }, null);
                    if (equipFromInventoryMethod != null)
                    {
                        harmony.Patch(equipFromInventoryMethod,
                            prefix: new HarmonyMethod(typeof(SimpleSidearms_WeaponSwap_Patches), nameof(EquipFromInventory_Prefix)));
                        AutoArmLogger.Log("Patched SimpleSidearms.equipSpecificWeaponFromInventory");
                    }

                    patchesApplied = true;
                }
                else
                {
                    AutoArmLogger.Log("WARNING: Could not find SimpleSidearms WeaponAssingment type");
                }
            }
            catch (Exception e)
            {
                Log.Error($"[AutoArm] Failed to patch SimpleSidearms weapon swap methods: {e}");
            }
        }

        public static void EquipSpecificWeapon_Prefix(Pawn pawn, ThingWithComps weapon, bool dropCurrent, bool intentionalDrop)
        {
            if (pawn == null || !pawn.IsColonist)
                return;

            // Mark that a SimpleSidearms swap is starting
            DroppedItemTracker.MarkSimpleSidearmsSwapInProgress(pawn);

            // Store the current forced weapon state before the swap
            if (pawn.equipment?.Primary != null && ForcedWeaponHelper.IsForced(pawn, pawn.equipment.Primary))
            {
                AutoArmLogger.LogWeapon(pawn, pawn.equipment.Primary, "SimpleSidearms swap starting - current weapon is forced");
            }
        }

        public static void EquipSpecificWeapon_Postfix(Pawn pawn, ThingWithComps weapon, bool dropCurrent, bool intentionalDrop)
        {
            if (pawn == null || !pawn.IsColonist)
                return;

            // Clear the swap flag after a short delay to ensure drop event is processed
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                DroppedItemTracker.ClearSimpleSidearmsSwapInProgress(pawn);

                // ALWAYS re-apply forced status to any forced weapon types
                // This ensures forced status is maintained during ANY weapon operation

                // Get all currently forced weapon defs for this pawn
                var forcedDefs = ForcedWeaponHelper.GetForcedWeaponDefs(pawn);

                if (forcedDefs.Count > 0)
                {
                    AutoArmLogger.LogPawn(pawn, $"Maintaining forced status for {forcedDefs.Count} weapon type(s) after SimpleSidearms operation");
                }

                // Check primary weapon
                if (pawn.equipment?.Primary != null)
                {
                    var primary = pawn.equipment.Primary;
                    if (forcedDefs.Contains(primary.def))
                    {
                        ForcedWeaponHelper.SetForced(pawn, primary);
                        AutoArmLogger.LogWeapon(pawn, primary, "SimpleSidearms operation completed - maintained forced status on primary");
                    }
                }

                // Check inventory weapons
                foreach (var item in pawn.inventory?.innerContainer ?? Enumerable.Empty<Thing>())
                {
                    if (item is ThingWithComps invWeapon && invWeapon.def.IsWeapon)
                    {
                        if (forcedDefs.Contains(invWeapon.def))
                        {
                            // No need to add it again, it's already in forcedDefs
                            AutoArmLogger.LogWeapon(pawn, invWeapon, "SimpleSidearms operation completed - maintained forced status on sidearm");
                        }
                    }
                }
            });
        }

        public static void EquipFromInventory_Prefix(Pawn pawn, ThingWithComps weapon, bool dropCurrent, bool intentionalDrop)
        {
            if (pawn == null || !pawn.IsColonist)
                return;

            // This method calls equipSpecificWeapon, so just mark the swap
            DroppedItemTracker.MarkSimpleSidearmsSwapInProgress(pawn);
            AutoArmLogger.LogPawn(pawn, "SimpleSidearms inventory swap starting");
        }
    }

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

                        // Only restore original primary for upgrades, not replacements
                        if (upgradeInfo.isTemporarySwap && upgradeInfo.originalPrimary != null &&
                            !upgradeInfo.originalPrimary.Destroyed &&
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

    // Patch to prevent SimpleSidearms from re-equipping weapons when outfit filter disallows them
    // [HarmonyPatch] - Removed to prevent auto-patching when SimpleSidearms is not installed
    public static class SimpleSidearms_JobGiver_RetrieveWeapon_Patch
    {
        static MethodBase TargetMethod()
        {
            // Find SimpleSidearms' JobGiver_RetrieveWeapon.TryGiveJobStatic
            var type = GenTypes.AllTypes.FirstOrDefault(t =>
                t.FullName == "SimpleSidearms.rimworld.JobGiver_RetrieveWeapon");
            return type?.GetMethod("TryGiveJobStatic",
                BindingFlags.Public | BindingFlags.Static);
        }

        [HarmonyPrefix]
        static bool Prefix(Pawn pawn, bool inCombat, ref Job __result)
        {
            // Check if outfit filter allows ANY weapons
            if (pawn?.outfits?.CurrentApparelPolicy?.filter != null)
            {
                var filter = pawn.outfits.CurrentApparelPolicy.filter;
                bool anyWeaponAllowed = WeaponThingFilterUtility.AllWeapons
                    .Any(td => filter.Allows(td));

                if (!anyWeaponAllowed)
                {
                    // No weapons allowed - prevent SimpleSidearms from creating job
                    __result = null;

                    // Only log once per pawn per minute to avoid spam
                    string logKey = $"{pawn.ThingID}_NoWeaponsAllowed";
                    if (!TimingHelper.IsOnCooldown(pawn, TimingHelper.CooldownType.ForcedWeaponLog))
                    {
                        TimingHelper.SetCooldown(pawn, TimingHelper.CooldownType.ForcedWeaponLog);
                    }

                    return false; // Skip original method
                }
            }

            return true; // Continue to original method
        }

        public static void ApplyPatch(Harmony harmony)
        {
            try
            {
                var targetMethod = TargetMethod();
                if (targetMethod != null)
                {
                    harmony.Patch(targetMethod,
                        prefix: new HarmonyMethod(typeof(SimpleSidearms_JobGiver_RetrieveWeapon_Patch), nameof(Prefix)));
                    AutoArmLogger.Log("Patched SimpleSidearms JobGiver_RetrieveWeapon");
                }
                else
                {
                    AutoArmLogger.Log("WARNING: Could not find SimpleSidearms JobGiver_RetrieveWeapon to patch");
                }
            }
            catch (Exception e)
            {
                Log.Error($"[AutoArm] Failed to patch SimpleSidearms JobGiver_RetrieveWeapon: {e}");
            }
        }
    }
}