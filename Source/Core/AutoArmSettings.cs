// AutoArm RimWorld 1.5+ mod - automatic weapon management
// This file: All mod configuration options and settings persistence
// Handles: Save/load settings, performance mode thresholds, cooldown timings
// Critical: ResetToDefaults() must match initial values for consistency
// Note: Settings cached by CleanupHelper for performance

using Verse;
using UnityEngine;
using AutoArm.Caching;
using AutoArm.Helpers;
using AutoArm.Logging;

namespace AutoArm
{
    public class AutoArmSettings : ModSettings
    {
        // Default values as constants to avoid duplication
        private const bool DEFAULT_MOD_ENABLED = true;
        private const bool DEFAULT_DEBUG_LOGGING = false;
        private const bool DEFAULT_SHOW_NOTIFICATIONS = true;
        private const bool DEFAULT_THINK_TREE_INJECTION_FAILED = false;
        private const bool DEFAULT_AUTO_EQUIP_SIDEARMS = true;
        private const bool DEFAULT_ALLOW_SIDEARM_UPGRADES = true;
        private const bool DEFAULT_ALLOW_FORCED_WEAPON_UPGRADES = false;
        private const bool DEFAULT_CHECK_CE_AMMO = true;
        private const bool DEFAULT_LAST_KNOWN_CE_AMMO_STATE = false;
        private const float DEFAULT_WEAPON_UPGRADE_THRESHOLD = 1.05f;
        private const float DEFAULT_WEAPON_TYPE_PREFERENCE = 0.11f;  // Slight ranged preference
        private const int DEFAULT_CHILDREN_MIN_AGE = 13;
        private const bool DEFAULT_ALLOW_CHILDREN_TO_EQUIP = true;
        private const bool DEFAULT_ALLOW_TEMPORARY_COLONISTS = false;
        private const bool DEFAULT_DISABLE_DURING_RAIDS = true;
        private const bool DEFAULT_RESPECT_WEAPON_BONDS = true;
        private const int DEFAULT_PERFORMANCE_MODE_COLONY_SIZE = 35;
        
        public bool modEnabled = DEFAULT_MOD_ENABLED;
        
        // Legacy settings for migration (remove in future version)
        private float rangedWeaponMultiplier = -1f;
        private float meleeWeaponMultiplier = -1f;
        public bool debugLogging = DEFAULT_DEBUG_LOGGING;
        public bool showNotifications = DEFAULT_SHOW_NOTIFICATIONS;
        public bool thinkTreeInjectionFailed = DEFAULT_THINK_TREE_INJECTION_FAILED;

        public bool autoEquipSidearms = DEFAULT_AUTO_EQUIP_SIDEARMS;
        public bool allowSidearmUpgrades = DEFAULT_ALLOW_SIDEARM_UPGRADES;
        public bool allowForcedWeaponUpgrades = DEFAULT_ALLOW_FORCED_WEAPON_UPGRADES;  // Allow upgrading forced weapons to better quality versions (disabled by default)
        public bool checkCEAmmo = DEFAULT_CHECK_CE_AMMO;
        public bool lastKnownCEAmmoState = DEFAULT_LAST_KNOWN_CE_AMMO_STATE;  // Track CE ammo system state to detect changes

        public float weaponUpgradeThreshold = DEFAULT_WEAPON_UPGRADE_THRESHOLD;
        public float weaponTypePreference = DEFAULT_WEAPON_TYPE_PREFERENCE;  // -1 = strong melee, 0 = balanced, 1 = strong ranged
        public int childrenMinAge = DEFAULT_CHILDREN_MIN_AGE;
        public bool allowChildrenToEquipWeapons = DEFAULT_ALLOW_CHILDREN_TO_EQUIP;  // Default to true to match vanilla behavior
        public bool allowTemporaryColonists = DEFAULT_ALLOW_TEMPORARY_COLONISTS;  // Default to false - don't let guests take our weapons!
        public bool disableDuringRaids = DEFAULT_DISABLE_DURING_RAIDS;  // Default to true for performance
        public bool respectWeaponBonds = DEFAULT_RESPECT_WEAPON_BONDS;  // Default to true - protect valuable bonded weapons
        public int performanceModeColonySize = DEFAULT_PERFORMANCE_MODE_COLONY_SIZE; // Disable upgrades for armed pawns in colonies larger than this

        public override void ExposeData()
        {
            Scribe_Values.Look(ref modEnabled, "modEnabled", DEFAULT_MOD_ENABLED);
            Scribe_Values.Look(ref debugLogging, "debugLogging", DEFAULT_DEBUG_LOGGING);
            Scribe_Values.Look(ref showNotifications, "showNotifications", DEFAULT_SHOW_NOTIFICATIONS);
            Scribe_Values.Look(ref thinkTreeInjectionFailed, "thinkTreeInjectionFailed", DEFAULT_THINK_TREE_INJECTION_FAILED);
            Scribe_Values.Look(ref autoEquipSidearms, "autoEquipSidearms", DEFAULT_AUTO_EQUIP_SIDEARMS);
            Scribe_Values.Look(ref allowSidearmUpgrades, "allowSidearmUpgrades", DEFAULT_ALLOW_SIDEARM_UPGRADES);
            Scribe_Values.Look(ref allowForcedWeaponUpgrades, "allowForcedWeaponUpgrades", DEFAULT_ALLOW_FORCED_WEAPON_UPGRADES);
            Scribe_Values.Look(ref checkCEAmmo, "checkCEAmmo", DEFAULT_CHECK_CE_AMMO);
            Scribe_Values.Look(ref lastKnownCEAmmoState, "lastKnownCEAmmoState", DEFAULT_LAST_KNOWN_CE_AMMO_STATE);
            Scribe_Values.Look(ref weaponUpgradeThreshold, "weaponUpgradeThreshold", DEFAULT_WEAPON_UPGRADE_THRESHOLD);
            
            // Handle legacy settings migration
            Scribe_Values.Look(ref rangedWeaponMultiplier, "rangedWeaponMultiplier", -1f);
            Scribe_Values.Look(ref meleeWeaponMultiplier, "meleeWeaponMultiplier", -1f);
            
            // If we have legacy settings, migrate them
            if (Scribe.mode == LoadSaveMode.PostLoadInit && rangedWeaponMultiplier > 0 && meleeWeaponMultiplier > 0)
            {
                // Simple percentage-based migration
                // What percentage did each change from default?
                float rangedChange = (rangedWeaponMultiplier - 10f) / 10f;  // -1 to +inf
                float meleeChange = (meleeWeaponMultiplier - 8f) / 8f;      // -1 to +inf
                
                // Positive rangedChange or negative meleeChange = prefer ranged
                // Negative rangedChange or positive meleeChange = prefer melee
                weaponTypePreference = Mathf.Clamp(rangedChange - meleeChange, -1f, 1f);
                
                // Clear legacy values
                rangedWeaponMultiplier = -1f;
                meleeWeaponMultiplier = -1f;
                
                if (debugLogging)
                {
                    AutoArmLogger.Debug($"Migrated legacy weapon multipliers to preference: {weaponTypePreference:F2}");
                }
            }
            
            Scribe_Values.Look(ref weaponTypePreference, "weaponTypePreference", DEFAULT_WEAPON_TYPE_PREFERENCE);
            Scribe_Values.Look(ref childrenMinAge, "childrenMinAge", DEFAULT_CHILDREN_MIN_AGE);
            Scribe_Values.Look(ref allowChildrenToEquipWeapons, "allowChildrenToEquipWeapons", DEFAULT_ALLOW_CHILDREN_TO_EQUIP);
            Scribe_Values.Look(ref allowTemporaryColonists, "allowTemporaryColonists", DEFAULT_ALLOW_TEMPORARY_COLONISTS);
            Scribe_Values.Look(ref disableDuringRaids, "disableDuringRaids", DEFAULT_DISABLE_DURING_RAIDS);
            Scribe_Values.Look(ref respectWeaponBonds, "respectWeaponBonds", DEFAULT_RESPECT_WEAPON_BONDS);
            Scribe_Values.Look(ref performanceModeColonySize, "performanceModeColonySize", DEFAULT_PERFORMANCE_MODE_COLONY_SIZE);

            base.ExposeData();
        }

        public void ResetToDefaults()
        {
            modEnabled = DEFAULT_MOD_ENABLED;
            debugLogging = DEFAULT_DEBUG_LOGGING;
            showNotifications = DEFAULT_SHOW_NOTIFICATIONS;
            thinkTreeInjectionFailed = DEFAULT_THINK_TREE_INJECTION_FAILED;
            autoEquipSidearms = DEFAULT_AUTO_EQUIP_SIDEARMS;
            allowSidearmUpgrades = DEFAULT_ALLOW_SIDEARM_UPGRADES;
            allowForcedWeaponUpgrades = DEFAULT_ALLOW_FORCED_WEAPON_UPGRADES;
            checkCEAmmo = DEFAULT_CHECK_CE_AMMO;
            lastKnownCEAmmoState = DEFAULT_LAST_KNOWN_CE_AMMO_STATE;
            weaponUpgradeThreshold = DEFAULT_WEAPON_UPGRADE_THRESHOLD;
            weaponTypePreference = DEFAULT_WEAPON_TYPE_PREFERENCE;
            childrenMinAge = DEFAULT_CHILDREN_MIN_AGE;
            allowChildrenToEquipWeapons = DEFAULT_ALLOW_CHILDREN_TO_EQUIP;
            allowTemporaryColonists = DEFAULT_ALLOW_TEMPORARY_COLONISTS;
            disableDuringRaids = DEFAULT_DISABLE_DURING_RAIDS;
            respectWeaponBonds = DEFAULT_RESPECT_WEAPON_BONDS;
            
            // Clear any cached settings values
            CleanupHelper.ClearAllCaches();
        }
    }
}
