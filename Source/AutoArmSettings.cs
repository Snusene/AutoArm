// AutoArm RimWorld 1.5+ mod - automatic weapon management
// This file: All mod configuration options and settings persistence
// Handles: Save/load settings, performance mode thresholds, cooldown timings
// Critical: ResetToDefaults() must match initial values for consistency
// Note: Settings cached by SettingsCacheHelper for performance

using Verse;
using UnityEngine;

namespace AutoArm
{
    public class AutoArmSettings : ModSettings
    {
        public bool modEnabled = true;
        
        // Legacy settings for migration (remove in future version)
        private float rangedWeaponMultiplier = -1f;
        private float meleeWeaponMultiplier = -1f;
        public bool debugLogging = false;
        public bool showNotifications = true;
        public bool thinkTreeInjectionFailed = false;

        public bool autoEquipSidearms = true;
        public bool allowSidearmUpgrades = true;
        public bool allowForcedWeaponUpgrades = false;  // Allow upgrading forced weapons to better quality versions (disabled by default)
        public bool checkCEAmmo = true;
        public bool lastKnownCEAmmoState = false;  // Track CE ammo system state to detect changes

        public float weaponUpgradeThreshold = 1.05f;
        public float weaponTypePreference = 0.11f;  // -1 = strong melee, 0 = balanced, 1 = strong ranged (default 0.11 = slight ranged)
        public int childrenMinAge = 13;
        public bool allowChildrenToEquipWeapons = true;  // Default to true to match vanilla behavior
        public bool allowTemporaryColonists = false;  // Default to false - don't let guests take our weapons!
        public bool disableDuringRaids = true;  // Default to true for performance
        public bool respectWeaponBonds = true;  // Default to true - protect valuable bonded weapons
        public int performanceModeColonySize = 35; // Disable upgrades for armed pawns in colonies larger than this

        public override void ExposeData()
        {
            Scribe_Values.Look(ref modEnabled, "modEnabled", true);
            Scribe_Values.Look(ref debugLogging, "debugLogging", false);
            Scribe_Values.Look(ref showNotifications, "showNotifications", true);
            Scribe_Values.Look(ref thinkTreeInjectionFailed, "thinkTreeInjectionFailed", false);
            Scribe_Values.Look(ref autoEquipSidearms, "autoEquipSidearms", true);
            Scribe_Values.Look(ref allowSidearmUpgrades, "allowSidearmUpgrades", true);
            Scribe_Values.Look(ref allowForcedWeaponUpgrades, "allowForcedWeaponUpgrades", false);
            Scribe_Values.Look(ref checkCEAmmo, "checkCEAmmo", true);
            Scribe_Values.Look(ref lastKnownCEAmmoState, "lastKnownCEAmmoState", false);
            Scribe_Values.Look(ref weaponUpgradeThreshold, "weaponUpgradeThreshold", 1.05f);
            
            // Handle legacy settings migration
            Scribe_Values.Look(ref rangedWeaponMultiplier, "rangedWeaponMultiplier", -1f);
            Scribe_Values.Look(ref meleeWeaponMultiplier, "meleeWeaponMultiplier", -1f);
            
            // If we have legacy settings, migrate them
            if (Scribe.mode == LoadSaveMode.PostLoadInit && rangedWeaponMultiplier > 0 && meleeWeaponMultiplier > 0)
            {
                // Calculate preference from old multipliers
                // Default was ranged=10, melee=8
                // If ranged is higher than default or melee is lower, prefer ranged
                // If melee is higher than default or ranged is lower, prefer melee
                float rangedDiff = (rangedWeaponMultiplier - 10f) / 10f; // Normalize to -1 to 1
                float meleeDiff = (8f - meleeWeaponMultiplier) / 8f; // Inverse for melee
                
                // Average the two to get overall preference
                weaponTypePreference = Mathf.Clamp((rangedDiff + meleeDiff) / 2f, -1f, 1f);
                
                // Clear legacy values
                rangedWeaponMultiplier = -1f;
                meleeWeaponMultiplier = -1f;
                
                AutoArmLogger.Log($"Migrated legacy weapon multipliers to preference: {weaponTypePreference:F2}");
            }
            
            Scribe_Values.Look(ref weaponTypePreference, "weaponTypePreference", 0.11f);
            Scribe_Values.Look(ref childrenMinAge, "childrenMinAge", 13);
            Scribe_Values.Look(ref allowChildrenToEquipWeapons, "allowChildrenToEquipWeapons", true);
            Scribe_Values.Look(ref allowTemporaryColonists, "allowTemporaryColonists", false);
            Scribe_Values.Look(ref disableDuringRaids, "disableDuringRaids", true);
            Scribe_Values.Look(ref respectWeaponBonds, "respectWeaponBonds", true);
            Scribe_Values.Look(ref performanceModeColonySize, "performanceModeColonySize", 35);

            base.ExposeData();
        }

        public void ResetToDefaults()
        {
            modEnabled = true;
            debugLogging = false;
            showNotifications = true;
            autoEquipSidearms = true;
            allowSidearmUpgrades = true;
            allowForcedWeaponUpgrades = false;
            checkCEAmmo = true;
            lastKnownCEAmmoState = false;
            weaponUpgradeThreshold = 1.05f;
            weaponTypePreference = 0.11f;
            childrenMinAge = 13;
            allowChildrenToEquipWeapons = true;  // Default to true to match vanilla behavior
            allowTemporaryColonists = false;  // Default to false - don't let guests take our weapons!
            disableDuringRaids = true;
            respectWeaponBonds = true;
            performanceModeColonySize = 35;
        }
    }
}