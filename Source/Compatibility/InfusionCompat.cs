using AutoArm.Logging;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;

namespace AutoArm
{
    public static class InfusionCompat
    {
        private static bool? _isLoaded = null;
        private static bool _initialized = false;
        private static bool _initFailed = false;
        private static readonly object _initLock = new object();

        // Score bonus per infusion - balanced for typical weapon scoring
        private const float SCORE_PER_INFUSION = 25f;

        // Track logged weapons to avoid spam
        private static HashSet<string> _loggedWeapons = new HashSet<string>();

        private static Type compInfusionType;
        private static MethodInfo getInfusionsMethod;
        private static PropertyInfo getInfusionsProperty;

        /// <summary>
        /// Checks if any Infusion mod is loaded and active
        /// </summary>
        public static bool IsLoaded()
        {
            if (_isLoaded == null)
            {
                _isLoaded = ModLister.AllInstalledMods.Any(m =>
                    m.Active && (
                        m.PackageIdPlayerFacing.IndexOf("infusion", StringComparison.OrdinalIgnoreCase) >= 0 &&
                        m.PackageIdPlayerFacing.IndexOf("autoarm", StringComparison.OrdinalIgnoreCase) < 0
                    ));
            }
            return _isLoaded.Value;
        }

        private static void EnsureInitialized()
        {
            if (_initialized || _initFailed || !IsLoaded())
                return;

            lock (_initLock)
            {
                // Double-check after acquiring lock
                if (_initialized || _initFailed)
                    return;

                try
                {
                    compInfusionType = GenTypes.AllTypes.FirstOrDefault(t =>
                        t.Name == "CompInfusion" &&
                        t.Namespace != "AutoArm");

                    if (compInfusionType == null)
                    {
                        if (AutoArmMod.settings?.debugLogging == true)
                        {
                            AutoArmLogger.Debug("InfusionCompat: Could not find CompInfusion type");
                        }
                        _initFailed = true;
                        return;
                    }

                    var members = compInfusionType.GetMembers(BindingFlags.Public | BindingFlags.Instance);

                    foreach (var member in members)
                    {
                        if (member.Name.IndexOf("infusion", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            if (member is PropertyInfo prop && IsCollectionType(prop.PropertyType))
                            {
                                getInfusionsProperty = prop;
                                break;
                            }
                            else if (member is MethodInfo method &&
                                    method.GetParameters().Length == 0 &&
                                    IsCollectionType(method.ReturnType))
                            {
                                getInfusionsMethod = method;
                                break;
                            }
                        }
                    }

                    _initialized = true;

                    if (AutoArmMod.settings?.debugLogging == true)
                    {
                        AutoArmLogger.Debug($"InfusionCompat initialized - Property: {getInfusionsProperty != null}, Method: {getInfusionsMethod != null}");
                    }
                }
                catch (Exception e)
                {
                    AutoArmLogger.Error("Failed to initialize Infusion 2 compatibility", e);
                    _initFailed = true;
                }
            }
        }

        private static bool IsCollectionType(Type type)
        {
            return type != typeof(string) &&
                   typeof(IEnumerable).IsAssignableFrom(type);
        }

        /// <summary>
        /// Calculate weapon score bonus based on number of infusions
        /// </summary>
        /// <param name="weapon">The weapon to check for infusions</param>
        /// <returns>Score bonus (25 points per infusion)</returns>
        public static float GetInfusionScoreBonus(ThingWithComps weapon)
        {
            if (!IsLoaded() || weapon == null)
                return 0f;

            EnsureInitialized();

            if (_initFailed || compInfusionType == null)
                return 0f;

            try
            {
                var comp = weapon.AllComps?.FirstOrDefault(c =>
                    c.GetType() == compInfusionType);

                if (comp == null)
                    return 0f;

                IEnumerable infusions = null;

                if (getInfusionsProperty != null)
                {
                    infusions = getInfusionsProperty.GetValue(comp) as IEnumerable;
                }
                else if (getInfusionsMethod != null)
                {
                    infusions = getInfusionsMethod.Invoke(comp, null) as IEnumerable;
                }

                if (infusions == null)
                {
                    var possibleNames = new[] { "Infusions", "infusions", "InfusionList", "GetInfusions" };
                    foreach (var name in possibleNames)
                    {
                        var prop = comp.GetType().GetProperty(name);
                        if (prop != null)
                        {
                            var value = prop.GetValue(comp);
                            if (value is IEnumerable enumerable)
                            {
                                infusions = enumerable;
                                break;
                            }
                        }
                    }
                }

                if (infusions != null)
                {
                    int count = 0;
                    foreach (var inf in infusions)
                    {
                        if (inf != null) count++;
                    }

                    if (count > 0)
                    {
                        // Only log first time we see this weapon to avoid spam
                        var cacheKey = $"infusion_{weapon.thingIDNumber}";
                        if (!_loggedWeapons.Contains(cacheKey))
                        {
                            _loggedWeapons.Add(cacheKey);
                            if (_loggedWeapons.Count > 100) _loggedWeapons.Clear(); // Prevent unbounded growth

                            if (Prefs.DevMode && AutoArmMod.settings?.debugLogging == true)
                            {
                                AutoArmLogger.Debug($"InfusionCompat: {weapon.Label} has {count} infusions (+{count * SCORE_PER_INFUSION} score)");
                            }
                        }
                    }

                    return count * SCORE_PER_INFUSION;
                }

                return 0f;
            }
            catch (Exception e)
            {
                if (Prefs.DevMode && AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.Debug($"InfusionCompat: Error checking infusions for {weapon?.Label}: {e.Message}");
                }
                return 0f;
            }
        }

        /// <summary>
        /// Quick check if a weapon has any infusions
        /// </summary>
        public static bool HasInfusions(ThingWithComps weapon)
        {
            return GetInfusionScoreBonus(weapon) > 0f;
        }

        /// <summary>
        /// Get the global infusion multiplier (currently always 1.0)
        /// </summary>
        public static float GetInfusionMultiplier()
        {
            return 1.0f;
        }

        /// <summary>
        /// Get a human-readable string describing the weapon's infusions
        /// </summary>
        public static string GetInfusionDetails(ThingWithComps weapon)
        {
            var bonus = GetInfusionScoreBonus(weapon);
            if (bonus > 0)
            {
                int count = (int)(bonus / SCORE_PER_INFUSION);
                return $"{count} infusion(s)";
            }
            return "No infusions";
        }

        /// <summary>
        /// Debug method to list all Infusion-related types found via reflection
        /// </summary>
        public static void DebugListInfusionTypes()
        {
            if (!IsLoaded())
            {
                AutoArmLogger.Debug("InfusionCompat: Infusion 2 is not loaded");
                return;
            }

            AutoArmLogger.Debug("\n=== InfusionCompat: Searching for Infusion Types ===");

            var infusionTypes = GenTypes.AllTypes
                .Where(t => t.Name.IndexOf("infusion", StringComparison.OrdinalIgnoreCase) >= 0 &&
                           !t.FullName.Contains("AutoArm"))
                .OrderBy(t => t.FullName)
                .Take(20)
                .ToList();

            AutoArmLogger.Debug($"InfusionCompat: Found {infusionTypes.Count} possible Infusion-related types:");

            foreach (var type in infusionTypes)
            {
                AutoArmLogger.Debug($"  - {type.FullName}");
            }

            var compType = GenTypes.AllTypes.FirstOrDefault(t =>
                t.Name == "CompInfusion" && t.Namespace != "AutoArm");

            if (compType != null)
            {
                AutoArmLogger.Debug($"\nInfusionCompat: Found CompInfusion: {compType.FullName}");
                AutoArmLogger.Debug("InfusionCompat: Members that might contain infusions:");

                foreach (var member in compType.GetMembers(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (member.Name.IndexOf("infusion", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        AutoArmLogger.Debug($"  - {member.MemberType}: {member.Name}");
                    }
                }
            }

            AutoArmLogger.Debug("=== InfusionCompat: End Types ===\n");
        }
    }
}