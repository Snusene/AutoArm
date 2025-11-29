
using AutoArm.Logging;
using HarmonyLib;
using RimWorld;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;

namespace AutoArm
{
    public static class CECompat
    {
        private static bool? _isLoaded = null;
        private static bool _initialized = false;

        private static HashSet<string> _loggedWeapons = new HashSet<string>();

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

                AutoArmLogger.Debug(() => $"[CombatExtended] Found {ceTypes.Count} types");

                compPropertiesAmmoUserType = ceTypes.FirstOrDefault(t => t.Name == "CompProperties_AmmoUser");
                if (compPropertiesAmmoUserType == null)
                {
                    AutoArmLogger.Warn("[CombatExtended] Could not find CompProperties_AmmoUser type");
                    return;
                }

                ammoLinkType = ceTypes.FirstOrDefault(t => t.Name == "AmmoLink");
                if (ammoLinkType == null)
                {
                    AutoArmLogger.Warn("[CombatExtended] Could not find AmmoLink type");
                    return;
                }

                ammoSetField = AccessTools.Field(compPropertiesAmmoUserType, "ammoSet");
                if (ammoSetField == null)
                {
                    AutoArmLogger.Warn("[CombatExtended] Could not find ammoSet field");
                    return;
                }

                ammoField = AccessTools.Field(ammoLinkType, "ammo");
                if (ammoField == null)
                {
                    AutoArmLogger.Warn("[CombatExtended] Could not find ammo field in AmmoLink");
                    return;
                }

                controllerType = ceTypes.FirstOrDefault(t => t.Name == "Controller");
                if (controllerType != null)
                {
                    var settingsField = AccessTools.Field(controllerType, "settings");
                    if (settingsField != null)
                    {
                        var settingsInstance = settingsField.GetValue(null);
                        if (settingsInstance != null)
                        {
                            settingsType = settingsInstance.GetType();
                            enableAmmoSystemProperty = AccessTools.Property(settingsType, "EnableAmmoSystem");
                        }
                    }
                }

                AutoArmLogger.Debug(() => "CombatExtended integration initialized successfully");
            }
            catch (Exception e)
            {
                AutoArmLogger.ErrorUI(e, "CECompat", "Initialization");
            }
        }

        public static bool ShouldCheckAmmo()
        {
            if (!IsLoaded() || AutoArmMod.settings?.checkCEAmmo != true)
                return false;

            return true;
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
                var settingsField = AccessTools.Field(controllerType, "settings");
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
            catch (Exception ex)
            {
                AutoArmLogger.Debug(() => $"CECompat.IsAmmoSystemEnabled failed: {ex.GetType().Name}");
            }

            return true;
        }

        public static bool ShouldSkipWeaponForCE(ThingWithComps weapon, Pawn pawn)
        {
            if (!ShouldCheckAmmo() || weapon == null || pawn == null)
                return false;

            if (!weapon.def.IsRangedWeapon)
                return false;

            EnsureInitialized();

            if (!IsAmmoSystemEnabled())
            {
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.Log($"CE ammo check skipped - CE ammo system disabled");
                }
                return false;
            }

            try
            {
                var ammoTypes = GetAmmoTypes(weapon.def);

                if (ammoTypes == null || ammoTypes.Count == 0)
                {
                    if (AutoArmMod.settings?.debugLogging == true)
                    {
                        AutoArmLogger.Log($"Weapon {weapon.LabelCap} has no ammo types defined");
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

                var logKey = $"ceammo_{weapon.LabelCap}_{pawn.thingIDNumber}";
                if (!_loggedWeapons.Contains(logKey))
                {
                    _loggedWeapons.Add(logKey);
                    if (_loggedWeapons.Count > 200)
                    {
                        _loggedWeapons.Clear();
                    }

                    if (AutoArmMod.settings?.debugLogging == true)
                    {
                        AutoArmLogger.Debug(() => $"CECompat: {weapon.Label} for {pawn.LabelShort} - Ammo types: [{string.Join(", ", ammoTypes.Select(a => a.defName))}], " +
                            $"Inventory: {inventoryAmmoCount}, " +
                            $"Map available: {(hasAmmo && inventoryAmmoCount == 0 ? "Yes" : "No")}, " +
                            $"Result: {(hasAmmo ? "Has ammo" : "No ammo - SKIP")}");
                    }
                }

                return !hasAmmo;
            }
            catch (Exception e)
            {
                if (Prefs.DevMode && AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.Debug(() => $"CECompat: Error checking ammo for {weapon?.LabelCap ?? weapon?.def?.label ?? weapon?.def?.defName}: {e.Message}");
                }
                return false;
            }
        }

        private static List<ThingDef> GetAmmoTypes(ThingDef weaponDef)
        {
            if (weaponDef == null)
                return null;

            CheckCacheClear();

            if (weaponAmmoCache.TryGetValue(weaponDef, out var cached))
                return cached;

            var ammoTypes = new List<ThingDef>();

            try
            {
                CompProperties ammoUserComp = null;
                if (weaponDef.comps != null)
                {
                    for (int i = 0; i < weaponDef.comps.Count; i++)
                    {
                        var comp = weaponDef.comps[i];
                        if (comp != null &&
                            (comp.GetType() == compPropertiesAmmoUserType ||
                             comp.GetType().Name == "CompProperties_AmmoUser"))
                        {
                            ammoUserComp = comp;
                            break;
                        }
                    }
                }

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

                var ammoTypesFieldInSet = AccessTools.Field(ammoSet.GetType(), "ammoTypes");
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
                if (Prefs.DevMode && AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.Debug(() => $"CECompat: Error getting ammo types for {weaponDef.defName}: {e.Message}");
                }
            }

            weaponAmmoCache[weaponDef] = ammoTypes;
            return ammoTypes;
        }


        private static bool IsAmmoAvailableOnMap(List<ThingDef> ammoTypes, Pawn pawn)
        {
            if (ammoTypes == null || ammoTypes.Count == 0 || pawn.Map == null)
                return false;

            var playerFaction = Faction.OfPlayer;

            foreach (var ammoType in ammoTypes)
            {
                var ammoThings = pawn.Map.listerThings.ThingsOfDef(ammoType);
                foreach (var ammoThing in ammoThings)
                {
                    // Use faction check (cheap) instead of pawn check (expensive)
                    if (!ammoThing.IsForbidden(playerFaction))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        public static float GetAmmoScoreModifier(ThingWithComps weapon, Pawn pawn)
        {
            if (!ShouldCheckAmmo() || weapon == null || !weapon.def.IsRangedWeapon)
                return 1f;

            EnsureInitialized();

            if (!IsAmmoSystemEnabled())
                return 1f;

            try
            {
                var ammoTypes = GetAmmoTypes(weapon.def);
                if (ammoTypes == null || ammoTypes.Count == 0)
                    return 1f;

                int totalAmmo = 0;

                if (pawn.inventory?.innerContainer != null)
                {
                    foreach (var ammoType in ammoTypes)
                    {
                        foreach (Thing thing in pawn.inventory.innerContainer)
                        {
                            if (thing.def == ammoType)
                            {
                                totalAmmo += thing.stackCount;
                            }
                        }
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
            catch (Exception ex)
            {
                AutoArmLogger.Debug(() => $"CECompat.GetAmmoScoreModifier failed: {ex.GetType().Name}");
                return 1f;
            }
        }

        public static void ClearCache()
        {
            weaponAmmoCache.Clear();
            _loggedWeapons.Clear();
        }

        /// <summary>
        /// Has ammo
        /// </summary>
        public static bool HasAmmo(Pawn pawn, ThingWithComps weapon)
        {
            if (!IsLoaded() || weapon == null || pawn == null)
                return true;

            return !ShouldSkipWeaponForCE(weapon, pawn);
        }

        /// <summary>
        /// Try to detect if CE's ammo system is enabled, with detailed result information
        /// </summary>
        public static bool TryDetectAmmoSystemEnabled(out string detectionResult)
        {
            if (!IsLoaded())
            {
                detectionResult = "Skipped";
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
                var settingsField = AccessTools.Field(controllerType, "settings");
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
                    return true;
                }
            }
            catch (Exception ex)
            {
                detectionResult = $"Error reading CE settings: {ex.Message}";
                return true;
            }
        }
    }
}
