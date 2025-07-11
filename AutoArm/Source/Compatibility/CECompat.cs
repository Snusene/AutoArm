using HarmonyLib;
using RimWorld;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;
using Verse.AI;

namespace AutoArm
{
    public static class CECompat
    {
        private static bool? _isLoaded = null;
        private static bool _initialized = false;

        // CE Types
        private static Type compPropertiesAmmoUserType;
        private static Type ammoLinkType;
        private static Type controllerType;
        private static Type settingsType;

        // CE Fields/Properties
        private static FieldInfo ammoSetField;
        private static FieldInfo ammoField; // Field in AmmoLink that contains the AmmoDef
        private static PropertyInfo enableAmmoSystemProperty;

        // Cache for ammo lookups
        private static Dictionary<ThingDef, List<ThingDef>> weaponAmmoCache = new Dictionary<ThingDef, List<ThingDef>>();
        
        private static int lastCacheClearTick = 0;
        private const int CacheClearInterval = 60000; // Clear every 1000 seconds

        private static void CheckCacheClear()
        {
            if (Find.TickManager.TicksGame - lastCacheClearTick > CacheClearInterval)
            {
                ClearCache();
                lastCacheClearTick = Find.TickManager.TicksGame;
            }
        }
        // Check if CE is loaded
        public static bool IsLoaded()
        {
            if (_isLoaded == null)
            {
                // Check for Combat Extended
                _isLoaded = ModLister.AllInstalledMods.Any(m =>
                    m.Active && (
                        m.PackageIdPlayerFacing.ToLower().Contains("ceteam.combatextended") ||
                        m.Name.ToLower().Contains("combat extended")));

                if (AutoArmMod.settings?.debugLogging == true)
                {
                    Log.Message($"[AutoArm] Combat Extended detection result: {_isLoaded}");
                }
            }
            return _isLoaded.Value;
        }

        private static void EnsureInitialized()
        {
            if (_initialized || !IsLoaded())
                return;

            _initialized = true;

            try
            {
                // Find CE namespace types
                var ceTypes = GenTypes.AllTypes.Where(t =>
                    t.Namespace == "CombatExtended" ||
                    t.FullName?.StartsWith("CombatExtended.") == true).ToList();

                if (AutoArmMod.settings?.debugLogging == true)
                {
                    Log.Message($"[AutoArm] Found {ceTypes.Count} Combat Extended types");
                }

                // Find CompProperties_AmmoUser
                compPropertiesAmmoUserType = ceTypes.FirstOrDefault(t => t.Name == "CompProperties_AmmoUser");
                if (compPropertiesAmmoUserType == null)
                {
                    Log.Warning("[AutoArm] Could not find CompProperties_AmmoUser type");
                    return;
                }

                // Find AmmoLink type
                ammoLinkType = ceTypes.FirstOrDefault(t => t.Name == "AmmoLink");
                if (ammoLinkType == null)
                {
                    Log.Warning("[AutoArm] Could not find AmmoLink type");
                    return;
                }

                // Get ammoSet field from CompProperties_AmmoUser
                ammoSetField = compPropertiesAmmoUserType.GetField("ammoSet");
                if (ammoSetField == null)
                {
                    Log.Warning("[AutoArm] Could not find ammoSet field");
                    return;
                }

                // Get ammo field from AmmoLink
                ammoField = ammoLinkType.GetField("ammo");
                if (ammoField == null)
                {
                    Log.Warning("[AutoArm] Could not find ammo field in AmmoLink");
                    return;
                }

                // Find Controller type and settings
                controllerType = ceTypes.FirstOrDefault(t => t.Name == "Controller");
                if (controllerType != null)
                {
                    var settingsField = controllerType.GetField("settings", BindingFlags.Public | BindingFlags.Static);
                    if (settingsField != null)
                    {
                        var settingsInstance = settingsField.GetValue(null);
                        if (settingsInstance != null)
                        {
                            settingsType = settingsInstance.GetType();
                            enableAmmoSystemProperty = settingsType.GetProperty("EnableAmmoSystem");
                        }
                    }
                }

                if (AutoArmMod.settings?.debugLogging == true)
                {
                    Log.Message("[AutoArm] Combat Extended compatibility initialized successfully");
                }
            }
            catch (Exception e)
            {
                Log.Warning($"[AutoArm] Failed to initialize Combat Extended compatibility: {e.Message}");
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    Log.Warning($"[AutoArm] Stack trace: {e.StackTrace}");
                }
            }
        }

        // Check if ammo system is enabled
        private static bool IsAmmoSystemEnabled()
        {
            if (!IsLoaded())
                return false;

            EnsureInitialized();

            if (controllerType == null || enableAmmoSystemProperty == null)
                return true; // Default to true if we can't check

            try
            {
                var settingsField = controllerType.GetField("settings", BindingFlags.Public | BindingFlags.Static);
                if (settingsField != null)
                {
                    var settings = settingsField.GetValue(null);
                    if (settings != null)
                    {
                        var enabled = enableAmmoSystemProperty.GetValue(settings);
                        if (enabled is bool boolValue)
                        {
                            return boolValue;
                        }
                    }
                }
            }
            catch
            {
                // Default to true if we can't determine
            }

            return true;
        }

        // Main check - should we skip this weapon due to CE requirements?
        public static bool ShouldSkipWeaponForCE(ThingWithComps weapon, Pawn pawn)
        {
            if (!IsLoaded() || weapon == null || pawn == null)
                return false;

            // Check if CE ammo checking is enabled in our settings
            if (AutoArmMod.settings?.checkCEAmmo != true)
                return false;

            // Check if CE's ammo system is enabled
            if (!IsAmmoSystemEnabled())
                return false;

            EnsureInitialized();

            // Only check ranged weapons (melee doesn't need ammo)
            if (!weapon.def.IsRangedWeapon)
                return false;

            try
            {
                // Get ammo types for this weapon
                var ammoTypes = GetAmmoTypesForWeapon(weapon.def);

                if (ammoTypes == null || ammoTypes.Count == 0)
                {
                    // No ammo needed - allow weapon
                    return false;
                }

                // Check if any ammo is available
                bool hasAmmo = false;

                // Check pawn's inventory first
                if (pawn.inventory?.innerContainer != null)
                {
                    foreach (var ammoType in ammoTypes)
                    {
                        if (pawn.inventory.innerContainer.Any(t => t.def == ammoType))
                        {
                            hasAmmo = true;
                            break;
                        }
                    }
                }

                // Check map for ammo if pawn doesn't have any
                if (!hasAmmo && pawn.Map != null)
                {
                    hasAmmo = IsAmmoAvailableOnMap(ammoTypes, pawn);
                }

                if (!hasAmmo && AutoArmMod.settings?.debugLogging == true)
                {
                    Log.Message($"[AutoArm] Skipping {weapon.def.defName} for {pawn.Name} - no ammo available");
                }

                return !hasAmmo; // Skip if no ammo
            }
            catch (Exception e)
            {
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    Log.Warning($"[AutoArm] Error checking CE ammo for {weapon.def.defName}: {e.Message}");
                }
                return false; // On error, don't skip
            }
        }

        // Get ammo types that a weapon can use
        private static List<ThingDef> GetAmmoTypesForWeapon(ThingDef weaponDef)
        {
            if (weaponDef == null)
                return null;

            CheckCacheClear();

            // Check cache first
            if (weaponAmmoCache.TryGetValue(weaponDef, out var cached))
                return cached;

            var ammoTypes = new List<ThingDef>();

            try
            {
                // Get CompProperties_AmmoUser from weapon
                var ammoUserComp = weaponDef.comps?.FirstOrDefault(c =>
                    c.GetType() == compPropertiesAmmoUserType ||
                    c.GetType().Name == "CompProperties_AmmoUser");

                if (ammoUserComp == null)
                {
                    // No ammo user comp - weapon doesn't use ammo
                    weaponAmmoCache[weaponDef] = ammoTypes;
                    return ammoTypes;
                }

                // Get ammoSet from the comp
                var ammoSet = ammoSetField?.GetValue(ammoUserComp);
                if (ammoSet == null)
                {
                    weaponAmmoCache[weaponDef] = ammoTypes;
                    return ammoTypes;
                }

                // Get ammoTypes field from AmmoSetDef
                var ammoTypesFieldInSet = ammoSet.GetType().GetField("ammoTypes");
                if (ammoTypesFieldInSet != null)
                {
                    var ammoTypesList = ammoTypesFieldInSet.GetValue(ammoSet) as IEnumerable;
                    if (ammoTypesList != null)
                    {
                        foreach (var ammoLink in ammoTypesList)
                        {
                            if (ammoLink != null && ammoField != null)
                            {
                                // Get the ammo ThingDef from the AmmoLink
                                var ammoDef = ammoField.GetValue(ammoLink) as ThingDef;
                                if (ammoDef != null)
                                {
                                    ammoTypes.Add(ammoDef);
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
                    Log.Warning($"[AutoArm] Error getting ammo types for {weaponDef.defName}: {e.Message}");
                }
            }

            // Cache the result
            weaponAmmoCache[weaponDef] = ammoTypes;
            return ammoTypes;
        }

        // Check if ammo is available on the map
        private static bool IsAmmoAvailableOnMap(List<ThingDef> ammoTypes, Pawn pawn)
        {
            if (ammoTypes == null || ammoTypes.Count == 0 || pawn.Map == null)
                return false;

            foreach (var ammoType in ammoTypes)
            {
                // Check stockpiles and general map
                var ammoThings = pawn.Map.listerThings.ThingsOfDef(ammoType);

                foreach (var ammoThing in ammoThings)
                {
                    // Make sure it's accessible
                    if (!ammoThing.IsForbidden(pawn) &&
                        pawn.CanReserveAndReach(ammoThing, PathEndMode.ClosestTouch, Danger.Some))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        // Get a score modifier based on ammo availability
        public static float GetAmmoScoreModifier(ThingWithComps weapon, Pawn pawn)
        {
            if (!IsLoaded() || weapon == null || !weapon.def.IsRangedWeapon)
                return 1f; // No modifier

            EnsureInitialized();

            // Check if CE ammo checking is enabled in settings
            if (AutoArmMod.settings?.checkCEAmmo != true)
                return 1f;

            // Check if CE's ammo system is enabled
            if (!IsAmmoSystemEnabled())
                return 1f;

            try
            {
                var ammoTypes = GetAmmoTypesForWeapon(weapon.def);
                if (ammoTypes == null || ammoTypes.Count == 0)
                    return 1f; // No ammo needed

                // Check how much ammo is available
                int totalAmmo = 0;

                // Count ammo in inventory
                if (pawn.inventory?.innerContainer != null)
                {
                    foreach (var ammoType in ammoTypes)
                    {
                        totalAmmo += pawn.inventory.innerContainer.Where(t => t.def == ammoType).Sum(t => t.stackCount);
                    }
                }

                // If pawn already has ammo, prefer this weapon
                if (totalAmmo > 50)
                    return 1.2f; // 20% bonus
                else if (totalAmmo > 0)
                    return 1.1f; // 10% bonus

                // Check map availability
                if (IsAmmoAvailableOnMap(ammoTypes, pawn))
                    return 0.9f; // 10% penalty - need to fetch ammo
                else
                    return 0.5f; // 50% penalty - no readily available ammo
            }
            catch
            {
                return 1f; // No modifier on error
            }
        }

        // Clear cache when needed
        public static void ClearCache()
        {
            weaponAmmoCache.Clear();
        }

        // Settings integration
        public static bool ShouldCheckAmmo()
        {
            return IsLoaded() && IsAmmoSystemEnabled() && AutoArmMod.settings?.checkCEAmmo == true;
        }
    }
}