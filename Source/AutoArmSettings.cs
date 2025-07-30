using Verse;

namespace AutoArm
{
    public class AutoArmSettings : ModSettings
    {
        public bool modEnabled = true;
        public bool debugLogging = false;
        public bool showNotifications = true;
        public bool thinkTreeInjectionFailed = false;

        public bool autoEquipSidearms = true;
        public bool allowSidearmUpgrades = true;
        public bool allowForcedWeaponUpgrades = false;  // Allow upgrading forced weapons to better quality versions (disabled by default)
        public bool checkCEAmmo = true;
        public bool lastKnownCEAmmoState = false;  // Track CE ammo system state to detect changes

        public float weaponUpgradeThreshold = 1.05f;
        public int childrenMinAge = 13;
        public bool allowChildrenToEquipWeapons = true;  // Default to true to match vanilla behavior
        public bool allowTemporaryColonists = false;  // Default to false - don't let guests take our weapons!
        public bool respectConceitedNobles = true;
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
            Scribe_Values.Look(ref childrenMinAge, "childrenMinAge", 13);
            Scribe_Values.Look(ref allowChildrenToEquipWeapons, "allowChildrenToEquipWeapons", true);
            Scribe_Values.Look(ref allowTemporaryColonists, "allowTemporaryColonists", false);
            Scribe_Values.Look(ref respectConceitedNobles, "respectConceitedNobles", true);
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
            childrenMinAge = 13;
            allowChildrenToEquipWeapons = true;  // Default to true to match vanilla behavior
            allowTemporaryColonists = false;  // Default to false - don't let guests take our weapons!
            respectConceitedNobles = true;
            performanceModeColonySize = 35;
        }
    }
}