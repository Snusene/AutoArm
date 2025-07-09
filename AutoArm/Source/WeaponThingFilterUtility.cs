using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace AutoArm
{
    public static class WeaponThingFilterUtility
    {
        private static List<ThingDef> _rangedWeapons;
        private static List<ThingDef> _meleeWeapons;
        private static List<ThingDef> _allWeapons;
        private static HashSet<string> _excludedDefNames;

        private static HashSet<string> ExcludedDefNames
        {
            get
            {
                if (_excludedDefNames == null)
                {
                    _excludedDefNames = new HashSet<string>
                    {
                        "WoodLog", "Steel", "Plasteel", "Uranium", "Jade",
                        "Silver", "Gold", "ComponentIndustrial", "ComponentSpacer",
                        "ChunkSlagSteel", "ChunkSlagSilver", "ChunkSlagGold",
                        "Chemfuel", "Neutroamine", "MedicineHerbal", "MedicineIndustrial",
                        "MedicineUltratech", "Penoxycyline", "Luciferium",
                        // Other items that shouldn't be weapons
                        "Beer"
                    };
                }
                return _excludedDefNames;
            }
        }

        public static List<ThingDef> RangedWeapons
        {
            get
            {
                if (_rangedWeapons == null)
                {
                    try
                    {
                        _rangedWeapons = DefDatabase<ThingDef>.AllDefsListForReading
                            .Where(td => td.IsRangedWeapon && !IsNonSelectable(td))
                            .OrderBy(td => td.techLevel)
                            .ThenBy(td => td.label)
                            .ToList();
                    }
                    catch (Exception e)
                    {
                        Log.Error($"[AutoArm] Error loading ranged weapons: {e}");
                        _rangedWeapons = new List<ThingDef>();
                    }
                }
                return _rangedWeapons;
            }
        }

        public static List<ThingDef> MeleeWeapons
        {
            get
            {
                if (_meleeWeapons == null)
                {
                    try
                    {
                        _meleeWeapons = DefDatabase<ThingDef>.AllDefsListForReading
                            .Where(td => td.IsMeleeWeapon && !IsNonSelectable(td))
                            .OrderBy(td => td.techLevel)
                            .ThenBy(td => td.label)
                            .ToList();
                    }
                    catch (Exception e)
                    {
                        Log.Error($"[AutoArm] Error loading melee weapons: {e}");
                        _meleeWeapons = new List<ThingDef>();
                    }
                }
                return _meleeWeapons;
            }
        }

        public static List<ThingDef> AllWeapons
        {
            get
            {
                if (_allWeapons == null)
                {
                    try
                    {
                        _allWeapons = DefDatabase<ThingDef>.AllDefsListForReading
                            .Where(td => td.IsWeapon && !td.IsApparel && !IsNonSelectable(td))
                            .OrderBy(td => td.techLevel)
                            .ThenBy(td => td.label)
                            .ToList();
                    }
                    catch (Exception e)
                    {
                        Log.Error($"[AutoArm] Error loading all weapons: {e}");
                        _allWeapons = new List<ThingDef>();
                    }
                }
                return _allWeapons;
            }
        }

        public static bool IsNonSelectable(this ThingDef td)
        {
            if (td == null)
                return true;

            if (string.IsNullOrEmpty(td.label))
                return true;

            if (td.thingClass == null)
                return true;

            if (ExcludedDefNames.Contains(td.defName))
                return true;

            // Exclude tools and materials that might be marked as weapons
            if (td.IsStuff || td.stuffProps != null)
                return true;

            // Exclude things with zero market value (likely debug items)
            if (td.BaseMarketValue <= 0)
                return true;

            // Exclude things that can't be equipped
            if (td.equipmentType == EquipmentType.None)
                return true;

            // Additional checks for modded content
            if (td.defName.StartsWith("Debug") || td.defName.StartsWith("Test"))
                return true;

            // Check if it's actually equippable
            if (!td.HasComp(typeof(CompEquippable)))
                return true;

            return false;
        }

        // Helper method to check if a weapon is allowed by an outfit
        public static bool IsWeaponAllowedByOutfit(ThingDef weaponDef, Pawn pawn)
        {
            if (weaponDef == null || pawn?.outfits == null)
                return true; // Allow if no outfit system

            var policy = pawn.outfits.CurrentApparelPolicy;
            if (policy?.filter == null)
                return true; // Allow if no filter

            return policy.filter.Allows(weaponDef);
        }

        // Check if a specific weapon instance is allowed
        public static bool IsWeaponAllowed(ThingWithComps weapon, Pawn pawn)
        {
            if (weapon?.def == null || pawn == null)
                return false;

            return IsWeaponAllowedByOutfit(weapon.def, pawn);
        }

        // Get best allowed weapon type for a pawn
        public static bool PrefersMeleeWeapon(Pawn pawn)
        {
            if (pawn == null)
                return false;

            // Brawlers always prefer melee
            if (pawn.story?.traits?.HasTrait(TraitDefOf.Brawler) == true)
                return true;

            // Check skill levels
            float meleeSkill = pawn.skills?.GetSkill(SkillDefOf.Melee)?.Level ?? 0f;
            float shootingSkill = pawn.skills?.GetSkill(SkillDefOf.Shooting)?.Level ?? 0f;

            // Prefer melee if significantly better at it
            return meleeSkill > shootingSkill + 3f;
        }

        // Clear caches when needed (e.g., when mods are hot-reloaded)
        public static void ClearCaches()
        {
            _rangedWeapons = null;
            _meleeWeapons = null;
            _allWeapons = null;
            Log.Message("[AutoArm] Weapon caches cleared");
        }
    }
}