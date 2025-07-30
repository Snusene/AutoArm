using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
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

            // Special case for Thrumbo Horn and Elephant Tusk
            // These are body parts that can be wielded as weapons
            if (td.defName == "ThrumboHorn" || td.defName == "ElephantTusk")
                return false;

            if (ExcludedDefNames.Contains(td.defName))
                return true;

            if (td.IsStuff || td.stuffProps != null)
                return true;

            if (td.BaseMarketValue <= 0)
                return true;

            if (td.equipmentType == EquipmentType.None)
                return true;

            if (td.defName.StartsWith("Debug") || td.defName.StartsWith("Test"))
                return true;

            if (!td.HasComp(typeof(CompEquippable)))
                return true;

            return false;
        }

        public static bool IsWeaponAllowedByOutfit(ThingDef weaponDef, Pawn pawn)
        {
            if (weaponDef == null || pawn?.outfits == null)
                return true;

            var policy = pawn.outfits.CurrentApparelPolicy;
            if (policy?.filter == null)
                return true;

            return policy.filter.Allows(weaponDef);
        }

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

        public static bool PrefersMeleeWeapon(Pawn pawn)
        {
            if (pawn == null)
                return false;

            if (pawn.story?.traits?.HasTrait(TraitDefOf.Brawler) == true)
                return true;

            float meleeSkill = pawn.skills?.GetSkill(SkillDefOf.Melee)?.Level ?? 0f;
            float shootingSkill = pawn.skills?.GetSkill(SkillDefOf.Shooting)?.Level ?? 0f;

            return meleeSkill > shootingSkill + 3f;
        }

        public static void ClearCaches()
        {
            _rangedWeapons = null;
            _meleeWeapons = null;
            _allWeapons = null;
            Log.Message("[AutoArm] Weapon caches cleared");
        }
    }
}