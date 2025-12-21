
using AutoArm.Compatibility;
using AutoArm.Definitions;
using AutoArm.Helpers;
using Verse;

namespace AutoArm
{
    public class AutoArmSettings : ModSettings
    {
        private const bool DEFAULT_MOD_ENABLED = true;

        private const bool DEFAULT_DEBUG_LOGGING = false;
        private const bool DEFAULT_SHOW_FORCED_LABELS = true;
        private const bool DEFAULT_SHOW_NOTIFICATIONS = true;
        private const bool DEFAULT_ONLY_EQUIP_FROM_STORAGE = false;

        private const bool DEFAULT_AUTO_EQUIP_SIDEARMS = true;

        private const bool DEFAULT_ALLOW_SIDEARM_UPGRADES = true;
        private const bool DEFAULT_ALLOW_FORCED_WEAPON_UPGRADES = false;
        private const bool DEFAULT_CHECK_CE_AMMO = true;
        private const bool DEFAULT_LAST_KNOWN_CE_AMMO_STATE = false;
        private const float DEFAULT_WEAPON_TYPE_PREFERENCE = Constants.DefaultWeaponTypePreference;
        private const bool DEFAULT_ALLOW_CHILDREN_TO_EQUIP = false;
        private const bool DEFAULT_ALLOW_TEMPORARY_COLONISTS = false;
        private const bool DEFAULT_DISABLE_DURING_RAIDS = false;
        private const bool DEFAULT_RESPECT_WEAPON_BONDS = true;

        public bool modEnabled = DEFAULT_MOD_ENABLED;

        public bool debugLogging = DEFAULT_DEBUG_LOGGING;
        public bool showForcedLabels = DEFAULT_SHOW_FORCED_LABELS;
        public bool showNotifications = DEFAULT_SHOW_NOTIFICATIONS;
        public bool onlyAutoEquipFromStorage = DEFAULT_ONLY_EQUIP_FROM_STORAGE;

        public bool autoEquipSidearms = DEFAULT_AUTO_EQUIP_SIDEARMS;
        public bool allowSidearmUpgrades = DEFAULT_ALLOW_SIDEARM_UPGRADES;

        public bool allowForcedWeaponUpgrades = DEFAULT_ALLOW_FORCED_WEAPON_UPGRADES;
        public bool checkCEAmmo = DEFAULT_CHECK_CE_AMMO;
        public bool lastKnownCEAmmoState = DEFAULT_LAST_KNOWN_CE_AMMO_STATE;

        public float weaponUpgradeThreshold = Constants.WeaponUpgradeThreshold;
        public float weaponTypePreference = DEFAULT_WEAPON_TYPE_PREFERENCE;
        public int childrenMinAge = Constants.ChildDefaultMinAge;
        public bool allowChildrenToEquipWeapons = DEFAULT_ALLOW_CHILDREN_TO_EQUIP;
        public bool allowTemporaryColonists = DEFAULT_ALLOW_TEMPORARY_COLONISTS;
        public bool disableDuringRaids = DEFAULT_DISABLE_DURING_RAIDS;
        public bool respectWeaponBonds = DEFAULT_RESPECT_WEAPON_BONDS;

        public bool preferSimilarWeapons = true;

        public bool allowGrenadeEquip = false;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref modEnabled, "modEnabled", DEFAULT_MOD_ENABLED);
            Scribe_Values.Look(ref debugLogging, "debugLogging", DEFAULT_DEBUG_LOGGING);
            Scribe_Values.Look(ref showForcedLabels, "showForcedLabels", DEFAULT_SHOW_FORCED_LABELS);
            Scribe_Values.Look(ref showNotifications, "showNotifications", DEFAULT_SHOW_NOTIFICATIONS);
            Scribe_Values.Look(ref onlyAutoEquipFromStorage, "onlyAutoEquipFromStorage", DEFAULT_ONLY_EQUIP_FROM_STORAGE);
            Scribe_Values.Look(ref autoEquipSidearms, "autoEquipSidearms", DEFAULT_AUTO_EQUIP_SIDEARMS);
            Scribe_Values.Look(ref allowSidearmUpgrades, "allowSidearmUpgrades", DEFAULT_ALLOW_SIDEARM_UPGRADES);
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

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                Patches.ConditionalPatcher.RefreshPatches();
            }
        }

        public void ResetToDefaults()
        {
            modEnabled = DEFAULT_MOD_ENABLED;
            debugLogging = DEFAULT_DEBUG_LOGGING;
            showForcedLabels = DEFAULT_SHOW_FORCED_LABELS;
            showNotifications = DEFAULT_SHOW_NOTIFICATIONS;
            onlyAutoEquipFromStorage = DEFAULT_ONLY_EQUIP_FROM_STORAGE;
            autoEquipSidearms = DEFAULT_AUTO_EQUIP_SIDEARMS;
            allowSidearmUpgrades = DEFAULT_ALLOW_SIDEARM_UPGRADES;
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

            Cleanup.PerformFullCleanup();
        }
    }
}
