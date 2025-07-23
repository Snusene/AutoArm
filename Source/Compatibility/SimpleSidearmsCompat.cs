using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;
using Verse.AI;

namespace AutoArm
{
    // Refactored to use helper classes (fixes redundancies #14, #18, #22)
    public static class SimpleSidearmsCompat
    {
        private static bool? _isLoaded = null;
        private static bool _initialized = false;

        // Core types and methods still needed
        private static Type compSidearmMemoryType;
        private static Type weaponAssingmentType;
        private static Type gettersFiltersType;
        private static Type statCalculatorType;
        private static Type thingDefStuffDefPairType;

        private static MethodInfo equipSpecificWeaponFromInventoryMethod;
        private static MethodInfo canPickupSidearmInstanceMethod;
        private static MethodInfo informOfDroppedSidearmMethod;
        private static PropertyInfo rememberedWeaponsProperty;
        private static FieldInfo thingField;
        private static FieldInfo stuffField;
        private static JobDef equipSecondaryJobDef;

        // Methods for swapping sidearms
        private static MethodInfo swapToSidearmMethod;
        private static MethodInfo sidearmToInventoryMethod;

        // Track pawns with sidearms temporarily in primary slot
        private static HashSet<Pawn> pawnsWithTemporarySidearmEquipped = new HashSet<Pawn>();

        // Track recently checked upgrade combinations
        private static Dictionary<string, int> recentlyCheckedUpgrades = new Dictionary<string, int>();
        private const int UPGRADE_CHECK_COOLDOWN = 2500;

        // Track when we last logged "forced weapon" for each pawn
        private static Dictionary<Pawn, int> lastForcedWeaponLogTick = new Dictionary<Pawn, int>();
        private const int FORCED_WEAPON_LOG_COOLDOWN = 10000;

        // Track pending sidearm upgrades
        private static Dictionary<Pawn, SidearmUpgradeInfo> pendingSidearmUpgrades = new Dictionary<Pawn, SidearmUpgradeInfo>();

        // Cache for PrimaryIsRememberedSidearm checks
        private static Dictionary<Pawn, bool> _primaryIsSidearmCache = new Dictionary<Pawn, bool>();
        private static Dictionary<Pawn, int> _primaryIsSidearmCacheTick = new Dictionary<Pawn, int>();
        private const int SIDEARM_CHECK_CACHE_LIFETIME = 120;

        // Reflection cache keys
        private const string REFLECTION_KEY_GETSIDEARMCOMP = "SimpleSidearms.GetSidearmComp";
        private const string REFLECTION_KEY_SKIPDANGEROUS = "SimpleSidearms.SkipDangerous";
        private const string REFLECTION_KEY_SKIPEMP = "SimpleSidearms.SkipEMP";
        private const string REFLECTION_KEY_ALLOWBLOCKED = "SimpleSidearms.AllowBlocked";

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
                _isLoaded = ModLister.GetActiveModWithIdentifier("PeteTimesSix.SimpleSidearms") != null ||
                           ModLister.GetActiveModWithIdentifier("petetimessix.simplesidearms") != null ||
                           ModLister.AllInstalledMods.Any(m => m.Active && 
                               (m.Name == "Simple Sidearms" || 
                                m.PackageIdPlayerFacing.ToLower().Contains("simplesidearms")));
                
                if (_isLoaded.Value)
                {
                    AutoArmDebug.Log("Simple Sidearms detected and loaded");
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
                weaponAssingmentType = GenTypes.AllTypes.FirstOrDefault(t =>
                    t.FullName == $"{ssNamespace}.Utilities.WeaponAssingment");
                gettersFiltersType = GenTypes.AllTypes.FirstOrDefault(t =>
                    t.FullName == $"{ssNamespace}.Utilities.GettersFilters");
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

                if (weaponAssingmentType != null)
                {
                    swapToSidearmMethod = weaponAssingmentType.GetMethod(
                        "SetPrimary",
                        new Type[] { typeof(Pawn), typeof(ThingWithComps), typeof(bool) });
                    sidearmToInventoryMethod = weaponAssingmentType.GetMethod(
                        "SidearmToInventory", 
                        new Type[] { typeof(Pawn), typeof(ThingWithComps), typeof(bool) });
                    
                    if (swapToSidearmMethod == null)
                    {
                        swapToSidearmMethod = weaponAssingmentType.GetMethod("TrySwapToSidearm");
                    }
                }
                
                equipSecondaryJobDef = DefDatabase<JobDef>.GetNamedSilentFail("EquipSecondary");

                // Cache settings access methods
                CacheSettingsAccess();

                _initialized = true;
                AutoArmDebug.Log("Simple Sidearms compatibility initialized successfully");
            }
            catch (Exception e)
            {
                Log.Warning($"[AutoArm] Failed to initialize Simple Sidearms compatibility: {e.Message}");
                AutoArmDebug.Log($"Stack trace: {e.StackTrace}");
            }
        }

        private static void CacheSettingsAccess()
        {
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
                            ReflectionHelper.CacheFieldGetter(REFLECTION_KEY_SKIPDANGEROUS, 
                                () => {
                                    var settings = settingsField.GetValue(null);
                                    var field = settingsType.GetField("SkipDangerousWeapons", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                                    return field?.GetValue(settings) ?? true;
                                });
                            
                            ReflectionHelper.CacheFieldGetter(REFLECTION_KEY_SKIPEMP, 
                                () => {
                                    var settings = settingsField.GetValue(null);
                                    var field = settingsType.GetField("SkipEMPWeapons", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                                    return field?.GetValue(settings) ?? false;
                                });
                            
                            ReflectionHelper.CacheFieldGetter(REFLECTION_KEY_ALLOWBLOCKED, 
                                () => {
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
                AutoArmDebug.Log($"WARNING: Failed to cache SimpleSidearms settings access: {e.Message}");
            }
        }

        public static void MarkWeaponAsRecentlyDropped(Thing weapon)
        {
            if (weapon != null)
            {
                DroppedItemTracker.MarkAsDropped(weapon);
                AutoArmDebug.Log($"Marked {weapon.Label} as recently dropped");
            }
        }

        private static bool IsRecentlyDropped(Thing weapon)
        {
            return weapon != null && DroppedItemTracker.WasRecentlyDropped(weapon);
        }

        private static void InformSimpleSidearmsOfDrop(Pawn pawn, ThingWithComps weapon)
        {
            if (weapon == null || !IsLoaded())
                return;

            MarkWeaponAsRecentlyDropped(weapon);

            try
            {
                var comp = GetSidearmComp(pawn);
                if (comp != null && informOfDroppedSidearmMethod != null)
                {
                    informOfDroppedSidearmMethod.Invoke(comp, new object[] { weapon, true });
                    AutoArmDebug.LogWeapon(pawn, weapon, "Informed SS of weapon drop");
                }
            }
            catch (Exception e)
            {
                AutoArmDebug.Log($"WARNING: Failed to inform SS of dropped weapon: {e.Message}");
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
                AutoArmDebug.Log($"WARNING: Error checking sidearm compatibility: {e.Message}");
            }

            return true;
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
                
            // Check cache first
            int currentTick = Find.TickManager.TicksGame;
            if (_primaryIsSidearmCache.TryGetValue(pawn, out bool cachedResult) &&
                _primaryIsSidearmCacheTick.TryGetValue(pawn, out int cacheTick) &&
                currentTick - cacheTick < SIDEARM_CHECK_CACHE_LIFETIME)
            {
                return cachedResult;
            }
                
            EnsureInitialized();
            if (!_initialized)
                return false;
                
            bool result = false;
            try
            {
                var comp = GetSidearmComp(pawn);
                if (comp == null)
                {
                    result = false;
                }
                else
                {
                    var rememberedSidearms = GetCurrentSidearmDefs(pawn, comp);
                    var currentWeaponDef = pawn.equipment.Primary.def;
                    result = rememberedSidearms.Contains(currentWeaponDef);
                    
                    // Debug logging (only if debug mode is on)
                    if (AutoArmMod.settings?.debugLogging == true)
                    {
                        AutoArmDebug.LogPawn(pawn, $"PrimaryIsRememberedSidearm: {currentWeaponDef.defName} = {result} (remembered: {string.Join(", ", rememberedSidearms.Select(d => d.defName))})");
                    }
                }
            }
            catch
            {
                result = false;
            }
            
            // Cache the result
            _primaryIsSidearmCache[pawn] = result;
            _primaryIsSidearmCacheTick[pawn] = currentTick;
            
            return result;
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
                    
                    AutoArmDebug.LogWeapon(pawn, sidearm, "Swapped sidearm to primary");
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
                        AutoArmDebug.LogWeapon(pawn, weapon, "Moved back to inventory");
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
        
        public static Job TryGetSidearmUpgradeJob(Pawn pawn)
        {
            if (!IsLoaded() || pawn == null || !pawn.IsColonist)
                return null;
                
            // Check if temporary colonists are allowed
            if (AutoArmMod.settings?.allowTemporaryColonists != true && JobGiverHelpers.IsTemporaryColonist(pawn))
                return null;
                
            // Use timing helper for failed searches
            if (TimingHelper.IsOnCooldown(pawn, TimingHelper.CooldownType.FailedUpgradeSearch))
                return null;

            if (AutoArmMod.settings?.autoEquipSidearms != true)
            {
                AutoArmDebug.Log("Sidearm auto-equip disabled in settings");
                return null;
            }

            EnsureInitialized();
            if (!_initialized)
            {
                AutoArmDebug.Log("SimpleSidearms not initialized properly");
                return null;
            }
            
            if (equipSecondaryJobDef == null)
            {
                AutoArmDebug.Log("Warning: EquipSecondary JobDef not found, will use vanilla Equip");
            }

            try
            {
                var weaponCounts = new Dictionary<ThingDef, int>();
                var worstQualityWeapons = new Dictionary<ThingDef, ThingWithComps>();

                // Count primary weapon
                if (pawn.equipment?.Primary != null)
                {
                    var def = pawn.equipment.Primary.def;
                    weaponCounts[def] = 1;
                }

                // Count and track inventory weapons
                foreach (var item in pawn.inventory?.innerContainer ?? Enumerable.Empty<Thing>())
                {
                    if (item is ThingWithComps weapon && weapon.def.IsWeapon)
                    {
                        var def = weapon.def;
                        weaponCounts[def] = weaponCounts.ContainsKey(def) ? weaponCounts[def] + 1 : 1;

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

                var filter = pawn.outfits?.CurrentApparelPolicy?.filter;
                    
                var nearbyWeapons = ImprovedWeaponCacheManager.GetWeaponsNear(pawn.Map, pawn.Position, 40f)
                    .Where(w => w != null &&
                               !w.IsForbidden(pawn) &&
                               !IsRecentlyDropped(w) && 
                               (filter == null || filter.Allows(w.def)) &&
                               pawn.CanReserveAndReach(w, PathEndMode.ClosestTouch, Danger.Deadly))
                    .OrderBy(w => w.Position.DistanceToSquared(pawn.Position))
                    .Take(20);

                foreach (var weapon in nearbyWeapons)
                {
                    int currentCount = weaponCounts.ContainsKey(weapon.def) ? weaponCounts[weapon.def] : 0;

                    if (currentCount > 0)
                    {
                        // Skip if we already have 2 or more of this weapon type
                        if (currentCount >= 2)
                        {
                            continue;
                        }
                        
                        // If we have exactly 1, only proceed if upgrades are enabled AND this is actually an upgrade
                        if (currentCount == 1)
                        {
                            // Skip if upgrades are disabled
                            if (AutoArmMod.settings?.allowSidearmUpgrades != true)
                            {
                                continue;
                            }
                            
                            // Skip if we don't have an existing weapon to compare with
                            if (!worstQualityWeapons.ContainsKey(weapon.def))
                            {
                                continue;
                            }
                            
                            var existingWeapon = worstQualityWeapons[weapon.def];

                            // Debug logging for forced weapon check
                            bool isDefForced = ForcedWeaponHelper.IsWeaponDefForced(pawn, existingWeapon.def);
                            if (AutoArmMod.settings?.debugLogging == true)
                            {
                                var forcedDefs = ForcedWeaponHelper.GetForcedWeaponDefs(pawn);
                                AutoArmDebug.LogPawn(pawn, $"Checking sidearm upgrade for {existingWeapon.def.defName}: forced={isDefForced}, all forced defs: {string.Join(", ", forcedDefs.Select(d => d.defName))}");
                            }

                            // Skip if the weapon type is forced
                            if (isDefForced)
                            {
                                int tick = Find.TickManager.TicksGame;
                                if (!lastForcedWeaponLogTick.TryGetValue(pawn, out int lastLog) || 
                                    tick - lastLog > FORCED_WEAPON_LOG_COOLDOWN)
                                {
                                    AutoArmDebug.LogPawn(pawn, $"Skipping upgrade of forced weapon type: {existingWeapon.def.defName}");
                                    lastForcedWeaponLogTick[pawn] = tick;
                                }
                                continue; 
                            }

                            QualityCategory existingQuality, newQuality;
                            bool hasExistingQ = existingWeapon.TryGetQuality(out existingQuality);
                            bool hasNewQ = weapon.TryGetQuality(out newQuality);

                            // Only proceed if this is actually a quality upgrade
                            if (!hasExistingQ || !hasNewQ || newQuality <= existingQuality)
                            {
                                continue;
                            }
                            
                            // This is a valid upgrade - check cooldowns
                            string upgradeKey = $"{pawn.ThingID}_{existingWeapon.ThingID}_{weapon.ThingID}";
                            if (recentlyCheckedUpgrades.TryGetValue(upgradeKey, out int lastCheckTick))
                            {
                                if (Find.TickManager.TicksGame - lastCheckTick < UPGRADE_CHECK_COOLDOWN)
                                {
                                    continue;
                                }
                            }
                            recentlyCheckedUpgrades[upgradeKey] = Find.TickManager.TicksGame;
                            
                            bool willIgnoreRestriction = false;
                            string upgradeReason;
                            if (!CanPickupWeaponAsSidearm(weapon, pawn, out upgradeReason))
                            {
                                if (upgradeReason != null && 
                                    (upgradeReason.ToLower().Contains("heavy") || 
                                     upgradeReason.ToLower().Contains("weight") ||
                                     upgradeReason.ToLower().Contains("mass") ||
                                     upgradeReason.ToLower().Contains("slots full") ||
                                     upgradeReason.ToLower().Contains("slot full") ||
                                     upgradeReason.ToLower().Contains("no space") ||
                                     upgradeReason.ToLower().Contains("capacity")))
                                {
                                    willIgnoreRestriction = true;
                                }
                                else
                                {
                                    continue;
                                }
                            }
                            
                            if (willIgnoreRestriction)
                            {
                                AutoArmDebug.LogPawn(pawn, $"Upgrading sidearm (ignoring SS restriction): {existingWeapon.Label} -> {weapon.Label}");
                            }
                            else
                            {
                                AutoArmDebug.LogPawn(pawn, $"Found upgrade: {existingWeapon.Label} -> {weapon.Label}");
                            }

                            if (pawn.equipment?.Primary == existingWeapon)
                            {
                                if (ForcedWeaponHelper.IsForced(pawn, existingWeapon))
                                {
                                    AutoArmDebug.LogWeapon(pawn, existingWeapon, "Skipping upgrade of forced primary weapon");
                                    continue; 
                                }
                                
                                AutoArmDebug.LogPawn(pawn, "Skipping primary weapon in sidearm check - should be handled by main weapon upgrade");
                                continue;
                            }
                            else
                            {
                                AutoArmDebug.LogPawn(pawn, $"Will upgrade sidearm using swap method: {existingWeapon.Label} -> {weapon.Label}");
                                
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
                                    
                                    AutoArmDebug.LogPawn(pawn, "Created swap-based upgrade job");
                                    return equipJob;
                                }
                                else
                                {
                                    pendingSidearmUpgrades.Remove(pawn);
                                    Log.Warning($"[AutoArm] Failed to swap sidearm for upgrade");
                                }
                            }
                        }
                        
                        // If we get here, we have a weapon of this type but it's not an upgrade
                        // Skip to next weapon
                        continue;
                    }

                    if (ForcedWeaponHelper.IsWeaponDefForced(pawn, weapon.def))
                    {
                        AutoArmDebug.LogPawn(pawn, $"Skipping {weapon.Label} - weapon type is forced");
                        continue;
                    }
                    
                    string reason;
                    if (!CanPickupWeaponAsSidearm(weapon, pawn, out reason))
                    {
                        continue;
                    }

                    AutoArmDebug.LogWeapon(pawn, weapon, "Picking up new sidearm");
                    
                    if (equipSecondaryJobDef == null)
                    {
                        Log.Error("[AutoArm] EquipSecondary JobDef not found - Simple Sidearms may not be properly initialized");
                        return null;  // Don't try to pick up sidearms if we can't do it properly
                    }
                    
                    return JobMaker.MakeJob(equipSecondaryJobDef, weapon);
                }

                // Track failed search
                TimingHelper.SetCooldown(pawn, TimingHelper.CooldownType.FailedUpgradeSearch);
                return null;
            }
            catch (Exception e)
            {
                AutoArmDebug.Log($"ERROR in sidearm check: {e.Message}");
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
                _primaryIsSidearmCache.Remove(pawn);
                _primaryIsSidearmCacheTick.Remove(pawn);
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
            
            // Clean up primary sidearm cache
            var expiredCacheEntries = _primaryIsSidearmCacheTick
                .Where(kvp => Find.TickManager.TicksGame - kvp.Value > SIDEARM_CHECK_CACHE_LIFETIME * 2)
                .Select(kvp => kvp.Key)
                .ToList();
                
            foreach (var pawn in expiredCacheEntries)
            {
                _primaryIsSidearmCache.Remove(pawn);
                _primaryIsSidearmCacheTick.Remove(pawn);
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
            _primaryIsSidearmCache.Clear();
            _primaryIsSidearmCacheTick.Clear();
            lastForcedWeaponLogTick.Clear();
            
            AutoArmDebug.Log("Cleared sidearm upgrade state after load");
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
                
                AutoArmDebug.Log($"Cancelled pending upgrade for {pawn.Name}");
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
                AutoArmDebug.Log($"WARNING: Error getting current sidearms: {e.Message}");
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
                
            if (!upgradeInfo.isTemporarySwap)
                return;
                
            try
            {
                AutoArmDebug.Log($"Completing sidearm upgrade for {pawn.Name}");
                
                if (upgradeInfo.oldWeapon != null && !upgradeInfo.oldWeapon.Destroyed)
                {
                    MarkWeaponAsRecentlyDropped(upgradeInfo.oldWeapon);
                    AutoArmDebug.Log($"Marked old weapon {upgradeInfo.oldWeapon.Label} as recently dropped");
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
                        AutoArmDebug.Log($"Moved upgraded weapon {equippedWeapon.Label} to inventory");
                    }
                }
                
                if (upgradeInfo.originalPrimary != null && !upgradeInfo.originalPrimary.Destroyed)
                {
                    if (pawn.inventory?.innerContainer?.Contains(upgradeInfo.originalPrimary) == true)
                    {
                        pawn.inventory.innerContainer.Remove(upgradeInfo.originalPrimary);
                        pawn.equipment.AddEquipment(upgradeInfo.originalPrimary);
                        
                        AutoArmDebug.Log($"Restored original primary weapon {upgradeInfo.originalPrimary.Label}");
                    }
                }
                
                if (AutoArmMod.settings?.showNotifications == true && 
                    PawnUtility.ShouldSendNotificationAbout(pawn))
                {
                    Messages.Message("AutoArm_UpgradedSidearm".Translate(
                        pawn.LabelShort.CapitalizeFirst(),
                        upgradeInfo.oldWeapon?.Label ?? "old sidearm",
                        equippedWeapon.Label ?? "new sidearm"
                    ), new LookTargets(pawn), MessageTypeDefOf.SilentInput, false);
                }
            }
            catch (Exception e)
            {
                Log.Error($"[AutoArm] Error completing sidearm upgrade: {e.Message}");
            }
            finally
            {
                pendingSidearmUpgrades.Remove(pawn);
                pawnsWithTemporarySidearmEquipped.Remove(pawn);
            }
        }

        // Methods that are called from elsewhere in the mod
        public static int GetMaxSidearmsForPawn(Pawn pawn)
        {
            // This method doesn't use helper caching because it's rarely called
            // and the implementation is already quite efficient
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
                AutoArmDebug.Log($"WARNING: Error getting max sidearms: {e.Message}");
            }

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
}
