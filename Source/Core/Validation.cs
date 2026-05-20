
using AutoArm.Definitions;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace AutoArm
{
    internal static class Validation
    {
        private static List<ThingDef> _rangedWeapons;

        private static List<ThingDef> _meleeWeapons;
        private static List<ThingDef> _allWeapons;

        private static readonly HashSet<string> ExcludedDefNames = new HashSet<string>
        {
            "PsychicInsanityLance", "PsychicShockLance", "PsychicSoothePulser", "PsychicAnimalPulser",
            "OrbitalTargeterPowerBeam", "OrbitalTargeterBombardment", "TornadoGenerator",

            "PowerClaw", "ScytherBlade", "FieldHand", "DrillArm",

            // Modded
            "Kiiro_PoisonBottle_Paralyzing", "Kiiro_PoisonBottle_Lethal",
            "Kiiro_PortableTurret_Base", "Kiiro_AutoTurret_Portable",
            "Kiiro_Stealth_Device", "Kiiro_Special_Item"
        };

        private static readonly Dictionary<ThingDef, bool> isWeaponDefCache = new Dictionary<ThingDef, bool>(256);

        public static void ClearWeaponDefCache()
        {
            isWeaponDefCache.Clear();
        }

        public static bool IsWeapon(Thing thing)
        {
            if (thing?.def == null)
                return false;

            var thingWithComps = thing as ThingWithComps;
            if (thingWithComps == null)
                return false;

            return IsWeapon(thing.def);
        }

        public static bool IsWeapon(ThingDef def)
        {
            if (def == null)
                return false;

            if (isWeaponDefCache.TryGetValue(def, out bool cached))
                return cached;

            bool result = ComputeIsWeapon(def);
            isWeaponDefCache[def] = result;
            return result;
        }

        private static bool ComputeIsWeapon(ThingDef def)
        {
            try
            {
                if (ExcludedDefNames.Contains(def.defName))
                {
                    if (AutoArmMod.settings?.debugLogging == true)
                    {
                        ExcludedItemTracker.TrackExcludedItem(def);
                    }
                    return false;
                }

                if (def.defName.EndsWith("_Unique"))
                {
                    if (def.thingClass != null &&
                        typeof(ThingWithComps).IsAssignableFrom(def.thingClass) &&
                        def.equipmentType != EquipmentType.None &&
                        !def.IsApparel)
                    {
                        return true;
                    }
                }


                if (!SafeCheckIsWeapon(def))
                {
                    LogValidationStep(def, "IsWeapon", false);
                    return false;
                }

                if (IsApparel(def))
                {
                    LogValidationStep(def, "IsApparel", true, "Weapon is marked as apparel");
                    return false;
                }

                if (!CheckEquipmentType(def))
                {
                    LogValidationStep(def, "CheckEquipmentType", false);
                    return false;
                }

                if (!HasEquippableComp(def))
                {
                    LogValidationStep(def, "HasEquippableComp", false);
                    return false;
                }

                if (ReferenceEquals(def, AutoArmDefOf.ElephantTusk) ||
                    ReferenceEquals(def, AutoArmDefOf.ThrumboHorn))
                    return true;

                if (def.IsIngestible)
                    return false;

                if (def.IsStuff)
                    return false;

                return true;
            }
            catch (Exception ex)
            {
                LogWeaponValidationFailure(def, ex);
                return false;
            }
        }


        private static void LogValidationStep(ThingDef def, string checkName, bool checkResult, string reason = null)
        {
            if (AutoArmMod.settings?.debugLogging != true)
                return;

            try
            {
                if (def.equipmentType != EquipmentType.Primary)
                    return;

                if (def.thingClass == null || !typeof(ThingWithComps).IsAssignableFrom(def.thingClass))
                    return;

                if (typeof(Building).IsAssignableFrom(def.thingClass))
                    return;

                var details = new System.Text.StringBuilder();
                details.Append($"[VALIDATION FAILED] {def.defName} ({def.label}) failed check '{checkName}'");

                if (!string.IsNullOrEmpty(reason))
                    details.Append($" - {reason}");

                details.AppendLine();
                details.AppendLine($"  Mod: {def.modContentPack?.Name ?? "unknown"}");
                details.AppendLine($"  ThingClass: {def.thingClass?.Name ?? "null"}");
                details.AppendLine($"  EquipmentType: {def.equipmentType}");
                details.AppendLine($"  IsWeapon: {TrySafeCheck(() => def.IsWeapon, "error")}");
                details.AppendLine($"  IsRangedWeapon: {TrySafeCheck(() => def.IsRangedWeapon, "error")}");
                details.AppendLine($"  IsApparel: {TrySafeCheck(() => def.IsApparel, "error")}");

                if (def.comps != null && def.comps.Count > 0)
                {
                    var compNames = string.Join(", ", def.comps.Select(c => c.compClass?.Name ?? "null"));
                    details.AppendLine($"  Comps: {compNames}");
                }
                else
                {
                    details.AppendLine($"  Comps: none");
                }

                AutoArmLogger.Debug(() => details.ToString());
            }
            catch (Exception e)
            {
                AutoArmLogger.WarnFileOnly($"LogValidationStep threw for {def?.defName ?? "null"}: {e.Message}");
            }
        }


        private static string TrySafeCheck(Func<bool> check, string errorValue)
        {
            try
            {
                return check().ToString();
            }
            catch
            {
                return errorValue;
            }
        }


        private static bool SafeCheckIsWeapon(ThingDef def)
        {
            if (def?.defName == null)
                return false;

            if (ExcludedDefNames.Contains(def.defName))
                return false;

            if (def.defName.EndsWith("_Unique"))
                return true;

            try
            {
                return def.IsWeapon;
            }
            catch (Exception ex)
            {
                LogWeaponValidationFailure(def, ex);
                return false;
            }
        }

        public static bool SafeCheckIsRangedWeapon(ThingDef def)
        {
            if (def?.defName == null)
                return false;

            if (ExcludedDefNames.Contains(def.defName))
                return false;

            try
            {
                return def.IsRangedWeapon;
            }
            catch (Exception ex)
            {
                LogWeaponValidationFailure(def, ex);
                return false;
            }
        }

        public static bool SafeCheckIsMeleeWeapon(ThingDef def)
        {
            if (def?.defName == null)
                return false;

            if (ExcludedDefNames.Contains(def.defName))
                return false;

            try
            {
                return def.IsMeleeWeapon;
            }
            catch (Exception ex)
            {
                LogWeaponValidationFailure(def, ex);
                return false;
            }
        }


        private static bool IsApparel(ThingDef def)
        {
            try
            {
                return def.IsApparel;
            }
            catch (Exception ex)
            {
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.Debug(() => $"IsApparel check failed for {def?.defName ?? "unknown"}: {ex.Message}");
                }
                return false;
            }
        }


        private static bool CheckEquipmentType(ThingDef def)
        {
            if (def?.defName?.EndsWith("_Unique") == true)
                return true;

            try
            {
                return def.equipmentType != EquipmentType.None;
            }
            catch (Exception ex)
            {
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.Debug(() => $"EquipmentType check failed for {def?.defName ?? "unknown"}: {ex.Message}");
                }
                return false;
            }
        }


        private static bool HasEquippableComp(ThingDef def)
        {
            if (def?.defName?.EndsWith("_Unique") == true)
                return true;

            try
            {
                return def.HasComp<CompEquippable>();
            }
            catch (Exception ex)
            {
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.Debug(() => $"CompEquippable check failed for {def?.defName ?? "unknown"}: {ex.Message}");
                }
                return false;
            }
        }


        private static void LogWeaponValidationFailure(ThingDef def, Exception ex)
        {
            if (def?.defName != null && ExcludedDefNames.Contains(def.defName))
                return;

            if (AutoArmMod.settings?.debugLogging == true)
            {
                var details = new System.Text.StringBuilder();
                details.AppendLine($"Weapon validation error: {ex.GetType().Name}");
                details.AppendLine($"  DefName: {def?.defName ?? "null"}");
                details.AppendLine($"  Label: {def?.label ?? "null"}");
                details.AppendLine($"  Mod: {def?.modContentPack?.Name ?? "unknown"}");
                details.AppendLine($"  Message: {ex.Message}");
                if (ex.InnerException != null)
                {
                    details.AppendLine($"  Inner: {ex.InnerException.Message}");
                }

                AutoArmLogger.Debug(() => details.ToString());
            }
        }

        private static readonly Comparison<ThingDef> WeaponOrder = CompareByTechLevelThenLabel;

        private static int CompareByTechLevelThenLabel(ThingDef a, ThingDef b)
        {
            int t = a.techLevel.CompareTo(b.techLevel);
            return t != 0 ? t : Comparer<string>.Default.Compare(a.label, b.label);
        }

        private static List<ThingDef> BuildWeaponList(Func<ThingDef, bool> filter, string label)
        {
            try
            {
                var list = new List<ThingDef>();
                foreach (var td in DefDatabase<ThingDef>.AllDefsListForReading)
                {
                    if (filter(td)) list.Add(td);
                }
                list.Sort(WeaponOrder);
                return list;
            }
            catch (Exception e)
            {
                AutoArmLogger.Error($"Critical error building {label} weapons cache", e);
                return new List<ThingDef>();
            }
        }

        public static List<ThingDef> RangedWeapons
        {
            get
            {
                if (_rangedWeapons == null)
                    _rangedWeapons = BuildWeaponList(td => IsWeapon(td) && SafeCheckIsRangedWeapon(td), "ranged");
                return _rangedWeapons;
            }
        }

        public static List<ThingDef> MeleeWeapons
        {
            get
            {
                if (_meleeWeapons == null)
                    _meleeWeapons = BuildWeaponList(td => IsWeapon(td) && SafeCheckIsMeleeWeapon(td), "melee");
                return _meleeWeapons;
            }
        }

        public static List<ThingDef> AllWeapons
        {
            get
            {
                if (_allWeapons == null)
                    _allWeapons = BuildWeaponList(td => IsWeapon(td), "all");
                return _allWeapons;
            }
        }

    }


    internal static class ExcludedItemTracker
    {
        private static Dictionary<string, int> excludedCounts = new Dictionary<string, int>();
        private static int lastReportTick = 0;
        private const int REPORT_INTERVAL = Constants.ExcludedItemReportInterval;

        public static void TrackExcludedItem(ThingDef def)
        {
            if (def?.defName == null) return;

            if (!excludedCounts.ContainsKey(def.defName))
                excludedCounts[def.defName] = 0;
            excludedCounts[def.defName]++;

            int currentTick = Find.TickManager?.TicksGame ?? 0;
            if (currentTick - lastReportTick > REPORT_INTERVAL && excludedCounts.Count > 0)
            {
                var topExcluded = excludedCounts
                    .OrderByDescending(kvp => kvp.Value)
                    .Take(5)
                    .Select(kvp => $"{kvp.Key}: {kvp.Value}")
                    .ToList();

                AutoArmLogger.Debug(() => $"Top excluded items (last minute): {string.Join(", ", topExcluded)}");
                excludedCounts.Clear();
                lastReportTick = currentTick;
            }
        }
    }
}
