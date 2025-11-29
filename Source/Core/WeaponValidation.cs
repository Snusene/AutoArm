
using AutoArm.Definitions;
using AutoArm.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace AutoArm.Weapons
{
    public static class WeaponValidation
    {
        private static List<ThingDef> _rangedWeapons;

        private static List<ThingDef> _meleeWeapons;
        private static List<ThingDef> _allWeapons;

        private static readonly HashSet<string> ExcludedDefNames = new HashSet<string>
        {
            "WoodLog", "Steel", "Plasteel", "Uranium", "Jade", "Silver", "Gold",
            "BlocksGranite", "BlocksLimestone", "BlocksSlate", "BlocksSandstone", "BlocksMarble",
            "ChunkGranite", "ChunkLimestone", "ChunkSlate", "ChunkSandstone", "ChunkMarble",
            "ChunkSlagSteel", "ChunkSlagSilver", "ChunkSlagGold", "ChunkSlagPlasteel",

            "ComponentIndustrial", "ComponentSpacer", "AIPersonaCore", "TechprofSubpersonaCore",
            "PowerfocusChip", "NanostructuringChip", "BroadshieldCore", "MechSerumHealer",
            "MechSerumResurrector", "SignalChip", "Biotuner",

            "MedicineHerbal", "MedicineIndustrial", "MedicineUltratech", "Penoxycyline",
            "Luciferium", "Neutroamine", "Ambrosia", "Beer", "Smokeleaf", "SmokeleafJoint",
            "Yayo", "Flake", "GoJuice", "WakeUp", "PsychiteTea", "Chemfuel",

            "RawPotatoes", "RawRice", "RawCorn", "RawBerries", "RawAgave", "RawFungus",
            "MealSimple", "MealFine", "MealLavish", "MealSurvivalPack", "MealNutrientPaste",
            "Pemmican", "Kibble", "Hay", "Chocolate", "InsectJelly", "Milk", "RawHops",
            "PsychoidLeaves", "SmokeleafLeaves", "Wort",

            "Meat_Human", "Meat_Megasloth", "Meat_Thrumbo", "Meat_Alphabeaver", "Meat_Muffalo",
            "Meat_Gazelle", "Meat_Iguana", "Meat_Rhinoceros", "Meat_Elephant", "Meat_Dromedary",
            "Meat_Horse", "Meat_Donkey", "Meat_Pig", "Meat_Cow", "Meat_Chicken", "Meat_Duck",

            "Cloth", "Synthread", "DevilstrandCloth", "Hyperweave", "WoolAlpaca", "WoolBison",
            "WoolMegasloth", "WoolMuffalo", "WoolSheep", "WoolYak", "Leather_Plain", "Leather_Dog",
            "Leather_Wolf", "Leather_Panthera", "Leather_Bear", "Leather_Human", "Leather_Pig",
            "Leather_Light", "Leather_Bird", "Leather_Chinchilla", "Leather_Fox", "Leather_Lizard",
            "Leather_Heavy", "Leather_Elephant", "Leather_Rhinoceros", "Leather_Thrumbo", "Leather_Patch",

            "Heart", "Lung", "Kidney", "Liver", "SimpleProstheticLeg", "SimpleProstheticArm",
            "BionicEye", "BionicArm", "BionicLeg", "BionicSpine", "BionicHeart", "BionicStomach",
            "ArchotechEye", "ArchotechArm", "ArchotechLeg", "CochlearImplant", "PowerClaw",
            "ScytherBlade", "FieldHand", "DrillArm", "MindscewHemisphere", "CircadianAssistant",

            "Shell_HighExplosive", "Shell_Incendiary", "Shell_EMP", "Shell_Firefoam", "Shell_Smoke",
            "Shell_AntigrainWarhead", "MortarShell", "ReinforcedBarrel",

            "PsychicInsanityLance", "PsychicShockLance", "PsychicSoothePulser", "PsychicAnimalPulser",
            "OrbitalTargeterPowerBeam", "OrbitalTargeterBombardment", "TornadoGenerator",
            "Apparel_ShieldBelt", "Apparel_SmokepopBelt", "Apparel_PsychicFoilHelmet",
            "FirefoamPopper", "HealrootCrafted",

            "EggChickenUnfertilized", "EggChickenFertilized", "EggDuckUnfertilized", "EggDuckFertilized",
            "EggGooseUnfertilized", "EggGooseFertilized", "EggTurkeyUnfertilized", "EggTurkeyFertilized",
            "EggCassowaryUnfertilized", "EggCassowaryFertilized", "EggEmuUnfertilized", "EggEmuFertilized",
            "EggOstrichUnfertilized", "EggOstrichFertilized", "EggIguanaUnfertilized", "EggIguanaFertilized",
            "EggCobraUnfertilized", "EggCobraFertilized", "EggTortoiseUnfertilized", "EggTortoiseFertilized",

            "RawHealroot", "RawDaylily", "RawBerries", "Plant_Psychoid", "Plant_Smokeleaf",
            "Plant_TreeOak", "Plant_TreePoplar", "Plant_TreePine", "Plant_TreeBirch", "Plant_TreeTeak",

            "BioMatter", "BioFuel", "RawMagicyte", "RefinedMagicyte", "Unobtainium",
            "NeuralChip", "QuantumChip", "NanoMaterial", "CarbonFiber", "ReinforcedGlass",

            "Kiiro_PoisonBottle_Paralyzing", "Kiiro_PoisonBottle_Lethal",
            "Kiiro_PortableTurret_Base", "Kiiro_AutoTurret_Portable",
            "Kiiro_Stealth_Device", "Kiiro_Special_Item"
        };

        /// <summary>
        /// Validates if a thing is actually a proper weapon that should be auto-equipped
        /// </summary>
        public static bool IsWeapon(Thing thing)
        {
            if (thing?.def == null)
                return false;

            var thingWithComps = thing as ThingWithComps;
            if (thingWithComps == null)
                return false;

            return IsWeapon(thing.def);
        }

        /// <summary>
        /// Validates weapon
        /// </summary>
        public static bool IsWeapon(ThingDef def)
        {
            try
            {
                if (def == null)
                    return false;

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

        /// <summary>
        /// Safely checks if ThingDef.IsRangedWeapon without throwing
        /// </summary>
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

        /// <summary>
        /// Safely checks if ThingDef.IsMeleeWeapon without throwing
        /// </summary>
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


        private static void LogWeaponValidationFailure(Thing thing, Exception ex)
        {
            if (thing?.def != null)
            {
                LogWeaponValidationFailure(thing.def, ex);
            }
            else
            {
                LogWeaponValidationFailure((ThingDef)null, ex);
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

        /// <summary>
        /// In exclusion list
        /// </summary>
        public static bool IsExcludedItem(ThingDef def)
        {
            return def != null && ExcludedDefNames.Contains(def.defName);
        }

        /// <summary>
        /// Debug helper to log why something is or isn't considered a weapon
        /// </summary>
        public static string GetWeaponValidationInfo(Thing thing)
        {
            if (thing?.def == null)
                return "Null thing or def";

            var reasons = new List<string>();

            if (ExcludedDefNames.Contains(thing.def.defName))
                reasons.Add($"Excluded item: {thing.def.defName}");

            if (!thing.def.IsWeapon)
                reasons.Add("Not marked as weapon");

            if (thing.def.IsApparel)
                reasons.Add("Is apparel");

            if (thing.def.equipmentType == EquipmentType.None)
                reasons.Add("No equipment type");

            if (!thing.def.HasComp(typeof(CompEquippable)))
                reasons.Add("No CompEquippable");

            return reasons.Any() ? string.Join(", ", reasons) : "Valid weapon";
        }

        /// <summary>
        /// Valid ranged
        /// </summary>
        public static List<ThingDef> RangedWeapons
        {
            get
            {
                if (_rangedWeapons == null)
                {
                    try
                    {
                        var allDefs = DefDatabase<ThingDef>.AllDefsListForReading;
                        var properWeapons = allDefs.Where(td => IsWeapon(td));
                        _rangedWeapons = properWeapons
                            .Where(td => SafeCheckIsRangedWeapon(td))
                            .OrderBy(td => td.techLevel)
                            .ThenBy(td => td.label)
                            .ToList();
                    }
                    catch (Exception e)
                    {
                        AutoArmLogger.Error("Critical error building ranged weapons cache", e);
                        _rangedWeapons = new List<ThingDef>();
                    }
                }
                return _rangedWeapons;
            }
        }

        /// <summary>
        /// Valid melee
        /// </summary>
        public static List<ThingDef> MeleeWeapons
        {
            get
            {
                if (_meleeWeapons == null)
                {
                    try
                    {
                        var allDefs = DefDatabase<ThingDef>.AllDefsListForReading;
                        var properWeapons = allDefs.Where(td => IsWeapon(td));
                        _meleeWeapons = properWeapons
                            .Where(td => SafeCheckIsMeleeWeapon(td))
                            .OrderBy(td => td.techLevel)
                            .ThenBy(td => td.label)
                            .ToList();
                    }
                    catch (Exception e)
                    {
                        AutoArmLogger.Error("Critical error building melee weapons cache", e);
                        _meleeWeapons = new List<ThingDef>();
                    }
                }
                return _meleeWeapons;
            }
        }

        /// <summary>
        /// All valid
        /// </summary>
        public static List<ThingDef> AllWeapons
        {
            get
            {
                if (_allWeapons == null)
                {
                    try
                    {
                        _allWeapons = DefDatabase<ThingDef>.AllDefsListForReading
                            .Where(td => IsWeapon(td))
                            .OrderBy(td => td.techLevel)
                            .ThenBy(td => td.label)
                            .ToList();
                    }
                    catch (Exception e)
                    {
                        AutoArmLogger.Error("Critical error building all weapons cache", e);
                        _allWeapons = new List<ThingDef>();
                    }
                }
                return _allWeapons;
            }
        }

        /// <summary>
        /// Outfit allowed
        /// </summary>
        public static bool IsWeaponAllowedByOutfit(ThingDef weaponDef, Pawn pawn)
        {
            if (weaponDef == null || pawn?.outfits == null)
                return true;

            var policy = pawn.outfits.CurrentApparelPolicy;
            if (policy?.filter == null)
                return true;

            return policy.filter.Allows(weaponDef);
        }

        /// <summary>
        /// Weapon allowed
        /// </summary>
        public static bool IsWeaponAllowed(ThingWithComps weapon, Pawn pawn)
        {
            if (weapon?.def == null || pawn == null)
                return false;

            var policy = pawn.outfits?.CurrentApparelPolicy;
            if (policy?.filter == null)
                return true;

            return policy.filter.Allows(weapon);
        }

        /// <summary>
        /// Clear cached weapon lists
        /// </summary>
        public static void ClearCaches()
        {
            _rangedWeapons = null;
            _meleeWeapons = null;
            _allWeapons = null;
            AutoArmLogger.Debug(() => "Weapon validation caches cleared");
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
