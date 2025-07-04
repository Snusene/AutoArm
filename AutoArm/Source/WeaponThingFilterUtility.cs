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

        public static List<ThingDef> RangedWeapons
        {
            get
            {
                if (_rangedWeapons == null)
                {
                    try
                    {
                        _rangedWeapons = DefDatabase<ThingDef>.AllDefsListForReading
                            .Where(td => td.IsRangedWeapon && !td.IsNonSelectable()).ToList();
                    }
                    catch
                    {
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
                            .Where(td => td.IsMeleeWeapon && !td.IsNonSelectable()).ToList();
                    }
                    catch
                    {
                        _meleeWeapons = new List<ThingDef>();
                    }
                }
                return _meleeWeapons;
            }
        }

        public static bool IsNonSelectable(this ThingDef td)
        {
            return td == null
                || string.IsNullOrEmpty(td.label)
                || td.modContentPack == null;
        }
    }
}
