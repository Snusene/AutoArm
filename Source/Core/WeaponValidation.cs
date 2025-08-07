// AutoArm RimWorld 1.5+ mod - automatic weapon management
// This file: Weapon validation and filtering logic for modded content safety
// Ensures weapons are proper, usable, and safe for auto-equip

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
        // Cached weapon lists
        private static List<ThingDef> _rangedWeapons;

        private static List<ThingDef> _meleeWeapons;
        private static List<ThingDef> _allWeapons;

        // Comprehensive list of items that might return IsWeapon = true but shouldn't be auto-equipped
        private static readonly HashSet<string> ExcludedDefNames = new HashSet<string>
        {
            // Building materials
            "WoodLog", "Steel", "Plasteel", "Uranium", "Jade", "Silver", "Gold",
            "BlocksGranite", "BlocksLimestone", "BlocksSlate", "BlocksSandstone", "BlocksMarble",
            "ChunkGranite", "ChunkLimestone", "ChunkSlate", "ChunkSandstone", "ChunkMarble",
            "ChunkSlagSteel", "ChunkSlagSilver", "ChunkSlagGold", "ChunkSlagPlasteel",

            // Components and tech
            "ComponentIndustrial", "ComponentSpacer", "AIPersonaCore", "TechprofSubpersonaCore",
            "PowerfocusChip", "NanostructuringChip", "BroadshieldCore", "MechSerumHealer",
            "MechSerumResurrector", "SignalChip", "Biotuner",

            // Medicine and drugs
            "MedicineHerbal", "MedicineIndustrial", "MedicineUltratech", "Penoxycyline",
            "Luciferium", "Neutroamine", "Ambrosia", "Beer", "Smokeleaf", "SmokeleafJoint",
            "Yayo", "Flake", "GoJuice", "WakeUp", "PsychiteTea", "Chemfuel",

            // Food
            "RawPotatoes", "RawRice", "RawCorn", "RawBerries", "RawAgave", "RawFungus",
            "MealSimple", "MealFine", "MealLavish", "MealSurvivalPack", "MealNutrientPaste",
            "Pemmican", "Kibble", "Hay", "Chocolate", "InsectJelly", "Milk", "RawHops",
            "PsychoidLeaves", "SmokeleafLeaves", "Wort",

            // Meat (any meat can be thrown)
            "Meat_Human", "Meat_Megasloth", "Meat_Thrumbo", "Meat_Alphabeaver", "Meat_Muffalo",
            "Meat_Gazelle", "Meat_Iguana", "Meat_Rhinoceros", "Meat_Elephant", "Meat_Dromedary",
            "Meat_Horse", "Meat_Donkey", "Meat_Pig", "Meat_Cow", "Meat_Chicken", "Meat_Duck",

            // Textiles
            "Cloth", "Synthread", "DevilstrandCloth", "Hyperweave", "WoolAlpaca", "WoolBison",
            "WoolMegasloth", "WoolMuffalo", "WoolSheep", "WoolYak", "Leather_Plain", "Leather_Dog",
            "Leather_Wolf", "Leather_Panthera", "Leather_Bear", "Leather_Human", "Leather_Pig",
            "Leather_Light", "Leather_Bird", "Leather_Chinchilla", "Leather_Fox", "Leather_Lizard",
            "Leather_Heavy", "Leather_Elephant", "Leather_Rhinoceros", "Leather_Thrumbo", "Leather_Patch",

            // Body parts and implants
            "Heart", "Lung", "Kidney", "Liver", "SimpleProstheticLeg", "SimpleProstheticArm",
            "BionicEye", "BionicArm", "BionicLeg", "BionicSpine", "BionicHeart", "BionicStomach",
            "ArchotechEye", "ArchotechArm", "ArchotechLeg", "CochlearImplant", "PowerClaw",
            "ScytherBlade", "FieldHand", "DrillArm", "MindscewHemisphere", "CircadianAssistant",

            // Shells and explosives (often throwable)
            "Shell_HighExplosive", "Shell_Incendiary", "Shell_EMP", "Shell_Firefoam", "Shell_Smoke",
            "Shell_AntigrainWarhead", "MortarShell", "ReinforcedBarrel",

            // Artifacts and misc
            "PsychicInsanityLance", "PsychicShockLance", "PsychicSoothePulser", "PsychicAnimalPulser",
            "OrbitalTargeterPowerBeam", "OrbitalTargeterBombardment", "TornadoGenerator",
            "Apparel_ShieldBelt", "Apparel_SmokepopBelt", "Apparel_PsychicFoilHelmet",
            "FirefoamPopper", "HealrootCrafted",

            // Eggs (yes, eggs can be thrown)
            "EggChickenUnfertilized", "EggChickenFertilized", "EggDuckUnfertilized", "EggDuckFertilized",
            "EggGooseUnfertilized", "EggGooseFertilized", "EggTurkeyUnfertilized", "EggTurkeyFertilized",
            "EggCassowaryUnfertilized", "EggCassowaryFertilized", "EggEmuUnfertilized", "EggEmuFertilized",
            "EggOstrichUnfertilized", "EggOstrichFertilized", "EggIguanaUnfertilized", "EggIguanaFertilized",
            "EggCobraUnfertilized", "EggCobraFertilized", "EggTortoiseUnfertilized", "EggTortoiseFertilized",

            // Seeds and plants
            "RawHealroot", "RawDaylily", "RawBerries", "Plant_Psychoid", "Plant_Smokeleaf",
            "Plant_TreeOak", "Plant_TreePoplar", "Plant_TreePine", "Plant_TreeBirch", "Plant_TreeTeak",

            // Modded items that commonly cause issues (common material mods)
            "BioMatter", "BioFuel", "RawMagicyte", "RefinedMagicyte", "Unobtainium",
            "NeuralChip", "QuantumChip", "NanoMaterial", "CarbonFiber", "ReinforcedGlass",

            // Kiiro Race special items that might cause issues
            "Kiiro_PoisonBottle_Paralyzing", "Kiiro_PoisonBottle_Lethal",
            "Kiiro_PortableTurret_Base", "Kiiro_AutoTurret_Portable",
            "Kiiro_Stealth_Device", "Kiiro_Special_Item"
        };

        /// <summary>
        /// Validates if a thing is actually a proper weapon that should be auto-equipped
        /// </summary>
        public static bool IsProperWeapon(Thing thing)
        {
            if (thing?.def == null)
                return false;

            var thingWithComps = thing as ThingWithComps;
            if (thingWithComps == null)
                return false;

            return IsProperWeapon(thing.def);
        }

        /// <summary>
        /// Validates if a ThingDef is actually a proper weapon that should be auto-equipped
        /// </summary>
        public static bool IsProperWeapon(ThingDef def)
        {
            try
            {
                // Layer 1: Basic null checks
                if (def == null)
                    return false;

                // Quick exclude check (safe - just string comparison)
                if (ExcludedDefNames.Contains(def.defName))
                {
                    // Track excluded items for debugging
                    if (AutoArmMod.settings?.debugLogging == true)
                    {
                        ExcludedItemTracker.TrackExcludedItem(def);
                    }
                    return false;
                }

                // Layer 2: Defensive property checks with individual exception handling
                // Each check is isolated to prevent one failure from breaking everything

                // Check if it's a weapon (this commonly throws with modded items)
                // Note: Exclusion check already done above, so wood/materials already filtered
                if (!SafeCheckIsWeapon(def))
                    return false;

                // Check if it's apparel (must not be apparel to be a weapon)
                if (SafeCheckIsApparel(def))
                    return false;

                // Check equipment type (custom equipment might have invalid types)
                if (!SafeCheckEquipmentType(def))
                    return false;

                // Check for CompEquippable (reflection-based, can fail with custom components)
                if (!SafeCheckHasEquippableComp(def))
                    return false;

                // Special cases that are allowed (safe - just string comparison)
                if (def.defName == "ElephantTusk" || def.defName == "ThrumboHorn" || def.defName == "MastodonTusk")
                    return true;

                return true;
            }
            catch (Exception ex)
            {
                // Final safety net - should rarely hit this with granular checks
                LogWeaponValidationFailure(def, ex);
                return false;
            }
        }

        /// <summary>
        /// Safely checks if ThingDef.IsWeapon without throwing
        /// </summary>
        private static bool SafeCheckIsWeapon(ThingDef def)
        {
            // Null check first
            if (def?.defName == null)
                return false;

            // CRITICAL: Check exclusion list BEFORE accessing IsWeapon
            // This prevents NullReferenceExceptions on items like Beer, WoodLog, etc.
            if (ExcludedDefNames.Contains(def.defName))
                return false;

            try
            {
                // Only access IsWeapon after exclusion check
                return def.IsWeapon;
            }
            catch (Exception ex)
            {
                // Log validation failure for debugging
                LogWeaponValidationFailure(def, ex);
                return false;
            }
        }

        /// <summary>
        /// Safely checks if ThingDef.IsRangedWeapon without throwing
        /// </summary>
        private static bool SafeCheckIsRangedWeapon(ThingDef def)
        {
            // Null check first
            if (def?.defName == null)
                return false;

            // Check exclusion list BEFORE accessing IsRangedWeapon
            if (ExcludedDefNames.Contains(def.defName))
                return false;

            try
            {
                return def.IsRangedWeapon;
            }
            catch (Exception)
            {
                // If IsRangedWeapon throws, it's not a valid ranged weapon
                return false;
            }
        }

        /// <summary>
        /// Safely checks if ThingDef.IsMeleeWeapon without throwing
        /// </summary>
        private static bool SafeCheckIsMeleeWeapon(ThingDef def)
        {
            // Null check first
            if (def?.defName == null)
                return false;

            // Check exclusion list BEFORE accessing IsMeleeWeapon
            if (ExcludedDefNames.Contains(def.defName))
                return false;

            try
            {
                return def.IsMeleeWeapon;
            }
            catch (Exception)
            {
                // If IsMeleeWeapon throws, it's not a valid melee weapon
                return false;
            }
        }

        /// <summary>
        /// Safely checks if ThingDef.IsApparel without throwing
        /// </summary>
        private static bool SafeCheckIsApparel(ThingDef def)
        {
            try
            {
                return def.IsApparel;
            }
            catch (Exception ex)
            {
                // If IsApparel throws, assume it's not apparel (safer for weapon detection)
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.Debug($"IsApparel check failed for {def?.defName ?? "unknown"}: {ex.Message}");
                }
                return false;
            }
        }

        /// <summary>
        /// Safely checks if equipment type is valid without throwing
        /// </summary>
        private static bool SafeCheckEquipmentType(ThingDef def)
        {
            try
            {
                // Equipment type must not be None for valid weapons
                return def.equipmentType != EquipmentType.None;
            }
            catch (Exception ex)
            {
                // If equipmentType throws (custom enum values), reject it
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.Debug($"EquipmentType check failed for {def?.defName ?? "unknown"}: {ex.Message}");
                }
                return false;
            }
        }

        /// <summary>
        /// Safely checks if ThingDef has CompEquippable without throwing
        /// </summary>
        private static bool SafeCheckHasEquippableComp(ThingDef def)
        {
            try
            {
                return def.HasComp(typeof(CompEquippable));
            }
            catch (Exception ex)
            {
                // If component check throws (reflection issues), reject it
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.Debug($"CompEquippable check failed for {def?.defName ?? "unknown"}: {ex.Message}");
                }
                return false;
            }
        }

        /// <summary>
        /// Logs detailed weapon validation failure information
        /// </summary>
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

        /// <summary>
        /// Logs detailed weapon validation failure information
        /// </summary>
        private static void LogWeaponValidationFailure(ThingDef def, Exception ex)
        {
            // Skip logging for known problematic items that throw NullReferenceException
            if (def?.defName != null && ExcludedDefNames.Contains(def.defName))
                return;

            // Only log detailed validation failures in debug mode
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

                AutoArmLogger.Debug(details.ToString());
            }
        }

        /// <summary>
        /// Checks if an item is in the exclusion list
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

        // ===== Cached weapon lists (from WeaponThingFilterUtility) =====

        /// <summary>
        /// All valid ranged weapons
        /// </summary>
        public static List<ThingDef> RangedWeapons
        {
            get
            {
                if (_rangedWeapons == null)
                {
                    try
                    {
                        // Build weapon list with proper filtering
                        var allDefs = DefDatabase<ThingDef>.AllDefsListForReading;
                        var properWeapons = allDefs.Where(td => IsProperWeapon(td));
                        _rangedWeapons = properWeapons
                            .Where(td => SafeCheckIsRangedWeapon(td))
                            .OrderBy(td => td.techLevel)
                            .ThenBy(td => td.label)
                            .ToList();
                    }
                    catch (Exception e)
                    {
                        Log.Error($"[AutoArm] Failed to load ranged weapons cache: {e.Message}");
                        AutoArmLogger.Error("Critical error building ranged weapons cache", e);
                        _rangedWeapons = new List<ThingDef>();
                    }
                }
                return _rangedWeapons;
            }
        }

        /// <summary>
        /// All valid melee weapons
        /// </summary>
        public static List<ThingDef> MeleeWeapons
        {
            get
            {
                if (_meleeWeapons == null)
                {
                    try
                    {
                        // Build weapon list with proper filtering
                        var allDefs = DefDatabase<ThingDef>.AllDefsListForReading;
                        var properWeapons = allDefs.Where(td => IsProperWeapon(td));
                        _meleeWeapons = properWeapons
                            .Where(td => SafeCheckIsMeleeWeapon(td))
                            .OrderBy(td => td.techLevel)
                            .ThenBy(td => td.label)
                            .ToList();
                    }
                    catch (Exception e)
                    {
                        Log.Error($"[AutoArm] Failed to load melee weapons cache: {e.Message}");
                        AutoArmLogger.Error("Critical error building melee weapons cache", e);
                        _meleeWeapons = new List<ThingDef>();
                    }
                }
                return _meleeWeapons;
            }
        }

        /// <summary>
        /// All valid weapons (ranged and melee)
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
                            .Where(td => IsProperWeapon(td))
                            .OrderBy(td => td.techLevel)
                            .ThenBy(td => td.label)
                            .ToList();
                    }
                    catch (Exception e)
                    {
                        Log.Error($"[AutoArm] Failed to load all weapons cache: {e.Message}");
                        AutoArmLogger.Error("Critical error building all weapons cache", e);
                        _allWeapons = new List<ThingDef>();
                    }
                }
                return _allWeapons;
            }
        }

        /// <summary>
        /// Check if a weapon is allowed by pawn's outfit filter
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
        /// Check if a specific weapon instance is allowed (includes quality checks)
        /// </summary>
        public static bool IsWeaponAllowed(ThingWithComps weapon, Pawn pawn)
        {
            if (weapon?.def == null || pawn == null)
                return false;

            var policy = pawn.outfits?.CurrentApparelPolicy;
            if (policy?.filter == null)
                return true;

            // Check the actual weapon instance (includes quality checks)
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
            if (AutoArmMod.settings?.debugLogging == true)
            {
                AutoArmLogger.Debug("Weapon validation caches cleared");
            }
        }
    }

    /// <summary>
    /// Tracks excluded items for debugging purposes
    /// </summary>
    internal static class ExcludedItemTracker
    {
        private static Dictionary<string, int> excludedCounts = new Dictionary<string, int>();
        private static int lastReportTick = 0;
        private const int REPORT_INTERVAL = Constants.ExcludedItemReportInterval; // Report every minute

        public static void TrackExcludedItem(ThingDef def)
        {
            if (def?.defName == null) return;

            if (!excludedCounts.ContainsKey(def.defName))
                excludedCounts[def.defName] = 0;
            excludedCounts[def.defName]++;

            // Report summary periodically
            int currentTick = Find.TickManager?.TicksGame ?? 0;
            if (currentTick - lastReportTick > REPORT_INTERVAL && excludedCounts.Count > 0)
            {
                var topExcluded = excludedCounts
                    .OrderByDescending(kvp => kvp.Value)
                    .Take(5)
                    .Select(kvp => $"{kvp.Key}: {kvp.Value}")
                    .ToList();

                AutoArmLogger.Debug($"Top excluded items (last minute): {string.Join(", ", topExcluded)}");
                excludedCounts.Clear();
                lastReportTick = currentTick;
            }
        }
    }
}