using AutoArm.Definitions;
using AutoArm.Helpers;
using AutoArm.Jobs;
using AutoArm.Logging;
using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace AutoArm
{
    /// <summary>
    /// Simplified SimpleSidearms compatibility
    /// Uses SS's own methods for equipping sidearms while respecting all rules
    /// </summary>
    public static class SimpleSidearmsCompat
    {
        // Job def for swapping sidearms
        private static JobDef _swapSidearmJobDef;
        private static bool? _isLoaded = null;
        private static bool _initialized = false;

        /// <summary>
        /// Returns true if SimpleSidearms reflection failed during initialization
        /// </summary>
        public static bool ReflectionFailed => _reflectionFailed;

        private static bool _reflectionFailed = false;

        // Core types from SimpleSidearms
        private static Type compSidearmMemoryType;
        private static Type thingDefStuffDefPairType;
        private static Type statCalculatorType;
        private static Type weaponAssingmentType;

        // Core methods we need
        private static MethodInfo getMemoryCompForPawnMethod;

        private static MethodInfo canPickupSidearmInstanceMethod;
        private static MethodInfo equipSidearmMethod;
        private static MethodInfo getSidearmsListMethod;

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

        public static void EnsureInitialized()
        {
            if (_initialized || !IsLoaded())
                return;

            try
            {
                // Find essential type and method only
                statCalculatorType = GenTypes.AllTypes.FirstOrDefault(t =>
                    t.FullName == "PeteTimesSix.SimpleSidearms.Utilities.StatCalculator");

                if (statCalculatorType == null)
                {
                    _reflectionFailed = true;
                    return;
                }

                // Get the essential validation method
                canPickupSidearmInstanceMethod = statCalculatorType.GetMethod("CanPickupSidearmInstance",
                    new Type[] { typeof(ThingWithComps), typeof(Pawn), typeof(string).MakeByRefType() });

                if (canPickupSidearmInstanceMethod == null)
                {
                    _reflectionFailed = true;
                    return;
                }

                // Optional types - don't fail if not found
                compSidearmMemoryType = GenTypes.AllTypes.FirstOrDefault(t =>
                    t.FullName == "SimpleSidearms.rimworld.CompSidearmMemory");
                    
                thingDefStuffDefPairType = GenTypes.AllTypes.FirstOrDefault(t =>
                    t.FullName == "SimpleSidearms.rimworld.ThingDefStuffDefPair");
                    
                weaponAssingmentType = GenTypes.AllTypes.FirstOrDefault(t =>
                    t.FullName == "PeteTimesSix.SimpleSidearms.Utilities.WeaponAssingment");

                // Optional methods
                if (compSidearmMemoryType != null)
                {
                    getMemoryCompForPawnMethod = compSidearmMemoryType.GetMethod("GetMemoryCompForPawn",
                        BindingFlags.Public | BindingFlags.Static,
                        null,
                        new Type[] { typeof(Pawn), typeof(bool) },
                        null);
                        
                    // If that doesn't work, try without the bool parameter
                    if (getMemoryCompForPawnMethod == null)
                    {
                        getMemoryCompForPawnMethod = compSidearmMemoryType.GetMethod("GetMemoryCompForPawn",
                            BindingFlags.Public | BindingFlags.Static,
                            null,
                            new Type[] { typeof(Pawn) },
                            null);
                    }
                    
                    getSidearmsListMethod = compSidearmMemoryType.GetMethod("GetRememberedWeapons",
                        BindingFlags.Public | BindingFlags.Instance);
                }

                if (weaponAssingmentType != null)
                {
                    equipSidearmMethod = weaponAssingmentType.GetMethod("equipSidearm",
                        BindingFlags.Public | BindingFlags.Static,
                        null,
                        new Type[] { typeof(Pawn), typeof(ThingWithComps), typeof(bool), typeof(bool) },
                        null);
                }

                _initialized = true;
            }
            catch
            {
                _reflectionFailed = true;
                _initialized = false;
            }
        }

        /// <summary>
        /// Check if SimpleSidearms allows this weapon as sidearm
        /// </summary>
        public static bool CanPickupSidearmInstance(ThingWithComps weapon, Pawn pawn, out string reason)
        {
            reason = "";
            if (!IsLoaded() || weapon == null || pawn == null)
                return true;

            EnsureInitialized();

            if (_reflectionFailed)
            {
                reason = "SimpleSidearms reflection failed";
                return false; // Block operations if reflection failed
            }

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
                // Log error but don't block equipping
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.Debug($"[{pawn.LabelShort}] SimpleSidearms CanPickupSidearmInstance error: {e.Message}");
                }
                // Don't set _reflectionFailed here - this is a runtime error, not a setup failure
                // Runtime errors shouldn't permanently disable SS integration
                reason = "SimpleSidearms validation error";
            }

            return true;
        }

        /// <summary>
        /// Check if a weapon is managed by SimpleSidearms (in the remembered list)
        /// </summary>
        public static bool IsSimpleSidearmsManaged(Pawn pawn, ThingWithComps weapon)
        {
            if (!IsLoaded() || pawn == null || weapon == null)
                return false;

            EnsureInitialized();

            if (_reflectionFailed)
                return false;

            try
            {
                // Get the memory comp for this pawn
                if (getMemoryCompForPawnMethod != null && compSidearmMemoryType != null)
                {
                    // Try to call with appropriate parameters based on method signature
                    object memoryComp = null;
                    var parameters = getMemoryCompForPawnMethod.GetParameters();
                    
                    if (parameters.Length == 2 && parameters[1].ParameterType == typeof(bool))
                    {
                        memoryComp = getMemoryCompForPawnMethod.Invoke(null, new object[] { pawn, false });
                    }
                    else if (parameters.Length == 1)
                    {
                        memoryComp = getMemoryCompForPawnMethod.Invoke(null, new object[] { pawn });
                    }
                    if (memoryComp != null && getSidearmsListMethod != null)
                    {
                        // Get the list of remembered weapons
                        // GetRememberedWeapons typically takes no parameters
                        object rememberedWeapons = null;
                        try
                        {
                            rememberedWeapons = getSidearmsListMethod.Invoke(memoryComp, new object[0]);
                        }
                        catch (TargetParameterCountException)
                        {
                            // Try with a bool parameter if no parameters didn't work
                            try
                            {
                                rememberedWeapons = getSidearmsListMethod.Invoke(memoryComp, new object[] { true });
                            }
                            catch (Exception e2)
                            {
                                AutoArmLogger.Debug($"Failed to invoke GetRememberedWeapons with parameter variations: {e2.Message}");
                                return false;
                            }
                        }
                        catch (Exception e)
                        {
                            AutoArmLogger.Debug($"Failed to invoke GetRememberedWeapons: {e.Message}");
                            return false;
                        }
                        if (rememberedWeapons != null)
                        {
                            // Check if this weapon's def/stuff combo is in the remembered list
                            foreach (var item in (System.Collections.IEnumerable)rememberedWeapons)
                            {
                                if (item != null)
                                {
                                    // The item is likely a ThingDefStuffDefPair
                                    var thingField = item.GetType().GetField("thing");
                                    var stuffField = item.GetType().GetField("stuff");

                                    if (thingField != null)
                                    {
                                        var thingDef = thingField.GetValue(item) as ThingDef;
                                        var stuffDef = stuffField?.GetValue(item) as ThingDef;

                                        if (thingDef == weapon.def && stuffDef == weapon.Stuff)
                                        {
                                            return true;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                AutoArmLogger.Debug($"Failed to check if weapon is SS managed: {e.Message}");
            }

            return false;
        }

        /// <summary>
        /// Create a job to equip a sidearm using SimpleSidearms' EquipSecondary job
        /// </summary>
        public static Job CreateEquipSidearmJob(Pawn pawn, ThingWithComps weapon)
        {
            if (!IsLoaded() || pawn == null || weapon == null || _reflectionFailed)
                return null;

            // Verify SimpleSidearms allows this
            string reason;
            if (!CanPickupSidearmInstance(weapon, pawn, out reason))
            {
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.Debug($"[{pawn.LabelShort}] SimpleSidearms rejects {weapon.Label}: {reason}");
                }
                return null;
            }

            // Use SimpleSidearms' EquipSecondary job
            var jobDef = DefDatabase<JobDef>.GetNamedSilentFail("EquipSecondary");
            if (jobDef != null)
            {
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.Debug($"[{pawn.LabelShort}] Creating EquipSecondary job for {weapon.Label}");
                }
                return JobMaker.MakeJob(jobDef, weapon);
            }

            return null;
        }

        /// <summary>
        /// Try to equip a sidearm using SimpleSidearms' internal method
        /// This is for direct equipping without creating a job
        /// </summary>
        public static bool TryEquipSidearm(Pawn pawn, ThingWithComps weapon, bool dropCurrent = true, bool intentional = false)
        {
            if (!IsLoaded() || pawn == null || weapon == null)
                return false;

            EnsureInitialized();

            if (_reflectionFailed || equipSidearmMethod == null)
                return false;

            try
            {
                bool result = (bool)equipSidearmMethod.Invoke(null, new object[] { pawn, weapon, dropCurrent, intentional });

                if (result && AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.Debug($"[{pawn.LabelShort}] Successfully equipped sidearm {weapon.Label} using SimpleSidearms");
                }

                return result;
            }
            catch (Exception e)
            {
                AutoArmLogger.Warn($"Failed to equip sidearm: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// Switch a sidearm to primary using SimpleSidearms' method
        /// </summary>
        public static bool SwitchToPrimary(Pawn pawn, ThingWithComps weapon)
        {
            if (!IsLoaded() || pawn == null || weapon == null)
                return false;

            EnsureInitialized();

            if (_reflectionFailed)
                return false;

            try
            {
                // Get the WeaponAssingment type
                if (weaponAssingmentType == null)
                {
                    AutoArmLogger.Warn("Could not find WeaponAssingment type for switching");
                    return false;
                }

                // Get the SetAsForced method to switch to primary
                var setAsForcedMethod = weaponAssingmentType.GetMethod(
                    "SetAsForced",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new Type[] { typeof(Pawn), typeof(Thing), typeof(bool) },
                    null);

                if (setAsForcedMethod == null)
                {
                    AutoArmLogger.Warn("Could not find SetAsForced method");
                    return false;
                }

                // Switch the weapon to primary (true = forced)
                setAsForcedMethod.Invoke(null, new object[] { pawn, weapon, false });

                if (AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.Debug($"[{pawn.LabelShort}] Switched {weapon.Label} to primary using SimpleSidearms");
                }

                return true;
            }
            catch (Exception e)
            {
                AutoArmLogger.Warn($"Failed to switch weapon to primary: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// Find the best sidearm to add or upgrade
        /// For upgrades: uses custom swap job to ensure old weapon ends up at new weapon location
        /// </summary>
        public static Job FindBestSidearmJob(Pawn pawn, Func<Pawn, ThingWithComps, float> getWeaponScore, int searchRadius)
        {
            if (!IsLoaded() || pawn == null || _reflectionFailed)
                return null;

            // Get current weapons separated by location
            var inventoryWeapons = new Dictionary<ThingDef, ThingWithComps>();
            ThingWithComps primaryWeapon = pawn.equipment?.Primary;

            // Add inventory weapons only
            if (pawn.inventory?.innerContainer != null)
            {
                foreach (Thing t in pawn.inventory.innerContainer)
                {
                    if (t is ThingWithComps weapon && t.def.IsWeapon)
                    {
                        // Keep the lower quality one for upgrade comparison
                        if (!inventoryWeapons.ContainsKey(weapon.def))
                        {
                            inventoryWeapons[weapon.def] = weapon;
                        }
                        else
                        {
                            float currentScore = getWeaponScore(pawn, inventoryWeapons[weapon.def]);
                            float newScore = getWeaponScore(pawn, weapon);
                            if (newScore < currentScore)
                            {
                                inventoryWeapons[weapon.def] = weapon;
                            }
                        }
                    }
                }
            }

            // Search for weapons - respect storage-only setting
            List<ThingWithComps> cachedWeapons;
            if (AutoArmMod.settings?.onlyAutoEquipFromStorage == true)
            {
                // Only look in storage zones and containers
                cachedWeapons = Caching.ImprovedWeaponCacheManager.GetStorageWeaponsNear(pawn.Map, pawn.Position, searchRadius);
                
                if (AutoArmMod.settings?.debugLogging == true && cachedWeapons.Count == 0)
                {
                    AutoArmLogger.Debug($"[{pawn.LabelShort}] No sidearm weapons found in storage within {searchRadius} units");
                }
            }
            else
            {
                // Look everywhere
                cachedWeapons = Caching.ImprovedWeaponCacheManager.GetWeaponsNear(pawn.Map, pawn.Position, searchRadius);
            }

            ThingWithComps bestWeapon = null;
            float bestScore = 0f;
            bool isUpgrade = false;
            ThingWithComps weaponToReplace = null;

            foreach (var weapon in cachedWeapons)
            {
                // Check if this is a same-type upgrade BEFORE doing SimpleSidearms slot validation
                // This allows upgrading weapons even when slots are full
                // Check both inventory weapons AND primary weapon for same-type upgrades
                bool isSameTypeUpgrade = inventoryWeapons.ContainsKey(weapon.def) || 
                                         (primaryWeapon != null && weapon.def == primaryWeapon.def);
                
                // For same-type upgrades, skip SimpleSidearms slot check
                // For new weapons, do full validation including SS slots
                if (isSameTypeUpgrade)
                {
                    // Do validation WITHOUT SimpleSidearms slot check
                    if (!ValidationHelper.CanPawnUseWeapon(pawn, weapon, out string reason, skipSimpleSidearmsCheck: true))
                    {
                        if (AutoArmMod.settings?.debugLogging == true)
                        {
                            AutoArmLogger.Debug($"[{pawn.LabelShort}] Weapon {weapon.Label} rejected: {reason}");
                        }
                        continue;
                    }
                }
                else
                {
                    // Full validation including SimpleSidearms slot check
                    if (!ValidationHelper.CanPawnUseWeapon(pawn, weapon, out string reason))
                    {
                        if (AutoArmMod.settings?.debugLogging == true)
                        {
                            AutoArmLogger.Debug($"[{pawn.LabelShort}] Weapon {weapon.Label} rejected: {reason}");
                        }
                        continue;
                    }
                }

                if (DroppedItemTracker.IsRecentlyDropped(weapon))
                    continue;

                // CHECK OUTFIT FILTER
                if (!(pawn.outfits?.CurrentApparelPolicy?.filter?.Allows(weapon) ?? true))
                {
                    if (AutoArmMod.settings?.debugLogging == true)
                    {
                        AutoArmLogger.Debug($"[{pawn.LabelShort}] Weapon {weapon.Label} rejected: Outfit filter");
                    }
                    continue;
                }

                float score = getWeaponScore(pawn, weapon);

                // Check if we already have this weapon type (in inventory or as primary)
                if (isSameTypeUpgrade)
                {
                    // Determine which existing weapon we're comparing against
                    ThingWithComps existingWeapon = null;
                    bool isPrimaryUpgrade = false;
                    
                    if (primaryWeapon != null && weapon.def == primaryWeapon.def)
                    {
                        existingWeapon = primaryWeapon;
                        isPrimaryUpgrade = true;
                    }
                    else if (inventoryWeapons.ContainsKey(weapon.def))
                    {
                        existingWeapon = inventoryWeapons[weapon.def];
                        isPrimaryUpgrade = false;
                    }
                    
                    if (existingWeapon == null)
                    {
                        continue; // Shouldn't happen, but be safe
                    }
                    
                    // OPTIMIZATION: Quick quality check to avoid scoring obviously inferior weapons
                    QualityCategory existingQuality = QualityCategory.Normal;
                    QualityCategory newQuality = QualityCategory.Normal;
                    
                    existingWeapon.TryGetQuality(out existingQuality);
                    weapon.TryGetQuality(out newQuality);
                    
                    // If quality is clearly worse, skip without scoring or logging
                    if (newQuality < existingQuality)
                    {
                        continue; // Obviously inferior, no need to score
                    }
                    
                    float existingScore = getWeaponScore(pawn, existingWeapon);
                    float threshold = AutoArmMod.settings?.weaponUpgradeThreshold ?? Constants.WeaponUpgradeThreshold;

                    // Check if SS manages this weapon
                    bool isSidearmManaged = IsSimpleSidearmsManaged(pawn, existingWeapon) || isPrimaryUpgrade;
                    
                    // Only log if same or better quality (we already filtered out worse quality)
                    if (AutoArmMod.settings?.debugLogging == true && newQuality >= existingQuality)
                    {
                        AutoArmLogger.Debug($"[{pawn.LabelShort}] Checking upgrade for {existingWeapon.Label}: SS-managed={isSidearmManaged}, existingScore={existingScore:F0}, newScore={score:F0}, threshold={threshold:F2}");
                    }

                    // For SS-managed weapons OR primary weapon OR any weapon in inventory, check upgrade settings
                    // (weapons in inventory should be considered as potential sidearms even if SS hasn't registered them yet)
                    if (isSidearmManaged || isPrimaryUpgrade || existingWeapon.holdingOwner == pawn.inventory.innerContainer)
                    {
                        // Check if sidearm upgrades are allowed
                        if (AutoArmMod.settings?.allowSidearmUpgrades != true)
                        {
                            if (AutoArmMod.settings?.debugLogging == true)
                            {
                                AutoArmLogger.Debug($"[{pawn.LabelShort}] Skipping {weapon.Label} - sidearm upgrades disabled");
                            }
                            continue;
                        }
                        
                        // Check if this weapon type is forced/preferred in SS
                        bool isForced, isPreferred;
                        if (IsWeaponTypeForced(pawn, existingWeapon.def, out isForced, out isPreferred))
                        {
                            // Weapon type is forced/preferred - check if forced upgrades are allowed
                            if (AutoArmMod.settings?.allowForcedWeaponUpgrades != true)
                            {
                                if (AutoArmMod.settings?.debugLogging == true)
                                {
                                    AutoArmLogger.Debug($"[{pawn.LabelShort}] Skipping {weapon.Label} - weapon type {existingWeapon.def.label} is {(isForced ? "forced" : "preferred")} and forced upgrades disabled");
                                }
                                continue;
                            }
                        }

                        // Only consider if it's a significant upgrade
                        if (score > existingScore * threshold)
                        {
                            if (score > bestScore)
                            {
                                bestWeapon = weapon;
                                bestScore = score;
                                isUpgrade = true;
                                weaponToReplace = existingWeapon;

                                if (AutoArmMod.settings?.debugLogging == true)
                                {
                                    AutoArmLogger.Debug($"[{pawn.LabelShort}] Found SS-managed weapon upgrade: {existingWeapon.Label} -> {weapon.Label}");
                                }
                            }
                        }
                        else
                        {
                            // Only log rejection if same or better quality (we already filtered out worse quality)
                            if (AutoArmMod.settings?.debugLogging == true && newQuality >= existingQuality)
                            {
                                AutoArmLogger.Debug($"[{pawn.LabelShort}] Weapon {weapon.Label} rejected: Not enough upgrade over {existingWeapon.Label} ({score} <= {existingScore} * {threshold})");
                            }
                        }
                    }
                    else
                    {
                        // Not SS-managed and not in inventory, skip (we already have this type elsewhere)
                        if (AutoArmMod.settings?.debugLogging == true)
                        {
                            AutoArmLogger.Debug($"[{pawn.LabelShort}] Skipping {weapon.Label} - already have {weapon.def.label} (not in inventory)");
                        }
                        continue;
                    }
                }
                else
                {
                    // New weapon type - check if SS allows it
                    string ssReason;
                    if (!CanPickupSidearmInstance(weapon, pawn, out ssReason))
                    {
                        if (AutoArmMod.settings?.debugLogging == true)
                        {
                            AutoArmLogger.Debug($"[{pawn.LabelShort}] SimpleSidearms rejects {weapon.Label}: {ssReason}");
                        }
                        continue;
                    }

                    // New sidearm
                    if (score > bestScore)
                    {
                        bestWeapon = weapon;
                        bestScore = score;
                        isUpgrade = false;
                        weaponToReplace = null;

                        if (AutoArmMod.settings?.debugLogging == true)
                        {
                            AutoArmLogger.Debug($"[{pawn.LabelShort}] Found new sidearm: {weapon.Label}");
                        }
                    }
                }
            }

            if (bestWeapon == null)
                return null;

            // Create appropriate job
            if (isUpgrade && weaponToReplace != null)
            {
                // Check if this is a primary weapon upgrade
                bool isPrimaryUpgrade = (weaponToReplace == pawn.equipment?.Primary);
                
                if (isPrimaryUpgrade)
                {
                    // For primary weapon upgrades, we need to drop the old primary at the new weapon location
                    if (AutoArmMod.settings?.debugLogging == true)
                    {
                        AutoArmLogger.Debug($"[{pawn.LabelShort}] Creating primary swap job for upgrade: {weaponToReplace.Label} -> {bestWeapon.Label}");
                    }
                    
                    // Create a custom primary swap job that drops the old weapon at the new weapon's location
                    var job = CreateSwapPrimaryJob(pawn, bestWeapon, weaponToReplace);
                    if (job != null)
                    {
                        AutoEquipTracker.MarkAsAutoEquip(job, pawn);
                        
                        // Show notification for primary upgrade
                        if (AutoArmMod.settings?.showNotifications == true && 
                            PawnUtility.ShouldSendNotificationAbout(pawn))
                        {
                            Messages.Message("AutoArm_UpgradedSidearm".Translate(
                                pawn.LabelShort.CapitalizeFirst(),
                                weaponToReplace.Label,
                                bestWeapon.Label
                            ), new LookTargets(pawn), MessageTypeDefOf.SilentInput, false);
                        }
                    }
                    return job;
                }
                else
                {
                    // For sidearm upgrades in inventory, pre-validate that the swap will work
                    if (!CanPerformSidearmSwap(pawn, bestWeapon, weaponToReplace))
                    {
                        if (AutoArmMod.settings?.debugLogging == true)
                        {
                            AutoArmLogger.Debug($"[{pawn.LabelShort}] Sidearm swap validation failed: {weaponToReplace.Label} -> {bestWeapon.Label}");
                        }
                        return null;
                    }
                    
                    if (AutoArmMod.settings?.debugLogging == true)
                    {
                        AutoArmLogger.Debug($"[{pawn.LabelShort}] Creating swap job for SS upgrade: {weaponToReplace.Label} -> {bestWeapon.Label}");
                    }

                    // Create our custom swap job
                    var job = CreateSwapSidearmJob(pawn, bestWeapon, weaponToReplace);
                    if (job != null)
                    {
                        AutoEquipTracker.MarkAsAutoEquip(job, pawn);
                        // SimpleSidearms is the authority - no need to track forcing separately
                    }
                    return job;
                }
            }
            else
            {
                // For new sidearms, use EquipSecondary
                var job = CreateEquipSidearmJob(pawn, bestWeapon);
                
                if (job != null)
                {
                    // Mark for AutoArm tracking
                    if (AutoArmMod.settings?.debugLogging == true)
                    {
                        AutoArmLogger.Debug($"[{pawn.LabelShort}] Creating new sidearm job for {bestWeapon.Label}");
                    }
                    
                    // Show immediate notification for new sidearm pickup
                    if (AutoArmMod.settings?.showNotifications == true && 
                        PawnUtility.ShouldSendNotificationAbout(pawn))
                    {
                        Messages.Message("AutoArm_EquippingSidearm".Translate(
                            pawn.LabelShort.CapitalizeFirst(),
                            bestWeapon.Label
                        ), new LookTargets(pawn), MessageTypeDefOf.SilentInput, false);
                    }
                }
                
                return job;
            }
        }

        /// <summary>
        /// Check if a sidearm swap can be performed successfully
        /// </summary>
        private static bool CanPerformSidearmSwap(Pawn pawn, ThingWithComps newWeapon, ThingWithComps oldWeapon)
        {
            if (pawn == null || newWeapon == null || oldWeapon == null)
                return false;
                
            // Make sure old weapon is actually in inventory
            if (!pawn.inventory.innerContainer.Contains(oldWeapon))
            {
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.Debug($"[{pawn.LabelShort}] Old weapon {oldWeapon.Label} not in inventory for swap");
                }
                return false;
            }
            
            // For same-type upgrades, skip slot limit check since we're replacing, not adding
            bool isSameTypeUpgrade = (newWeapon.def == oldWeapon.def);
            
            if (isSameTypeUpgrade)
            {
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.Debug($"[{pawn.LabelShort}] Same-type upgrade detected, bypassing SS slot limit check");
                }
                // For same-type upgrades, we don't need SS validation since we're replacing
                // The swap will maintain the same number of slots
                return true;
            }
            else
            {
                // Different weapon type - check if SimpleSidearms would allow it
                string reason;
                if (!CanPickupSidearmInstance(newWeapon, pawn, out reason))
                {
                    if (AutoArmMod.settings?.debugLogging == true)
                    {
                        AutoArmLogger.Debug($"[{pawn.LabelShort}] SS pre-validation rejects {newWeapon.Label}: {reason}");
                    }
                    return false;
                }
            }
            
            return true;
        }
        
        /// <summary>
        /// Create a job to swap a sidearm with a better weapon
        /// </summary>
        public static Job CreateSwapSidearmJob(Pawn pawn, ThingWithComps newWeapon, ThingWithComps oldWeapon)
        {
            if (_swapSidearmJobDef == null)
            {
                // Create the job def if it doesn't exist
                _swapSidearmJobDef = new JobDef
                {
                    defName = "AutoArmSwapSidearm",
                    driverClass = typeof(JobDriver_SwapSidearm),
                    reportString = "swapping sidearm.",
                    casualInterruptible = false
                };
            }

            return JobMaker.MakeJob(_swapSidearmJobDef, newWeapon, oldWeapon);
        }
        
        /// <summary>
        /// Create a job to swap the primary weapon with a better weapon
        /// </summary>
        public static Job CreateSwapPrimaryJob(Pawn pawn, ThingWithComps newWeapon, ThingWithComps oldWeapon)
        {
            // For primary swaps, we can use a regular equip job with a custom driver
            // that drops the old weapon at the pickup location
            var swapPrimaryJobDef = new JobDef
            {
                defName = "AutoArmSwapPrimary",
                driverClass = typeof(JobDriver_SwapPrimary),
                reportString = "swapping primary weapon.",
                casualInterruptible = false
            };
            
            return JobMaker.MakeJob(swapPrimaryJobDef, newWeapon, oldWeapon);
        }

        /// <summary>
        /// Forget a weapon in SimpleSidearms memory
        /// </summary>
        private static void ForgetSidearmInMemory(Pawn pawn, ThingWithComps weapon)
        {
            if (!IsLoaded() || pawn == null || weapon == null)
                return;

            EnsureInitialized();
            if (_reflectionFailed)
                return;

            try
            {
                if (getMemoryCompForPawnMethod != null && compSidearmMemoryType != null)
                {
                    // Try to call with appropriate parameters based on method signature
                    object memoryComp = null;
                    var parameters = getMemoryCompForPawnMethod.GetParameters();
                    
                    if (parameters.Length == 2 && parameters[1].ParameterType == typeof(bool))
                    {
                        // Signature: GetMemoryCompForPawn(Pawn pawn, bool fillExistingIfCreating)
                        memoryComp = getMemoryCompForPawnMethod.Invoke(null, new object[] { pawn, false });
                    }
                    else if (parameters.Length == 1)
                    {
                        // Signature: GetMemoryCompForPawn(Pawn pawn)
                        memoryComp = getMemoryCompForPawnMethod.Invoke(null, new object[] { pawn });
                    }
                    
                    if (memoryComp != null)
                    {
                        // Call InformOfDroppedSidearm with intentional = true
                        var informDropMethod = compSidearmMemoryType.GetMethod("InformOfDroppedSidearm",
                            BindingFlags.Public | BindingFlags.Instance);

                        if (informDropMethod != null)
                        {
                            informDropMethod.Invoke(memoryComp, new object[] { weapon, true });
                        }
                    }
                }
            }
            catch (Exception e)
            {
                AutoArmLogger.Debug($"Failed to forget weapon in SS memory: {e.Message}");
            }
        }

        /// <summary>
        /// Reorder weapons after an equip to ensure the best weapon is primary
        /// Uses SimpleSidearms' own logic to determine the best weapon setup
        /// </summary>
        public static void ReorderWeaponsAfterEquip(Pawn pawn)
        {
            if (!IsLoaded() || pawn == null)
                return;

            EnsureInitialized();

            if (_reflectionFailed)
                return;

            try
            {
                // Get the WeaponAssingment type
                if (weaponAssingmentType == null)
                {
                    AutoArmLogger.Warn("Could not find WeaponAssingment type");
                    return;
                }

                // Get the equipBestWeaponFromInventoryByPreference method
                var reorderMethod = weaponAssingmentType.GetMethod(
                    "equipBestWeaponFromInventoryByPreference",
                    BindingFlags.Public | BindingFlags.Static);

                if (reorderMethod == null)
                {
                    AutoArmLogger.Warn("Could not find equipBestWeaponFromInventoryByPreference method");
                    return;
                }

                // Get the DroppingModeEnum type and Calm value
                var droppingModeType = GenTypes.AllTypes.FirstOrDefault(t =>
                    t.FullName == "PeteTimesSix.SimpleSidearms.Utilities.Enums+DroppingModeEnum");

                if (droppingModeType == null)
                {
                    AutoArmLogger.Warn("Could not find DroppingModeEnum type");
                    return;
                }

                // Get the Calm enum value
                var calmValue = Enum.Parse(droppingModeType, "Calm");

                // Correct signature: (Pawn, DroppingModeEnum, PrimaryWeaponMode?, Pawn target)
                // We pass null for PrimaryWeaponMode? and target
                reorderMethod.Invoke(null, new object[] { pawn, calmValue, null, null });

                if (AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.Debug($"[{pawn.LabelShort}] SimpleSidearms reordered weapons after auto-equip");
                }
            }
            catch (Exception e)
            {
                AutoArmLogger.Warn($"Failed to reorder weapons: {e.Message}");
            }
        }

        public static void CleanupAfterLoad()
        {
            _isLoaded = null;
            _initialized = false;
            _reflectionFailed = false;
            thingDefStuffDefPairType = null;
        }

        /// <summary>
        /// Check if a weapon type is forced/preferred in SimpleSidearms
        /// This includes ForcedWeapon, DefaultRangedWeapon, and PreferredMeleeWeapon
        /// </summary>
        public static bool IsWeaponTypeForced(Pawn pawn, ThingDef weaponDef, out bool isForced, out bool isPreferred)
        {
            isForced = false;
            isPreferred = false;
            
            if (!IsLoaded() || pawn == null || weaponDef == null)
                return false;

            EnsureInitialized();
            if (_reflectionFailed)
                return false;

            try
            {
                if (getMemoryCompForPawnMethod != null && compSidearmMemoryType != null)
                {
                    // Try to call with appropriate parameters based on method signature
                    object memoryComp = null;
                    var parameters = getMemoryCompForPawnMethod.GetParameters();
                    
                    if (parameters.Length == 2 && parameters[1].ParameterType == typeof(bool))
                    {
                        memoryComp = getMemoryCompForPawnMethod.Invoke(null, new object[] { pawn, false });
                    }
                    else if (parameters.Length == 1)
                    {
                        memoryComp = getMemoryCompForPawnMethod.Invoke(null, new object[] { pawn });
                    }
                    
                    // Removed debug logging for performance - this is called very frequently
                    
                    if (memoryComp != null)
                    {
                        // Use cached ThingDefStuffDefPair type
                        var pairType = thingDefStuffDefPairType;
                        
                        // Check ForcedWeapon (returns ThingDefStuffDefPair?)
                        var forcedWeaponProp = compSidearmMemoryType.GetProperty("ForcedWeapon");
                        
                        if (forcedWeaponProp != null)
                        {
                            var forcedWeaponNullable = forcedWeaponProp.GetValue(memoryComp);
                            
                            if (forcedWeaponNullable != null)
                            {
                                // Check if it's a nullable struct or a direct struct
                                var forcedWeapon = forcedWeaponNullable;
                                
                                // Check if it's a nullable type
                                var hasValueProp = forcedWeaponNullable.GetType().GetProperty("HasValue");
                                if (hasValueProp != null)
                                {
                                    // It's a nullable struct
                                    if (!(bool)hasValueProp.GetValue(forcedWeaponNullable))
                                    {
                                        // Nullable without value, skip
                                    }
                                    else
                                    {
                                        // Extract the value from nullable
                                        var valueProp = forcedWeaponNullable.GetType().GetProperty("Value");
                                        if (valueProp != null)
                                        {
                                            forcedWeapon = valueProp.GetValue(forcedWeaponNullable);
                                        }
                                    }
                                }
                                
                                // Now check the actual struct (whether it came from nullable or direct)
                                if (forcedWeapon != null && pairType != null)
                                {
                                    var thingField = pairType.GetField("thing");
                                    if (thingField != null)
                                    {
                                        var forcedDef = thingField.GetValue(forcedWeapon) as ThingDef;
                                        if (forcedDef == weaponDef)
                                        {
                                            isForced = true;
                                            if (AutoArmMod.settings?.debugLogging == true)
                                            {
                                                AutoArmLogger.Debug($"[SS Check] {weaponDef.defName} is FORCED for {pawn.LabelShort}");
                                            }
                                            return true;
                                        }
                                    }
                                }
                            }
                        }

                        // Check ForcedWeaponWhileDrafted (if pawn is drafted)
                        if (pawn.Drafted)
                        {
                            var forcedDraftedProp = compSidearmMemoryType.GetProperty("ForcedWeaponWhileDrafted");
                            if (forcedDraftedProp != null)
                            {
                                var forcedDraftedNullable = forcedDraftedProp.GetValue(memoryComp);
                                if (forcedDraftedNullable != null)
                                {
                                    var hasValueProp = forcedDraftedNullable.GetType().GetProperty("HasValue");
                                    if (hasValueProp != null && (bool)hasValueProp.GetValue(forcedDraftedNullable))
                                    {
                                        var valueProp = forcedDraftedNullable.GetType().GetProperty("Value");
                                        if (valueProp != null)
                                        {
                                            var forcedDrafted = valueProp.GetValue(forcedDraftedNullable);
                                            if (forcedDrafted != null && pairType != null)
                                            {
                                                var thingField = pairType.GetField("thing");
                                                if (thingField != null)
                                                {
                                                    var forcedDef = thingField.GetValue(forcedDrafted) as ThingDef;
                                                    if (forcedDef == weaponDef)
                                                    {
                                                        isForced = true;
                                                        if (AutoArmMod.settings?.debugLogging == true)
                                                        {
                                                            AutoArmLogger.Debug($"[SS Check] {weaponDef.defName} is FORCED WHILE DRAFTED for {pawn.LabelShort}");
                                                        }
                                                        return true;
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }

                        // Check DefaultRangedWeapon
                        if (weaponDef.IsRangedWeapon)
                        {
                            var defaultRangedProp = compSidearmMemoryType.GetProperty("DefaultRangedWeapon");
                            if (defaultRangedProp != null)
                            {
                                var defaultRangedNullable = defaultRangedProp.GetValue(memoryComp);
                                if (defaultRangedNullable != null)
                                {
                                    var hasValueProp = defaultRangedNullable.GetType().GetProperty("HasValue");
                                    if (hasValueProp != null && (bool)hasValueProp.GetValue(defaultRangedNullable))
                                    {
                                        var valueProp = defaultRangedNullable.GetType().GetProperty("Value");
                                        if (valueProp != null)
                                        {
                                            var defaultRanged = valueProp.GetValue(defaultRangedNullable);
                                            if (defaultRanged != null && pairType != null)
                                            {
                                                var thingField = pairType.GetField("thing");
                                                if (thingField != null)
                                                {
                                                    var defaultDef = thingField.GetValue(defaultRanged) as ThingDef;
                                                    if (defaultDef == weaponDef)
                                                    {
                                                        isPreferred = true;
                                                        if (AutoArmMod.settings?.debugLogging == true)
                                                        {
                                                            AutoArmLogger.Debug($"[SS Check] {weaponDef.defName} is DEFAULT RANGED for {pawn.LabelShort}");
                                                        }
                                                        return true;
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }

                        // Check PreferredMeleeWeapon
                        if (weaponDef.IsMeleeWeapon)
                        {
                            var preferredMeleeProp = compSidearmMemoryType.GetProperty("PreferredMeleeWeapon");
                            if (preferredMeleeProp != null)
                            {
                                var preferredMeleeNullable = preferredMeleeProp.GetValue(memoryComp);
                                if (preferredMeleeNullable != null)
                                {
                                    var hasValueProp = preferredMeleeNullable.GetType().GetProperty("HasValue");
                                    if (hasValueProp != null && (bool)hasValueProp.GetValue(preferredMeleeNullable))
                                    {
                                        var valueProp = preferredMeleeNullable.GetType().GetProperty("Value");
                                        if (valueProp != null)
                                        {
                                            var preferredMelee = valueProp.GetValue(preferredMeleeNullable);
                                            if (preferredMelee != null && pairType != null)
                                            {
                                                var thingField = pairType.GetField("thing");
                                                if (thingField != null)
                                                {
                                                    var preferredDef = thingField.GetValue(preferredMelee) as ThingDef;
                                                    if (preferredDef == weaponDef)
                                                    {
                                                        isPreferred = true;
                                                        if (AutoArmMod.settings?.debugLogging == true)
                                                        {
                                                            AutoArmLogger.Debug($"[SS Check] {weaponDef.defName} is PREFERRED MELEE for {pawn.LabelShort}");
                                                        }
                                                        return true;
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        
                        // Removed "not forced" debug logging for performance - this is called very frequently
                    }
                }
            }
            catch (Exception e)
            {
                AutoArmLogger.Debug($"Failed to check SS forced/preferred status: {e.Message}\nStackTrace: {e.StackTrace}");
            }

            return false;
        }

        /// <summary>
        /// Check if AutoArm should skip this weapon based on SS forcing and upgrade settings
        /// </summary>
        public static bool ShouldSkipWeaponUpgrade(Pawn pawn, ThingDef currentWeaponDef, ThingDef newWeaponDef)
        {
            if (!IsLoaded() || pawn == null)
                return false;

            bool isForced, isPreferred;
            if (IsWeaponTypeForced(pawn, currentWeaponDef, out isForced, out isPreferred))
            {
                // If SS has forced/preferred this weapon type
                if (newWeaponDef != currentWeaponDef)
                {
                    // Different weapon type - always block
                    if (AutoArmMod.settings?.debugLogging == true)
                    {
                        AutoArmLogger.Debug($"[{pawn.LabelShort}] Cannot change from {currentWeaponDef.label} to {newWeaponDef.label} - weapon type is {(isForced ? "forced" : "preferred")} in SimpleSidearms");
                    }
                    return true;
                }
                else
                {
                    // Same weapon type - check if forced upgrades are allowed
                    if (AutoArmMod.settings?.allowForcedWeaponUpgrades != true)
                    {
                        if (AutoArmMod.settings?.debugLogging == true)
                        {
                            AutoArmLogger.Debug($"[{pawn.LabelShort}] Cannot upgrade {currentWeaponDef.label} - weapon type is {(isForced ? "forced" : "preferred")} and forced upgrades disabled");
                        }
                        return true;
                    }
                }
            }
            
            return false;
        }

        /// <summary>
        /// Tell SimpleSidearms to force a weapon type (used for bonded weapons)
        /// </summary>
        public static bool SetWeaponAsForced(Pawn pawn, ThingWithComps weapon)
        {
            if (!IsLoaded() || pawn == null || weapon == null)
                return false;

            EnsureInitialized();
            if (_reflectionFailed)
                return false;

            try
            {
                // First try using SetAsForced method if available (cleaner approach)
                if (weaponAssingmentType != null)
                {
                    // Look for SetAsForced(Pawn, Thing, bool)
                    var setAsForcedMethod = weaponAssingmentType.GetMethod("SetAsForced",
                        BindingFlags.Public | BindingFlags.Static,
                        null,
                        new Type[] { typeof(Pawn), typeof(Thing), typeof(bool) },
                        null);
                    
                    if (AutoArmMod.settings?.debugLogging == true)
                    {
                        AutoArmLogger.Debug($"[SS SetForced Debug] Found SetAsForced method: {setAsForcedMethod != null}");
                    }
                        
                    if (setAsForcedMethod != null)
                    {
                        // Call SetAsForced(pawn, weapon, true) where true = force it
                        setAsForcedMethod.Invoke(null, new object[] { pawn, weapon, true });
                        
                        if (AutoArmMod.settings?.debugLogging == true)
                        {
                            AutoArmLogger.Debug($"[{pawn.LabelShort}] Called SetAsForced for {weapon.Label}");
                        }
                        return true;
                    }
                }
                
                // Fallback: Try setting the property directly
                if (getMemoryCompForPawnMethod != null && compSidearmMemoryType != null)
                {
                    // Try to call with appropriate parameters based on method signature
                    object memoryComp = null;
                    var parameters = getMemoryCompForPawnMethod.GetParameters();
                    
                    if (parameters.Length == 2 && parameters[1].ParameterType == typeof(bool))
                    {
                        // Use true for fillExistingIfCreating to ensure comp is created
                        memoryComp = getMemoryCompForPawnMethod.Invoke(null, new object[] { pawn, true });
                    }
                    else if (parameters.Length == 1)
                    {
                        memoryComp = getMemoryCompForPawnMethod.Invoke(null, new object[] { pawn });
                    }
                    
                    if (AutoArmMod.settings?.debugLogging == true)
                    {
                        AutoArmLogger.Debug($"[SS SetForced Debug] Got memory comp: {memoryComp != null}");
                    }
                    
                    if (memoryComp != null)
                    {
                        // Use cached ThingDefStuffDefPair type
                        var pairType = thingDefStuffDefPairType;
                        
                        if (pairType != null)
                        {
                            // Try to create the pair - constructor might be (ThingDef, ThingDef?) or just (ThingDef)
                            object weaponPair = null;
                            try
                            {
                                // Try with both parameters
                                weaponPair = Activator.CreateInstance(pairType, weapon.def, weapon.Stuff);
                            }
                            catch (Exception)
                            {
                                // Try with just ThingDef
                                try
                                {
                                    weaponPair = Activator.CreateInstance(pairType, weapon.def);
                                }
                                catch (Exception e2)
                                {
                                    AutoArmLogger.Debug($"Failed to create ThingDefStuffDefPair: {e2.Message}");
                                    return false;
                                }
                            }
                            
                            if (weaponPair != null)
                            {
                                // Set the stuff field if needed
                                if (weapon.Stuff != null)
                                {
                                    var stuffField = pairType.GetField("stuff");
                                    if (stuffField != null)
                                    {
                                        stuffField.SetValue(weaponPair, weapon.Stuff);
                                    }
                                }
                                
                                // Set as ForcedWeapon
                                var forcedWeaponProp = compSidearmMemoryType.GetProperty("ForcedWeapon");
                                if (forcedWeaponProp != null)
                                {
                                    // Check if the property type is nullable or not
                                    var propertyType = forcedWeaponProp.PropertyType;
                                    object valueToSet = weaponPair;
                                    
                                    if (propertyType.IsGenericType && propertyType.GetGenericTypeDefinition() == typeof(Nullable<>))
                                    {
                                        // Property expects nullable, wrap the struct
                                        var nullableType = typeof(Nullable<>).MakeGenericType(pairType);
                                        valueToSet = Activator.CreateInstance(nullableType, weaponPair);
                                        
                                        if (AutoArmMod.settings?.debugLogging == true)
                                        {
                                            AutoArmLogger.Debug($"[SS SetForced Debug] Property expects nullable, wrapping {weapon.def.defName}");
                                        }
                                    }
                                    else
                                    {
                                        if (AutoArmMod.settings?.debugLogging == true)
                                        {
                                            AutoArmLogger.Debug($"[SS SetForced Debug] Property expects non-nullable, using direct struct for {weapon.def.defName}");
                                        }
                                    }
                                    
                                    if (AutoArmMod.settings?.debugLogging == true)
                                    {
                                        AutoArmLogger.Debug($"[SS SetForced Debug] Setting ForcedWeapon property for {weapon.def.defName}");
                                    }
                                    
                                    forcedWeaponProp.SetValue(memoryComp, valueToSet);
                                    
                                    // Verify it was set
                                    var verifyValue = forcedWeaponProp.GetValue(memoryComp);
                                    
                                    if (AutoArmMod.settings?.debugLogging == true)
                                    {
                                        AutoArmLogger.Debug($"[SS SetForced Debug] After setting, ForcedWeapon value is: {verifyValue}, Type: {verifyValue?.GetType()?.FullName ?? "null"}");
                                        
                                        if (verifyValue != null)
                                        {
                                            var hasValueProp = verifyValue.GetType().GetProperty("HasValue");
                                            if (hasValueProp != null)
                                            {
                                                bool hasValue = (bool)hasValueProp.GetValue(verifyValue);
                                                AutoArmLogger.Debug($"[SS SetForced Debug] HasValue: {hasValue}");
                                                
                                                if (hasValue)
                                                {
                                                    var valueProp = verifyValue.GetType().GetProperty("Value");
                                                    if (valueProp != null)
                                                    {
                                                        var actualPair = valueProp.GetValue(verifyValue);
                                                        if (actualPair != null)
                                                        {
                                                            var thingField = pairType.GetField("thing");
                                                            if (thingField != null)
                                                            {
                                                                var verifyDef = thingField.GetValue(actualPair) as ThingDef;
                                                                AutoArmLogger.Debug($"[SS SetForced Debug] Verified forced weapon def: {verifyDef?.defName ?? "null"}");
                                                            }
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    }
                                    return true;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                AutoArmLogger.Debug($"Failed to set weapon as forced in SS: {e.Message}");
            }
            
            return false;
        }

        /// <summary>
        /// Check if a weapon is bonded and sync with SimpleSidearms if needed
        /// Should be called when a weapon becomes primary
        /// </summary>
        public static void SyncBondedPrimaryWeapon(Pawn pawn, ThingWithComps weapon)
        {
            if (!IsLoaded() || pawn == null || weapon == null)
                return;
                
            // Only sync if respecting weapon bonds
            if (AutoArmMod.settings?.respectWeaponBonds != true)
                return;
                
            // Check if this weapon is bonded to this pawn
            var biocomp = weapon.TryGetComp<CompBiocodable>();
            if (biocomp?.CodedPawn == pawn)
            {
                // This is a bonded weapon for this pawn - tell SS to force it
                SetWeaponAsForced(pawn, weapon);
            }
        }

        /// <summary>
        /// Inform SimpleSidearms that a weapon was dropped
        /// </summary>
        public static void InformOfDroppedSidearm(Pawn pawn, ThingWithComps weapon)
        {
            if (!IsLoaded() || pawn == null || weapon == null)
                return;

            EnsureInitialized();

            if (_reflectionFailed)
                return;

            try
            {
                if (getMemoryCompForPawnMethod != null && compSidearmMemoryType != null)
                {
                    // Try to call with appropriate parameters based on method signature
                    object memoryComp = null;
                    var parameters = getMemoryCompForPawnMethod.GetParameters();
                    
                    if (parameters.Length == 2 && parameters[1].ParameterType == typeof(bool))
                    {
                        memoryComp = getMemoryCompForPawnMethod.Invoke(null, new object[] { pawn, false });
                    }
                    else if (parameters.Length == 1)
                    {
                        memoryComp = getMemoryCompForPawnMethod.Invoke(null, new object[] { pawn });
                    }
                    if (memoryComp != null)
                    {
                        var informDropMethod = compSidearmMemoryType.GetMethod("InformOfDroppedSidearm",
                            BindingFlags.Public | BindingFlags.Instance);

                        if (informDropMethod != null)
                        {
                            informDropMethod.Invoke(memoryComp, new object[] { weapon, true }); // intentional = true
                        }
                    }
                }
            }
            catch (Exception e)
            {
                AutoArmLogger.Warn($"Failed to inform SimpleSidearms of weapon drop: {e.Message}");
            }
        }

        /// <summary>
        /// Inform SimpleSidearms that a weapon was added
        /// </summary>
        public static void InformOfAddedSidearm(Pawn pawn, ThingWithComps weapon)
        {
            if (!IsLoaded() || pawn == null || weapon == null)
                return;

            EnsureInitialized();

            if (_reflectionFailed)
                return;

            try
            {
                if (getMemoryCompForPawnMethod != null && compSidearmMemoryType != null)
                {
                    // Try to call with appropriate parameters based on method signature
                    object memoryComp = null;
                    var parameters = getMemoryCompForPawnMethod.GetParameters();
                    
                    if (parameters.Length == 2 && parameters[1].ParameterType == typeof(bool))
                    {
                        memoryComp = getMemoryCompForPawnMethod.Invoke(null, new object[] { pawn, false });
                    }
                    else if (parameters.Length == 1)
                    {
                        memoryComp = getMemoryCompForPawnMethod.Invoke(null, new object[] { pawn });
                    }
                    if (memoryComp != null)
                    {
                        var informAddMethod = compSidearmMemoryType.GetMethod("InformOfAddedSidearm",
                            BindingFlags.Public | BindingFlags.Instance);

                        if (informAddMethod != null)
                        {
                            // Try with just weapon parameter first
                            try
                            {
                                informAddMethod.Invoke(memoryComp, new object[] { weapon });
                            }
                            catch (TargetParameterCountException)
                            {
                                // If that fails, try with weapon and intentional flag (like InformOfDroppedSidearm)
                                try
                                {
                                    informAddMethod.Invoke(memoryComp, new object[] { weapon, true });
                                }
                                catch (Exception e2)
                                {
                                    AutoArmLogger.Warn($"Failed to inform SimpleSidearms of weapon add with both parameter variations: {e2.Message}");
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                AutoArmLogger.Warn($"Failed to inform SimpleSidearms of weapon add: {e.Message}");
            }
        }
        /// <summary>
        /// JobDriver for swapping sidearms - ensures old weapon ends up at new weapon location
        /// </summary>
        public class JobDriver_SwapSidearm : JobDriver
        {
            private ThingWithComps NewWeapon => (ThingWithComps)job.targetA.Thing;
            private ThingWithComps OldWeapon => (ThingWithComps)job.targetB.Thing;

            public override bool TryMakePreToilReservations(bool errorOnFailed)
            {
                // Reserve the new weapon so no one else takes it
                return pawn.Reserve(NewWeapon, job, 1, -1, null, errorOnFailed);
            }

            protected override IEnumerable<Toil> MakeNewToils()
            {
                // Fail if new weapon disappears
                this.FailOnDestroyedNullOrForbidden(TargetIndex.A);
                this.FailOnDespawnedNullOrForbidden(TargetIndex.A);

                // Go to the new weapon
                yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.ClosestTouch)
                    .FailOnDespawnedNullOrForbidden(TargetIndex.A)
                    .FailOnSomeonePhysicallyInteracting(TargetIndex.A);

                // Do the ATOMIC swap at this location
                yield return new Toil
                {
                    initAction = delegate
                    {
                        try
                        {
                            // Verify both weapons still exist
                            if (NewWeapon == null || OldWeapon == null)
                            {
                                if (AutoArmMod.settings?.debugLogging == true)
                                {
                                    AutoArmLogger.Debug($"[{pawn.LabelShort}] Swap cancelled - weapon no longer exists");
                                }
                                return;
                            }

                            // ATOMIC SWAP - Critical section
                            // We need to ensure the inventory swap happens atomically
                            
                            // 1. Find old weapon's position in inventory
                            int oldWeaponIndex = pawn.inventory.innerContainer.IndexOf(OldWeapon);
                            if (oldWeaponIndex < 0)
                            {
                                AutoArmLogger.Warn($"[{pawn.LabelShort}] Swap aborted - {OldWeapon.Label} not in inventory");
                                return;
                            }
                            
                            // 2. Pre-validate that SS will accept the swap
                            // SKIP validation for same-type upgrades - we're replacing, not adding
                            bool isSameTypeUpgrade = (OldWeapon.def == NewWeapon.def);
                            if (!isSameTypeUpgrade)
                            {
                                string validationReason;
                                if (!CanPickupSidearmInstance(NewWeapon, pawn, out validationReason))
                                {
                                    if (AutoArmMod.settings?.debugLogging == true)
                                    {
                                        AutoArmLogger.Debug($"[{pawn.LabelShort}] Swap aborted - SS would reject {NewWeapon.Label}: {validationReason}");
                                    }
                                    return;
                                }
                            }
                            else if (AutoArmMod.settings?.debugLogging == true)
                            {
                                AutoArmLogger.Debug($"[{pawn.LabelShort}] Same-type swap - skipping SS validation (replacing {OldWeapon.Label} with {NewWeapon.Label})");
                            }
                            
                            // Store forced/preferred status before swap to maintain it
                            bool wasForced = false;
                            bool wasPreferred = false;
                            if (OldWeapon.def == NewWeapon.def)  // Same-type upgrade
                            {
                                IsWeaponTypeForced(pawn, OldWeapon.def, out wasForced, out wasPreferred);
                                if ((wasForced || wasPreferred) && AutoArmMod.settings?.debugLogging == true)
                                {
                                    AutoArmLogger.Debug($"[{pawn.LabelShort}] Will maintain {(wasForced ? "forced" : "preferred")} status for {NewWeapon.Label}");
                                }
                            }
                            
                            // 3. Pick up new weapon from ground (but don't add to inventory yet)
                            if (NewWeapon.Spawned)
                            {
                                NewWeapon.DeSpawn();
                            }
                            
                            // 4. Store reference to old weapon before removing it
                            ThingWithComps oldWeaponToPlace = OldWeapon;
                            IntVec3 dropPosition = pawn.Position; // Drop at current position (new weapon location)
                            
                            // 5. ATOMIC OPERATION - Direct swap in container
                            // This maintains the same slot position, avoiding SS weight/slot recalculation issues
                            try
                            {
                                if (AutoArmMod.settings?.debugLogging == true)
                                {
                                    int invCount = pawn.inventory.innerContainer.Count;
                                    AutoArmLogger.Debug($"[{pawn.LabelShort}] Performing atomic swap: {OldWeapon.Label} (slot {oldWeaponIndex}) -> {NewWeapon.Label}");
                                    AutoArmLogger.Debug($"[{pawn.LabelShort}] Current inventory count: {invCount} items");
                                }
                                
                                // Handle stackable weapons
                                if (oldWeaponToPlace.stackCount > 1)
                                {
                                    // Split off one item from the stack
                                    oldWeaponToPlace = (ThingWithComps)OldWeapon.SplitOff(1);
                                    // The remaining stack stays in inventory
                                    if (AutoArmMod.settings?.debugLogging == true)
                                    {
                                        AutoArmLogger.Debug($"[{pawn.LabelShort}] Split stack: keeping {OldWeapon.stackCount} in inventory, dropping 1");
                                    }
                                }
                                else
                                {
                                    // Remove the entire item - we'll replace it atomically
                                    pawn.inventory.innerContainer.Remove(OldWeapon);
                                }
                                
                                // Add new weapon to inventory immediately
                                // This should succeed since we just freed up the slot
                                if (!pawn.inventory.innerContainer.TryAdd(NewWeapon))
                                {
                                    // Unexpected failure - try to restore old weapon
                                    AutoArmLogger.Error($"[{pawn.LabelShort}] CRITICAL: Failed to add {NewWeapon.Label} during atomic swap!");
                                    
                                    // Try to put old weapon back
                                    if (!pawn.inventory.innerContainer.TryAdd(oldWeaponToPlace))
                                    {
                                        // Can't restore - drop it
                                        GenPlace.TryPlaceThing(oldWeaponToPlace, dropPosition, pawn.Map, ThingPlaceMode.Near);
                                    }
                                    
                                    // Re-spawn new weapon since we couldn't pick it up
                                    if (!NewWeapon.Spawned)
                                    {
                                        GenPlace.TryPlaceThing(NewWeapon, dropPosition, pawn.Map, ThingPlaceMode.Near);
                                    }
                                    return;
                                }
                                
                                // Play the weapon's equip sound if it has one
                                if (NewWeapon.def.soundInteract != null)
                                {
                                    NewWeapon.def.soundInteract.PlayOneShot(new TargetInfo(pawn.Position, pawn.Map, false));
                                }
                                
                                // Success! Now place the old weapon on ground
                                GenPlace.TryPlaceThing(oldWeaponToPlace, dropPosition, pawn.Map, ThingPlaceMode.Near);
                                
                                // Unforbid the dropped weapon - it's an upgrade, not trash!
                                if (oldWeaponToPlace != null && oldWeaponToPlace.Spawned)
                                {
                                    oldWeaponToPlace.SetForbidden(false, false);
                                }
                                
                                // 6. Update SimpleSidearms memory AFTER successful swap
                                // The order here is important - inform of drop first, then add
                                InformOfDroppedSidearm(pawn, oldWeaponToPlace);
                                InformOfAddedSidearm(pawn, NewWeapon);
                                
                                // 6b. Maintain forced/preferred status for same-type upgrades
                                if ((wasForced || wasPreferred) && OldWeapon.def == NewWeapon.def)
                                {
                                    if (wasForced)
                                    {
                                        // Re-force the weapon type after swap
                                        // Note: This might briefly make it primary - SS will reorder
                                        SetWeaponAsForced(pawn, NewWeapon);
                                        if (AutoArmMod.settings?.debugLogging == true)
                                        {
                                            AutoArmLogger.Debug($"[{pawn.LabelShort}] Re-forced {NewWeapon.Label} after swap");
                                        }
                                    }
                                    // Note: Preferred status should be maintained automatically by SS
                                    // since we're just swapping same weapon type
                                }
                                
                                // 7. Mark old weapon as dropped for AutoArm tracking
                                DroppedItemTracker.MarkAsDropped(oldWeaponToPlace, 600);
                                
                                if (AutoArmMod.settings?.debugLogging == true)
                                {
                                    AutoArmLogger.Debug($"[{pawn.LabelShort}] Atomic swap successful: {OldWeapon.Label} -> {NewWeapon.Label}");
                                    AutoArmLogger.Debug($"[{pawn.LabelShort}] Dropped {oldWeaponToPlace.Label} at {dropPosition}");
                                }
                            }
                            catch (Exception swapEx)
                            {
                                AutoArmLogger.Error($"[{pawn.LabelShort}] Atomic swap failed: {swapEx.Message}", swapEx);
                                
                                // Emergency cleanup - ensure weapons end up somewhere
                                if (!NewWeapon.Spawned && NewWeapon.holdingOwner == null)
                                {
                                    GenPlace.TryPlaceThing(NewWeapon, dropPosition, pawn.Map, ThingPlaceMode.Near);
                                }
                                if (oldWeaponToPlace != null && oldWeaponToPlace.holdingOwner == null && !oldWeaponToPlace.Spawned)
                                {
                                    GenPlace.TryPlaceThing(oldWeaponToPlace, dropPosition, pawn.Map, ThingPlaceMode.Near);
                                }
                                return;
                            }

                            // Show notification for sidearm upgrade
                            if (AutoArmMod.settings?.showNotifications == true && 
                                PawnUtility.ShouldSendNotificationAbout(pawn))
                            {
                                Messages.Message("AutoArm_UpgradedSidearm".Translate(
                                    pawn.LabelShort.CapitalizeFirst(),
                                    OldWeapon.Label,
                                    NewWeapon.Label
                                ), new LookTargets(pawn), MessageTypeDefOf.SilentInput, false);
                            }

                            // Let SimpleSidearms reorder weapons if needed
                            // This ensures SS picks the best weapon as primary
                            ReorderWeaponsAfterEquip(pawn);
                            
                            if (AutoArmMod.settings?.debugLogging == true)
                            {
                                AutoArmLogger.Debug($"[{pawn.LabelShort}] Sidearm swap complete and reordered");
                            }
                        }
                        catch (Exception e)
                        {
                            AutoArmLogger.Error($"[{pawn.LabelShort}] Error during sidearm swap: {e.Message}", e);
                        }
                    },
                    defaultCompleteMode = ToilCompleteMode.Instant
                };
            }
        }
        
        /// <summary>
        /// JobDriver for swapping primary weapons - drops old primary at new weapon location
        /// </summary>
        public class JobDriver_SwapPrimary : JobDriver
        {
            private ThingWithComps NewWeapon => (ThingWithComps)job.targetA.Thing;
            private ThingWithComps OldWeapon => (ThingWithComps)job.targetB.Thing;

            public override bool TryMakePreToilReservations(bool errorOnFailed)
            {
                // Reserve the new weapon so no one else takes it
                return pawn.Reserve(NewWeapon, job, 1, -1, null, errorOnFailed);
            }

            protected override IEnumerable<Toil> MakeNewToils()
            {
                // Fail if new weapon disappears
                this.FailOnDestroyedNullOrForbidden(TargetIndex.A);
                this.FailOnDespawnedNullOrForbidden(TargetIndex.A);

                // Go to the new weapon
                yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.ClosestTouch)
                    .FailOnDespawnedNullOrForbidden(TargetIndex.A)
                    .FailOnSomeonePhysicallyInteracting(TargetIndex.A);

                // Do the swap - drop old primary and equip new weapon
                yield return new Toil
                {
                    initAction = delegate
                    {
                        try
                        {
                            // Verify both weapons still exist
                            if (NewWeapon == null || OldWeapon == null)
                            {
                                if (AutoArmMod.settings?.debugLogging == true)
                                {
                                    AutoArmLogger.Debug($"[{pawn.LabelShort}] Primary swap cancelled - weapon no longer exists");
                                }
                                return;
                            }

                            // Verify old weapon is still the primary
                            if (pawn.equipment?.Primary != OldWeapon)
                            {
                                AutoArmLogger.Warn($"[{pawn.LabelShort}] Primary swap aborted - {OldWeapon.Label} is no longer primary");
                                return;
                            }
                            
                            IntVec3 dropPosition = pawn.Position; // Drop at current position (new weapon location)
                            
                            if (AutoArmMod.settings?.debugLogging == true)
                            {
                                AutoArmLogger.Debug($"[{pawn.LabelShort}] Swapping primary: {OldWeapon.Label} -> {NewWeapon.Label}");
                            }
                            
                            // Store forced/preferred status before swap to maintain it
                            bool wasForced = false;
                            bool wasPreferred = false;
                            if (OldWeapon.def == NewWeapon.def)  // Same-type upgrade
                            {
                                IsWeaponTypeForced(pawn, OldWeapon.def, out wasForced, out wasPreferred);
                                if ((wasForced || wasPreferred) && AutoArmMod.settings?.debugLogging == true)
                                {
                                    AutoArmLogger.Debug($"[{pawn.LabelShort}] Will maintain {(wasForced ? "forced" : "preferred")} status for {NewWeapon.Label}");
                                }
                            }
                            
                            // 1. Drop the current primary weapon
                            ThingWithComps droppedWeapon;
                            if (!pawn.equipment.TryDropEquipment(OldWeapon, out droppedWeapon, dropPosition))
                            {
                                AutoArmLogger.Error($"[{pawn.LabelShort}] Failed to drop primary weapon {OldWeapon.Label}");
                                return;
                            }
                            
                            // Unforbid the dropped weapon - it's an upgrade, not trash!
                            if (droppedWeapon != null && droppedWeapon.Spawned)
                            {
                                droppedWeapon.SetForbidden(false, false);
                            }
                            
                            // 2. Pick up new weapon from ground
                            if (NewWeapon.Spawned)
                            {
                                NewWeapon.DeSpawn();
                            }
                            
                            // 3. Equip the new weapon
                            pawn.equipment.AddEquipment(NewWeapon);
                            
                            // Play the weapon's equip sound if it has one
                            if (NewWeapon.def.soundInteract != null)
                            {
                                NewWeapon.def.soundInteract.PlayOneShot(new TargetInfo(pawn.Position, pawn.Map, false));
                            }
                            
                            // 4. Update SimpleSidearms memory if loaded
                            if (IsLoaded())
                            {
                                // The old primary is now on the ground, inform SS
                                InformOfDroppedSidearm(pawn, droppedWeapon);
                                
                                // The new weapon is now primary, inform SS
                                InformOfAddedSidearm(pawn, NewWeapon);
                                
                                // Maintain forced/preferred status for same-type upgrades
                                if ((wasForced || wasPreferred) && OldWeapon.def == NewWeapon.def)
                                {
                                    if (wasForced)
                                    {
                                        SetWeaponAsForced(pawn, NewWeapon);
                                        if (AutoArmMod.settings?.debugLogging == true)
                                        {
                                            AutoArmLogger.Debug($"[{pawn.LabelShort}] Re-forced {NewWeapon.Label} after primary swap");
                                        }
                                    }
                                }
                                
                                // Let SimpleSidearms reorder if needed
                                ReorderWeaponsAfterEquip(pawn);
                            }
                            
                            // 5. Mark old weapon as dropped for AutoArm tracking
                            DroppedItemTracker.MarkAsDropped(droppedWeapon, 600);
                            
                            if (AutoArmMod.settings?.debugLogging == true)
                            {
                                AutoArmLogger.Debug($"[{pawn.LabelShort}] Primary swap successful: {OldWeapon.Label} -> {NewWeapon.Label}");
                                AutoArmLogger.Debug($"[{pawn.LabelShort}] Dropped {droppedWeapon.Label} at {dropPosition}");
                            }

                            // Show notification for primary upgrade
                            if (AutoArmMod.settings?.showNotifications == true && 
                                PawnUtility.ShouldSendNotificationAbout(pawn))
                            {
                                Messages.Message("AutoArm_UpgradedWeapon".Translate(
                                    pawn.LabelShort.CapitalizeFirst(),
                                    OldWeapon.Label,
                                    NewWeapon.Label
                                ), new LookTargets(pawn), MessageTypeDefOf.SilentInput, false);
                            }
                        }
                        catch (Exception e)
                        {
                            AutoArmLogger.Error($"[{pawn.LabelShort}] Error during primary swap: {e.Message}", e);
                        }
                    },
                    defaultCompleteMode = ToilCompleteMode.Instant
                };
            }
        }
    }
}

namespace AutoArm.Jobs
{
    // JobDriver_SwapSidearm and JobDriver_SwapPrimary are now in SimpleSidearmsCompat as nested classes
}

namespace AutoArm.Patches
{
    // REMOVED PROBLEMATIC PATCH - It was causing infinite recursion by calling the original method from within its own prefix
    // The patch attempted to intercept SimpleSidearms' job creation but the approach was flawed
    // TODO: If we need to fix SS job failures, use a different approach:
    // - Option 1: Patch the JobDriver's toils to handle drop failures gracefully
    // - Option 2: Have AutoArm check more frequently to beat SS to the upgrade
    // - Option 3: Add a user setting to disable SS's auto-upgrade feature
    
    /// <summary>
    /// Patch to detect when SimpleSidearms' EquipSecondary job fails
    /// </summary>
    [HarmonyPatch]
    public static class SimpleSidearms_JobDriver_EquipSecondary_Patch
    {
        private static bool _initialized = false;
        private static Type _jobDriverType = null;
        
        public static void Initialize()
        {
            if (_initialized || !SimpleSidearmsCompat.IsLoaded())
                return;
                
            try
            {
                // Find the EquipSecondary JobDriver
                _jobDriverType = GenTypes.AllTypes.FirstOrDefault(t => 
                    t.FullName?.Contains("SimpleSidearms") == true && 
                    t.Name == "JobDriver_EquipSecondary");
                    
                if (_jobDriverType == null)
                {
                    // Try alternative search
                    _jobDriverType = GenTypes.AllTypes.FirstOrDefault(t => 
                        t.BaseType == typeof(JobDriver) && 
                        t.FullName?.Contains("EquipSecondary") == true);
                }
                
                if (_jobDriverType != null)
                {
                    var harmony = AutoArmInit.harmonyInstance;
                    if (harmony != null)
                    {
                        // Patch the EndJobWith method to detect failures
                        var endJobMethod = typeof(JobDriver).GetMethod("EndJobWith", 
                            BindingFlags.Public | BindingFlags.Instance);
                            
                        if (endJobMethod != null)
                        {
                            var postfix = typeof(SimpleSidearms_JobDriver_EquipSecondary_Patch)
                                .GetMethod(nameof(EndJobWith_Postfix), BindingFlags.Static | BindingFlags.NonPublic);
                            harmony.Patch(endJobMethod, postfix: new HarmonyMethod(postfix));
                            
                            if (AutoArmMod.settings?.debugLogging == true)
                            {
                                AutoArmLogger.Debug("[SS JobFix] Patched JobDriver.EndJobWith for failure detection");
                            }
                        }
                    }
                }
                
                _initialized = true;
            }
            catch (Exception e)
            {
                AutoArmLogger.Warn($"[SS JobFix] Failed to patch EquipSecondary JobDriver: {e.Message}");
            }
        }
        
        private static void EndJobWith_Postfix(JobDriver __instance, JobCondition condition)
        {
            // Only care about EquipSecondary jobs that failed
            if (__instance?.job?.def?.defName != "EquipSecondary")
                return;
                
            if (condition != JobCondition.Succeeded && condition != JobCondition.None)
            {
                var pawn = __instance.pawn;
                var weapon = __instance.job?.targetA.Thing as ThingWithComps;
                
                if (pawn != null && weapon != null && AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.Debug($"[SS JobFix] EquipSecondary job FAILED for {pawn.LabelShort} trying to equip {weapon.Label}. Condition: {condition}");
                    
                    // Log why it might have failed
                    if (condition == JobCondition.Incompletable)
                    {
                        AutoArmLogger.Debug($"[SS JobFix] Job was incompletable - likely no valid drop space or path blocked");
                        
                        // Check current situation
                        float weight = MassUtility.GearAndInventoryMass(pawn);
                        float capacity = MassUtility.Capacity(pawn);
                        AutoArmLogger.Debug($"[SS JobFix] Pawn weight: {weight:F1}/{capacity:F1} kg");
                        
                        var room = pawn.GetRoom();
                        if (room != null)
                        {
                            AutoArmLogger.Debug($"[SS JobFix] Room size: {room.CellCount} cells, Outdoors: {room.PsychologicallyOutdoors}");
                        }
                    }
                }
            }
        }
    }
    
    /// <summary>
    /// Initialize all SimpleSidearms patches
    /// </summary>
    [HarmonyPatch(typeof(Game), "InitNewGame")]
    public static class SimpleSidearms_AllPatches_Initializer
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            if (SimpleSidearmsCompat.IsLoaded())
            {
                SimpleSidearms_JobDriver_EquipSecondary_Patch.Initialize();
            }
        }
    }
}