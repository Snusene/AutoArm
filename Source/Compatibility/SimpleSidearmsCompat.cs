using AutoArm.Definitions;
using AutoArm.Helpers;
using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Reflection;
using Verse;
using Verse.AI;

namespace AutoArm.Compatibility
{
    internal static class SimpleSidearmsCompat
    {
        private static bool? _isLoaded = null;

        private static bool _initialized = false;
        private static bool _reflectionFailed = false;

        private static Type _compSidearmMemoryType;

        private static Type _weaponAssingmentType;
        private static Type _statCalculatorType;
        private static Type _thingDefStuffDefPairType;
        private static Type _ssModType;
        private static Type _ssSettingsType;

        private static MethodInfo _getMemoryCompMethod;

        private static MethodInfo _canPickupSidearmMethod;
        private static MethodInfo _forgetSidearmMethod;
        private static MethodInfo _informAddedSidearmMethod;
        private static MethodInfo _informAddedPrimaryMethod;

        private static PropertyInfo _rememberedWeaponsProp;

        private static PropertyInfo _ssSettingsActivePresetProp;

        private static PropertyInfo _ssModSettingsStaticProp;

        private static FieldInfo _pairThingField;

        private static FieldInfo _pairStuffField;

        private delegate bool CanPickupSidearmDelegate(ThingWithComps weapon, Pawn pawn, out string reason);
        private delegate object GetMemoryCompDelegate(Pawn pawn, bool fillExisting);
        private delegate object GetMemoryCompSingleParamDelegate(Pawn pawn);

        private static CanPickupSidearmDelegate _canPickupDelegateTyped;
        private static Func<ThingWithComps, Pawn, (bool canPickup, string reason)> _canPickupDelegate;

        private static CanPickupSidearmDelegate _canUseSidearmDelegateTyped;

        private static GetMemoryCompDelegate _getMemoryDelegateTyped;
        private static GetMemoryCompSingleParamDelegate _getMemoryDelegateSingleParam;
        private static Func<Pawn, bool, object> _getMemoryDelegate;

        private static Action<object, Thing, bool> _informDroppedDelegate;
        private static Action<object, Thing> _informAddedDelegate;
        private static Action<object, Thing> _informAddedPrimaryDelegate;

        // Swap validation (no limit checks)
        private static MethodInfo _canUseSidearmInstanceMethod;
        private static Func<ThingWithComps, Pawn, (bool canUse, string reason)> _canUseSidearmDelegate;

        // Per-weapon validation (no slot checks)
        private static MethodInfo _isValidSidearmMethod;
        private static Func<ThingDef, ThingDef, (bool isValid, string reason)> _isValidSidearmDelegate;

        private static readonly object[] _canUseSidearmArgs = new object[3];
        private static readonly object[] _isValidSidearmArgs = new object[2];
        private static object _sidearmPairScratch;

        private static readonly Dictionary<int, WeaponCheckCache> _pawnCaches = new Dictionary<int, WeaponCheckCache>();

        // TickScheduler lookups
        private static readonly Dictionary<int, Pawn> idToPawnLookup = new Dictionary<int, Pawn>();
        private static readonly Dictionary<int, PairKey> hashToPairLookup = new Dictionary<int, PairKey>();

        private static readonly Dictionary<int, int> _lastUpgradeCheckTick = new Dictionary<int, int>();
        private const int UPGRADE_CHECK_COOLDOWN = Constants.SSUpgradeCheckCooldown;
        private const int CACHE_LIFETIME = Constants.StandardCacheDuration;
        private const int MAX_CACHE_SIZE = Constants.SSMaxPawnCacheSize;
        private const int INACTIVE_PAWN_TIMEOUT = Constants.SSInactivePawnTimeout;

        // Cached SS active
        private static bool _isActiveCache;
        private static int _isActiveCacheExpiry;


        private class WeaponCheckCache
        {
            public Dictionary<PairKey, (bool canPickup, string reason, int expiry)> ValidationCache = new Dictionary<PairKey, (bool, string, int)>();
            public int LastCleanupTick = 0;
            public int CachedWeaponCount = -1;
            public int CachedWeaponCountTick = -1;
        }

        private struct PairKey : IEquatable<PairKey>
        {
            public readonly ThingDef Thing;
            public readonly ThingDef Stuff;
            public readonly int WeaponCount;

            public PairKey(ThingDef thing, ThingDef stuff, int weaponCount = 0)
            { Thing = thing; Stuff = stuff; WeaponCount = weaponCount; }

            public bool Equals(PairKey other)
            { return Thing == other.Thing && Stuff == other.Stuff && WeaponCount == other.WeaponCount; }

            public override bool Equals(object obj)
            { return obj is PairKey other && Equals(other); }

            public override int GetHashCode()
            {
                unchecked
                {
                    int h = Thing != null ? Thing.shortHash : 0;
                    int s = Stuff != null ? Stuff.shortHash : 0;
                    return ((h * 397) ^ s) * 31 + WeaponCount;
                }
            }

            public int ToEncodedInt()
            {
                int thingHash = Thing?.shortHash ?? 0;
                int stuffHash = Stuff?.shortHash ?? 0;
                return (thingHash << 16) | (stuffHash & 0xFFFF);
            }
        }


        public static bool ReflectionFailed => _reflectionFailed;

        public static bool IsReady => IsLoaded && !_reflectionFailed;

        public static bool CanAutoEquipSidearms()
        {
            if (AutoArmMod.settings?.autoEquipSidearms != true)
                return false;

            return !IsLoaded || IsReady;
        }

        public static bool CanUpgradeSidearms()
        {
            if (AutoArmMod.settings?.allowSidearmUpgrades != true)
                return false;

            return !IsLoaded || IsReady;
        }

        public static bool IsLoaded
        {
            get
            {
                if (_isLoaded == null)
                {
                    _isLoaded = false;
                    foreach (var m in ModLister.AllInstalledMods)
                    {
                        if (m.Active && (
                            m.PackageIdPlayerFacing.Equals("PeteTimesSix.SimpleSidearms", StringComparison.OrdinalIgnoreCase) ||
                            m.PackageIdPlayerFacing.IndexOf("simplesidearms", StringComparison.OrdinalIgnoreCase) >= 0))
                        {
                            _isLoaded = true;
                            break;
                        }
                    }
                }
                return _isLoaded.Value;
            }
        }

        private static void Initialize()
        {
            if (_initialized || !IsLoaded) return;

            try
            {
                _compSidearmMemoryType = AccessTools.TypeByName("SimpleSidearms.rimworld.CompSidearmMemory")
                    ?? AccessTools.TypeByName("PeteTimesSix.SimpleSidearms.CompSidearmMemory");
                _weaponAssingmentType = AccessTools.TypeByName("PeteTimesSix.SimpleSidearms.Utilities.WeaponAssingment");
                _statCalculatorType = AccessTools.TypeByName("PeteTimesSix.SimpleSidearms.Utilities.StatCalculator");
                _thingDefStuffDefPairType = AccessTools.TypeByName("SimpleSidearms.rimworld.ThingDefStuffDefPair")
                    ?? AccessTools.TypeByName("PeteTimesSix.SimpleSidearms.ThingDefStuffDefPair");
                _ssModType = AccessTools.TypeByName("PeteTimesSix.SimpleSidearms.SimpleSidearms");
                _ssSettingsType = AccessTools.TypeByName("PeteTimesSix.SimpleSidearms.SimpleSidearms_Settings");

                if (_compSidearmMemoryType == null || _weaponAssingmentType == null || _statCalculatorType == null)
                {
                    AutoArmLogger.Warn("SimpleSidearms types not found - integration disabled");
                    _reflectionFailed = true;
                    _initialized = true;
                    return;
                }

                _getMemoryCompMethod = AccessTools.Method(_compSidearmMemoryType, "GetMemoryCompForPawn",
                    new Type[] { typeof(Pawn), typeof(bool) });
                if (_getMemoryCompMethod == null)
                {
                    _getMemoryCompMethod = AccessTools.Method(_compSidearmMemoryType, "GetMemoryCompForPawn",
                        new Type[] { typeof(Pawn) });
                }

                _canPickupSidearmMethod = AccessTools.Method(_statCalculatorType, "CanPickupSidearmInstance",
                    new Type[] { typeof(ThingWithComps), typeof(Pawn), typeof(string).MakeByRefType() });
                if (_canPickupSidearmMethod == null)
                {
                    _canPickupSidearmMethod = AccessTools.Method(_statCalculatorType, "canCarrySidearmInstance",
                        new Type[] { typeof(ThingWithComps), typeof(Pawn), typeof(string).MakeByRefType() });
                }
                if (_canPickupSidearmMethod == null)
                {
                    _canPickupSidearmMethod = AccessTools.Method(_statCalculatorType, "CanPickupSidearm",
                        new Type[] { typeof(ThingWithComps), typeof(Pawn), typeof(string).MakeByRefType() });
                }

                // Checks biocoding/roles only
                _canUseSidearmInstanceMethod = AccessTools.Method(_statCalculatorType, "canUseSidearmInstance",
                    new Type[] { typeof(ThingWithComps), typeof(Pawn), typeof(string).MakeByRefType() });

                // Checks weight/selection limits
                if (_thingDefStuffDefPairType != null)
                {
                    _isValidSidearmMethod = AccessTools.Method(_statCalculatorType, "isValidSidearm",
                        new Type[] { _thingDefStuffDefPairType, typeof(string).MakeByRefType() });
                }

                _forgetSidearmMethod = AccessTools.Method(_compSidearmMemoryType, "InformOfDroppedSidearm",
                    new Type[] { typeof(Thing), typeof(bool) });
                _informAddedSidearmMethod = AccessTools.Method(_compSidearmMemoryType, "InformOfAddedSidearm",
                    new Type[] { typeof(Thing) });
                _informAddedPrimaryMethod = AccessTools.Method(_compSidearmMemoryType, "InformOfAddedPrimary",
                    new Type[] { typeof(Thing) });
                _rememberedWeaponsProp = AccessTools.Property(_compSidearmMemoryType, "RememberedWeapons");

                if (_ssModType != null)
                {
                    _ssModSettingsStaticProp = AccessTools.Property(_ssModType, "Settings");
                }
                if (_ssSettingsType != null)
                {
                    _ssSettingsActivePresetProp = AccessTools.Property(_ssSettingsType, "ActivePreset");
                }

                if (_thingDefStuffDefPairType != null)
                {
                    _pairThingField = AccessTools.Field(_thingDefStuffDefPairType, "thing");
                    _pairStuffField = AccessTools.Field(_thingDefStuffDefPairType, "stuff");
                }

                if (_canPickupSidearmMethod == null)
                {
                    AutoArmLogger.Warn("SimpleSidearms CanPickupSidearm method not found");
                    _reflectionFailed = true;
                    _initialized = true;
                    return;
                }

                if (_getMemoryCompMethod == null)
                {
                    AutoArmLogger.Warn("SimpleSidearms GetMemoryCompForPawn method not found - integration disabled");
                    _reflectionFailed = true;
                    _initialized = true;
                    return;
                }

                TryCompileDelegates();

                _initialized = true;
                AutoArmLogger.Debug(() => $"Integration initialized (reflection: {AutoArmLogger.FormatBool(!_reflectionFailed)})");
            }
            catch (Exception e)
            {
                AutoArmLogger.Warn($"SimpleSidearms integration failed: {e.Message}");
                _reflectionFailed = true;
                _initialized = true;
            }
        }

        private static void TryCompileDelegates()
        {

            if (_canPickupSidearmMethod != null)
            {
                try
                {
                    _canPickupDelegateTyped = (CanPickupSidearmDelegate)
                        Delegate.CreateDelegate(typeof(CanPickupSidearmDelegate), _canPickupSidearmMethod, throwOnBindFailure: false);

                    if (_canPickupDelegateTyped != null)
                    {
                        _canPickupDelegate = (weapon, pawn) =>
                        {
                            string reason;
                            bool result = _canPickupDelegateTyped(weapon, pawn, out reason);
                            return (result, reason ?? "");
                        };
                    }
                }
                catch (Exception e)
                {
                    AutoArmLogger.Debug(() => $"[SimpleSidearms] CanPickup delegate bind failed, using reflection: {e.Message}");
                    _canPickupDelegate = (weapon, pawn) =>
                    {
                        var parameters = new object[] { weapon, pawn, null };
                        var result = (bool)_canPickupSidearmMethod.Invoke(null, parameters);
                        return (result, parameters[2] as string ?? "");
                    };
                }
            }

            if (_getMemoryCompMethod != null)
            {
                var paramCount = _getMemoryCompMethod.GetParameters().Length;
                try
                {
                    if (paramCount == 2)
                    {
                        _getMemoryDelegateTyped = (GetMemoryCompDelegate)
                            Delegate.CreateDelegate(typeof(GetMemoryCompDelegate), _getMemoryCompMethod, throwOnBindFailure: false);

                        if (_getMemoryDelegateTyped != null)
                        {
                            _getMemoryDelegate = (pawn, fillExisting) => _getMemoryDelegateTyped(pawn, fillExisting);
                        }
                    }
                    else
                    {
                        _getMemoryDelegateSingleParam = (GetMemoryCompSingleParamDelegate)
                            Delegate.CreateDelegate(typeof(GetMemoryCompSingleParamDelegate), _getMemoryCompMethod, throwOnBindFailure: false);

                        if (_getMemoryDelegateSingleParam != null)
                        {
                            _getMemoryDelegate = (pawn, fillExisting) => _getMemoryDelegateSingleParam(pawn);
                        }
                    }
                }
                catch (Exception e)
                {
                    AutoArmLogger.Debug(() => $"[SimpleSidearms] GetMemoryComp delegate bind failed, using reflection: {e.Message}");
                    if (paramCount == 2)
                    {
                        _getMemoryDelegate = (pawn, fillExisting) =>
                            _getMemoryCompMethod.Invoke(null, new object[] { pawn, fillExisting });
                    }
                    else
                    {
                        _getMemoryDelegate = (pawn, fillExisting) =>
                            _getMemoryCompMethod.Invoke(null, new object[] { pawn });
                    }
                }
            }

            if (_forgetSidearmMethod != null)
            {
                try
                {
                    _informDroppedDelegate = (Action<object, Thing, bool>)
                        Delegate.CreateDelegate(typeof(Action<object, Thing, bool>), _forgetSidearmMethod, throwOnBindFailure: false);
                }
                catch (Exception e)
                {
                    AutoArmLogger.Debug(() => $"[SimpleSidearms] InformDropped delegate bind failed, using reflection: {e.Message}");
                    _informDroppedDelegate = (memory, thing, intentional) =>
                    {
                        _forgetSidearmMethod.Invoke(memory, new object[] { thing, intentional });
                    };
                }
            }

            if (_informAddedSidearmMethod != null)
            {
                try
                {
                    _informAddedDelegate = (Action<object, Thing>)
                        Delegate.CreateDelegate(typeof(Action<object, Thing>), _informAddedSidearmMethod, throwOnBindFailure: false);
                }
                catch (Exception e)
                {
                    AutoArmLogger.Debug(() => $"[SimpleSidearms] InformAdded delegate bind failed, using reflection: {e.Message}");
                    _informAddedDelegate = (memory, thing) =>
                    {
                        _informAddedSidearmMethod.Invoke(memory, new object[] { thing });
                    };
                }
            }

            if (_informAddedPrimaryMethod != null)
            {
                try
                {
                    _informAddedPrimaryDelegate = (Action<object, Thing>)
                        Delegate.CreateDelegate(typeof(Action<object, Thing>), _informAddedPrimaryMethod, throwOnBindFailure: false);
                }
                catch (Exception e)
                {
                    AutoArmLogger.Debug(() => $"[SimpleSidearms] InformAddedPrimary delegate bind failed, using reflection: {e.Message}");
                    _informAddedPrimaryDelegate = (memory, thing) =>
                    {
                        _informAddedPrimaryMethod.Invoke(memory, new object[] { thing });
                    };
                }
            }

            // Biocoding/roles check
            if (_canUseSidearmInstanceMethod != null)
            {
                try
                {
                    _canUseSidearmDelegateTyped = (CanPickupSidearmDelegate)
                        Delegate.CreateDelegate(typeof(CanPickupSidearmDelegate), _canUseSidearmInstanceMethod, throwOnBindFailure: false);
                }
                catch (Exception e)
                {
                    AutoArmLogger.Debug(() => $"[SimpleSidearms] CanUseSidearm delegate bind failed: {e.Message}");
                }

                if (_canUseSidearmDelegateTyped != null)
                {
                    _canUseSidearmDelegate = (weapon, pawn) =>
                    {
                        bool result = _canUseSidearmDelegateTyped(weapon, pawn, out string reason);
                        return (result, reason ?? "");
                    };
                }
                else
                {
                    _canUseSidearmDelegate = (weapon, pawn) =>
                    {
                        _canUseSidearmArgs[0] = weapon;
                        _canUseSidearmArgs[1] = pawn;
                        _canUseSidearmArgs[2] = null;
                        var result = (bool)_canUseSidearmInstanceMethod.Invoke(null, _canUseSidearmArgs);
                        return (result, _canUseSidearmArgs[2] as string ?? "");
                    };
                }
            }

            // Weight/selection limits
            if (_isValidSidearmMethod != null && _thingDefStuffDefPairType != null)
            {
                try { _sidearmPairScratch = Activator.CreateInstance(_thingDefStuffDefPairType); }
                catch (Exception e) { AutoArmLogger.Debug(() => $"[SimpleSidearms] pair scratch init failed: {e.Message}"); }

                _isValidSidearmDelegate = (thingDef, stuffDef) =>
                {
                    try
                    {
                        if (_sidearmPairScratch == null) return (true, "");
                        _pairThingField?.SetValue(_sidearmPairScratch, thingDef);
                        _pairStuffField?.SetValue(_sidearmPairScratch, stuffDef);
                        _isValidSidearmArgs[0] = _sidearmPairScratch;
                        _isValidSidearmArgs[1] = null;
                        var result = (bool)_isValidSidearmMethod.Invoke(null, _isValidSidearmArgs);
                        return (result, _isValidSidearmArgs[1] as string ?? "");
                    }
                    catch (Exception e)
                    {
                        AutoArmLogger.Debug(() => $"[SimpleSidearms] isValidSidearm check failed: {e.Message}");
                        return (false, "SimpleSidearms not ready");
                    }
                };
            }
        }


        public static void EnsureInitialized()
        {
            if (!IsLoaded) return;
            Initialize();
        }

        public static void CleanupAfterLoad()
        {
            _pawnCaches.Clear();
            _lastUpgradeCheckTick.Clear();

            AutoArmLogger.Debug(() => "Caches for SimpleSidearm cleared after save/load");
        }

        public static bool IsManagingPawn(Pawn pawn)
        {
            if (!IsLoaded || pawn == null) return false;
            Initialize();
            if (_reflectionFailed) return false;

            if (!IsSimpleSidearmsActive())
                return false;

            return GetMemoryComp(pawn) != null;
        }


        private static string GetSimpleSidearmsActivePreset()
        {
            try
            {
                if (_ssModSettingsStaticProp != null && _ssSettingsActivePresetProp != null)
                {
                    var ssSettings = _ssModSettingsStaticProp.GetValue(null, null);
                    if (ssSettings != null)
                    {
                        var activePreset = _ssSettingsActivePresetProp.GetValue(ssSettings, null);
                        return activePreset?.ToString();
                    }
                }

                var ssModType = _ssModType ?? AccessTools.TypeByName("PeteTimesSix.SimpleSidearms.SimpleSidearms");
                if (ssModType != null && typeof(Mod).IsAssignableFrom(ssModType) && _ssSettingsType != null)
                {
                    var ssMod = LoadedModManager.GetMod(ssModType);
                    if (ssMod != null)
                    {
                        var getSettingsMethod = AccessTools.Method(typeof(Mod), "GetSettings");
                        if (getSettingsMethod != null)
                        {
                            var concreteMethod = getSettingsMethod.MakeGenericMethod(_ssSettingsType);
                            var settings = concreteMethod.Invoke(ssMod, new object[0]);
                            if (settings != null)
                            {
                                var presetField = AccessTools.Field(settings.GetType(), "ActivePreset");
                                if (presetField != null)
                                {
                                    var activePreset = presetField.GetValue(settings);
                                    return activePreset?.ToString();
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
                    AutoArm.AutoArmLogger.Debug(() => $"[SimpleSidearms] Failed to get active preset: {e.Message}");
                }
            }

            return null;
        }


        private static bool IsSimpleSidearmsActive()
        {
            int tick = Find.TickManager?.TicksGame ?? 0;
            if (tick < _isActiveCacheExpiry)
                return _isActiveCache;

            string preset = GetSimpleSidearmsActivePreset();
            _isActiveCache = preset != "Disabled";
            _isActiveCacheExpiry = tick + CACHE_LIFETIME;
            return _isActiveCache;
        }

        public static bool CanPickupSidearm(ThingWithComps weapon, Pawn pawn, out string reason)
        {
            reason = "";
            if (!IsLoaded || weapon == null || pawn == null) return true;
            Initialize();
            if (_reflectionFailed)
            {
                reason = "SimpleSidearms reflection failed";
                return false;
            }

            int currentTick = Find.TickManager.TicksGame;

            var cache = GetOrCreateCache(pawn);
            int weaponCount;
            if (cache.CachedWeaponCountTick == currentTick)
            {
                weaponCount = cache.CachedWeaponCount;
            }
            else
            {
                weaponCount = CountCarriedWeapons(pawn);
                cache.CachedWeaponCount = weaponCount;
                cache.CachedWeaponCountTick = currentTick;
            }
            var key = new PairKey(weapon.def, weapon.Stuff, weaponCount);

            if (cache.ValidationCache.TryGetValue(key, out var cached))
            {
                if (currentTick < cached.expiry)
                {
                    reason = cached.reason;
                    return cached.canPickup;
                }
            }

            bool canPickup = true;
            try
            {
                if (_canPickupDelegate != null)
                {
                    var result = _canPickupDelegate(weapon, pawn);
                    canPickup = result.canPickup;
                    reason = result.reason;
                }
                else if (_canPickupSidearmMethod != null)
                {
                    var parameters = new object[] { weapon, pawn, null };
                    canPickup = (bool)_canPickupSidearmMethod.Invoke(null, parameters);
                    reason = parameters[2] as string ?? "";
                }
            }
            catch (Exception e)
            {
                AutoArmLogger.Debug(() => $"SimpleSidearmsCompat.CanPickupSidearm threw: {e.GetType().Name}: {e.Message}");
                reason = $"SimpleSidearms check failed: {e.Message}";
                canPickup = true;
            }

            int expireTick = currentTick + CACHE_LIFETIME;
            cache.ValidationCache[key] = (canPickup, reason, expireTick);
            int pawnId = pawn.thingIDNumber;
            int pairHash = key.ToEncodedInt();

            if (cached.expiry > 0)
            {
                // Cancel old schedule
                TickScheduler.Cancel(TickScheduler.EventType.SimpleSidearmsValidation, pawnId, pairHash);
            }

            // Track lookups
            idToPawnLookup[pawnId] = pawn;
            hashToPairLookup[pairHash] = key;
            TickScheduler.Schedule(expireTick, TickScheduler.EventType.SimpleSidearmsValidation, pawnId, pairHash);

            return canPickup;
        }

        // Skips slot checks
        public static bool CanUseSidearmForSwap(ThingWithComps newWeapon, ThingWithComps oldWeapon, Pawn pawn, out string reason)
        {
            reason = "";
            if (!IsLoaded || newWeapon == null || pawn == null) return true;
            Initialize();
            if (_reflectionFailed) return true;

            // Check biocoding/role
            if (_canUseSidearmDelegate != null)
            {
                try
                {
                    var result = _canUseSidearmDelegate(newWeapon, pawn);
                    if (!result.canUse)
                    {
                        reason = result.reason;
                        return false;
                    }
                }
                catch (Exception e)
                {
                    AutoArmLogger.Debug(() => $"[SimpleSidearms] canUseSidearmInstance check failed: {e.Message}");
                }
            }

            if (_isValidSidearmDelegate != null)
            {
                var validResult = _isValidSidearmDelegate(newWeapon.def, newWeapon.Stuff);
                if (!validResult.isValid)
                {
                    reason = validResult.reason;
                    string rejectReason = reason;
                    AutoArmLogger.Debug(() => $"[SimpleSidearms] Swap rejected - {newWeapon.Label} not valid sidearm: {rejectReason}");
                    return false;
                }
            }

            // Same category
            bool sameCategory = oldWeapon != null &&
                newWeapon.def.IsRangedWeapon == oldWeapon.def.IsRangedWeapon;

            if (sameCategory)
                return true;

            // Cross-category check
            return CanPickupSidearm(newWeapon, pawn, out reason);
        }

        public static Job TryGetWeaponJob(Pawn pawn, ThingWithComps weapon, bool bypassCooldown = false)
        {
            if (!IsLoaded || pawn == null || weapon == null) return null;
            Initialize();
            if (_reflectionFailed) return null;

            if (!IsSimpleSidearmsActive())
            {
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArm.AutoArmLogger.Debug(() => "[SimpleSidearms] Integration skipped - SimpleSidearms is disabled");
                }
                return null;
            }

            if (IsSSJobInProgress(pawn))
                return null;

            if (!bypassCooldown)
            {
                if (_lastUpgradeCheckTick.TryGetValue(pawn.thingIDNumber, out int lastTick))
                {
                    if (Find.TickManager.TicksGame - lastTick < UPGRADE_CHECK_COOLDOWN)
                        return null;
                }
            }

            Job job = TryGetWeaponJobCore(pawn, weapon);

            if (!bypassCooldown)
            {
                int currentTick = Find.TickManager.TicksGame;
                _lastUpgradeCheckTick[pawn.thingIDNumber] = job == null
                    ? currentTick
                    : currentTick - UPGRADE_CHECK_COOLDOWN - 1;
            }

            return job;
        }

        private static Job TryGetWeaponJobCore(Pawn pawn, ThingWithComps weapon)
        {
            if (!IsManagingPawn(pawn))
                return null;

            var currentPrimary = pawn.equipment?.Primary;
            if (currentPrimary != null)
            {
                if (ShouldUpgradePrimary(pawn, weapon, currentPrimary))
                {
                    return CreatePrimaryUpgradeJob(pawn, weapon, currentPrimary);
                }

                if (ShouldTreatAsPrimaryReplacement(pawn, weapon, currentPrimary))
                {
                    bool isCrossType = weapon.def.IsRangedWeapon != currentPrimary.def.IsRangedWeapon;
                    if (isCrossType)
                    {
                        string ssReason;
                        if (CanPickupSidearm(weapon, pawn, out ssReason))
                        {
                            AutoArmLogger.Debug(() => $"[SimpleSidearms] Cross-def primary replacement: from {currentPrimary.Label} to {weapon.Label}");
                            return CreatePrimaryUpgradeJob(pawn, weapon, currentPrimary);
                        }
                    }
                    else
                    {
                        AutoArmLogger.Debug(() => $"[SimpleSidearms] Cross-def primary replacement: from {currentPrimary.Label} to {weapon.Label}");
                        return CreatePrimaryUpgradeJob(pawn, weapon, currentPrimary);
                    }
                }
            }

            if (AutoArmMod.settings?.allowSidearmUpgrades == true)
            {
                var sidearmToReplace = FindSidearmToReplace(pawn, weapon);
                if (sidearmToReplace != null)
                {
                    // Swap validation
                    string upgradeReason;
                    if (CanUseSidearmForSwap(weapon, sidearmToReplace, pawn, out upgradeReason))
                    {
                        return CreateSidearmUpgradeJob(pawn, weapon, sidearmToReplace);
                    }
                }
            }

            if (AutoArmMod.settings?.autoEquipSidearms == true)
            {
                var inv = pawn.inventory?.innerContainer;
                if (inv != null)
                {
                    ThingWithComps worstSameDef = null;
                    float worstScore = float.MaxValue;
                    int sidearmCount = 0;
                    foreach (var thing in inv)
                    {
                        var w = thing as ThingWithComps;
                        if (w == null || !Validation.IsWeapon(w.def)) continue;
                        sidearmCount++;
                        if (w.def != weapon.def) continue;

                        float s = CalculateWeaponScore(pawn, w);
                        if (s < worstScore)
                        {
                            worstScore = s;
                            worstSameDef = w;
                        }
                    }

                    if (worstSameDef != null)
                    {
                        float newScore = CalculateWeaponScore(pawn, weapon);

                        if (newScore > worstScore)
                        {
                            string swapReason;
                            if (!CanUseSidearmForSwap(weapon, worstSameDef, pawn, out swapReason))
                            {
                                AutoArmLogger.Debug(() => $"[SimpleSidearms] Same-def swap rejected: {swapReason}");
                                return null;
                            }
                            AutoArmLogger.Debug(() => $"[SimpleSidearms] Same-def sidearm improvement: from {worstSameDef.Label} ({worstScore:F1}) to {weapon.Label} ({newScore:F1}), swapping");
                            return CreateSidearmUpgradeJob(pawn, weapon, worstSameDef);
                        }
                        else
                        {
                            AutoArmLogger.Debug(() => $"[SimpleSidearms] Duplicate sidearm skipped (not better): existing {worstSameDef.Label} ({worstScore:F1}) vs new {weapon.Label} ({newScore:F1})");
                            return null;
                        }
                    }

                    if (!IsReady)
                    {
                        AutoArmLogger.Debug(() => "[SimpleSidearms] Sidearm add blocked - SS reflection unavailable");
                        return null;
                    }
                }

                string reason;
                if (CanPickupSidearm(weapon, pawn, out reason))
                {
                    return CreateAddSidearmJob(pawn, weapon);
                }
            }

            return null;
        }

        private static bool IsSSJobInProgress(Pawn pawn)
        {
            var cur = pawn.CurJobDef;
            if (cur == null) return false;
            return cur == AutoArmDefOf.EquipSecondary ||
                   cur == AutoArmDefOf.ReequipSecondary ||
                   cur == AutoArmDefOf.ReequipSecondaryCombat;
        }

        public static bool IsSidearm(Pawn pawn, ThingWithComps weapon)
        {
            if (!IsLoaded || pawn == null || weapon == null) return false;
            Initialize();
            if (_reflectionFailed) return false;

            var memory = GetMemoryComp(pawn);
            if (memory == null) return false;

            var rememberedWeapons = _rememberedWeaponsProp?.GetValue(memory) as System.Collections.IEnumerable;
            if (rememberedWeapons != null)
            {
                foreach (var item in rememberedWeapons)
                {
                    if (item != null)
                    {
                        if (_pairThingField != null)
                        {
                            var thingDef = _pairThingField.GetValue(item) as ThingDef;
                            var stuffDef = _pairStuffField?.GetValue(item) as ThingDef;
                            if (thingDef == weapon.def && stuffDef == weapon.Stuff)
                                return true;
                        }
                    }
                }
            }

            return false;
        }

        public static void InformOfDroppedWeapon(Pawn pawn, ThingWithComps weapon)
        {
            if (!IsLoaded || pawn == null || weapon == null) return;
            Initialize();
            if (_reflectionFailed) return;

            var memory = GetMemoryComp(pawn);
            if (memory == null) return;

            if (_informDroppedDelegate != null)
            {
                _informDroppedDelegate(memory, weapon, true);
                AutoArmLogger.Debug(() => $"[{pawn.LabelShort}] SS InformOfDroppedSidearm: forgot {weapon.Label}");
            }
            else if (_forgetSidearmMethod != null)
            {
                _forgetSidearmMethod.Invoke(memory, new object[] { weapon, true });
                AutoArmLogger.Debug(() => $"[{pawn.LabelShort}] SS InformOfDroppedSidearm (reflection): forgot {weapon.Label}");
            }
            else
            {
                AutoArmLogger.Debug(() => $"[{pawn.LabelShort}] SS InformOfDroppedSidearm FAILED: no delegate or method available for {weapon.Label}");
            }

            InvalidateCanPickupCache(pawn);
        }

        public static void InformOfAddedSidearm(Pawn pawn, ThingWithComps weapon)
        {
            if (!IsLoaded || pawn == null || weapon == null) return;
            Initialize();
            if (_reflectionFailed) return;

            var memory = GetMemoryComp(pawn);
            if (memory == null) return;

            if (_informAddedDelegate != null)
            {
                _informAddedDelegate(memory, weapon);
            }
            else if (_informAddedSidearmMethod != null)
            {
                _informAddedSidearmMethod.Invoke(memory, new object[] { weapon });
            }

            InvalidateCanPickupCache(pawn);
        }

        public static void InformOfAddedPrimary(Pawn pawn, ThingWithComps weapon)
        {
            if (!IsLoaded || pawn == null || weapon == null) return;
            Initialize();
            if (_reflectionFailed) return;

            var memory = GetMemoryComp(pawn);
            if (memory == null) return;

            if (_informAddedPrimaryDelegate != null)
            {
                _informAddedPrimaryDelegate(memory, weapon);
            }
            else if (_informAddedPrimaryMethod != null)
            {
                _informAddedPrimaryMethod.Invoke(memory, new object[] { weapon });
            }
            else
            {
                InformOfAddedSidearm(pawn, weapon);
            }

            InvalidateCanPickupCache(pawn);
        }

        public static void LogRememberedWeapons(Pawn pawn, string context)
        {
            if (!IsLoaded || pawn == null) return;
            Initialize();
            var memory = GetMemoryComp(pawn);
            if (memory == null) return;

            var remembered = _rememberedWeaponsProp?.GetValue(memory) as System.Collections.IEnumerable;
            if (remembered == null) return;

            var names = new System.Text.StringBuilder();
            int count = 0;
            foreach (var item in remembered)
            {
                if (item == null) continue;
                var thingDef = _pairThingField?.GetValue(item) as ThingDef;
                if (thingDef == null) continue;
                if (count > 0) names.Append(", ");
                names.Append(thingDef.defName);
                count++;
            }
            AutoArmLogger.Debug(() => $"[{pawn.LabelShort}] SS remembered ({context}): [{count}] {names}");
        }

        public static void ClearAllCaches()
        {
            _pawnCaches.Clear();
            _lastUpgradeCheckTick.Clear();
            idToPawnLookup.Clear();
            hashToPairLookup.Clear();
            _isActiveCacheExpiry = 0;
            AutoArmLogger.Debug(() => "[SimpleSidearms] Caches cleared");
        }

        public static void CleanupCaches()
        {
            // Early exit if nothing to clean
            if (_pawnCaches.Count == 0 && _lastUpgradeCheckTick.Count == 0)
                return;

            int currentTick = Find.TickManager.TicksGame;

            var validIds = new HashSet<int>();
            if (Find.Maps != null)
            {
                foreach (var map in Find.Maps)
                {
                    if (map?.mapPawns?.AllPawns == null) continue;
                    foreach (var p in map.mapPawns.AllPawns)
                    {
                        if (p == null || p.Dead || p.Destroyed || !p.Spawned) continue;
                        if (!p.IsColonist && !p.IsPrisonerOfColony) continue;
                        validIds.Add(p.thingIDNumber);
                    }
                }
            }

            var toRemove = ListPool<int>.Get();

            foreach (var kvp in _pawnCaches)
            {
                var pawnId = kvp.Key;
                var cache = kvp.Value;

                if (!validIds.Contains(pawnId))
                {
                    toRemove.Add(pawnId);
                    continue;
                }

                if (_lastUpgradeCheckTick.TryGetValue(pawnId, out int lastCheck))
                {
                    if (currentTick - lastCheck > INACTIVE_PAWN_TIMEOUT)
                    {
                        toRemove.Add(pawnId);
                        continue;
                    }
                }

                if (currentTick - cache.LastCleanupTick > 10000)
                {
                    cache.LastCleanupTick = currentTick;

                    var expiredKeys = ListPool<PairKey>.Get();
                    foreach (var entry in cache.ValidationCache)
                    {
                        if (currentTick > entry.Value.expiry)
                        {
                            expiredKeys.Add(entry.Key);
                        }
                    }

                    foreach (var key in expiredKeys)
                    {
                        cache.ValidationCache.Remove(key);
                    }

                    ListPool<PairKey>.Return(expiredKeys);
                }
            }

            var orphanedIds = ListPool<int>.Get();
            foreach (var id in _lastUpgradeCheckTick.Keys)
            {
                if (!validIds.Contains(id) ||
                    (currentTick - _lastUpgradeCheckTick[id] > INACTIVE_PAWN_TIMEOUT))
                {
                    orphanedIds.Add(id);
                }
            }

            toRemove.AddRange(orphanedIds);
            ListPool<int>.Return(orphanedIds);

            foreach (var pawnId in toRemove)
            {
                _pawnCaches.Remove(pawnId);
                _lastUpgradeCheckTick.Remove(pawnId);
                // Events will no-op
            }

            if (_pawnCaches.Count > MAX_CACHE_SIZE)
            {
                int entriesToRemove = _pawnCaches.Count - MAX_CACHE_SIZE;
                var candidates = ListPool<KeyValuePair<int, int>>.Get(_lastUpgradeCheckTick.Count);

                foreach (var kvp in _lastUpgradeCheckTick)
                {
                    candidates.Add(kvp);
                }

                candidates.SortBy(kvp => kvp.Value);

                var oldestIds = ListPool<int>.Get(Math.Min(entriesToRemove, candidates.Count));
                for (int i = 0; i < Math.Min(entriesToRemove, candidates.Count); i++)
                {
                    oldestIds.Add(candidates[i].Key);
                }

                foreach (var pawnId in oldestIds)
                {
                    _pawnCaches.Remove(pawnId);
                    _lastUpgradeCheckTick.Remove(pawnId);
                }

                ListPool<int>.Return(oldestIds);
                ListPool<KeyValuePair<int, int>>.Return(candidates);

                AutoArmLogger.Debug(() => $"[SimpleSidearms] Cache trimmed to {MAX_CACHE_SIZE} entries");
            }

            ListPool<int>.Return(toRemove);

            if (hashToPairLookup.Count > 0)
            {
                var liveHashes = new HashSet<int>();
                foreach (var cache in _pawnCaches.Values)
                {
                    if (cache == null) continue;
                    foreach (var key in cache.ValidationCache.Keys)
                        liveHashes.Add(key.ToEncodedInt());
                }

                var orphanHashes = ListPool<int>.Get();
                foreach (var hash in hashToPairLookup.Keys)
                {
                    if (!liveHashes.Contains(hash))
                        orphanHashes.Add(hash);
                }
                foreach (var hash in orphanHashes)
                    hashToPairLookup.Remove(hash);
                ListPool<int>.Return(orphanHashes);
            }
        }

        public static void RemovePawn(Pawn pawn)
        {
            if (pawn == null) return;

            int pawnId = pawn.thingIDNumber;
            if (_pawnCaches.TryGetValue(pawnId, out var cache))
            {
                // Cancel validations
                foreach (var key in cache.ValidationCache.Keys)
                {
                    int pairHash = key.ToEncodedInt();
                    TickScheduler.Cancel(TickScheduler.EventType.SimpleSidearmsValidation, pawnId, pairHash);
                }
            }

            _pawnCaches.Remove(pawnId);
            _lastUpgradeCheckTick.Remove(pawnId);
            idToPawnLookup.Remove(pawnId);
        }

        private static void InvalidateCanPickupCache(Pawn pawn)
        {
            if (pawn == null) return;
            int pawnId = pawn.thingIDNumber;
            WeaponCheckCache cache;
            if (_pawnCaches.TryGetValue(pawnId, out cache) && cache != null)
            {
                // Cancel validations
                foreach (var key in cache.ValidationCache.Keys)
                {
                    int pairHash = key.ToEncodedInt();
                    TickScheduler.Cancel(TickScheduler.EventType.SimpleSidearmsValidation, pawnId, pairHash);
                }

                cache.ValidationCache.Clear();
                cache.LastCleanupTick = Find.TickManager.TicksGame;
            }
            _lastUpgradeCheckTick[pawnId] = 0;
        }

        public static void InvalidatePawnCache(Pawn pawn) => InvalidateCanPickupCache(pawn);

        public static void InvalidateAllCaches()
        {
            foreach (var kvp in _pawnCaches)
            {
                if (kvp.Value != null)
                {
                    kvp.Value.ValidationCache.Clear();
                }
            }
            _lastUpgradeCheckTick.Clear();
            idToPawnLookup.Clear();
            hashToPairLookup.Clear();
            _isActiveCacheExpiry = 0;
            AutoArmLogger.Debug(() => "[SimpleSidearms] Caches invalidated");
        }

        private static object GetMemoryComp(Pawn pawn)
        {
            if (_getMemoryDelegate != null)
            {
                return _getMemoryDelegate(pawn, true);
            }
            else if (_getMemoryCompMethod != null)
            {
                var paramCount = _getMemoryCompMethod.GetParameters().Length;
                if (paramCount == 2)
                {
                    return _getMemoryCompMethod.Invoke(null, new object[] { pawn, true });
                }
                else
                {
                    return _getMemoryCompMethod.Invoke(null, new object[] { pawn });
                }
            }
            return null;
        }

        private static int CountCarriedWeapons(Pawn pawn)
        {
            int count = pawn.equipment?.Primary != null ? 1 : 0;
            var inv = pawn.inventory?.innerContainer;
            if (inv != null)
            {
                foreach (var thing in inv)
                {
                    if (Validation.IsWeapon(thing.def))
                        count++;
                }
            }
            return count;
        }

        private static WeaponCheckCache GetOrCreateCache(Pawn pawn)
        {
            int pawnId = pawn.thingIDNumber;
            if (!_pawnCaches.TryGetValue(pawnId, out var cache))
            {
                if (_pawnCaches.Count >= MAX_CACHE_SIZE)
                {
                    int targetSize = MAX_CACHE_SIZE - (MAX_CACHE_SIZE / 4);
                    int toRemove = _pawnCaches.Count - targetSize;

                    var lruCandidates = ListPool<KeyValuePair<int, int>>.Get(_lastUpgradeCheckTick.Count);
                    foreach (var kvp in _lastUpgradeCheckTick)
                    {
                        lruCandidates.Add(kvp);
                    }

                    lruCandidates.SortBy(kvp => kvp.Value);

                    int removed = 0;
                    for (int i = 0; i < Math.Min(toRemove, lruCandidates.Count); i++)
                    {
                        var oldPawnId = lruCandidates[i].Key;
                        _pawnCaches.Remove(oldPawnId);
                        _lastUpgradeCheckTick.Remove(oldPawnId);
                        // Events will no-op
                        removed++;
                    }

                    ListPool<KeyValuePair<int, int>>.Return(lruCandidates);

                    if (AutoArmMod.settings?.debugLogging == true && removed > 0)
                    {
                        AutoArmLogger.Debug(() => $"[SimpleSidearms] Batch evicted {removed} LRU pawns (was {_pawnCaches.Count + removed}, now {_pawnCaches.Count})");
                    }
                }

                cache = new WeaponCheckCache();
                _pawnCaches[pawnId] = cache;
            }
            return cache;
        }

        public static bool ShouldTreatAsPrimaryReplacement(Pawn pawn, ThingWithComps newWeapon, ThingWithComps currentPrimary)
        {
            if (currentPrimary == null || newWeapon == null)
                return false;

            float currentScore = CalculateWeaponScore(pawn, currentPrimary);
            float newScore = CalculateWeaponScore(pawn, newWeapon);

            float threshold = AutoArmMod.settings?.weaponUpgradeThreshold ?? Definitions.Constants.WeaponUpgradeThreshold;

            return newScore > currentScore * threshold;
        }

        private static bool ShouldUpgradePrimary(Pawn pawn, ThingWithComps newWeapon, ThingWithComps currentPrimary)
        {
            if (newWeapon.def.IsRangedWeapon != currentPrimary.def.IsRangedWeapon)
                return false;

            return ShouldTreatAsPrimaryReplacement(pawn, newWeapon, currentPrimary);
        }

        private static ThingWithComps FindSidearmToReplace(Pawn pawn, ThingWithComps newWeapon)
        {
            if (pawn.inventory?.innerContainer == null)
                return null;

            // Prevent duplicates
            foreach (Thing thing in pawn.inventory.innerContainer)
            {
                if (thing is ThingWithComps w && w.def == newWeapon.def && w.def.IsWeapon)
                {
                    AutoArmLogger.Debug(() => $"[SimpleSidearms] Skipping cross-def swap - pawn already has {newWeapon.def.defName}");
                    return null;
                }
            }

            float newWeaponWeight = newWeapon.GetStatValue(StatDefOf.Mass, cacheStaleAfterTicks: 2500);
            float currentFreeSpace = MassUtility.FreeSpace(pawn);

            float newScore = CalculateWeaponScore(pawn, newWeapon);
            ThingWithComps worstSidearm = null;
            float worstScore = float.MaxValue;
            bool worstIsForced = false;

            foreach (Thing thing in pawn.inventory.innerContainer)
            {
                var weapon = thing as ThingWithComps;
                if (weapon == null || !Validation.IsWeapon(weapon.def))
                    continue;

                bool isForced = ForcedWeapons.IsForced(pawn, weapon);

                if (isForced && AutoArmMod.settings?.allowForcedWeaponUpgrades != true)
                {
                    AutoArmLogger.Debug(() => $"[SimpleSidearms] Skipping forced sidearm {weapon.Label}");
                    continue;
                }

                bool isSameType = weapon.def.IsRangedWeapon == newWeapon.def.IsRangedWeapon;
                bool isSameDefUpgrade = weapon.def == newWeapon.def;

                if (isForced && !isSameDefUpgrade)
                {
                    AutoArmLogger.Debug(() => $"[SimpleSidearms] Skipping forced sidearm {weapon.Label} for cross-def swap");
                    continue;
                }

                float effectiveThreshold;
                if (isSameDefUpgrade)
                {
                    // Same-def: just needs to be better
                    effectiveThreshold = 1.0f;
                }
                else
                {
                    effectiveThreshold = AutoArmMod.settings?.weaponUpgradeThreshold ?? Definitions.Constants.WeaponUpgradeThreshold;
                    if (!isSameType)
                    {
                        effectiveThreshold *= 1.5f;
                    }
                    if (!IsSidearm(pawn, weapon))
                    {
                        effectiveThreshold *= 1.2f;
                    }
                }

                float weightDiff = newWeaponWeight - weapon.GetStatValue(StatDefOf.Mass, cacheStaleAfterTicks: 2500);
                if (weightDiff > currentFreeSpace)
                {
                    AutoArmLogger.Debug(() => $"Can't replace {weapon.Label} with {newWeapon.Label} - would exceed weight limit");
                    continue;
                }

                float score = CalculateWeaponScore(pawn, weapon);

                if (isForced)
                {
                    score *= 1.2f;
                }

                if (score < worstScore)
                {
                    if (newScore > score * effectiveThreshold)
                    {
                        worstScore = score;
                        worstSidearm = weapon;
                        worstIsForced = isForced;
                    }
                }
            }

            if (worstSidearm != null)
            {
                bool isSameDef = worstSidearm.def == newWeapon.def;
                float finalThreshold;

                if (isSameDef)
                {
                    // Same-def: just needs to be better
                    finalThreshold = worstIsForced ? 1.2f : 1.0f;
                }
                else
                {
                    finalThreshold = AutoArmMod.settings?.weaponUpgradeThreshold ?? Definitions.Constants.WeaponUpgradeThreshold;
                    if (worstIsForced)
                    {
                        finalThreshold *= 1.2f;
                    }
                    if (worstSidearm.def.IsRangedWeapon != newWeapon.def.IsRangedWeapon)
                    {
                        finalThreshold *= 1.5f;
                    }
                }

                if (newScore > worstScore * finalThreshold)
                {
                    if (AutoArmMod.settings?.debugLogging == true)
                    {
                        string typeInfo = worstSidearm.def.IsRangedWeapon != newWeapon.def.IsRangedWeapon ? " (cross-type)" : "";
                        string forcedInfo = worstIsForced ? " (forced)" : "";
                        string sameDefInfo = isSameDef ? " (same-def)" : "";
                        AutoArmLogger.Debug(() => $"Replacing sidearm {worstSidearm.Label}{forcedInfo} (score {worstScore:F1}) with {newWeapon.Label} (score {newScore:F1}){typeInfo}{sameDefInfo}");
                    }
                    return worstSidearm;
                }
            }

            return null;
        }

        private static float CalculateWeaponScore(Pawn pawn, ThingWithComps weapon)
        {
            if (weapon == null || !weapon.def.IsWeapon) return 0f;

            return AutoArm.Caching.WeaponCache.GetCachedScore(pawn, weapon);
        }


        private static Job CreatePrimaryUpgradeJob(Pawn pawn, ThingWithComps newWeapon, ThingWithComps oldWeapon)
        {
            return JobMaker.MakeJob(AutoArmDefOf.AutoArmSwapPrimary, newWeapon, oldWeapon);
        }

        private static Job CreateSidearmUpgradeJob(Pawn pawn, ThingWithComps newWeapon, ThingWithComps oldWeapon)
        {
            return JobMaker.MakeJob(AutoArmDefOf.AutoArmSwapSidearm, newWeapon, oldWeapon);
        }

        private static Job CreateAddSidearmJob(Pawn pawn, ThingWithComps weapon)
        {
            Job job;
            if (AutoArmDefOf.EquipSecondary != null)
                job = JobMaker.MakeJob(AutoArmDefOf.EquipSecondary, weapon);
            else
                job = JobMaker.MakeJob(JobDefOf.Equip, weapon);

            job.count = 1;
            return job;
        }


        public static void OnValidationExpiredEvent(int pawnId, int pairHash)
        {
            if (!hashToPairLookup.TryGetValue(pairHash, out var key))
                return;

            if (_pawnCaches.TryGetValue(pawnId, out var cache))
            {
                cache.ValidationCache.Remove(key);
            }
        }

        public static void Reset()
        {
            _pawnCaches.Clear();
            _lastUpgradeCheckTick.Clear();
            idToPawnLookup.Clear();
            hashToPairLookup.Clear();
            _isActiveCacheExpiry = 0;
        }

        // Stub for old save compat
        public class SimpleSidearmsMapComponent : MapComponent
        {
            public SimpleSidearmsMapComponent(Map map) : base(map) { }
        }
    }

    [HarmonyPatch]
    [HarmonyPatchCategory(Patches.PatchCategories.Compatibility)]
    internal static class CompSidearmMemory_InformOfAddedSidearm_Patch
    {
        public static bool Prepare()
        {
            return SimpleSidearmsCompat.IsLoaded && !SimpleSidearmsCompat.ReflectionFailed;
        }

        public static MethodBase TargetMethod()
        {
            var type = AccessTools.TypeByName("SimpleSidearms.rimworld.CompSidearmMemory")
                ?? AccessTools.TypeByName("PeteTimesSix.SimpleSidearms.CompSidearmMemory");
            if (type == null) return null;
            return AccessTools.Method(type, "InformOfAddedSidearm", new Type[] { typeof(Thing) });
        }

        [HarmonyPostfix]
        public static void Postfix(object __instance, Thing weapon)
        {
            if (AutoArmMod.settings?.modEnabled != true)
                return;

            try
            {
                var comp = __instance as ThingComp;
                var pawn = comp?.parent as Pawn;
                if (pawn == null) return;

                SimpleSidearmsCompat.InvalidatePawnCache(pawn);

                if (AutoArmMod.settings?.showNotifications != true) return;
                if (pawn.Faction != Faction.OfPlayerSilentFail) return;
                if (!PawnUtility.ShouldSendNotificationAbout(pawn)) return;

                var curJobDef = pawn.CurJob?.def;
                if (curJobDef == null || curJobDef != AutoArmDefOf.EquipSecondary) return;

                var weaponWithComps = weapon as ThingWithComps;
                if (weaponWithComps == null || !AutoArm.Validation.IsWeapon(weaponWithComps)) return;

                ThingWithComps existingSameDef = null;
                var inv = pawn.inventory?.innerContainer;
                if (inv != null)
                {
                    for (int i = 0; i < inv.Count; i++)
                    {
                        if (inv[i] is ThingWithComps twc &&
                            twc != weaponWithComps &&
                            twc.def == weaponWithComps.def &&
                            AutoArm.Validation.IsWeapon(twc))
                        {
                            existingSameDef = twc;
                            break;
                        }
                    }
                }

                if (existingSameDef != null)
                {
                    Messages.Message("AutoArm_UpgradedSidearm".Translate(
                        pawn.LabelShort.CapitalizeFirst(),
                        existingSameDef.Label ?? existingSameDef.def?.label ?? "old sidearm",
                        weaponWithComps.Label ?? weaponWithComps.def?.label ?? "new sidearm"
                    ), new LookTargets(pawn), MessageTypeDefOf.SilentInput, false);
                }
                else
                {
                    Messages.Message("AutoArm_EquippedSidearm".Translate(
                        pawn.LabelShort.CapitalizeFirst(),
                        weaponWithComps.Label ?? weaponWithComps.def?.label ?? "sidearm"
                    ), new LookTargets(pawn), MessageTypeDefOf.SilentInput, false);
                }
            }
            catch (System.Exception e)
            {
                AutoArm.AutoArmLogger.ErrorPatch(e, "CompSidearmMemory_InformOfAddedSidearm_Patch.Postfix");
            }
        }
    }

    [HarmonyPatch]
    [HarmonyPatchCategory(Patches.PatchCategories.Compatibility)]
    internal static class CompSidearmMemory_InformOfDroppedSidearm_Patch
    {
        public static bool Prepare()
        {
            return SimpleSidearmsCompat.IsLoaded && !SimpleSidearmsCompat.ReflectionFailed;
        }

        public static MethodBase TargetMethod()
        {
            var type = AccessTools.TypeByName("SimpleSidearms.rimworld.CompSidearmMemory")
                ?? AccessTools.TypeByName("PeteTimesSix.SimpleSidearms.CompSidearmMemory");
            if (type == null) return null;
            return AccessTools.Method(type, "InformOfDroppedSidearm", new Type[] { typeof(Thing), typeof(bool) });
        }

        [HarmonyPostfix]
        public static void Postfix(object __instance)
        {
            if (AutoArmMod.settings?.modEnabled != true)
                return;

            try
            {
                var comp = __instance as ThingComp;
                var pawn = comp?.parent as Pawn;
                if (pawn == null) return;

                SimpleSidearmsCompat.InvalidatePawnCache(pawn);
            }
            catch (System.Exception e)
            {
                AutoArm.AutoArmLogger.ErrorPatch(e, "CompSidearmMemory_InformOfDroppedSidearm_Patch.Postfix");
            }
        }
    }

    [HarmonyPatch]
    [HarmonyPatchCategory(Patches.PatchCategories.Compatibility)]
    [HarmonyPriority(Priority.Last)]
    internal static class JobGiver_RetrieveWeapon_TryGiveJob_Patch
    {
        private static Type _jobGiverType;

        public static bool Prepare()
        {
            if (!SimpleSidearmsCompat.IsLoaded)
                return false;

            // SS uses namespace SimpleSidearms.rimworld for this class
            _jobGiverType = AccessTools.TypeByName("SimpleSidearms.rimworld.JobGiver_RetrieveWeapon");

            if (_jobGiverType != null)
            {
                AutoArm.AutoArmLogger.Debug(() => "Found JobGiver_RetrieveWeapon for SS re-equip blocking");
            }

            return _jobGiverType != null;
        }

        public static MethodBase TargetMethod()
        {
            if (_jobGiverType == null) return null;
            return AccessTools.Method(_jobGiverType, "TryGiveJob", new Type[] { typeof(Pawn) });
        }

        [HarmonyPostfix]
        public static void Postfix(Pawn pawn, ref Job __result)
        {
            if (AutoArmMod.settings?.modEnabled != true)
                return;

            try
            {
                // Block duplicate re-equips
                if (__result == null || pawn?.inventory?.innerContainer == null)
                    return;

                var targetWeapon = __result.targetA.Thing as ThingWithComps;
                if (targetWeapon == null || !targetWeapon.def.IsWeapon)
                    return;

                if (AutoArm.ForcedWeapons.IsForced(pawn, targetWeapon))
                    return;

                foreach (var thing in pawn.inventory.innerContainer)
                {
                    if (thing is ThingWithComps w && w.def == targetWeapon.def && w.def.IsWeapon)
                    {
                        // Let AutoArm handle upgrades
                        float existingScore = AutoArm.Caching.WeaponCache.GetCachedScore(pawn, w);
                        float newScore = AutoArm.Caching.WeaponCache.GetCachedScore(pawn, targetWeapon);

                        if (newScore <= existingScore)
                        {
                            AutoArm.AutoArmLogger.Debug(() =>
                                $"[{pawn.LabelShort}] Blocked SS re-equip: already has {w.Label} ({existingScore:F1}), target {targetWeapon.Label} ({newScore:F1}) not better");
                            __result = null;
                            return;
                        }
                        else
                        {
                            return;
                        }
                    }
                }
            }
            catch (System.Exception e)
            {
                AutoArm.AutoArmLogger.ErrorPatch(e, "JobGiver_RetrieveWeapon_TryGiveJob_Patch.Postfix");
            }
        }
    }

    [HarmonyPatch]
    [HarmonyPatchCategory(Patches.PatchCategories.Compatibility)]
    internal static class FloatMenuOptionProvider_EquipSidearm_Patch
    {
        public static bool Prepare()
        {
            return SimpleSidearmsCompat.IsLoaded;
        }

        public static MethodBase TargetMethod()
        {
            var type = AccessTools.TypeByName("PeteTimesSix.SimpleSidearms.FloatMenuOptionProvider_EquipSidearm");
            if (type == null) return null;
            return AccessTools.Method(type, "GetSingleOptionFor", new Type[] { typeof(Thing), typeof(FloatMenuContext) });
        }

        [HarmonyPostfix]
        public static void Postfix(Thing clickedThing, FloatMenuContext context, ref FloatMenuOption __result)
        {
            if (__result == null || __result.action == null)
                return;
            if (AutoArmMod.settings?.modEnabled != true)
                return;
            if (clickedThing == null || !clickedThing.def.IsWeapon)
                return;

            var twc = clickedThing as ThingWithComps;
            if (twc == null)
                return;

            var pawn = context?.FirstSelectedPawn;
            if (pawn == null)
                return;

            string oldPrefix = "Equip".Translate(clickedThing.LabelShort).ToString();
            string newPrefix = "ForceEquipApparel".Translate(clickedThing.LabelShort, clickedThing).ToString();
            if (__result.Label != null && __result.Label.StartsWith(oldPrefix))
            {
                __result.Label = newPrefix + __result.Label.Substring(oldPrefix.Length);
            }

            var originalAction = __result.action;
            __result.action = delegate
            {
                if (pawn != null && !pawn.Destroyed && !pawn.Dead && twc != null && !twc.Destroyed)
                    AutoArm.Jobs.AutoEquipState.SetWeaponToForce(pawn, twc);
                originalAction();
            };
        }
    }
}
