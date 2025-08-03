// AutoArm RimWorld 1.5+ mod - automatic weapon management
// This file: Dynamic weapon provider for tests
// Provides fallback weapons when vanilla defs are missing

using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using AutoArm.Logging; using AutoArm.Testing;
using AutoArm.Definitions;

namespace AutoArm.Testing.Helpers
{
    public static class TestWeaponProvider
    {
        private static List<ThingDef> _cachedRangedWeapons;
        private static List<ThingDef> _cachedMeleeWeapons;
        
        /// <summary>
        /// Get any available ranged weapon def, with vanilla preference
        /// </summary>
        public static ThingDef GetAnyRangedWeapon(string preferredVanilla = "Gun_AssaultRifle")
        {
            // Try vanilla first
            var vanilla = DefDatabase<ThingDef>.GetNamedSilentFail(preferredVanilla);
            if (vanilla != null && vanilla.IsRangedWeapon)
                return vanilla;
                
            // Cache and return any ranged weapon
            if (_cachedRangedWeapons == null)
            {
                _cachedRangedWeapons = DefDatabase<ThingDef>.AllDefs
                    .Where(d => d.IsRangedWeapon && d.tradeability != Tradeability.None)
                    .OrderByDescending(d => d.BaseMarketValue) // Prefer better weapons
                    .ToList();
            }
            
            var weapon = _cachedRangedWeapons.FirstOrDefault();
            if (weapon == null)
            {
                AutoArmLogger.LogError("[TEST] No ranged weapons found in game!");
            }
            return weapon;
        }
        
        /// <summary>
        /// Get any available melee weapon def, with vanilla preference
        /// </summary>
        public static ThingDef GetAnyMeleeWeapon(string preferredVanilla = "MeleeWeapon_LongSword")
        {
            // Try vanilla first
            var vanilla = DefDatabase<ThingDef>.GetNamedSilentFail(preferredVanilla);
            if (vanilla != null && vanilla.IsMeleeWeapon)
                return vanilla;
                
            // Cache and return any melee weapon
            if (_cachedMeleeWeapons == null)
            {
                _cachedMeleeWeapons = DefDatabase<ThingDef>.AllDefs
                    .Where(d => d.IsMeleeWeapon && d.tradeability != Tradeability.None)
                    .OrderByDescending(d => d.BaseMarketValue) // Prefer better weapons
                    .ToList();
            }
            
            var weapon = _cachedMeleeWeapons.FirstOrDefault();
            if (weapon == null)
            {
                AutoArmLogger.LogError("[TEST] No melee weapons found in game!");
            }
            return weapon;
        }
        
        /// <summary>
        /// Get a pair of weapons with different quality levels for testing
        /// </summary>
        public static (ThingDef lower, ThingDef higher) GetWeaponPairForTesting(bool ranged = true)
        {
            if (ranged)
            {
                // Try vanilla pairs first
                var pistol = VanillaWeaponDefOf.Gun_Autopistol;
                var rifle = VanillaWeaponDefOf.Gun_AssaultRifle;
                
                if (pistol != null && rifle != null)
                    return (pistol, rifle);
                    
                // Fallback to any two ranged weapons
                var weapons = _cachedRangedWeapons ?? DefDatabase<ThingDef>.AllDefs
                    .Where(d => d.IsRangedWeapon && d.tradeability != Tradeability.None)
                    .OrderBy(d => d.BaseMarketValue)
                    .ToList();
                    
                if (weapons.Count >= 2)
                    return (weapons[0], weapons[weapons.Count - 1]);
            }
            else
            {
                // Try vanilla pairs first
                var knife = VanillaWeaponDefOf.MeleeWeapon_Knife;
                var sword = VanillaWeaponDefOf.MeleeWeapon_LongSword;
                
                if (knife != null && sword != null)
                    return (knife, sword);
                    
                // Fallback to any two melee weapons
                var weapons = _cachedMeleeWeapons ?? DefDatabase<ThingDef>.AllDefs
                    .Where(d => d.IsMeleeWeapon && d.tradeability != Tradeability.None)
                    .OrderBy(d => d.BaseMarketValue)
                    .ToList();
                    
                if (weapons.Count >= 2)
                    return (weapons[0], weapons[weapons.Count - 1]);
            }
            
            // Last resort - return same weapon twice
            var anyWeapon = ranged ? GetAnyRangedWeapon() : GetAnyMeleeWeapon();
            return (anyWeapon, anyWeapon);
        }
    }
}
