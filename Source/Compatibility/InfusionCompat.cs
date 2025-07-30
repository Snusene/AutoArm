using System;
using System.Collections;
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

        private static Type compInfusionType;
        private static MethodInfo getInfusionsMethod;
        private static PropertyInfo getInfusionsProperty;

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

            try
            {
                compInfusionType = GenTypes.AllTypes.FirstOrDefault(t =>
                    t.Name == "CompInfusion" &&
                    t.Namespace != "AutoArm");

                if (compInfusionType == null)
                {
                    if (AutoArmMod.settings?.debugLogging == true)
                    {
                        Log.Message("[AutoArm] Could not find CompInfusion type");
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
                    Log.Message($"[AutoArm] Infusion 2 init: Found property: {getInfusionsProperty != null}, Found method: {getInfusionsMethod != null}");
                }
            }
            catch (Exception e)
            {
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    Log.Warning($"[AutoArm] Failed to initialize Infusion 2 compatibility: {e.Message}");
                }
                _initFailed = true;
            }
        }

        private static bool IsCollectionType(Type type)
        {
            return type != typeof(string) &&
                   typeof(IEnumerable).IsAssignableFrom(type);
        }

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
                    c.GetType() == compInfusionType ||
                    c.GetType().Name == "CompInfusion");

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

                    if (count > 0 && AutoArmMod.settings?.debugLogging == true)
                    {
                        Log.Message($"[AutoArm] {weapon.Label} has {count} infusions");
                    }

                    return count * 25f;
                }

                return 0f;
            }
            catch (Exception e)
            {
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    Log.Warning($"[AutoArm] Error checking infusions: {e.Message}");
                }
                return 0f;
            }
        }

        public static bool HasInfusions(ThingWithComps weapon)
        {
            return GetInfusionScoreBonus(weapon) > 0f;
        }

        public static float GetInfusionMultiplier()
        {
            return 1.0f;
        }

        public static string GetInfusionDetails(ThingWithComps weapon)
        {
            var bonus = GetInfusionScoreBonus(weapon);
            if (bonus > 0)
            {
                int count = (int)(bonus / 25f);
                return $"{count} infusion(s)";
            }
            return "No infusions";
        }

        public static void DebugListInfusionTypes()
        {
            if (!IsLoaded())
            {
                Log.Message("[AutoArm] Infusion 2 is not loaded");
                return;
            }

            Log.Message("\n[AutoArm] === Searching for Infusion Types ===");

            var infusionTypes = GenTypes.AllTypes
                .Where(t => t.Name.IndexOf("infusion", StringComparison.OrdinalIgnoreCase) >= 0 &&
                           !t.FullName.Contains("AutoArm"))
                .OrderBy(t => t.FullName)
                .Take(20)
                .ToList();

            Log.Message($"[AutoArm] Found {infusionTypes.Count} possible Infusion-related types:");

            foreach (var type in infusionTypes)
            {
                Log.Message($"  - {type.FullName}");
            }

            var compType = GenTypes.AllTypes.FirstOrDefault(t =>
                t.Name == "CompInfusion" && t.Namespace != "AutoArm");

            if (compType != null)
            {
                Log.Message($"\n[AutoArm] Found CompInfusion: {compType.FullName}");
                Log.Message("[AutoArm] Members that might contain infusions:");

                foreach (var member in compType.GetMembers(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (member.Name.IndexOf("infusion", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        Log.Message($"  - {member.MemberType}: {member.Name}");
                    }
                }
            }

            Log.Message("[AutoArm] === End Types ===\n");
        }
    }
}