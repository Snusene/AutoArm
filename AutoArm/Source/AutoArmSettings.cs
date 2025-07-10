using Verse;

namespace AutoArm
{
    public class AutoArmSettings : ModSettings
    {
        // Basic settings
        public bool modEnabled = true;
        public bool debugLogging = false;
        public bool showNotifications = true;
        public bool thinkTreeInjectionFailed = false;

        // Mod compatibility settings
        public bool autoEquipSidearms = true;
        public bool checkCEAmmo = true;

        // Advanced settings (for future use)
        public float weaponUpgradeThreshold = 1.05f;
        public int childrenMinAge = 13;
        public bool allowChildrenToEquipWeapons = false;
        public bool respectConceitedNobles = true;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref modEnabled, "modEnabled", true);
            Scribe_Values.Look(ref debugLogging, "debugLogging", false);
            Scribe_Values.Look(ref showNotifications, "showNotifications", true);
            Scribe_Values.Look(ref thinkTreeInjectionFailed, "thinkTreeInjectionFailed", false);
            Scribe_Values.Look(ref autoEquipSidearms, "autoEquipSidearms", true);
            Scribe_Values.Look(ref checkCEAmmo, "checkCEAmmo", true);
            Scribe_Values.Look(ref weaponUpgradeThreshold, "weaponUpgradeThreshold", 1.05f);
            Scribe_Values.Look(ref childrenMinAge, "childrenMinAge", 13);
            Scribe_Values.Look(ref allowChildrenToEquipWeapons, "allowChildrenToEquipWeapons", false);
            Scribe_Values.Look(ref respectConceitedNobles, "respectConceitedNobles", true);
            base.ExposeData();
        }

        public void ResetToDefaults()
        {
            modEnabled = true;
            debugLogging = false;
            showNotifications = true;
            autoEquipSidearms = true;
            checkCEAmmo = true;
            weaponUpgradeThreshold = 1.05f;
            childrenMinAge = 13;
            allowChildrenToEquipWeapons = false;
            respectConceitedNobles = true;
        }
    }
}