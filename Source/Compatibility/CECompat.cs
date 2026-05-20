
using AutoArm.Helpers;
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
    internal static class CECompat
    {
        private static bool? _isLoaded = null;
        private static bool _initialized = false;

        private static readonly HashSet<string> _loggedWeapons = new HashSet<string>();

        private static Type compPropertiesAmmoUserType;
        private static Type ammoLinkType;
        private static Type controllerType;
        private static Type settingsType;

        private static FieldInfo ammoSetField;
        private static FieldInfo ammoField;
        private static FieldInfo _settingsField;
        private static PropertyInfo enableAmmoSystemProperty;

        private static readonly Dictionary<ThingDef, List<ThingDef>> weaponAmmoCache = new Dictionary<ThingDef, List<ThingDef>>();

        private static readonly Dictionary<Type, FieldInfo> ammoTypesFieldCache = new Dictionary<Type, FieldInfo>();

        private static int _ammoSystemEnabledCachedTick = -1;
        private static bool _ammoSystemEnabledCached;

        private static int lastCacheClearTick = 0;
        private const int CacheClearInterval = 60000;

        private static FieldInfo GetAmmoTypesField(Type type)
        {
            if (type == null) return null;
            if (!ammoTypesFieldCache.TryGetValue(type, out var field))
            {
                field = AccessTools.Field(type, "ammoTypes");
                ammoTypesFieldCache[type] = field;
            }
            return field;
        }

        private static void CheckCacheClear()
        {
            if (Find.TickManager.TicksGame - lastCacheClearTick > CacheClearInterval)
            {
                ClearCache();
                lastCacheClearTick = Find.TickManager.TicksGame;
            }
        }

        public static bool IsLoaded
        {
            get
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
        }

        private static void EnsureInitialized()
        {
            if (_initialized || !IsLoaded)
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
                    _settingsField = AccessTools.Field(controllerType, "settings");
                    if (_settingsField != null)
                    {
                        var settingsInstance = _settingsField.GetValue(null);
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
            if (!IsLoaded || AutoArmMod.settings?.checkCEAmmo != true)
                return false;

            return true;
        }

        public static bool IsAmmoSystemEnabled()
        {
            if (!IsLoaded)
                return false;

            EnsureInitialized();

            if (controllerType == null || enableAmmoSystemProperty == null)
                return true;

            int tick = Find.TickManager?.TicksGame ?? 0;
            if (tick == _ammoSystemEnabledCachedTick)
                return _ammoSystemEnabledCached;

            bool result = true;
            try
            {
                if (_settingsField != null)
                {
                    var settings = _settingsField.GetValue(null);
                    if (settings != null)
                    {
                        var enabled = enableAmmoSystemProperty.GetValue(settings);
                        if (enabled is bool boolValue)
                            result = boolValue;
                    }
                }
            }
            catch (Exception ex)
            {
                AutoArmLogger.Debug(() => $"CECompat.IsAmmoSystemEnabled failed: {ex.GetType().Name}");
            }

            _ammoSystemEnabledCached = result;
            _ammoSystemEnabledCachedTick = tick;
            return result;
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
                AutoArmLogger.Debug(() => "CE ammo check skipped - CE ammo system disabled");
                return false;
            }

            try
            {
                var ammoTypes = GetAmmoTypes(weapon.def);

                if (ammoTypes == null || ammoTypes.Count == 0)
                {
                    AutoArmLogger.Debug(() => $"Weapon {weapon.LabelCap} has no ammo types defined");
                    return false;
                }

                bool hasAmmo = false;
                int inventoryAmmoCount = 0;

                if (pawn.inventory?.innerContainer != null)
                {
                    foreach (var ammoType in ammoTypes)
                    {
                        foreach (var t in pawn.inventory.innerContainer)
                        {
                            if (t.def == ammoType)
                            {
                                hasAmmo = true;
                                inventoryAmmoCount += t.stackCount;
                                break;
                            }
                        }
                    }
                }

                if (!hasAmmo && pawn.Map != null)
                {
                    hasAmmo = IsAmmoAvailableOnMap(ammoTypes, pawn);
                }

                if (AutoArmMod.settings?.debugLogging == true)
                {
                    var logKey = $"ceammo_{weapon.LabelCap}_{pawn.thingIDNumber}";
                    if (!_loggedWeapons.Contains(logKey))
                    {
                        _loggedWeapons.Add(logKey);
                        if (_loggedWeapons.Count > 200)
                        {
                            _loggedWeapons.Clear();
                        }

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
                AutoArmLogger.Debug(() => $"CECompat: Error checking ammo for {weapon?.LabelCap ?? weapon?.def?.label ?? weapon?.def?.defName}: {e.Message}");
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
                var ammoUserComps = ListPool<CompProperties>.Get();
                if (weaponDef.comps != null)
                {
                    for (int i = 0; i < weaponDef.comps.Count; i++)
                    {
                        var comp = weaponDef.comps[i];
                        if (comp != null &&
                            (comp.GetType() == compPropertiesAmmoUserType ||
                             comp.GetType().Name == "CompProperties_AmmoUser"))
                        {
                            ammoUserComps.Add(comp);
                        }
                    }
                }

                if (ammoUserComps.Count == 0)
                {
                    ListPool<CompProperties>.Return(ammoUserComps);
                    weaponAmmoCache[weaponDef] = ammoTypes;
                    return ammoTypes;
                }

                foreach (var ammoUserComp in ammoUserComps)
                {
                    var ammoSet = ammoSetField?.GetValue(ammoUserComp);
                    if (ammoSet == null)
                        continue;

                    var ammoTypesFieldInSet = GetAmmoTypesField(ammoSet.GetType());
                    if (ammoTypesFieldInSet == null)
                        continue;

                    var ammoTypesList = ammoTypesFieldInSet.GetValue(ammoSet) as IEnumerable;
                    if (ammoTypesList == null)
                        continue;

                    foreach (var ammoLink in ammoTypesList)
                    {
                        if (ammoLink == null || ammoField == null)
                            continue;

                        var ammoDef = ammoField.GetValue(ammoLink) as ThingDef;
                        if (ammoDef != null && !ammoTypes.Contains(ammoDef))
                            ammoTypes.Add(ammoDef);
                    }
                }

                ListPool<CompProperties>.Return(ammoUserComps);
            }
            catch (Exception e)
            {
                AutoArmLogger.Debug(() => $"CECompat: Error getting ammo types for {weaponDef.defName}: {e.Message}");
            }

            weaponAmmoCache[weaponDef] = ammoTypes;
            return ammoTypes;
        }


        private static bool IsAmmoAvailableOnMap(List<ThingDef> ammoTypes, Pawn pawn)
        {
            if (ammoTypes == null || ammoTypes.Count == 0 || pawn.Map == null)
                return false;

            var playerFaction = Faction.OfPlayerSilentFail;
            if (playerFaction == null)
                return false;

            foreach (var ammoType in ammoTypes)
            {
                var ammoThings = pawn.Map.listerThings.ThingsOfDef(ammoType);
                foreach (var ammoThing in ammoThings)
                {
                    // Faction check cheaper
                    if (!ammoThing.IsForbidden(playerFaction))
                    {
                        return true;
                    }
                }
            }

            var mapPawns = pawn.Map.mapPawns?.AllPawnsSpawned;
            if (mapPawns != null)
            {
                for (int i = 0; i < mapPawns.Count; i++)
                {
                    var p = mapPawns[i];
                    if (p == null || p.Faction != playerFaction) continue;

                    var inv = p.inventory?.innerContainer;
                    if (inv == null) continue;

                    for (int j = 0; j < inv.Count; j++)
                    {
                        var def = inv[j]?.def;
                        if (def == null) continue;
                        for (int k = 0; k < ammoTypes.Count; k++)
                        {
                            if (def == ammoTypes[k])
                                return true;
                        }
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
                {
                    string logKey = "no-ammo-types:" + weapon.def.defName;
                    if (!_loggedWeapons.Contains(logKey))
                    {
                        _loggedWeapons.Add(logKey);
                        AutoArmLogger.Debug(() => $"[CE] {weapon.def.defName}: ammo system enabled but no ammo types resolved");
                    }
                    return 1f;
                }

                int totalAmmo = 0;

                if (pawn.inventory?.innerContainer != null)
                {
                    foreach (Thing thing in pawn.inventory.innerContainer)
                    {
                        if (ammoTypes.Contains(thing.def))
                        {
                            totalAmmo += thing.stackCount;
                        }
                    }
                }

                if (totalAmmo > 30)
                    return 1.2f;
                else if (totalAmmo > 5)
                    return 1.1f;
                else if (totalAmmo > 0)
                    return 1.05f;

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

        public static bool TryDetectAmmoSystemEnabled(out string detectionResult)
        {
            if (!IsLoaded)
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
                if (_settingsField == null)
                {
                    detectionResult = "Could not find settings field on Controller";
                    return false;
                }

                var settings = _settingsField.GetValue(null);
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
