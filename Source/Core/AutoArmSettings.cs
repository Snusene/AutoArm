// AutoArm RimWorld 1.5+ mod - automatic weapon management
// This file: All mod configuration options and settings persistence
// Handles: Save/load settings, performance mode thresholds, cooldown timings
// Critical: ResetToDefaults() must match initial values for consistency
// Note: Settings cached by CleanupHelper for performance

using AutoArm.Definitions;
using AutoArm.Helpers;
using AutoArm.Logging;
using UnityEngine;
using Verse;

namespace AutoArm
{
    public class AutoArmSettings : ModSettings
    {
        // Default values as constants to avoid duplication
        private const bool DEFAULT_MOD_ENABLED = true;

        private const bool DEFAULT_DEBUG_LOGGING = false;
        private const bool DEFAULT_SHOW_NOTIFICATIONS = true;

        // Removed thinkTreeInjectionFailed - we now rely entirely on think tree priority
        private const bool DEFAULT_AUTO_EQUIP_SIDEARMS = true;

        private const bool DEFAULT_ALLOW_SIDEARM_UPGRADES = true;
        private const bool DEFAULT_ALLOW_FORCED_WEAPON_UPGRADES = true;
        private const bool DEFAULT_CHECK_CE_AMMO = true;
        private const bool DEFAULT_LAST_KNOWN_CE_AMMO_STATE = false;
        private const float DEFAULT_WEAPON_TYPE_PREFERENCE = Constants.DefaultWeaponTypePreference;  // Slight ranged preference
        private const bool DEFAULT_ALLOW_CHILDREN_TO_EQUIP = true;
        private const bool DEFAULT_ALLOW_TEMPORARY_COLONISTS = false;
        private const bool DEFAULT_DISABLE_DURING_RAIDS = true;
        private const bool DEFAULT_RESPECT_WEAPON_BONDS = true;

        public bool modEnabled = DEFAULT_MOD_ENABLED;

        public bool debugLogging = DEFAULT_DEBUG_LOGGING;
        public bool showNotifications = DEFAULT_SHOW_NOTIFICATIONS;

        private bool _autoEquipSidearms = DEFAULT_AUTO_EQUIP_SIDEARMS;
        private bool _allowSidearmUpgrades = DEFAULT_ALLOW_SIDEARM_UPGRADES;

        public bool autoEquipSidearms
        {
            get
            {
                // Disable if SimpleSidearms is loaded but reflection failed
                if (SimpleSidearmsCompat.IsLoaded() && SimpleSidearmsCompat.ReflectionFailed)
                    return false;
                return _autoEquipSidearms;
            }
            set { _autoEquipSidearms = value; }
        }

        public bool allowSidearmUpgrades
        {
            get
            {
                // Disable if SimpleSidearms is loaded but reflection failed
                if (SimpleSidearmsCompat.IsLoaded() && SimpleSidearmsCompat.ReflectionFailed)
                    return false;
                return _allowSidearmUpgrades;
            }
            set { _allowSidearmUpgrades = value; }
        }

        public bool allowForcedWeaponUpgrades = DEFAULT_ALLOW_FORCED_WEAPON_UPGRADES;  // Allow upgrading forced weapons to better quality versions (enabled by default)
        public bool checkCEAmmo = DEFAULT_CHECK_CE_AMMO;
        public bool lastKnownCEAmmoState = DEFAULT_LAST_KNOWN_CE_AMMO_STATE;  // Track CE ammo system state to detect changes

        public float weaponUpgradeThreshold = Constants.WeaponUpgradeThreshold;
        public float weaponTypePreference = DEFAULT_WEAPON_TYPE_PREFERENCE;  // -1 = strong melee, 0 = balanced, 1 = strong ranged
        public int childrenMinAge = Constants.ChildDefaultMinAge;
        public bool allowChildrenToEquipWeapons = DEFAULT_ALLOW_CHILDREN_TO_EQUIP;  // Default to true to match vanilla behavior
        public bool allowTemporaryColonists = DEFAULT_ALLOW_TEMPORARY_COLONISTS;  // Default to false - don't let guests take our weapons!
        public bool disableDuringRaids = DEFAULT_DISABLE_DURING_RAIDS;  // Default to true for performance
        public bool respectWeaponBonds = DEFAULT_RESPECT_WEAPON_BONDS;  // Default to true - protect valuable bonded weapons

        // Test-specific settings
        public bool preferSimilarWeapons = true;  // For testing weapon similarity preferences

        public bool allowGrenadeEquip = false;    // For testing grenade/explosive weapon handling

        public override void ExposeData()
        {
            Scribe_Values.Look(ref modEnabled, "modEnabled", DEFAULT_MOD_ENABLED);
            Scribe_Values.Look(ref debugLogging, "debugLogging", DEFAULT_DEBUG_LOGGING);
            Scribe_Values.Look(ref showNotifications, "showNotifications", DEFAULT_SHOW_NOTIFICATIONS);
            Scribe_Values.Look(ref _autoEquipSidearms, "autoEquipSidearms", DEFAULT_AUTO_EQUIP_SIDEARMS);
            Scribe_Values.Look(ref _allowSidearmUpgrades, "allowSidearmUpgrades", DEFAULT_ALLOW_SIDEARM_UPGRADES);
            Scribe_Values.Look(ref allowForcedWeaponUpgrades, "allowForcedWeaponUpgrades", DEFAULT_ALLOW_FORCED_WEAPON_UPGRADES);
            Scribe_Values.Look(ref checkCEAmmo, "checkCEAmmo", DEFAULT_CHECK_CE_AMMO);
            Scribe_Values.Look(ref lastKnownCEAmmoState, "lastKnownCEAmmoState", DEFAULT_LAST_KNOWN_CE_AMMO_STATE);
            Scribe_Values.Look(ref weaponUpgradeThreshold, "weaponUpgradeThreshold", Constants.WeaponUpgradeThreshold);

            Scribe_Values.Look(ref weaponTypePreference, "weaponTypePreference", DEFAULT_WEAPON_TYPE_PREFERENCE);
            Scribe_Values.Look(ref childrenMinAge, "childrenMinAge", Constants.ChildDefaultMinAge);
            Scribe_Values.Look(ref allowChildrenToEquipWeapons, "allowChildrenToEquipWeapons", DEFAULT_ALLOW_CHILDREN_TO_EQUIP);
            Scribe_Values.Look(ref allowTemporaryColonists, "allowTemporaryColonists", DEFAULT_ALLOW_TEMPORARY_COLONISTS);
            Scribe_Values.Look(ref disableDuringRaids, "disableDuringRaids", DEFAULT_DISABLE_DURING_RAIDS);
            Scribe_Values.Look(ref respectWeaponBonds, "respectWeaponBonds", DEFAULT_RESPECT_WEAPON_BONDS);

            base.ExposeData();
        }

        public void ResetToDefaults()
        {
            modEnabled = DEFAULT_MOD_ENABLED;
            debugLogging = DEFAULT_DEBUG_LOGGING;
            showNotifications = DEFAULT_SHOW_NOTIFICATIONS;
            _autoEquipSidearms = DEFAULT_AUTO_EQUIP_SIDEARMS;
            _allowSidearmUpgrades = DEFAULT_ALLOW_SIDEARM_UPGRADES;
            allowForcedWeaponUpgrades = DEFAULT_ALLOW_FORCED_WEAPON_UPGRADES;
            checkCEAmmo = DEFAULT_CHECK_CE_AMMO;
            lastKnownCEAmmoState = DEFAULT_LAST_KNOWN_CE_AMMO_STATE;
            weaponUpgradeThreshold = Constants.WeaponUpgradeThreshold;
            weaponTypePreference = DEFAULT_WEAPON_TYPE_PREFERENCE;
            childrenMinAge = Constants.ChildDefaultMinAge;
            allowChildrenToEquipWeapons = DEFAULT_ALLOW_CHILDREN_TO_EQUIP;
            allowTemporaryColonists = DEFAULT_ALLOW_TEMPORARY_COLONISTS;
            disableDuringRaids = DEFAULT_DISABLE_DURING_RAIDS;
            respectWeaponBonds = DEFAULT_RESPECT_WEAPON_BONDS;

            // Clear any cached settings values by performing a full cleanup
            CleanupHelper.PerformFullCleanup();
        }
    }
}