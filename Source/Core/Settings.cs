
using AutoArm.Definitions;
using AutoArm.Helpers;
using Verse;

namespace AutoArm
{
    public sealed class AutoArmSettings : ModSettings
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
        private const float DEFAULT_WEAPON_UPGRADE_THRESHOLD = Constants.WeaponUpgradeThreshold;
        private const float DEFAULT_WEAPON_TYPE_PREFERENCE = Constants.DefaultWeaponTypePreference;
        private const int DEFAULT_CHILDREN_MIN_AGE = Constants.ChildDefaultMinAge;
        private const bool DEFAULT_ALLOW_CHILDREN_TO_EQUIP = false;
        private const bool DEFAULT_ALLOW_TEMPORARY_COLONISTS = false;
        private const bool DEFAULT_DISABLE_DURING_RAIDS = false;
        private const bool DEFAULT_RESPECT_WEAPON_BONDS = true;

        public bool modEnabled;
        public bool debugLogging;
        public bool showForcedLabels;
        public bool showNotifications;
        public bool onlyAutoEquipFromStorage;
        public bool autoEquipSidearms;
        public bool allowSidearmUpgrades;
        public bool allowForcedWeaponUpgrades;
        public bool checkCEAmmo;
        public bool lastKnownCEAmmoState;
        public float weaponUpgradeThreshold;
        public float weaponTypePreference;
        public int childrenMinAge;
        public bool allowChildrenToEquipWeapons;
        public bool allowTemporaryColonists;
        public bool disableDuringRaids;
        public bool respectWeaponBonds;

        public AutoArmSettings()
        {
            ApplyDefaults();
        }

        public void CopyFrom(AutoArmSettings other)
        {
            if (other == null) return;
            modEnabled = other.modEnabled;
            debugLogging = other.debugLogging;
            showForcedLabels = other.showForcedLabels;
            showNotifications = other.showNotifications;
            onlyAutoEquipFromStorage = other.onlyAutoEquipFromStorage;
            autoEquipSidearms = other.autoEquipSidearms;
            allowSidearmUpgrades = other.allowSidearmUpgrades;
            allowForcedWeaponUpgrades = other.allowForcedWeaponUpgrades;
            checkCEAmmo = other.checkCEAmmo;
            lastKnownCEAmmoState = other.lastKnownCEAmmoState;
            weaponUpgradeThreshold = other.weaponUpgradeThreshold;
            weaponTypePreference = other.weaponTypePreference;
            childrenMinAge = other.childrenMinAge;
            allowChildrenToEquipWeapons = other.allowChildrenToEquipWeapons;
            allowTemporaryColonists = other.allowTemporaryColonists;
            disableDuringRaids = other.disableDuringRaids;
            respectWeaponBonds = other.respectWeaponBonds;
        }

        public AutoArmSettings Clone()
        {
            var clone = new AutoArmSettings();
            clone.CopyFrom(this);
            return clone;
        }

        private void ApplyDefaults()
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
            weaponUpgradeThreshold = DEFAULT_WEAPON_UPGRADE_THRESHOLD;
            weaponTypePreference = DEFAULT_WEAPON_TYPE_PREFERENCE;
            childrenMinAge = DEFAULT_CHILDREN_MIN_AGE;
            allowChildrenToEquipWeapons = DEFAULT_ALLOW_CHILDREN_TO_EQUIP;
            allowTemporaryColonists = DEFAULT_ALLOW_TEMPORARY_COLONISTS;
            disableDuringRaids = DEFAULT_DISABLE_DURING_RAIDS;
            respectWeaponBonds = DEFAULT_RESPECT_WEAPON_BONDS;
        }

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
            Scribe_Values.Look(ref weaponUpgradeThreshold, "weaponUpgradeThreshold", DEFAULT_WEAPON_UPGRADE_THRESHOLD);
            Scribe_Values.Look(ref weaponTypePreference, "weaponTypePreference", DEFAULT_WEAPON_TYPE_PREFERENCE);
            Scribe_Values.Look(ref childrenMinAge, "childrenMinAge", DEFAULT_CHILDREN_MIN_AGE);
            Scribe_Values.Look(ref allowChildrenToEquipWeapons, "allowChildrenToEquipWeapons", DEFAULT_ALLOW_CHILDREN_TO_EQUIP);
            Scribe_Values.Look(ref allowTemporaryColonists, "allowTemporaryColonists", DEFAULT_ALLOW_TEMPORARY_COLONISTS);
            Scribe_Values.Look(ref disableDuringRaids, "disableDuringRaids", DEFAULT_DISABLE_DURING_RAIDS);
            Scribe_Values.Look(ref respectWeaponBonds, "respectWeaponBonds", DEFAULT_RESPECT_WEAPON_BONDS);

            base.ExposeData();

            if (Scribe.mode == LoadSaveMode.LoadingVars || Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (float.IsNaN(weaponUpgradeThreshold) || float.IsInfinity(weaponUpgradeThreshold))
                    weaponUpgradeThreshold = DEFAULT_WEAPON_UPGRADE_THRESHOLD;
                weaponUpgradeThreshold = UnityEngine.Mathf.Clamp(weaponUpgradeThreshold,
                    Constants.WeaponUpgradeThresholdMin, Constants.WeaponUpgradeThresholdMax);

                if (float.IsNaN(weaponTypePreference) || float.IsInfinity(weaponTypePreference))
                    weaponTypePreference = DEFAULT_WEAPON_TYPE_PREFERENCE;
                weaponTypePreference = UnityEngine.Mathf.Clamp(weaponTypePreference, -1f, 1f);

                childrenMinAge = UnityEngine.Mathf.Clamp(childrenMinAge,
                    (int)Constants.ChildMinAgeLimit, (int)Constants.ChildMaxAgeLimit);
            }

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                Patches.ConditionalPatcher.RefreshPatches();
            }
        }

        public void ResetToDefaults()
        {
            ApplyDefaults();
            if (Current.Game != null && Current.ProgramState == ProgramState.Playing)
            {
                Cleanup.PerformFullCleanup();
            }
        }
    }
}
