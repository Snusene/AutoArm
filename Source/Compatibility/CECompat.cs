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

        private static Type compPropertiesAmmoUserType;
        private static Type ammoLinkType;
        private static Type controllerType;
        private static Type settingsType;

        private static FieldInfo ammoSetField;
        private static FieldInfo ammoField;
        private static PropertyInfo enableAmmoSystemProperty;

        private static Dictionary<ThingDef, List<ThingDef>> weaponAmmoCache = new Dictionary<ThingDef, List<ThingDef>>();

        private static int lastCacheClearTick = 0;
        private const int CacheClearInterval = 60000;

        private static void CheckCacheClear()
        {
            if (Find.TickManager.TicksGame - lastCacheClearTick > CacheClearInterval)
            {
                ClearCache();
                lastCacheClearTick = Find.TickManager.TicksGame;
            }
        }

        public static bool IsLoaded()
        {
            if (_isLoaded == null)
            {
                _isLoaded = ModLister.AllInstalledMods.Any(m =>
                    m.Active && (
                        m.PackageIdPlayerFacing.Equals("CETeam.CombatExtended", StringComparison.OrdinalIgnoreCase) ||
                        m.PackageIdPlayerFacing.Equals("CETeam.CombatExtended.Unofficial", StringComparison.OrdinalIgnoreCase)
                    ));

                if (AutoArmMod.settings?.debugLogging == true)
                {
                    Log.Message($"[AutoArm] Combat Extended detection result: {_isLoaded}");
                    if (_isLoaded.Value)
                    {
                        var ceMod = ModLister.AllInstalledMods.FirstOrDefault(m =>
                            m.Active && (
                                m.PackageIdPlayerFacing.Equals("CETeam.CombatExtended", StringComparison.OrdinalIgnoreCase) ||
                                m.PackageIdPlayerFacing.Equals("CETeam.CombatExtended.Unofficial", StringComparison.OrdinalIgnoreCase)
                            ));
                        if (ceMod != null)
                        {
                            Log.Message($"[AutoArm] Detected CE mod: {ceMod.Name} ({ceMod.PackageIdPlayerFacing})");
                        }
                    }
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
                var ceTypes = GenTypes.AllTypes.Where(t =>
                    t.Namespace == "CombatExtended" ||
                    t.FullName?.StartsWith("CombatExtended.") == true).ToList();

                if (AutoArmMod.settings?.debugLogging == true)
                {
                    Log.Message($"[AutoArm] Found {ceTypes.Count} Combat Extended types");
                }

                compPropertiesAmmoUserType = ceTypes.FirstOrDefault(t => t.Name == "CompProperties_AmmoUser");
                if (compPropertiesAmmoUserType == null)
                {
                    Log.Warning("[AutoArm] Could not find CompProperties_AmmoUser type");
                    return;
                }

                ammoLinkType = ceTypes.FirstOrDefault(t => t.Name == "AmmoLink");
                if (ammoLinkType == null)
                {
                    Log.Warning("[AutoArm] Could not find AmmoLink type");
                    return;
                }

                ammoSetField = compPropertiesAmmoUserType.GetField("ammoSet");
                if (ammoSetField == null)
                {
                    Log.Warning("[AutoArm] Could not find ammoSet field");
                    return;
                }

                ammoField = ammoLinkType.GetField("ammo");
                if (ammoField == null)
                {
                    Log.Warning("[AutoArm] Could not find ammo field in AmmoLink");
                    return;
                }

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

        public static bool IsAmmoSystemEnabled()
        {
            if (!IsLoaded())
                return false;

            EnsureInitialized();

            if (controllerType == null || enableAmmoSystemProperty == null)
                return true;

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
            }

            return true;
        }

        public static bool ShouldSkipWeaponForCE(ThingWithComps weapon, Pawn pawn)
        {
            if (!IsLoaded() || weapon == null || pawn == null)
                return false;

            if (AutoArmMod.settings?.checkCEAmmo != true)
            {
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmDebug.Log($"CE ammo check skipped - AutoArm setting disabled");
                }
                return false;
            }

            if (!IsAmmoSystemEnabled())
            {
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmDebug.Log($"CE ammo check skipped - CE ammo system disabled");
                }
                return false;
            }

            EnsureInitialized();

            if (!weapon.def.IsRangedWeapon)
                return false;

            try
            {
                var ammoTypes = GetAmmoTypesForWeapon(weapon.def);

                if (ammoTypes == null || ammoTypes.Count == 0)
                {
                    if (AutoArmMod.settings?.debugLogging == true)
                    {
                        AutoArmDebug.Log($"Weapon {weapon.def.defName} has no ammo types defined");
                    }
                    return false;
                }

                bool hasAmmo = false;
                int inventoryAmmoCount = 0;

                if (pawn.inventory?.innerContainer != null)
                {
                    foreach (var ammoType in ammoTypes)
                    {
                        var ammoStack = pawn.inventory.innerContainer.FirstOrDefault(t => t.def == ammoType);
                        if (ammoStack != null)
                        {
                            hasAmmo = true;
                            inventoryAmmoCount += ammoStack.stackCount;
                        }
                    }
                }

                if (!hasAmmo && pawn.Map != null)
                {
                    hasAmmo = IsAmmoAvailableOnMap(ammoTypes, pawn);
                }

                if (AutoArmMod.settings?.debugLogging == true)
                {
                    string ammoTypeNames = string.Join(", ", ammoTypes.Select(a => a.defName));
                    AutoArmDebug.LogWeapon(pawn, weapon,
                        $"CE ammo check - Needs: [{ammoTypeNames}], " +
                        $"Inventory: {inventoryAmmoCount}, " +
                        $"Map available: {(hasAmmo && inventoryAmmoCount == 0 ? "Yes" : "No")}, " +
                        $"Result: {(hasAmmo ? "Has ammo" : "No ammo - SKIP")}");
                }

                return !hasAmmo;
            }
            catch (Exception e)
            {
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    Log.Warning($"[AutoArm] Error checking CE ammo for {weapon.def.defName}: {e.Message}");
                }
                return false;
            }
        }

        private static List<ThingDef> GetAmmoTypesForWeapon(ThingDef weaponDef)
        {
            if (weaponDef == null)
                return null;

            CheckCacheClear();

            if (weaponAmmoCache.TryGetValue(weaponDef, out var cached))
                return cached;

            var ammoTypes = new List<ThingDef>();

            try
            {
                var ammoUserComp = weaponDef.comps?.FirstOrDefault(c =>
                    c.GetType() == compPropertiesAmmoUserType ||
                    c.GetType().Name == "CompProperties_AmmoUser");

                if (ammoUserComp == null)
                {
                    weaponAmmoCache[weaponDef] = ammoTypes;
                    return ammoTypes;
                }

                var ammoSet = ammoSetField?.GetValue(ammoUserComp);
                if (ammoSet == null)
                {
                    weaponAmmoCache[weaponDef] = ammoTypes;
                    return ammoTypes;
                }

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

            weaponAmmoCache[weaponDef] = ammoTypes;
            return ammoTypes;
        }

        private static bool IsAmmoAvailableOnMap(List<ThingDef> ammoTypes, Pawn pawn)
        {
            if (ammoTypes == null || ammoTypes.Count == 0 || pawn.Map == null)
                return false;

            foreach (var ammoType in ammoTypes)
            {
                var ammoThings = pawn.Map.listerThings.ThingsOfDef(ammoType);

                foreach (var ammoThing in ammoThings)
                {
                    if (!ammoThing.IsForbidden(pawn) &&
                        pawn.CanReserveAndReach(ammoThing, PathEndMode.ClosestTouch, Danger.Some))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        public static float GetAmmoScoreModifier(ThingWithComps weapon, Pawn pawn)
        {
            if (!IsLoaded() || weapon == null || !weapon.def.IsRangedWeapon)
                return 1f;

            EnsureInitialized();

            if (AutoArmMod.settings?.checkCEAmmo != true)
                return 1f;

            if (!IsAmmoSystemEnabled())
                return 1f;

            try
            {
                var ammoTypes = GetAmmoTypesForWeapon(weapon.def);
                if (ammoTypes == null || ammoTypes.Count == 0)
                    return 1f;

                int totalAmmo = 0;

                if (pawn.inventory?.innerContainer != null)
                {
                    foreach (var ammoType in ammoTypes)
                    {
                        totalAmmo += pawn.inventory.innerContainer.Where(t => t.def == ammoType).Sum(t => t.stackCount);
                    }
                }

                if (totalAmmo > 50)
                    return 1.2f;
                else if (totalAmmo > 0)
                    return 1.1f;

                if (IsAmmoAvailableOnMap(ammoTypes, pawn))
                    return 0.9f;
                else
                    return 0.5f;
            }
            catch
            {
                return 1f;
            }
        }

        public static void ClearCache()
        {
            weaponAmmoCache.Clear();
        }

        public static bool ShouldCheckAmmo()
        {
            bool isLoaded = IsLoaded();
            bool ammoSystemEnabled = IsAmmoSystemEnabled();
            bool settingEnabled = AutoArmMod.settings?.checkCEAmmo == true;
            bool result = isLoaded && ammoSystemEnabled && settingEnabled;

            if (AutoArmMod.settings?.debugLogging == true)
            {
                AutoArmDebug.Log($"CE ammo check - Loaded: {isLoaded}, CE Ammo System: {ammoSystemEnabled}, AutoArm Setting: {settingEnabled}, Result: {result}");
            }

            return result;
        }

        /// <summary>
        /// Try to detect if CE's ammo system is enabled, with detailed result information
        /// </summary>
        public static bool TryDetectAmmoSystemEnabled(out string detectionResult)
        {
            if (!IsLoaded())
            {
                detectionResult = "Combat Extended not loaded";
                return false;
            }

            EnsureInitialized();

            if (controllerType == null)
            {
                detectionResult = "Could not find CE Controller class";
                return false;
            }

            if (enableAmmoSystemProperty == null)
            {
                detectionResult = "Could not find EnableAmmoSystem property";
                return false;
            }

            try
            {
                var settingsField = controllerType.GetField("settings", BindingFlags.Public | BindingFlags.Static);
                if (settingsField == null)
                {
                    detectionResult = "Could not find settings field on Controller";
                    return false;
                }

                var settings = settingsField.GetValue(null);
                if (settings == null)
                {
                    detectionResult = "CE settings instance is null";
                    return false;
                }

                var enabled = enableAmmoSystemProperty.GetValue(settings);
                if (enabled is bool boolValue)
                {
                    detectionResult = $"Successfully detected: Ammo system is {(boolValue ? "enabled" : "disabled")}";
                    return boolValue;
                }
                else
                {
                    detectionResult = $"Unexpected property type: {enabled?.GetType()?.Name ?? "null"}";
                    return true; // Default to enabled if we can't determine
                }
            }
            catch (Exception ex)
            {
                detectionResult = $"Error reading CE settings: {ex.Message}";
                return true; // Default to enabled on error
            }
        }
    }
}